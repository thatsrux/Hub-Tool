using HubTool;
using NAudio.CoreAudioApi;
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
    && PeripheralCatalog.Icon(new Device { Kind = "Keyboard" }) == "keyboard"
    && PeripheralCatalog.Icon(new Device { Kind = "Light" }) == "light", "Headphones, microphone, keyboard and lights get distinct icons");
var lightPacket = QuikLightProtocol.Simple(2, 135, [75]);
var lightFrame = QuikLightProtocol.Frame(3, Enumerable.Repeat(new LightControls.Rgb(10, 20, 30), 54).ToArray());
Check(lightPacket.SequenceEqual(new byte[] { 82, 66, 7, 2, 135, 75, 111 }) && lightFrame.Length == 277
    && lightFrame[0] == 83 && lightFrame[1] == 67 && lightFrame[5] == 128 && lightFrame[6] == 1 && lightFrame[275] == 54,
    "QuikLight commands use the controller's RB/SC framing and all 54 LED zones");
Check(QuikLightProtocol.Brightness(0) == 100 && QuikLightProtocol.Brightness(25) == 75 && QuikLightProtocol.Brightness(100) == 0,
    "QuikLight attenuation is inverted at the USB boundary so Hub brightness follows the conventional direction");
Check(!PeripheralCatalog.IsPeripheral(new Device { Id = "input:Keyboard:generic", Name = "Tastiera HID", Kind = "Keyboard" })
    && !PeripheralCatalog.IsPeripheral(new Device { Id = "ROOT\\MOUSE\\0000", Name = "HID-compliant mouse", Kind = "Mouse" })
    && PeripheralCatalog.IsPeripheral(new Device { Id = InputControls.KeyboardId, Name = "Tastiera", Kind = "Keyboard" }),
    "Generic HID interfaces are replaced by canonical Windows input devices");
Check(!PeripheralCatalog.IsPeripheral(new Device { Id = "camera:@device:sw:{category}\\{obs}", Name = "OBS Virtual Camera", Kind = "Camera" })
    && !PeripheralCatalog.IsPeripheral(new Device { Id = "SWD\\PRINTENUM\\PDF", Name = "Microsoft Print to PDF", Kind = "PrintQueue" })
    && !PeripheralCatalog.IsPeripheral(new Device { Id = "SWD\\PRINTENUM\\ONENOTE", Name = "OneNote (Desktop) - Protetto", Kind = "PrintQueue" })
    && PeripheralCatalog.IsPeripheral(new Device { Id = "SWD\\PRINTENUM\\BROTHER", Name = "Brother DCP-L2620DW Printer", Kind = "PrintQueue" }),
    "Virtual cameras and software print queues are excluded while physical printers remain");
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
var toggleDevice = new Device
{
    Controls = [new("sidetone:enabled:11", "Eco microfono attivo", 0, 1, 1, Toggle: true),
        new("binary", "Interruttore driver", 0, 1, 1), new("level", "Livello", 0, 100, 1)],
    Values = new() { ["sidetone:enabled:11"] = 1, ["level"] = 30 }
};
Check(MainWindow.CanToggle(toggleDevice.Controls[0]) && MainWindow.CanToggle(toggleDevice.Controls[1]) && !MainWindow.CanToggle(toggleDevice.Controls[2])
    && MainWindow.ToggleControlValue(toggleDevice, "sidetone:enabled:11") == 0,
    "Binary controls can be toggled by shortcuts");
toggleDevice.Values["sidetone:enabled:11"] = 0;
Check(MainWindow.ToggleControlValue(toggleDevice, "sidetone:enabled:11") == 1,
    "Control shortcuts toggle from off back to on");
Check(AudioTopologyControls.RequiresVolumeFallback("Altoparlanti (fifine Microphone)")
    && !AudioTopologyControls.RequiresVolumeFallback("Altoparlanti Realtek"), "Fifine sidetone bypasses its ineffective mute node");
Check(StartupManager.BuildCommand(@"C:\Program Files\Hub Tool\HubTool.exe") == "\"C:\\Program Files\\Hub Tool\\HubTool.exe\" --startup",
    "Windows startup command quotes the executable and uses silent startup mode");
var audioMigration = new Settings
{
    Devices = [new Device { Id = "audio:old", Name = "Headset", Kind = "Uscita audio", AudioFormFactor = 1 }],
    Shortcuts = [new Shortcut { DeviceId = "audio:old", Action = "toggle-control", Control = "sidetone:enabled:1" }]
};
DeviceService.ReconcileAudioEndpointIds(audioMigration,
    [
        new Device { Id = "audio:new", Name = "Headset", Kind = "Uscita audio", AudioFormFactor = 1, PhysicalId = "USB\\VID_1234" },
        new Device { Id = "audio:mic", Name = "Microphone", Kind = "Microfono", AudioFormFactor = 4, PhysicalId = "USB\\VID_1234" }
    ]);
