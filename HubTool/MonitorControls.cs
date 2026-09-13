using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HubTool;

/// <summary>Only exposes brightness/contrast when the monitor answers the native capability read.</summary>
public static class MonitorControls
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice display, uint flags);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll")] private static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint value, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool SetMonitorBrightness(IntPtr monitor, uint value);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetMonitorContrast(IntPtr monitor, out uint min, out uint value, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)] private static extern bool SetMonitorContrast(IntPtr monitor, uint value);

    private static void Visit(Action<string, PhysicalMonitor> visit)
    {
        // Do not throw across an unmanaged callback boundary.
        Exception? failure = null;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            try
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
                if (!GetMonitorInfo(monitor, ref info)) return true;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0) return true;
                var physical = new PhysicalMonitor[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical)) return true;
                try
                {
                    for (uint i = 0; i < count; i++)
                    {
                        var display = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
                        if (EnumDisplayDevices(info.Device, i, ref display, 1) && !string.IsNullOrEmpty(display.Id))
                            visit("monitor:" + display.Id, physical[i]);
                    }
                }
                finally { DestroyPhysicalMonitors(count, physical); }
            }
            catch (Exception ex) { failure = ex; return false; }
            return true;
        }, IntPtr.Zero);
        if (failure != null) throw failure;
    }

    public static List<Device> Enumerate()
    {
        var result = new List<Device>();
        Visit((id, physical) =>
        {
            var device = new Device { Id = id, Name = physical.Description, Kind = "Monitor", Connected = true };
            if (GetMonitorBrightness(physical.Handle, out var min, out var value, out var max) && max > min)
            {
                device.Controls.Add(new("brightness", "Luminosità DDC/CI", min, max, 1));
                device.Values["brightness"] = value;
            }
            if (GetMonitorContrast(physical.Handle, out min, out value, out max) && max > min)
            {
                device.Controls.Add(new("contrast", "Contrasto DDC/CI", min, max, 1));
                device.Values["contrast"] = value;
            }
            result.Add(device);
        });
        return result;
    }

    public static void Set(string id, string control, double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
        bool found = false;
        Visit((key, physical) =>
        {
            if (key != id) return;
            found = true;
            uint min, current, max;
            bool readable = control switch
            {
                "brightness" => GetMonitorBrightness(physical.Handle, out min, out current, out max),
                "contrast" => GetMonitorContrast(physical.Handle, out min, out current, out max),
                _ => throw new NotSupportedException(control)
            };
            if (!readable) throw new Win32Exception();
            if (value < min || value > max) throw new ArgumentOutOfRangeException(nameof(value));
            if (!(control == "brightness" ? SetMonitorBrightness(physical.Handle, (uint)value) : SetMonitorContrast(physical.Handle, (uint)value)))
                throw new Win32Exception();
        });
        if (!found) throw new InvalidOperationException("Monitor non più disponibile");
    }
}
