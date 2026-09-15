using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace HubTool;

public sealed class LightControls : IDisposable
{
    public const string DeviceId = "light:quiklight:1a86-fe07";
    private const int VendorId = 0x1A86;
    private const int ProductId = 0xFE07;
    private readonly object stateGate = new();
    private readonly SemaphoreSlim usbGate = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private Device? device;
    private SafeFileHandle? handle;
    private byte messageId = 1;
    private bool applyRequested = true;
    private bool dxWasRunning;

    public LightControls() => worker = Task.Run(RunAsync);

    public static Device? Discover(Device? saved)
    {
        if (HidPath() == null) return null;
        var values = saved?.Values != null ? new Dictionary<string, double>(saved.Values) : ReadDxLightDefaults();
        Default(values, "light:enabled", 1); Default(values, "light:sync", 0);
        Default(values, "light:red", 227); Default(values, "light:green", 0); Default(values, "light:blue", 255);
        Default(values, "light:brightness", 100); Default(values, "light:fps", 15);
        Default(values, "light:saturation", 125); Default(values, "light:smoothing", 55);
        var importedLedCount = values.Remove("light:led-count", out var configuredCount) ? configuredCount : 54;
        var result = new Device
        {
            Id = DeviceId, Name = "Luci dietro al monitor", ProductName = "DX Light / QuikLight",
            Kind = "Light", Manufacturer = "Robobloq", Driver = "USB HID 1A86:FE07", Connected = true,
            Restore = saved?.Restore ?? true, Overlay = saved?.Overlay ?? false, Values = values,
            Controls =
            [
                new("light:enabled", "Luci accese", 0, 1, 1, Toggle: true),
                new("light:sync", "Sincronizza con i colori dello schermo", 0, 1, 1, Toggle: true),
                new("light:red", "Rosso", 0, 255, 1), new("light:green", "Verde", 0, 255, 1), new("light:blue", "Blu", 0, 255, 1),
                new("light:brightness", "Luminosità", 0, 100, 1, "%"), new("light:fps", "Fluidità sync", 5, 30, 1, "fps"),
                new("light:saturation", "Intensità colori sync", 0, 200, 5, "%"), new("light:smoothing", "Morbidezza transizioni", 0, 90, 5, "%")
            ]
        };
        result.ControlMemory["light:led-count"] = saved?.ControlMemory.GetValueOrDefault("light:led-count", importedLedCount) ?? importedLedCount;
        return result;
    }

    public void Attach(Device? value)
    {
        lock (stateGate) { device = value; applyRequested = true; }
    }

    public Task SetAsync(Device target, string key, double value)
    {
        lock (stateGate)
        {
            target.Values[key] = value;
            device = target;
            applyRequested = true;
        }
        target.NotifyValues();
        return Task.CompletedTask;
    }

