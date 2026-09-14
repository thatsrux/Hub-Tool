using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HubTool;

/// <summary>Windows input preferences are shared by every mouse/keyboard, not per HID.</summary>
public static class InputControls
{
    public const string MouseId = "windows:mouse";
    public const string KeyboardId = "windows:keyboard";

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool Read(uint action, uint parameter, ref uint value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool Write(uint action, uint parameter, IntPtr value, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetDoubleClickTime(uint value);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool SwapMouseButton(bool swap);

    private static uint Get(uint action)
    {
        uint value = 0;
        if (!Read(action, 0, ref value, 0)) throw new Win32Exception();
        return value;
    }

    public static List<Device> Enumerate() =>
    [
        new Device
        {
            Id = MouseId, Name = "Mouse · preferenze Windows", Kind = "Mouse", Connected = true,
            Controls =
            [
                new("speed", "Velocità del puntatore", 1, 20, 1),
                new("double-click", "Intervallo doppio clic", 200, 900, 25, "ms"),
                new("wheel", "Righe per scatto della rotella", 1, 100, 1),
                new("swap", "Scambia pulsante principale", 0, 1, 1, Toggle: true)
            ],
            Values = new() { ["speed"] = Get(0x70), ["double-click"] = GetDoubleClickTime(),
                ["wheel"] = Get(0x68), ["swap"] = GetSystemMetrics(23) != 0 ? 1 : 0 }
        },
        new Device
        {
            Id = KeyboardId, Name = "Tastiera · preferenze Windows", Kind = "Keyboard", Connected = true,
            Controls =
            [
                new("repeat-speed", "Velocità ripetizione", 0, 31, 1),
                new("repeat-delay", "Ritardo ripetizione (0 rapido, 3 lento)", 0, 3, 1)
            ],
            Values = new() { ["repeat-speed"] = Get(0x0A), ["repeat-delay"] = Get(0x16) }
        }
    ];

    public static void Set(string deviceId, string control, double value)
    {
        var definition = Enumerate().Single(d => d.Id == deviceId).Controls.Single(c => c.Id == control);
        if (!double.IsFinite(value) || value < definition.Min || value > definition.Max)
            throw new ArgumentOutOfRangeException(nameof(value));
        uint v = (uint)Math.Round(value);
        bool success = (deviceId, control) switch
        {
            (MouseId, "speed") => Write(0x71, 0, (IntPtr)v, 3),
            (MouseId, "double-click") => SetDoubleClickTime(v),
            (MouseId, "wheel") => Write(0x69, v, IntPtr.Zero, 3),
            (MouseId, "swap") => Swap(v != 0),
            (KeyboardId, "repeat-speed") => Write(0x0B, v, IntPtr.Zero, 3),
            (KeyboardId, "repeat-delay") => Write(0x17, v, IntPtr.Zero, 3),
            _ => throw new NotSupportedException(control)
        };
        if (!success) throw new Win32Exception();
    }

    private static bool Swap(bool value) { SwapMouseButton(value); return true; }
}
