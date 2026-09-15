using NAudio.CoreAudioApi;

namespace HubTool;

public static class AudioTopologyControls
{
    private static readonly Guid MicrophoneSubtype = new("DFF21BE1-F70F-11D0-B917-00A0C9223196");
    private static readonly Guid VolumeSubtype = new("3A5ACC00-C557-11D0-8A2B-00A0C9255AC1");
    private static readonly Guid MuteSubtype = new("02B223C0-C557-11D0-8A2B-00A0C9255AC1");
    private const string VolumePrefix = "sidetone:volume:";
    private const string EnabledPrefix = "sidetone:enabled:";

    public static bool IsSidetone(string key) => key.StartsWith("sidetone:", StringComparison.Ordinal);

    public static void Discover(MMDevice endpoint, Device device)
    {
        if (endpoint.DataFlow != DataFlow.Render) return;
        var path = FindMicrophonePath(endpoint);
        if (path.Count == 0) return;

        var volumePart = path.AsEnumerable().Reverse().FirstOrDefault(p => p.GetSubType == VolumeSubtype);
        if (volumePart != null)
        {
            var volume = volumePart.AudioVolumeLevel;
            if (volume != null)
            {
                for (uint channel = 0; channel < volume.ChannelCount; channel++)
                {
                    volume.GetLevelRange(channel, out float min, out float max, out _);
                    if (!float.IsFinite(min) || !float.IsFinite(max) || max <= min) continue;
                    var key = VolumeKey(volumePart.LocalId, channel);
                    var label = volume.ChannelCount == 1 ? "Volume eco microfono" : $"Eco microfono · canale {channel + 1}";
                    device.Controls.Add(new(key, label, 0, 100, 1, "%"));
                    device.Values[key] = LevelToPercent(volume.GetLevel(channel), min, max);
                }
            }
        }

        var mutePart = path.AsEnumerable().Reverse().FirstOrDefault(p => p.GetSubType == MuteSubtype);
        if (mutePart?.AudioMute is { } mute)
        {
            var key = EnabledKey(mutePart.LocalId);
            device.Controls.Add(new(key, "Eco microfono attivo", 0, 1, 1, Toggle: true));
            device.Values[key] = mute.IsMuted ? 0 : 1;
        }
    }

    public static void Read(MMDevice endpoint, Device device)
    {
        var controls = new List<(DeviceControl Control, string Kind, uint PartId, uint Channel)>();
        foreach (var control in device.Controls.Where(c => IsSidetone(c.Id)))
        {
            if (!TryParse(control.Id, out var kind, out uint partId, out uint channel)) continue;
            controls.Add((control, kind, partId, channel));
        }
        var parts = FindParts(endpoint, controls.Select(c => c.PartId));
        foreach (var item in controls)
        {
            if (!parts.TryGetValue(item.PartId, out var part)) continue;
            if (item.Kind == "volume" && part.AudioVolumeLevel is { } volume && item.Channel < volume.ChannelCount)
            {
                volume.GetLevelRange(item.Channel, out float min, out float max, out _);
                device.Values[item.Control.Id] = LevelToPercent(volume.GetLevel(item.Channel), min, max);
            }
            else if (item.Kind == "enabled" && part.AudioMute is { } mute)
                device.Values[item.Control.Id] = mute.IsMuted ? 0 : 1;
        }
    }

    public static void Set(MMDevice endpoint, string key, double value)
    {
        if (!TryParse(key, out var kind, out uint partId, out uint channel)) throw new NotSupportedException(key);
        var part = FindPart(endpoint, partId) ?? throw new NotSupportedException("Controllo eco microfono non più esposto dal driver");
        if (kind == "enabled" && part.AudioMute is { } mute)
        {
            mute.IsMuted = value < .5;
            return;
        }
        if (kind == "volume" && part.AudioVolumeLevel is { } volume && channel < volume.ChannelCount)
        {
            volume.GetLevelRange(channel, out float min, out float max, out _);
            var level = PercentToLevel(value, min, max);
            volume.SetLevel(channel, level);
            return;
        }
        throw new NotSupportedException(key);
    }