    public static bool IsDxLightRunning()
    {
        var processes = Process.GetProcessesByName("DX Light");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private async Task RunAsync()
    {
        ScreenSampler? sampler = null;
        var previous = new Rgb[54];
        var dxRunning = false;
        var nextDxCheck = DateTime.MinValue;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (DateTime.UtcNow >= nextDxCheck)
                {
                    dxRunning = IsDxLightRunning();
                    nextDxCheck = DateTime.UtcNow.AddMilliseconds(250);
                }
                if (dxRunning)
                {
                    dxWasRunning = true;
                    CloseHandle();
                    await Task.Delay(750, stop.Token);
                    continue;
                }
                if (dxWasRunning)
                {
                    dxWasRunning = false;
                    lock (stateGate) applyRequested = true;
                    await Task.Delay(350, stop.Token);
                }

                Device? current;
                Dictionary<string, double>? values;
                bool shouldApply;
                int ledCount;
                lock (stateGate)
                {
                    current = device;
                    values = current == null ? null : new(current.Values);
                    shouldApply = applyRequested;
                    applyRequested = false;
                    ledCount = (int)Math.Clamp(current?.ControlMemory.GetValueOrDefault("light:led-count", 54) ?? 54, 1, 120);
                }
                if (current == null || values == null || !current.Connected)
                {
                    CloseHandle();
                    await Task.Delay(750, stop.Token);
                    continue;
                }

                try
                {
                    var enabled = Get(values, "light:enabled", 1) != 0;
                    var sync = enabled && Get(values, "light:sync", 0) != 0;
                    if (shouldApply)
                    {
                        if (!enabled) await SendSimpleAsync(151, []);
                        else
                        {
                            // QuikLight's command uses attenuation: 0 is brightest and 100 is darkest.
                            // Hub exposes the conventional direction and converts only at the USB boundary.
                            await SendSimpleAsync(135, [QuikLightProtocol.Brightness(Get(values, "light:brightness", 100))]);
                            if (!sync) await SendStaticAsync(values, ledCount);
                        }
                        current.Error = "";
                    }
                    if (!sync)
                    {
                        sampler?.Dispose(); sampler = null;
                        await Task.Delay(400, stop.Token);
                        continue;
                    }

                    sampler ??= new ScreenSampler();
                    var colors = sampler.Capture(ledCount, Get(values, "light:saturation", 125) / 100d);
                    var smoothing = Math.Clamp(Get(values, "light:smoothing", 55) / 100d, 0, .9);
                    if (previous.Length != ledCount) previous = new Rgb[ledCount];
                    for (var i = 0; i < colors.Length; i++)
                    {
                        if (previous[i] != default && smoothing > 0) colors[i] = Rgb.Blend(previous[i], colors[i], 1 - smoothing);
                        previous[i] = colors[i];
                    }
                    await SendFrameAsync(colors);
                    current.Error = "";
                    var fps = Math.Clamp((int)Math.Round(Get(values, "light:fps", 15)), 5, 30);
                    await Task.Delay(1000 / fps, stop.Token);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    current.Error = "Controllo luci: " + ex.Message;
                    CloseHandle();
                    lock (stateGate) applyRequested = true;
                    await Task.Delay(1000, stop.Token);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { sampler?.Dispose(); CloseHandle(); }
    }

    private async Task SendStaticAsync(IReadOnlyDictionary<string, double> values, int ledCount)
    {
        var payload = new byte[] { 1, Byte(values, "light:red"), Byte(values, "light:green"), Byte(values, "light:blue"),
            (byte)ledCount, (byte)Math.Min(254, ledCount + 1), 0, 0, 0, 254 };
        await SendSimpleAsync(134, payload);
    }

    private async Task SendFrameAsync(Rgb[] colors)
    {
        await SendPacketAsync(QuikLightProtocol.Frame(NextId(), colors));
    }

    private async Task SendSimpleAsync(byte action, byte[] payload)
    {
        await SendPacketAsync(QuikLightProtocol.Simple(NextId(), action, payload));
    }

    private async Task SendPacketAsync(byte[] packet)
    {
        await usbGate.WaitAsync(stop.Token);
        try
        {
            EnsureOpen();
            for (var offset = 0; offset < packet.Length; offset += 64)
            {
                var report = new byte[65];
                Buffer.BlockCopy(packet, offset, report, 1, Math.Min(64, packet.Length - offset));
                if (!WriteFile(handle!, report, report.Length, out var written, IntPtr.Zero) || written != report.Length)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Scrittura USB non riuscita");
            }
        }
        finally { usbGate.Release(); }
    }

    private void EnsureOpen()
    {
        if (handle is { IsInvalid: false, IsClosed: false }) return;
        var path = HidPath() ?? throw new IOException("controller QuikLight non trovato");
        handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); handle = null; throw new System.ComponentModel.Win32Exception(error, "controller occupato; chiudi DX Light"); }
    }

