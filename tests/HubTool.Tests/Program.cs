using HubTool;
using System.Text.Json;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message); passed++;
}

Settings.DataDirectoryOverride = Path.Combine(Path.GetTempPath(), "HubTool.Tests-" + Guid.NewGuid().ToString("N"));
Check(!PeripheralCatalog.IsNativePeripheral("Processor", "ACPI\\CPU0", "CPU")
    && !PeripheralCatalog.IsNativePeripheral("System", "PCI\\BUS", "PCI bus")
    && !PeripheralCatalog.IsNativePeripheral("USB", "PCI\\USB", "USB host controller"), "Inventory excludes CPU, buses and host controllers");
Check(PeripheralCatalog.IsNativePeripheral("USB", "USB\\VID_1234&PID_5678", "Generic USB Hub")
    && !PeripheralCatalog.IsNativePeripheral("USB", "USB\\ROOT_HUB30", "USB Root Hub"), "External USB hubs are retained while root hubs are excluded");
Check(PeripheralCatalog.Icon(new Device { Kind = "Uscita audio", AudioFormFactor = 3 }) == "headphones"
    && PeripheralCatalog.Icon(new Device { Kind = "Microfono" }) == "microphone"
    && PeripheralCatalog.Icon(new Device { Kind = "Keyboard" }) == "keyboard", "Headphones, microphone and keyboard get distinct icons");
Check(Math.Abs(AudioTopologyControls.LevelToPercent(12, 0, 16) - 75) < .001
    && Math.Abs(AudioTopologyControls.PercentToLevel(63, 0, 16) - 10.08) < .001
    && AudioTopologyControls.IsSidetone("sidetone:enabled:12"), "Sidetone values map safely between driver levels and UI percent");
var fallbackDisplay = new Device
{
    Controls = [new("sidetone:volume:10:0", "Volume", 0, 100, 1), new("sidetone:enabled:11", "Enabled", 0, 1, 1, Toggle: true)],
    Values = new() { ["sidetone:volume:10:0"] = 0, ["sidetone:enabled:11"] = 1 },
    ControlMemory = new() { ["sidetone:fallback-active"] = 1, ["sidetone:volume:10:0"] = 64 }
};
AudioTopologyControls.RestoreDisplayState(fallbackDisplay);
Check(fallbackDisplay.Values["sidetone:volume:10:0"] == 64 && fallbackDisplay.Values["sidetone:enabled:11"] == 0,
    "Sidetone fallback preserves volume while showing the disabled state");
var grouped = PeripheralCatalog.Prepare([
    new Device { Id = "HID\\ONE", Kind = "Keyboard", Name = "Keyboard", ContainerId = "same" },
    new Device { Id = "HID\\TWO", Kind = "Keyboard", Name = "Keyboard", ContainerId = "same" },
    new Device { Id = "windows:keyboard", Kind = "Keyboard", Values = new() { ["repeat-speed"] = 20 }, Controls = [new("repeat-speed", "Repeat", 0, 31, 1)] },
    new Device { Id = "DISPLAY\\ABC\\123", Kind = "Monitor", Name = "Monitor" },
    new Device { Id = "monitor:\\\\?\\DISPLAY#ABC#123#{guid}", Kind = "Monitor", Name = "Monitor" }
]);
Check(grouped.Count(PeripheralCatalog.IsVisible) == 2 && grouped.Single(d => d.Id.StartsWith("input:")).Controls.Count == 1,
    "Input interfaces are grouped and PnP monitor duplicates are removed");
var audio = new Device { Id = "audio:one", Name = "Headset", Connected = true, Volume = .3f, Overlay = true, Restore = true,
    Values = new() { ["channel:0"] = 30 }, Controls = [new("channel:0", "Channel", 0, 100, 1)] };
