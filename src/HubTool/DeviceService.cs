using NAudio.CoreAudioApi;

namespace HubTool;

public sealed class DeviceService : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly Dictionary<string, MMDevice> watched = new();
    public event Action<string>? AudioChanged;
    public Settings State { get; }
    public List<string> Errors { get; } = new();

    public DeviceService(Settings settings)
    {
        State = settings;
        var removed = State.Devices.Where(d => !PeripheralCatalog.IsPeripheral(d)).Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        State.Devices.RemoveAll(d => removed.Contains(d.Id));
        foreach (var profile in State.Profiles)
        {
            foreach (var id in profile.Audio.Keys.Where(removed.Contains).ToArray()) profile.Audio.Remove(id);
            foreach (var id in profile.Controls.Keys.Where(removed.Contains).ToArray()) profile.Controls.Remove(id);
        }
        State.Shortcuts.RemoveAll(s => s.DeviceId.Length > 0 && removed.Contains(s.DeviceId));
        foreach (var device in State.Devices) device.Connected = false;
    }

    public async Task RefreshAsync()
    {
        Errors.Clear();
        var found = await Task.Run(() =>
        {
            var list = Native.Enumerate();
            try { list.AddRange(InputControls.Enumerate()); }
            catch (Exception ex) { lock (Errors) Errors.Add("Input Windows: " + ex.Message); }
            try { list.AddRange(MonitorControls.Enumerate()); }
            catch (Exception ex) { lock (Errors) Errors.Add("Monitor: " + ex.Message); }
            try { list.AddRange(CameraControls.Enumerate()); }
            catch (Exception ex) { lock (Errors) Errors.Add("Webcam: " + ex.Message); }
            return list;
        });
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active))
        {
            using (endpoint)
            {
                try
                {
                    var audio = endpoint.AudioEndpointVolume;
                    var device = new Device
                    {
                        Id = "audio:" + endpoint.ID, Name = endpoint.FriendlyName,
                        Kind = endpoint.DataFlow == DataFlow.Capture ? "Microfono" : "Uscita audio",
                        Connected = true, Volume = audio.MasterVolumeLevelScalar, Muted = audio.Mute
                    };
                    try { device.AudioFormFactor = Convert.ToInt32(endpoint.Properties[PropertyKeys.PKEY_AudioEndpoint_FormFactor].Value); }
                    catch (Exception) { device.AudioFormFactor = -1; }
                    var range = audio.VolumeRange;
                    if (range.MaxDecibels > range.MinDecibels)
                    {
                        device.Controls.Add(new("decibels", "Livello endpoint", range.MinDecibels, range.MaxDecibels,
                            Math.Max(.1, range.IncrementDecibels), "dB"));
                        device.Values["decibels"] = audio.MasterVolumeLevel;
                    }
                    for (int i = 0; i < audio.Channels.Count; i++)
                    {
                        device.Controls.Add(new("channel:" + i, "Canale " + (i + 1), 0, 100, 1, "%"));
                        device.Values["channel:" + i] = audio.Channels[i].VolumeLevelScalar * 100;
                    }
                    AudioTopologyControls.Discover(endpoint, device);
                    found.Add(device);
                }
                catch (Exception ex) { Errors.Add(endpoint.ID + ": " + ex.Message); }
            }
        }
        found = PeripheralCatalog.Prepare(found);
        foreach (var current in found.Where(d => d.PhysicalId.Length > 0))
        {
            var old = State.Devices.FirstOrDefault(d => d.Id.Equals(current.PhysicalId, StringComparison.OrdinalIgnoreCase));
            if (old == null) continue;
            var existing = State.Devices.FirstOrDefault(d => d.Id.Equals(current.Id, StringComparison.OrdinalIgnoreCase));
            if (existing == null) old.Id = current.Id;
            else { existing.Overlay |= old.Overlay; State.Devices.Remove(old); }
            foreach (var profile in State.Profiles)
            {
                if (profile.Controls.Remove(current.PhysicalId, out var controls)) profile.Controls.TryAdd(current.Id, controls);
                if (profile.Audio.Remove(current.PhysicalId, out var audio)) profile.Audio.TryAdd(current.Id, audio);
            }
            foreach (var shortcut in State.Shortcuts.Where(s => s.DeviceId == current.PhysicalId)) shortcut.DeviceId = current.Id;
        }
        var restore = DeviceInventory.Merge(State.Devices, found);
        Errors.AddRange(found.Where(d => d.Error.Length > 0).Select(d => d.Name + ": " + d.Error));
        foreach (var device in restore)
        {
            var failures = await FlushAsync(device);
            Errors.AddRange(failures.Select(error => device.Name + ": " + error));
        }
        State.Save();
        UpdateAudioSubscriptions();
    }

    private void UpdateAudioSubscriptions()
    {
        var active = State.Devices.Where(d => d.Connected && d.Id.StartsWith("audio:")).Select(d => d.Id).ToHashSet();
        foreach (var id in watched.Keys.Where(id => !active.Contains(id)).ToArray())
        {
            watched[id].Dispose(); watched.Remove(id);
        }
        foreach (var id in active.Where(id => !watched.ContainsKey(id)))
        {
            try
            {
                var endpoint = enumerator.GetDevice(id[6..]);
                endpoint.AudioEndpointVolume.OnVolumeNotification += _ => AudioChanged?.Invoke(id);
                watched[id] = endpoint;
            }
            catch (Exception ex) { Errors.Add("Notifiche audio: " + ex.Message); }
        }
    }

    public void ReadAudio(Device device)
    {
        if (!device.Connected || !device.Id.StartsWith("audio:")) return;
        using var endpoint = enumerator.GetDevice(device.Id[6..]);
        device.Volume = endpoint.AudioEndpointVolume.MasterVolumeLevelScalar;
        device.Muted = endpoint.AudioEndpointVolume.Mute;
        device.Values["decibels"] = endpoint.AudioEndpointVolume.MasterVolumeLevel;
        for (int i = 0; i < endpoint.AudioEndpointVolume.Channels.Count; i++)
            device.Values["channel:" + i] = endpoint.AudioEndpointVolume.Channels[i].VolumeLevelScalar * 100;
        AudioTopologyControls.Read(endpoint, device);
        device.NotifyValues();
    }

    public void SetAudio(Device device, float volume, bool mute)
    {
        if (!float.IsFinite(volume)) throw new ArgumentOutOfRangeException(nameof(volume));
        volume = Math.Clamp(volume, 0, 1);
        device.Pending ??= device.Connected ? new DeviceRequest() : DeviceRequest.Snapshot(device);
        if (!device.Connected && device.Volume is float previous && previous != volume)
        {
            foreach (var key in device.Pending.Controls.Keys.Where(k => k.StartsWith("channel:")).ToArray())
                device.Pending.Controls[key] = previous > 0 ? Math.Clamp(device.Pending.Controls[key] * volume / previous, 0, 100) : volume * 100;
        }
        device.Pending.Audio = new AudioSetting { Volume = volume, Muted = mute };
        device.Pending.Controls.Remove("decibels");
        State.Save();
        if (!device.Connected)
        {
            device.Volume = volume; device.Muted = mute; device.Restore = true; State.Save(); return;
        }
        try
        {
            WriteAudio(device, volume, mute);
            device.Pending.Audio = null;
            if (device.Pending.Controls.Count == 0) device.Pending = null;
        }
        finally { State.Save(); }
    }

    private void WriteAudio(Device device, float volume, bool mute)
    {
        if (device.Connected)
        {
            using var endpoint = enumerator.GetDevice(device.Id[6..]);
            endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
            endpoint.AudioEndpointVolume.Mute = mute;
        }
        device.Volume = volume; device.Muted = mute;
        if (device.Connected) ReadAudio(device);
    }

    public async Task SetControlAsync(Device device, string key, double value)
    {
        var control = device.Controls.FirstOrDefault(c => c.Id == key)
            ?? throw new NotSupportedException("Controllo non esposto: " + key);
        if (!double.IsFinite(value) || value < control.Min || value > control.Max)
            throw new ArgumentOutOfRangeException(nameof(value));
        device.Pending ??= device.Connected ? new DeviceRequest() : DeviceRequest.Snapshot(device);
        if (key == "decibels")
            foreach (var channel in device.Pending.Controls.Keys.Where(k => k.StartsWith("channel:")).ToArray()) device.Pending.Controls.Remove(channel);
        device.Pending.Controls[key] = value;
        State.Save();
        if (!device.Connected) { device.Values[key] = value; device.Restore = true; State.Save(); return; }
        try
        {
            await WriteControlAsync(device, key, value);
            device.Pending.Controls.Remove(key);
            if (device.Pending.Audio == null && device.Pending.Controls.Count == 0) device.Pending = null;
        }
        finally { State.Save(); }
    }

    public async Task<CameraControls.ResetResult> ResetCameraAsync(Device device)
    {
        if (!device.Connected || !device.Id.StartsWith("camera:", StringComparison.Ordinal))
            throw new InvalidOperationException("Videocamera non collegata");
        var result = await Task.Run(() => CameraControls.Reset(device.Id));
        device.Values = result.Values;
        device.NotifyValues();
        if (device.Pending != null)
        {
            foreach (var key in device.Pending.Controls.Keys.Where(k => k.StartsWith("camera:") || k.StartsWith("video:")).ToArray())
                device.Pending.Controls.Remove(key);
            if (device.Pending.Audio == null && device.Pending.Controls.Count == 0) device.Pending = null;
        }
        State.Save();
        return result;
    }

    private async Task WriteControlAsync(Device device, string key, double value)
    {
        var control = device.Controls.FirstOrDefault(c => c.Id == key) ?? throw new NotSupportedException("Controllo non esposto: " + key);
        if (!double.IsFinite(value) || value < control.Min || value > control.Max) throw new ArgumentOutOfRangeException(nameof(value));
        if (device.Connected)
        {
            if (device.Id.StartsWith("windows:")) InputControls.Set(device.Id, key, value);
            else if (device.Kind is "Keyboard" or "Mouse") InputControls.Set(device.Kind == "Keyboard" ? InputControls.KeyboardId : InputControls.MouseId, key, value);
            else if (device.Id.StartsWith("monitor:")) await Task.Run(() => MonitorControls.Set(device.Id, key, value));
            else if (device.Id.StartsWith("camera:"))
            {
                device.Values = await Task.Run(() => CameraControls.Set(device.Id, key, value));
                device.NotifyValues();
                return;
            }
            else if (device.Id.StartsWith("audio:"))
            {
                using var endpoint = enumerator.GetDevice(device.Id[6..]);
                if (key == "decibels") endpoint.AudioEndpointVolume.MasterVolumeLevel = (float)value;
                else if (key.StartsWith("channel:") && int.TryParse(key[8..], out int index) && index >= 0 && index < endpoint.AudioEndpointVolume.Channels.Count)
                    endpoint.AudioEndpointVolume.Channels[index].VolumeLevelScalar = (float)value / 100;
                else if (AudioTopologyControls.IsSidetone(key)) AudioTopologyControls.Set(endpoint, device, key, value);
                else throw new NotSupportedException(key);
                ReadAudio(device);
                return;
            }
            else throw new NotSupportedException("Provider assente per " + device.Kind);
        }
        device.Values[key] = value;
        device.NotifyValues();
    }

    public Profile Capture(string name)
    {
        var profile = new Profile { Name = name };
        foreach (var device in State.Devices.Where(PeripheralCatalog.IsVisible))
        {
            var request = DeviceRequest.Snapshot(device);
            if (device.Pending is { } pending)
            {
                if (pending.Audio is { } audio) request.Audio = new AudioSetting { Volume = audio.Volume, Muted = audio.Muted };
                foreach (var (key, value) in pending.Controls) request.Controls[key] = value;
                if (pending.Controls.ContainsKey("decibels"))
                    foreach (var key in request.Controls.Keys.Where(k => k.StartsWith("channel:")).ToArray()) request.Controls.Remove(key);
            }
            if (request.Audio != null) profile.Audio[device.Id] = request.Audio;
            if (request.Controls.Count > 0) profile.Controls[device.Id] = request.Controls;
        }
        return profile;
    }

    public async Task<List<string>> ApplyAsync(Profile profile)
    {
        var failures = new List<string>();
        foreach (var id in profile.Audio.Keys.Union(profile.Controls.Keys))
        {
            var device = State.Devices.FirstOrDefault(d => StringComparer.OrdinalIgnoreCase.Equals(d.Id, id));
            if (device == null) { failures.Add("Dispositivo sconosciuto: " + id); continue; }
            device.Pending = new DeviceRequest
            {
                Audio = profile.Audio.TryGetValue(id, out var audio) ? new AudioSetting { Volume = audio.Volume, Muted = audio.Muted } : null,
                Controls = profile.Controls.TryGetValue(id, out var values) ? new Dictionary<string, double>(values) : new()
            };
            device.Restore = true;
            State.Save();
            if (!device.Connected)
            {
                if (device.Pending.Audio is { } target) { device.Volume = target.Volume; device.Muted = target.Muted; }
                foreach (var (key, value) in device.Pending.Controls) device.Values[key] = value;
            }
            var errors = await FlushAsync(device);
            failures.AddRange(errors.Select(error => device.Name + ": " + error));
        }
        State.Save();
        return failures;
    }

    private Task<List<string>> FlushAsync(Device device) => RequestApplier.ApplyAsync(device,
        audio => WriteAudio(device, audio.Volume, audio.Muted), (key, value) => WriteControlAsync(device, key, value));

    public void Dispose()
    {
        foreach (var endpoint in watched.Values) endpoint.Dispose();
        watched.Clear(); enumerator.Dispose();
    }
}

public static class DeviceInventory
{
    public static List<Device> Merge(List<Device> saved, IEnumerable<Device> discovered)
    {
        var connected = saved.Where(d => d.Connected).Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lookup = saved.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var device in saved) device.Connected = false;
        var restore = new List<Device>();
        foreach (var current in discovered)
        {
            if (!lookup.TryGetValue(current.Id, out var device))
            {
                saved.Add(current); lookup[current.Id] = current; continue;
            }
            device.Name = current.Name; device.Kind = current.Kind; device.Connected = true;
            device.Controls = current.Controls; device.Manufacturer = current.Manufacturer; device.Driver = current.Driver;
            device.AudioFormFactor = current.AudioFormFactor; device.ProductName = current.ProductName;
            device.ContainerId = current.ContainerId; device.PhysicalId = current.PhysicalId;
            device.Error = current.Error;
            if (device.Restore && !connected.Contains(device.Id)) device.Pending ??= DeviceRequest.Snapshot(device);
            if (device.Pending != null) restore.Add(device);
            device.Volume = current.Volume; device.Muted = current.Muted; device.Values = current.Values;
            AudioTopologyControls.RestoreDisplayState(device);
        }
        return restore;
    }
}
