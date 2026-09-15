using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HubTool;

/// <summary>Very small native AVICap host: Windows owns capture and rendering, so preview adds no video framework.</summary>
internal sealed class CameraPreviewHost : HwndHost
{
    private const int WsChild = 0x40000000, WsVisible = 0x10000000;
    private const int WmCapDriverConnect = 0x40A, WmCapDriverDisconnect = 0x40B;
    private const int WmCapSetCallbackFrame = 0x405, WmCapGetVideoFormat = 0x42C;
    private const int WmCapSetPreview = 0x432, WmCapSetPreviewRate = 0x434, WmCapSetScale = 0x435;
    private const int WmCapGrabFrame = 0x43C;
    private readonly string cameraName;
    private IntPtr preview;
    private bool connected;

    public CameraPreviewHost(string cameraName)
    {
        this.cameraName = cameraName;
        MinHeight = 220; Focusable = false;
        Loaded += (_, _) => Connect();
        Unloaded += (_, _) => Disconnect();
        IsVisibleChanged += (_, _) => { if (IsVisible) Connect(); else Disconnect(); };
    }

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        preview = CapCreateCaptureWindow("Hub camera preview", WsChild | WsVisible, 0, 0, 640, 360, parent.Handle, 0);
        if (preview == IntPtr.Zero) throw new InvalidOperationException("Windows non ha potuto creare l’anteprima della videocamera");
        Dispatcher.BeginInvoke(Connect);
        return new HandleRef(this, preview);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        Disconnect(); DestroyWindow(hwnd.Handle); preview = IntPtr.Zero;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        if (preview != IntPtr.Zero) MoveWindow(preview, 0, 0, Math.Max(1, (int)rcBoundingBox.Width), Math.Max(1, (int)rcBoundingBox.Height), true);
    }

    private void Connect()
    {
        if (connected || preview == IntPtr.Zero || !IsVisible) return;
        var index = FindDriver(cameraName);
        if (index < 0 || SendMessage(preview, WmCapDriverConnect, new IntPtr(index), IntPtr.Zero) == IntPtr.Zero) return;
        connected = true;
        SendMessage(preview, WmCapSetScale, new IntPtr(1), IntPtr.Zero);
        SendMessage(preview, WmCapSetPreviewRate, new IntPtr(33), IntPtr.Zero);
        SendMessage(preview, WmCapSetPreview, new IntPtr(1), IntPtr.Zero);
    }

    private void Disconnect()
    {
        if (!connected || preview == IntPtr.Zero) return;
        SendMessage(preview, WmCapSetPreview, IntPtr.Zero, IntPtr.Zero);
        SendMessage(preview, WmCapDriverDisconnect, IntPtr.Zero, IntPtr.Zero); connected = false;
    }

    public void Suspend() => Disconnect();
    public void Resume() => Connect();

    public CameraFrameAnalysis CaptureAnalysis()
    {
        Connect();
        if (!connected) throw new InvalidOperationException("La videocamera è occupata o non disponibile");
        var formatSize = SendMessage(preview, WmCapGetVideoFormat, IntPtr.Zero, IntPtr.Zero).ToInt32();
        if (formatSize < 40) throw new IOException("La videocamera non espone il formato del fotogramma");
        var format = Marshal.AllocHGlobal(formatSize);
        try
        {
            SendMessage(preview, WmCapGetVideoFormat, new IntPtr(formatSize), format);
            int width = Marshal.ReadInt32(format, 4), signedHeight = Marshal.ReadInt32(format, 8);
            int height = Math.Abs(signedHeight), bits = Marshal.ReadInt16(format, 14), bytesPerPixel = bits / 8;
            int stride = ((width * bits + 31) / 32) * 4;
            byte[]? pixels = null; uint capturedBufferLength = 0, capturedBytesUsed = 0;
            FrameCallback callback = (IntPtr _, ref VideoHeader header) =>
            {
                capturedBufferLength = header.BufferLength; capturedBytesUsed = header.BytesUsed;
                var reported = header.BytesUsed > 0 ? header.BytesUsed : header.BufferLength;
                var length = (int)Math.Min(reported, (uint)Math.Max(0, stride * height));
                if (header.Data != IntPtr.Zero && length > 0) { pixels = new byte[length]; Marshal.Copy(header.Data, pixels, 0, length); }
                return IntPtr.Zero;
            };
            var callbackPointer = Marshal.GetFunctionPointerForDelegate(callback);
            SendMessage(preview, WmCapSetCallbackFrame, IntPtr.Zero, callbackPointer);
            SendMessage(preview, WmCapGrabFrame, IntPtr.Zero, IntPtr.Zero);
            SendMessage(preview, WmCapSetCallbackFrame, IntPtr.Zero, IntPtr.Zero);
            GC.KeepAlive(callback);
            if (pixels != null && pixels.Length > 0 && pixels.Length < stride * height)
            {
                try
                {
                    using var encoded = new MemoryStream(pixels, writable: false);
                    var frame = BitmapFrame.Create(encoded, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);
                    width = converted.PixelWidth; height = converted.PixelHeight; bytesPerPixel = 3; stride = (width * 3 + 3) & ~3;
                    pixels = new byte[stride * height]; converted.CopyPixels(pixels, stride, 0);
                }
                catch { }
            }
            if (width <= 0 || height <= 0 || bytesPerPixel < 3 || pixels == null || pixels.Length < stride * height)
                throw new IOException($"Formato del fotogramma videocamera non supportato ({width}×{height}, {bits} bit, buffer {capturedBufferLength}/{capturedBytesUsed})");
            double luma = 0, squared = 0, red = 0, green = 0, blue = 0; int dark = 0, bright = 0, count = 0;
            int step = Math.Max(1, Math.Min(width, height) / 160);
            for (int y = 0; y < height; y += step)
            for (int x = 0; x < width; x += step)
            {
                int i = y * stride + x * bytesPerPixel; double b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                double value = .0722 * b + .7152 * g + .2126 * r;
                luma += value; squared += value * value; blue += b; green += g; red += r; count++;
                if (value < 28) dark++; if (value > 228) bright++;
            }
            var mean = luma / count;
            return new(mean, Math.Sqrt(Math.Max(0, squared / count - mean * mean)), dark / (double)count, bright / (double)count,
                red / count, green / count, blue / count);
        }
        finally { Marshal.FreeHGlobal(format); }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr FrameCallback(IntPtr window, ref VideoHeader header);
    [StructLayout(LayoutKind.Sequential)]
    private struct VideoHeader
    {
        public IntPtr Data; public uint BufferLength; public uint BytesUsed; public IntPtr User;
        public uint Flags; public uint Loops; public IntPtr Next; public IntPtr Reserved;
    }

    private static int FindDriver(string name)
    {
        int fallback = -1;
        for (short i = 0; i < 10; i++)
        {
            var driver = new System.Text.StringBuilder(256); var version = new System.Text.StringBuilder(256);
            if (!CapGetDriverDescription(i, driver, driver.Capacity, version, version.Capacity)) continue;
            fallback = fallback < 0 ? i : fallback;
            if (driver.ToString().Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(driver.ToString(), StringComparison.OrdinalIgnoreCase)) return i;
        }
        return fallback;
    }

    [DllImport("avicap32.dll", CharSet = CharSet.Unicode, EntryPoint = "capCreateCaptureWindowW")]
    private static extern IntPtr CapCreateCaptureWindow(string title, int style, int x, int y, int width, int height, IntPtr parent, int id);
    [DllImport("avicap32.dll", CharSet = CharSet.Unicode, EntryPoint = "capGetDriverDescriptionW")]
    private static extern bool CapGetDriverDescription(short index, System.Text.StringBuilder name, int nameSize, System.Text.StringBuilder version, int versionSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
}

internal readonly record struct CameraFrameAnalysis(double MeanLuma, double Contrast, double DarkRatio, double BrightRatio,
    double MeanRed, double MeanGreen, double MeanBlue);