var devices = new List<Device> { audio };
var restore = DeviceInventory.Merge(devices, []);
Check(devices.Count == 1 && !audio.Connected && audio.Volume == .3f, "Disconnect preserves device and volume");
Check(audio.Values["channel:0"] == 30 && audio.Overlay && restore.Count == 0, "Disconnect preserves channel values and overlay preference");
restore = DeviceInventory.Merge(devices, [new Device { Id = "AUDIO:ONE", Name = "Headset", Connected = true, Volume = .8f,
    Controls = [new("channel:0", "Channel", 0, 100, 1)], Values = new() { ["channel:0"] = 80 } }]);
Check(devices.Count == 1 && restore.Single() == audio && audio.Pending?.Audio?.Volume == .3f && audio.Volume == .8f,
    "Reconnection separates the saved target from observed hardware state");
await RequestApplier.ApplyAsync(audio, target => audio.Volume = target.Volume, (key, value) => { audio.Values[key] = value; return Task.CompletedTask; });
restore = DeviceInventory.Merge(devices, [new Device { Id = "audio:one", Connected = true, Volume = .6f }]);
Check(restore.Count == 0 && audio.Volume == .6f, "Ordinary refresh does not repeatedly enforce restoration");

var state = new Settings { Devices = devices };
state.Save();
var reloaded = Settings.Load();
Check(reloaded.Devices.Single().Overlay && reloaded.Devices.Single().Volume == .6f, "Settings round trip preserves preferences");
using var service = new DeviceService(reloaded);
Check(!reloaded.Devices.Single().Connected, "Startup invalidates stale connection state");
var profile = service.Capture("Work");
reloaded.Devices.Single().Volume = .9f;
Check(profile.Audio["audio:one"].Volume == .6f, "Captured profile is independent of live state");
var failures = await service.ApplyAsync(profile);
Check(failures.Count == 0 && reloaded.Devices.Single().Volume == .6f && reloaded.Devices.Single().Restore,
    "Applying profile to absent device queues restoration without COM writes");
bool rejected = false;
try { service.SetAudio(reloaded.Devices.Single(), float.NaN, false); } catch (ArgumentOutOfRangeException) { rejected = true; }
Check(rejected, "Rejects NaN audio before persistence");
rejected = false;
try { await service.SetControlAsync(reloaded.Devices.Single(), "unknown", 1); } catch (NotSupportedException) { rejected = true; }
Check(rejected, "Rejects unknown controls");

var retryDevice = new Device { Id = "monitor:retry", Connected = true, Values = new() { ["brightness"] = 20, ["contrast"] = 30 },
    Pending = new DeviceRequest { Controls = new() { ["brightness"] = 70, ["contrast"] = 60 } } };
var applied = new List<string>();
var partialErrors = await RequestApplier.ApplyAsync(retryDevice, _ => { }, (key, value) =>
{
    if (key == "brightness") throw new IOException("Driver temporarily unavailable");
    applied.Add(key); retryDevice.Values[key] = value; return Task.CompletedTask;
});
Check(partialErrors.Count == 1 && retryDevice.Pending!.Controls.Count == 1 && retryDevice.Pending.Controls["brightness"] == 70,
    "Partial failure retains only the failed requested field");
var pendingState = new Settings { Devices = [retryDevice] }; pendingState.Save();
var pendingReload = Settings.Load().Devices.Single();
var retryList = DeviceInventory.Merge([pendingReload], [new Device { Id = "monitor:retry", Connected = true, Values = new() { ["brightness"] = 20 } }]);
Check(retryList.Count == 1 && pendingReload.Pending!.Controls["brightness"] == 70 && pendingReload.Values["brightness"] == 20,
    "Reload and refresh preserve a failed target alongside current value");
await RequestApplier.ApplyAsync(pendingReload, _ => { }, (key, value) => { applied.Add(key); return Task.CompletedTask; });
Check(pendingReload.Pending == null && applied.SequenceEqual(new[] { "contrast", "brightness" }), "Retry skips already successful fields");

