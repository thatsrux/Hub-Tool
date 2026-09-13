using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HubTool;

public sealed class Device
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool Connected { get; set; }
    public bool Overlay { get; set; }
    public bool Restore { get; set; }
    public float? Volume { get; set; }
    public bool Muted { get; set; }
    public Dictionary<string, double> Values { get; set; } = new();
    public List<DeviceControl> Controls { get; set; } = new();
    public string Manufacturer { get; set; } = "";
    public string Driver { get; set; } = "";
    public string Error { get; set; } = "";
    [JsonIgnore] public string Status => Connected ? "Collegato" : "Scollegato · in memoria";
}

public sealed record DeviceControl(string Id, string Label, double Min, double Max, double Step, string Unit = "", bool Toggle = false);
public sealed class AudioSetting { public float Volume { get; set; } public bool Muted { get; set; } }
public sealed class Profile
{
    public string Name { get; set; } = "";
    public Dictionary<string, AudioSetting> Audio { get; set; } = new();
    public Dictionary<string, Dictionary<string, double>> Controls { get; set; } = new();
    public override string ToString() => Name;
}

public sealed class Shortcut
{
    public string Gesture { get; set; } = "Ctrl+Alt+H";
    public string Action { get; set; } = "overlay";
    public string DeviceId { get; set; } = "";
    public string Control { get; set; } = "";
    public double Value { get; set; }
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Label { get; set; } = "Overlay";
    public override string ToString() => Gesture + "   →   " + Label;
}

public sealed class Settings
{
    public List<Device> Devices { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
    public List<Shortcut> Shortcuts { get; set; } = new() { new() };
    public bool OverlayEnabled { get; set; }
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    [JsonIgnore] public string RecoveryNotice { get; set; } = "";
    public static string? DataDirectoryOverride { get; set; }
    public static string Folder => DataDirectoryOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HubTool");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Settings Load()
    {
        var path = Path.Combine(Folder, "settings.json");
        if (!File.Exists(path)) return new();
        try
        {
            var state = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? throw new JsonException("Dati vuoti");
            state.Validate();
            return state;
        }
        catch (JsonException)
        {
            var backup = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
            File.Copy(path, backup);
            return new() { RecoveryNotice = "Preferenze non leggibili. Copia conservata in " + backup };
        }
    }

    public void Validate()
    {
        if (Devices == null || Profiles == null || Shortcuts == null) throw new JsonException("Collezioni mancanti");
        if (Devices.Any(d => d == null || string.IsNullOrEmpty(d.Id) || d.Name == null || d.Kind == null || d.Values == null || d.Controls == null
            || d.Values.Any(v => !double.IsFinite(v.Value)) || d.Volume is float volume && (!float.IsFinite(volume) || volume < 0 || volume > 1)))
            throw new JsonException("Dispositivo non valido");
        if (Devices.Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Devices.Count)
            throw new JsonException("ID duplicati");
        if (Profiles.Any(p => p == null || p.Name == null || p.Audio == null || p.Controls == null || p.Audio.Values.Any(v => v == null || !float.IsFinite(v.Volume) || v.Volume < 0 || v.Volume > 1)
            || p.Controls.Values.Any(v => v == null || v.Values.Any(n => !double.IsFinite(n)))))
            throw new JsonException("Profilo non valido");
        if (Shortcuts.Any(s => s == null || s.Gesture == null || s.Action == null || !double.IsFinite(s.Value))) throw new JsonException("Shortcut non valida");
        if (OverlayLeft.HasValue && !double.IsFinite(OverlayLeft.Value) || OverlayTop.HasValue && !double.IsFinite(OverlayTop.Value)) throw new JsonException("Posizione overlay non valida");
    }

    public void Save()
    {
        Validate();
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, JsonOptions));
        File.Move(path + ".tmp", path, true);
    }
}