Check(audioMigration.Devices.Single().Id == "audio:new" && audioMigration.Shortcuts.Single().DeviceId == "audio:new",
    "Audio endpoint ID changes preserve device settings and shortcuts");
var duplicateAudio = new Settings
{
    Devices =
    [
        new Device { Id = "audio:stale", Name = "Headset", Kind = "Uscita audio", AudioFormFactor = 1 },
        new Device { Id = "audio:active", Name = "Headset", Kind = "Uscita audio", AudioFormFactor = 1 }
    ],
    Shortcuts = [new Shortcut { DeviceId = "audio:stale", Action = "toggle-control", Control = "sidetone:enabled:1" }]
};
DeviceService.ReconcileAudioEndpointIds(duplicateAudio,
    [new Device { Id = "audio:active", Name = "Headset", Kind = "Uscita audio", AudioFormFactor = 1, PhysicalId = "USB\\VID_1234" }]);
Check(duplicateAudio.Devices.Count == 1 && duplicateAudio.Shortcuts.Single().DeviceId == "audio:active",
    "Stale audio duplicates no longer leave shortcuts bound to a disconnected endpoint");
Check(CameraControls.PreferredDefaultFlags(3) == 1 && CameraControls.PreferredDefaultFlags(2) == 2,
    "Camera reset prefers automatic mode and falls back to manual mode");
Check(MainWindow.SnapSliderValue(7.6, -10, 10, 3) == 8 && MainWindow.SnapSliderValue(99.9, 0, 90, 5) == 90,
    "Slider values snap from their real minimum and stay inside driver bounds");
var smartCamera = new Device { Id = "camera:test", Kind = "Camera",
    Controls = [new("video:0", "Brightness", 0, 100, 1), new("video:1", "Contrast", 0, 100, 1), new("video:3", "Saturation", 0, 100, 5),
        new("camera:4:auto", "Exposure auto", 0, 1, 1, Toggle: true)],
    Values = new() { ["video:0"] = 45, ["video:1"] = 45, ["video:3"] = 40, ["camera:4:auto"] = 0 } };
var enhancement = CameraControls.RecommendEnhancement(smartCamera, new CameraFrameAnalysis(62, 24, .3, .01, 60, 64, 70));
Check(enhancement["camera:4:auto"] == 1 && enhancement["video:0"] > 45 && enhancement["video:1"] > 45
    && (enhancement["video:3"] % 5) == 0, "Camera enhancement uses frame statistics and snaps every recommendation to driver steps");
var grouped = PeripheralCatalog.Prepare([
    new Device { Id = "HID\\ONE", Kind = "Keyboard", Name = "Keyboard", ContainerId = "same" },
    new Device { Id = "HID\\TWO", Kind = "Keyboard", Name = "Keyboard", ContainerId = "same" },
    new Device { Id = "windows:keyboard", Kind = "Keyboard", Values = new() { ["repeat-speed"] = 20 }, Controls = [new("repeat-speed", "Repeat", 0, 31, 1)] },
    new Device { Id = "DISPLAY\\ABC\\123", Kind = "Monitor", Name = "Monitor" },
    new Device { Id = "monitor:\\\\?\\DISPLAY#ABC#123#{guid}", Kind = "Monitor", Name = "Monitor" }
]);
Check(grouped.Count(PeripheralCatalog.IsVisible) == 2 && grouped.Single(d => d.Id == InputControls.KeyboardId).Controls.Count == 1,
    "Generic input interfaces and PnP monitor duplicates are removed");