var transferState = new Settings { Devices = [new Device { Id = "audio:export", Name = "Exported headset", Kind = "Uscita audio", Volume = .2f }] };
transferState.Profiles.Add(new Profile { Name = "Travel", Audio = new() { ["audio:export"] = new AudioSetting { Volume = .2f, Muted = true } } });
var profilePath = Path.Combine(Settings.Folder, "test.hubprofile");
ProfileTransfer.Export(transferState, transferState.Profiles.Single(), profilePath);
var importState = new Settings();
var imported = ProfileTransfer.Import(importState, profilePath);
Check(imported.Audio["audio:export"].Muted && !importState.Devices.Single().Connected && !importState.Devices.Single().Restore && importState.Devices.Single().Pending == null,
    "Profile import preserves data without applying device settings");
Check(ProfileTransfer.Import(importState, profilePath).Name == "Travel (2)", "Import gives duplicate profile a distinct name");
var invalidPath = Path.Combine(Settings.Folder, "invalid.hubprofile");
File.WriteAllText(invalidPath, "{\"Version\":1,\"Profile\":{\"Name\":\"Bad\",\"Audio\":{\"missing\":{\"Volume\":2}}},\"Devices\":[]}");
rejected = false;
try { ProfileTransfer.Import(importState, invalidPath); } catch (JsonException) { rejected = true; }
Check(rejected && importState.Profiles.Count == 2, "Invalid import is rejected before mutating existing profiles");

File.WriteAllText(Path.Combine(Settings.Folder, "settings.json"), "{broken");
Check(Settings.Load().Devices.Count == 0 && Directory.GetFiles(Settings.Folder, "*.corrupt-*").Length == 1,
    "Corrupt JSON is retained for recovery");

if (args.Contains("--hardware-read"))
{
    var input = InputControls.Enumerate();
    Check(input.Count == 2 && input.All(d => d.Controls.Count > 0), "Windows exposes input preference controls");
    var liveState = new Settings();
    using var live = new DeviceService(liveState);
    await live.RefreshAsync();
    Check(liveState.Devices.Any(d => d.Connected), "Real SetupAPI inventory is populated");
    var fifineOutput = liveState.Devices.FirstOrDefault(d => d.Connected && d.Kind == "Uscita audio" && d.Name.Contains("fifine", StringComparison.OrdinalIgnoreCase));
    if (fifineOutput != null)
    {
        var sidetoneControls = fifineOutput.Controls.Where(c => AudioTopologyControls.IsSidetone(c.Id)).ToList();
        Check(sidetoneControls.Count >= 2, "Fifine playback topology exposes microphone sidetone controls");
        foreach (var control in sidetoneControls) await live.SetControlAsync(fifineOutput, control.Id, fifineOutput.Values[control.Id]);
        Check(sidetoneControls.All(c => fifineOutput.Values.ContainsKey(c.Id)), "Sidetone controls accept a no-change hardware write");
        if (args.Contains("--sidetone-toggle"))
        {
            var enabled = sidetoneControls.Single(c => c.Id.StartsWith("sidetone:enabled:", StringComparison.Ordinal));
            var original = fifineOutput.Values[enabled.Id];
            await live.SetControlAsync(fifineOutput, enabled.Id, original == 0 ? 1 : 0);
            Check(fifineOutput.Values[enabled.Id] != original, "Sidetone toggle changes the effective hardware state");
            await live.SetControlAsync(fifineOutput, enabled.Id, original);
            Check(fifineOutput.Values[enabled.Id] == original, "Sidetone toggle restores its initial hardware state");
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(new { Devices = liveState.Devices.Count,
        Audio = liveState.Devices.Count(d => d.Volume.HasValue),
        Sidetone = liveState.Devices.Count(d => d.Controls.Any(c => AudioTopologyControls.IsSidetone(c.Id))),
        MonitorsWithControls = liveState.Devices.Count(d => d.Id.StartsWith("monitor:") && d.Controls.Count > 0),
        VisiblePeripherals = liveState.Devices.Count(PeripheralCatalog.IsVisible), Errors = live.Errors }));
}
Console.WriteLine($"{passed} checks passed");
