using HubTool;
using System.Text.Json;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message); passed++;
}

Settings.DataDirectoryOverride = Path.Combine(Path.GetTempPath(), "HubTool.Tests-" + Guid.NewGuid().ToString("N"));
var audio = new Device { Id = "audio:one", Name = "Headset", Connected = true, Volume = .3f, Overlay = true, Restore = true,
    Values = new() { ["channel:0"] = 30 }, Controls = [new("channel:0", "Channel", 0, 100, 1)] };
var devices = new List<Device> { audio };
var restore = DeviceInventory.Merge(devices, []);
Check(devices.Count == 1 && !audio.Connected && audio.Volume == .3f, "Disconnect preserves device and volume");
Check(audio.Values["channel:0"] == 30 && audio.Overlay && restore.Count == 0, "Disconnect preserves channel values and overlay preference");
restore = DeviceInventory.Merge(devices, [new Device { Id = "AUDIO:ONE", Name = "Headset", Connected = true, Volume = .8f,
    Controls = [new("channel:0", "Channel", 0, 100, 1)], Values = new() { ["channel:0"] = 80 } }]);
Check(devices.Count == 1 && restore.Single() == audio && audio.Volume == .3f, "Reconnection uses case-insensitive identity and saved target");
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
    Console.WriteLine(JsonSerializer.Serialize(new { Devices = liveState.Devices.Count,
        Audio = liveState.Devices.Count(d => d.Volume.HasValue),
        MonitorsWithControls = liveState.Devices.Count(d => d.Id.StartsWith("monitor:") && d.Controls.Count > 0),
        Errors = live.Errors }));
}
Console.WriteLine($"{passed} checks passed");