var printerDevices = PeripheralCatalog.Prepare([
    new Device { Id = "SWD\\PRINTENUM\\BROTHER", Name = "Brother Printer", Kind = "PrintQueue", ContainerId = "printer-one" },
    new Device { Id = "SWD\\ESCL\\BROTHER", Name = "Brother Scanner", Kind = "Image", ContainerId = "printer-one" },
    new Device { Id = "SWD\\DAFWSDPROVIDER\\BROTHER", Name = "Brother Scanner [network]", ProductName = "Brother Scanner", Kind = "Image", ContainerId = "printer-one" },
    new Device { Id = "SWD\\PRINTENUM\\PDF", Name = "Microsoft Print to PDF", Kind = "PrintQueue" }
]);
Check(printerDevices.Count == 2 && printerDevices.Count(d => d.Kind == "Image") == 1 && printerDevices.Any(d => d.Kind == "PrintQueue"),
    "Physical multifunction printer is retained with one scanner interface");
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
var forgotten = new Device { Id = "usb:forgotten", Name = "Forgotten", Kind = "USB", Connected = true };
reloaded.Devices.Add(forgotten);
using (var forgetting = new DeviceService(reloaded, controlLights: false))
{
    forgetting.ForgetDevice(forgotten);
    Check(!reloaded.Devices.Contains(forgotten) && reloaded.HiddenDeviceIds.Contains("usb:forgotten"), "Forgetting removes and persistently hides a device");
    forgetting.RestoreForgottenDevices();
    Check(reloaded.HiddenDeviceIds.Count == 0, "Forgotten devices can be restored");
    var permanent = new Device { Id = "usb:permanent", PhysicalId = "container:permanent", Name = "Unused", Kind = "USB", Connected = true };
    reloaded.Devices.Add(permanent); reloaded.Shortcuts.Add(new() { DeviceId = permanent.Id, Action = "set-control" });
    forgetting.RemoveDevicePermanently(permanent);
    Check(!reloaded.Devices.Contains(permanent) && reloaded.RemovedDeviceIds.Contains(permanent.Id)
        && reloaded.RemovedDeviceIds.Contains(permanent.PhysicalId) && reloaded.Shortcuts.All(s => s.DeviceId != permanent.Id),
        "Permanent removal blocks both device identifiers and removes its shortcuts");
}
using var service = new DeviceService(reloaded);
Check(!reloaded.Devices.Single().Connected, "Startup invalidates stale connection state");
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

File.WriteAllText(Path.Combine(Settings.Folder, "settings.json"), "{broken");
Check(Settings.Load().Devices.Count == 0 && Directory.GetFiles(Settings.Folder, "*.corrupt-*").Length == 1,
    "Corrupt JSON is retained for recovery");

if (args.Contains("--hardware-read"))
{
    var input = InputControls.Enumerate();
    Check(input.Count == 2 && input.All(d => d.Controls.Count > 0), "Windows exposes input preference controls");
    var liveState = new Settings();
    using var live = new DeviceService(liveState, controlLights: false);
    await live.RefreshAsync();
    Check(liveState.Devices.Any(d => d.Connected), "Real SetupAPI inventory is populated");
    Check(liveState.Devices.Any(d => d.Id == LightControls.DeviceId && d.Connected && d.Controls.Count >= 9),
        "DX Light monitor LEDs are detected as a configurable device");
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
            var volume = sidetoneControls.Single(c => c.Id.StartsWith("sidetone:volume:", StringComparison.Ordinal));
            using var audioEnumerator = new MMDeviceEnumerator();
            using var endpoint = audioEnumerator.GetDevice(fifineOutput.Id[6..]);
            var originalRawVolume = AudioTopologyControls.ReadRawVolumeForDiagnostics(endpoint, volume.Id);
            var testVolume = originalRawVolume > 1 ? originalRawVolume : 60;
            await live.SetControlAsync(fifineOutput, volume.Id, testVolume);
            var original = fifineOutput.Values[enabled.Id];
            await live.SetControlAsync(fifineOutput, enabled.Id, 0);
            Check(AudioTopologyControls.ReadRawVolumeForDiagnostics(endpoint, volume.Id) <= 1,
                "Fifine sidetone disable changes the physical Windows value");
            await live.SetControlAsync(fifineOutput, enabled.Id, 1);
            Check(Math.Abs(AudioTopologyControls.ReadRawVolumeForDiagnostics(endpoint, volume.Id) - testVolume) <= 2,
                "Fifine sidetone enable restores the physical Windows value");
            await live.SetControlAsync(fifineOutput, volume.Id, originalRawVolume);
            Check(!AudioTopologyControls.IsFallbackActiveForDiagnostics(fifineOutput),
                "Fifine sidetone restore clears the volume fallback");
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(new { Devices = liveState.Devices.Count,
        Audio = liveState.Devices.Count(d => d.Volume.HasValue),
        Sidetone = liveState.Devices.Count(d => d.Controls.Any(c => AudioTopologyControls.IsSidetone(c.Id))),
        MonitorsWithControls = liveState.Devices.Count(d => d.Id.StartsWith("monitor:") && d.Controls.Count > 0),
        VisiblePeripherals = liveState.Devices.Count(PeripheralCatalog.IsVisible), Errors = live.Errors }));
}
if (args.Contains("--light-write"))
{
    var light = LightControls.Discover(null) ?? throw new Exception("FAIL: QuikLight controller not found");
    light.Values["light:sync"] = 0;
    using var controller = new LightControls();
    controller.Attach(light);
    await controller.SetAsync(light, "light:brightness", light.Values["light:brightness"]);
    await Task.Delay(1800);
    Check(light.Error.Length == 0, "QuikLight accepts a no-change brightness and static-color write");
}
Console.WriteLine($"{passed} checks passed");
