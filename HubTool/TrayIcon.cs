using System.Runtime.InteropServices;

namespace HubTool;

/// <summary>Native notification-area icon; avoids bringing Windows Forms into the WPF app.</summary>
internal sealed class TrayIcon : IDisposable
{
    public const int CallbackMessage = 0x8002;
    public static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private Data data;
    private readonly Action open, overlay, exit;
    private bool added;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Data
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref Data data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr item, string text);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);

    public TrayIcon(IntPtr window, Action open, Action overlay, Action exit)
    {
        this.open = open; this.overlay = overlay; this.exit = exit;
        ExtractIconEx(Environment.ProcessPath!, 0, out var large, out var small, 1);
        if (large != IntPtr.Zero) DestroyIcon(large);
        data = new Data { Size = (uint)Marshal.SizeOf<Data>(), Window = window, Id = 1, Flags = 7,
            Callback = CallbackMessage, Icon = small, Tip = "Hub Tool", Info = "", InfoTitle = "" };
        Add();
    }

    private void Add() { added = Shell_NotifyIcon(0, ref data); }
    public bool Handle(int message, IntPtr lParam)
    {
        if (message == TaskbarCreated) { Add(); return true; }
        if (message != CallbackMessage) return false;
        if (lParam.ToInt32() == 0x203) open();
        if (lParam.ToInt32() == 0x205)
        {
            var menu = CreatePopupMenu();
            try
            {
                AppendMenu(menu, 0, (UIntPtr)1, "Apri Hub Tool"); AppendMenu(menu, 0, (UIntPtr)2, "Overlay"); AppendMenu(menu, 0, (UIntPtr)3, "Esci");
                GetCursorPos(out var point); Native.SetForegroundWindow(data.Window);
                uint choice = TrackPopupMenuEx(menu, 0x180, point.X, point.Y, data.Window, IntPtr.Zero);
                if (choice == 1) open(); else if (choice == 2) overlay(); else if (choice == 3) exit();
            }
            finally { DestroyMenu(menu); }
        }
        return true;
    }

    public void Dispose()
    {
        if (added) Shell_NotifyIcon(2, ref data);
        if (data.Icon != IntPtr.Zero) { DestroyIcon(data.Icon); data.Icon = IntPtr.Zero; }
    }
}
