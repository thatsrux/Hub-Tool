using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel;

namespace HubTool;

public sealed class Device : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private float? volume;
    private bool muted;
    public void NotifyValues() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Values)));
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ContainerId { get; set; } = "";
    public string PhysicalId { get; set; } = "";
    public int AudioFormFactor { get; set; } = -1;
    [JsonIgnore] public string Category => PeripheralCatalog.Category(this);
    [JsonIgnore] public string TypeLabel => PeripheralCatalog.Icon(this) switch
    {
        "headphones" => "Cuffie", "speaker" => "Altoparlanti", "microphone" => "Microfono", "keyboard" => "Tastiera", "mouse" => "Mouse",
        "monitor" => "Monitor", "camera" => "Videocamera", "printer" => "Stampante", "scanner" => "Scanner", "drive" => "Memoria USB", "gamepad" => "Gamepad", _ => "Hub USB"
    };
    [JsonIgnore] public System.Windows.Media.Geometry IconGeometry => DeviceIcons.For(PeripheralCatalog.Icon(this));
    [JsonIgnore] public System.Windows.Media.Brush IconBrush => DeviceIcons.Accent(PeripheralCatalog.Icon(this));
    public bool Connected { get; set; }
    public bool Overlay { get; set; }
    public bool Restore { get; set; }
    public float? Volume { get => volume; set { if (volume == value) return; volume = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Volume))); } }
    public bool Muted { get => muted; set { if (muted == value) return; muted = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Muted))); } }
    public Dictionary<string, double> Values { get; set; } = new();
    public DeviceRequest? Pending { get; set; }
    public List<DeviceControl> Controls { get; set; } = new();
    public string Manufacturer { get; set; } = "";
    public string Driver { get; set; } = "";
    public string Error { get; set; } = "";
    [JsonIgnore] public string Status => (Connected ? "Collegato" : "Scollegato · in memoria") + (Pending != null ? " · impostazioni in attesa" : "");
}

public sealed class DeviceRequest
{
    public AudioSetting? Audio { get; set; }
    public Dictionary<string, double> Controls { get; set; } = new();
    public static DeviceRequest Snapshot(Device device) => new()
    {
        Audio = device.Volume.HasValue ? new AudioSetting { Volume = device.Volume.Value, Muted = device.Muted } : null,
        Controls = device.Values.Where(kv => kv.Key != "decibels").ToDictionary(kv => kv.Key, kv => kv.Value)
    };
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
    private string? lastSavedJson;

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
        if (Devices.Any(d => d.Controls.Any(c => c == null || string.IsNullOrEmpty(c.Id) || c.Label == null || !double.IsFinite(c.Min)
            || !double.IsFinite(c.Max) || !double.IsFinite(c.Step) || c.Max < c.Min || c.Step <= 0)
            || d.Controls.Select(c => c.Id).Distinct().Count() != d.Controls.Count))
            throw new JsonException("Descrittori dei controlli non validi");
        if (Devices.Any(d => d.Pending != null && (d.Pending.Controls == null || d.Pending.Controls.Values.Any(v => !double.IsFinite(v))
            || d.Pending.Audio is { } a && (!float.IsFinite(a.Volume) || a.Volume < 0 || a.Volume > 1))))
            throw new JsonException("Impostazioni in attesa non valide");
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
        var json = JsonSerializer.Serialize(this, JsonOptions);
        if (json == lastSavedJson && File.Exists(path)) return;
        File.WriteAllText(path + ".tmp", json);
        File.Move(path + ".tmp", path, true);
        lastSavedJson = json;
    }
}
