using System.IO;
using System.Text.Json;

namespace HubTool;

public static class ProfileTransfer
{
    public sealed class Document
    {
        public int Version { get; set; } = 1;
        public Profile Profile { get; set; } = new();
        public List<Device> Devices { get; set; } = new();
    }

    public static void Export(Settings state, Profile profile, string path)
    {
        var ids = profile.Audio.Keys.Union(profile.Controls.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var document = new Document { Profile = profile, Devices = state.Devices.Where(d => ids.Contains(d.Id)).Select(d => new Device
        {
            Id = d.Id, Name = d.Name, Kind = d.Kind, Controls = d.Controls.ToList()
        }).ToList() };
        File.WriteAllText(path, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static Profile Import(Settings state, string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Profilo troppo grande (massimo 4 MiB)");
        var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path)) ?? throw new InvalidDataException("File vuoto");
        if (document.Version != 1 || document.Profile == null || document.Devices == null)
            throw new InvalidDataException("Formato profilo non supportato");
        var candidate = new Settings { Devices = document.Devices, Profiles = [document.Profile], Shortcuts = [] };
        candidate.Validate();
        foreach (var id in document.Profile.Audio.Keys.Union(document.Profile.Controls.Keys))
            if (!state.Devices.Any(d => StringComparer.OrdinalIgnoreCase.Equals(d.Id, id)) && !document.Devices.Any(d => StringComparer.OrdinalIgnoreCase.Equals(d.Id, id)))
                throw new InvalidDataException("Manca la descrizione del dispositivo " + id);
        var baseName = string.IsNullOrWhiteSpace(document.Profile.Name) ? "Profilo importato" : document.Profile.Name.Trim();
        document.Profile.Name = baseName;
        for (int suffix = 2; state.Profiles.Any(p => p.Name.Equals(document.Profile.Name, StringComparison.OrdinalIgnoreCase)); suffix++)
            document.Profile.Name = baseName + " (" + suffix + ")";
        // Import stores configuration only: it never applies it or imports executable shortcuts.
        foreach (var incoming in document.Devices)
        {
            if (state.Devices.Any(d => StringComparer.OrdinalIgnoreCase.Equals(d.Id, incoming.Id))) continue;
            state.Devices.Add(new Device { Id = incoming.Id, Name = incoming.Name, Kind = incoming.Kind, Controls = incoming.Controls });
        }
        state.Profiles.Add(document.Profile);
        return document.Profile;
    }
}
