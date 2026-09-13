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
                    found.Add(device);
                }
                catch (Exception ex) { Errors.Add(endpoint.ID + ": " + ex.Message); }
            }
        }
        var restore = DeviceInventory.Merge(State.Devices, found);
        foreach (var device in restore)
        {
            // Capture before SetAudio refreshes channel state from Windows.
            var values = device.Values.ToArray();
            try
            {
                if (device.Volume.HasValue) SetAudio(device, device.Volume.Value, device.Muted);
                foreach (var (key, value) in values)
                    if (key != "decibels") await SetControlAsync(device, key, value);
            }
            catch (Exception ex) { device.Error = "Ripristino incompleto: " + ex.Message; Errors.Add(device.Name + ": " + device.Error); }
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
    }

    public void SetAudio(Device device, float volume, bool mute)
    {
        if (!float.IsFinite(volume)) throw new ArgumentOutOfRangeException(nameof(volume));
        volume = Math.Clamp(volume, 0, 1);
        if (device.Connected)
        {
            using var endpoint = enumerator.GetDevice(device.Id[6..]);
            endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
            endpoint.AudioEndpointVolume.Mute = mute;
        }
        device.Volume = volume; device.Muted = mute;
        if (device.Connected) ReadAudio(device);
        else device.Restore = true;
    }

    public async Task SetControlAsync(Device device, string key, double value)
    {
        var control = device.Controls.FirstOrDefault(c => c.Id == key)
            ?? throw new NotSupportedException("Controllo non esposto: " + key);
        if (!double.IsFinite(value) || value < control.Min || value > control.Max)
            throw new ArgumentOutOfRangeException(nameof(value));
        if (device.Connected)
        {
            if (device.Id.StartsWith("windows:")) InputControls.Set(device.Id, key, value);
            else if (device.Id.StartsWith("monitor:")) await Task.Run(() => MonitorControls.Set(device.Id, key, value));
            else if (device.Id.StartsWith("audio:"))
            {
                using var endpoint = enumerator.GetDevice(device.Id[6..]);
                if (key == "decibels") endpoint.AudioEndpointVolume.MasterVolumeLevel = (float)value;
                else if (key.StartsWith("channel:") && int.TryParse(key[8..], out int index) && index >= 0 && index < endpoint.AudioEndpointVolume.Channels.Count)
                    endpoint.AudioEndpointVolume.Channels[index].VolumeLevelScalar = (float)value / 100;
                else throw new NotSupportedException(key);
                ReadAudio(device);
            }
            else throw new NotSupportedException("Provider assente per " + device.Kind);
        }
        else device.Restore = true;
        device.Values[key] = value;
    }

    public Profile Capture(string name) => new()
    {
        Name = name,
        Audio = State.Devices.Where(d => d.Volume.HasValue).ToDictionary(d => d.Id,
            d => new AudioSetting { Volume = d.Volume!.Value, Muted = d.Muted }),
        Controls = State.Devices.Where(d => d.Values.Count > 0).ToDictionary(d => d.Id,
            d => d.Values.Where(kv => kv.Key != "decibels").ToDictionary(kv => kv.Key, kv => kv.Value))
    };

    public async Task<List<string>> ApplyAsync(Profile profile)
    {
        var failures = new List<string>();
        foreach (var (id, value) in profile.Audio)
        {
            var device = State.Devices.FirstOrDefault(d => d.Id == id);
            if (device == null) { failures.Add("Dispositivo sconosciuto: " + id); continue; }
            try { SetAudio(device, value.Volume, value.Muted); device.Restore = true; }
            catch (Exception ex) { failures.Add(device.Name + ": " + ex.Message); }
        }
        foreach (var (id, controls) in profile.Controls)
        {
            var device = State.Devices.FirstOrDefault(d => d.Id == id);
            if (device == null) { failures.Add("Dispositivo sconosciuto: " + id); continue; }
            foreach (var (key, value) in controls)
            {
                try { await SetControlAsync(device, key, value); device.Restore = true; }
                catch (Exception ex) { failures.Add(device.Name + " / " + key + ": " + ex.Message); }
            }
        }
        State.Save();
        return failures;
    }

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
            device.Error = current.Error;
            if (device.Restore && !connected.Contains(device.Id)) restore.Add(device);
            else { device.Volume = current.Volume; device.Muted = current.Muted; device.Values = current.Values; }
        }
        return restore;
    }
}