    private void CloseHandle() { handle?.Dispose(); handle = null; }
    private byte NextId() { messageId++; if (messageId == 255) messageId = 1; return messageId; }
    private static byte Byte(IReadOnlyDictionary<string, double> values, string key) => (byte)Math.Clamp(Math.Round(Get(values, key, 0)), 0, 255);
    private static double Get(IReadOnlyDictionary<string, double> values, string key, double fallback) => values.TryGetValue(key, out var value) ? value : fallback;
    private static void Default(Dictionary<string, double> values, string key, double value) { if (!values.ContainsKey(key)) values[key] = value; }

    private static Dictionary<string, double> ReadDxLightDefaults()
    {
        var values = new Dictionary<string, double>();
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "quiklight-desktop", "config.json");
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var item = json.RootElement.GetProperty("devices").EnumerateArray().FirstOrDefault(d =>
                d.TryGetProperty("path", out var p) && p.GetString()?.Contains("VID_1A86&PID_FE07", StringComparison.OrdinalIgnoreCase) == true);
            if (item.ValueKind != JsonValueKind.Object) return values;
            if (item.TryGetProperty("isSwitchOn", out var on)) values["light:enabled"] = on.GetBoolean() ? 1 : 0;
            if (item.TryGetProperty("isSyncScreen", out var sync)) values["light:sync"] = sync.GetBoolean() ? 1 : 0;
            if (item.TryGetProperty("hue", out var hue) && ParseHex(hue.GetString(), out var color))
            { values["light:red"] = color.R; values["light:green"] = color.G; values["light:blue"] = color.B; }
            if (item.TryGetProperty("brightnessColor", out var brightness) && brightness.TryGetProperty("a", out var alpha))
                values["light:brightness"] = Math.Clamp(alpha.GetDouble() * 100, 0, 100);
            if (item.TryGetProperty("lampsAmount", out var count)) values["light:led-count"] = count.GetInt32();
        }
        catch { }
        return values;
    }

    private static bool ParseHex(string? value, out Rgb color)
    {
        color = default;
        if (value?.Length != 7 || value[0] != '#') return false;
        try { color = new(Convert.ToByte(value[1..3], 16), Convert.ToByte(value[3..5], 16), Convert.ToByte(value[5..7], 16)); return true; }
        catch { return false; }
    }

    public void Dispose()
    {
        stop.Cancel();
        try { worker.Wait(TimeSpan.FromSeconds(2)); } catch { }
        CloseHandle(); stop.Dispose(); usbGate.Dispose();
    }

    internal readonly record struct Rgb(byte R, byte G, byte B)
    {
        public static Rgb Blend(Rgb before, Rgb after, double amount) => new(
            (byte)Math.Clamp(Math.Round(before.R + (after.R - before.R) * amount), 0, 255),
            (byte)Math.Clamp(Math.Round(before.G + (after.G - before.G) * amount), 0, 255),
            (byte)Math.Clamp(Math.Round(before.B + (after.B - before.B) * amount), 0, 255));
    }

    private static string? HidPath()
    {
        HidD_GetHidGuid(out var hidGuid);
        var set = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) return null;
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref data)) break;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);
                var buffer = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, required, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
                    if (path != null && path.Contains("vid_1a86&pid_fe07&mi_00", StringComparison.OrdinalIgnoreCase)) return path;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)] private struct DeviceInterfaceData { public int Size; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref DeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterfaceData data, IntPtr detail, uint size, out uint required, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(SafeFileHandle file, byte[] buffer, int bytes, out int written, IntPtr overlapped);
}

internal static class QuikLightProtocol
{
    internal static byte Brightness(double hubPercent) => (byte)(100 - Math.Clamp(Math.Round(hubPercent), 0, 100));
    internal static byte[] Simple(byte messageId, byte action, byte[] payload)
    {
        var packet = new byte[6 + payload.Length];
        packet[0] = (byte)'R'; packet[1] = (byte)'B'; packet[2] = (byte)packet.Length; packet[3] = messageId; packet[4] = action;
        payload.CopyTo(packet, 5); packet[^1] = Checksum(packet); return packet;
    }

