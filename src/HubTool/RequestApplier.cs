namespace HubTool;

/// <summary>Only successful fields are removed; driver failures never erase the requested target.</summary>
public static class RequestApplier
{
    public static async Task<List<string>> ApplyAsync(Device device, Action<AudioSetting> audioWriter,
        Func<string, double, Task> controlWriter)
    {
        var errors = new List<string>();
        var request = device.Pending;
        if (!device.Connected || request == null) return errors;
        if (request.Audio is { } audio)
        {
            try { audioWriter(audio); request.Audio = null; }
            catch (Exception ex) { errors.Add("Audio: " + ex.Message); }
        }
        // Auto mode follows the manual value, even after importing reordered JSON.
        foreach (var (key, value) in request.Controls.OrderBy(kv => kv.Key.EndsWith(":auto")).ToArray())
        {
            try { await controlWriter(key, value); request.Controls.Remove(key); }
            catch (Exception ex) { errors.Add(key + ": " + ex.Message); }
        }
        if (request.Audio == null && request.Controls.Count == 0) device.Pending = null;
        device.Error = errors.Count == 0 ? "" : "Applicazione incompleta · " + string.Join("; ", errors);
        return errors;
    }
}