    public static double LevelToPercent(float level, float min, float max) =>
        max <= min ? 0 : Math.Clamp((level - min) * 100d / (max - min), 0, 100);

    public static float PercentToLevel(double percent, float min, float max) =>
        (float)Math.Clamp(min + (max - min) * Math.Clamp(percent, 0, 100) / 100d, min, max);

    private static string VolumeKey(uint partId, uint channel) => $"{VolumePrefix}{partId}:{channel}";
    private static string EnabledKey(uint partId) => EnabledPrefix + partId;

    private static bool TryParse(string key, out string kind, out uint partId, out uint channel)
    {
        kind = ""; partId = channel = 0;
        var parts = key.Split(':');
        if (parts.Length == 3 && parts[0] == "sidetone" && parts[1] == "enabled" && uint.TryParse(parts[2], out partId))
        { kind = "enabled"; return true; }
        if (parts.Length == 4 && parts[0] == "sidetone" && parts[1] == "volume" && uint.TryParse(parts[2], out partId) && uint.TryParse(parts[3], out channel))
        { kind = "volume"; return true; }
        return false;
    }

    private static List<Part> FindMicrophonePath(MMDevice endpoint)
    {
        foreach (var root in ConnectedRoots(endpoint))
        {
            var path = new List<Part>();
            if (FindPath(root, MicrophoneSubtype, new HashSet<string>(StringComparer.Ordinal), path)) return path;
        }
        return [];
    }

    private static Part? FindPart(MMDevice endpoint, uint localId)
    {
        foreach (var root in ConnectedRoots(endpoint))
        {
            var found = FindPart(root, localId, new HashSet<string>(StringComparer.Ordinal));
            if (found != null) return found;
        }
        return null;
    }

    private static Dictionary<uint, Part> FindParts(MMDevice endpoint, IEnumerable<uint> localIds)
    {
        var wanted = localIds.ToHashSet();
        var found = new Dictionary<uint, Part>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in ConnectedRoots(endpoint)) CollectParts(root, wanted, visited, found);
        return found;
    }

    private static void CollectParts(Part part, HashSet<uint> wanted, HashSet<string> visited, Dictionary<uint, Part> found)
    {
        if (!visited.Add(part.GlobalId)) return;
        if (wanted.Contains(part.LocalId)) found[part.LocalId] = part;
        if (found.Count == wanted.Count) return;
        foreach (var next in Neighbors(part)) CollectParts(next, wanted, visited, found);
    }

    private static IEnumerable<Part> ConnectedRoots(MMDevice endpoint)
    {
        var topology = endpoint.DeviceTopology;
        for (uint i = 0; i < topology.ConnectorCount; i++)
        {
            var connector = topology.GetConnector(i);
            if (connector.IsConnected) yield return connector.ConnectedTo.Part;
        }
    }

    private static bool FindPath(Part part, Guid target, HashSet<string> visited, List<Part> path)
    {
        if (!visited.Add(part.GlobalId)) return false;
        path.Add(part);
        if (part.GetSubType == target) return true;
        foreach (var next in Neighbors(part))
            if (FindPath(next, target, visited, path)) return true;
        path.RemoveAt(path.Count - 1);
        return false;
    }

    private static Part? FindPart(Part part, uint localId, HashSet<string> visited)
    {
        if (!visited.Add(part.GlobalId)) return null;
        if (part.LocalId == localId) return part;
        foreach (var next in Neighbors(part))
        {
            var found = FindPart(next, localId, visited);
            if (found != null) return found;
        }
        return null;
    }

    private static IEnumerable<Part> Neighbors(Part part)
    {
        PartsList? incoming = null, outgoing = null;
        try { incoming = part.PartsIncoming; } catch { }
        if (incoming != null) for (uint i = 0; i < incoming.Count; i++) yield return incoming[i];
        try { outgoing = part.PartsOutgoing; } catch { }
        if (outgoing != null) for (uint i = 0; i < outgoing.Count; i++) yield return outgoing[i];
    }
}