    internal static byte[] Frame(byte messageId, IReadOnlyList<LightControls.Rgb> colors)
    {
        var packet = new byte[7 + colors.Count * 5];
        packet[0] = (byte)'S'; packet[1] = (byte)'C'; packet[2] = (byte)(packet.Length >> 8); packet[3] = (byte)packet.Length;
        packet[4] = messageId; packet[5] = 128;
        for (var i = 0; i < colors.Count; i++)
        {
            var offset = 6 + i * 5; var index = (byte)(i + 1);
            packet[offset] = index; packet[offset + 1] = colors[i].R; packet[offset + 2] = colors[i].G;
            packet[offset + 3] = colors[i].B; packet[offset + 4] = index;
        }
        packet[^1] = Checksum(packet); return packet;
    }

    private static byte Checksum(byte[] packet) { var sum = 0; foreach (var value in packet) sum += value; return (byte)sum; }
}

internal sealed class ScreenSampler : IDisposable
{
    private const int Width = 160, Height = 90;
    private readonly IntPtr screenDc, memoryDc, bitmap, oldBitmap, bits;
    private readonly int[] pixels = new int[Width * Height];

    public ScreenSampler()
    {
        screenDc = GetDC(IntPtr.Zero); memoryDc = CreateCompatibleDC(screenDc);
        var info = new BitmapInfo { Header = new BitmapInfoHeader { Size = 40, Width = Width, Height = -Height, Planes = 1, BitCount = 32 } };
        bitmap = CreateDIBSection(memoryDc, ref info, 0, out bits, IntPtr.Zero, 0);
        if (screenDc == IntPtr.Zero || memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        oldBitmap = SelectObject(memoryDc, bitmap);
    }

    public LightControls.Rgb[] Capture(int ledCount, double saturation)
    {
        var sourceWidth = GetSystemMetrics(0); var sourceHeight = GetSystemMetrics(1);
        if (!StretchBlt(memoryDc, 0, 0, Width, Height, screenDc, 0, 0, sourceWidth, sourceHeight, 0x00CC0020))
            throw new System.ComponentModel.Win32Exception();
        Marshal.Copy(bits, pixels, 0, pixels.Length);
        var side = Math.Max(1, (int)Math.Round(ledCount * 9d / 34d));
        var top = Math.Max(1, ledCount - side * 2);
        var result = new LightControls.Rgb[ledCount];
        for (var i = 0; i < side; i++) result[i] = Average(Width - 8, i * Height / side, Width, (i + 1) * Height / side, saturation);
        Array.Reverse(result, 0, side);
        for (var i = 0; i < top; i++) result[side + i] = Average(i * Width / top, 0, (i + 1) * Width / top, 8, saturation);
        Array.Reverse(result, side, top);
        for (var i = 0; i < side; i++) result[side + top + i] = Average(0, i * Height / side, 8, (i + 1) * Height / side, saturation);
        return result;
    }

    private LightControls.Rgb Average(int x0, int y0, int x1, int y1, double saturation)
    {
        long red = 0, green = 0, blue = 0, count = 0;
        for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++)
        {
            var pixel = pixels[y * Width + x]; blue += pixel & 255; green += pixel >> 8 & 255; red += pixel >> 16 & 255; count++;
        }
        if (count == 0) return default;
        var r = red / (double)count; var g = green / (double)count; var b = blue / (double)count;
        var gray = r * .2126 + g * .7152 + b * .0722;
        r = gray + (r - gray) * saturation; g = gray + (g - gray) * saturation; b = gray + (b - gray) * saturation;
        return new((byte)Math.Clamp(Math.Round(r), 0, 255), (byte)Math.Clamp(Math.Round(g), 0, 255), (byte)Math.Clamp(Math.Round(b), 0, 255));
    }

    public void Dispose()
    {
        if (oldBitmap != IntPtr.Zero) SelectObject(memoryDc, oldBitmap);
        if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
        if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
    }

    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; public uint Colors; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dest, int xDest, int yDest, int wDest, int hDest, IntPtr source, int xSrc, int ySrc, int wSrc, int hSrc, uint operation);
}
