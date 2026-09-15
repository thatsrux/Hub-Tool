using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HubTool;

/// <summary>Captures small camera frames with the Windows inbox AVICap API and displays them as a normal WPF image.</summary>
internal sealed class CameraPreviewHost : Grid, IDisposable
{
    private const int WmCapDriverConnect = 0x40A, WmCapDriverDisconnect = 0x40B;
    private const int WmCapSetCallbackFrame = 0x405, WmCapGetVideoFormat = 0x42C, WmCapGrabFrame = 0x43C;
    private readonly string cameraName;
    private readonly Image image = new() { Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
    private readonly TextBlock message = new()
    {
        Text = "Avvio anteprima…", Foreground = Brushes.White, Opacity = .72,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private IntPtr captureWindow;
    private bool connected, suspended, disposed, capturing;

    public CameraPreviewHost(string cameraName)
    {
        this.cameraName = cameraName;
        MinHeight = 220; Focusable = false; Background = Brushes.Black;
        Children.Add(image); Children.Add(message);
        timer.Tick += (_, _) => RefreshFrame();
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible) Start(); else Stop(); };
    }

    public bool HasFrame => image.Source != null;
    public int FramePixelWidth => image.Source is BitmapSource bitmap ? bitmap.PixelWidth : 0;
    public int FramePixelHeight => image.Source is BitmapSource bitmap ? bitmap.PixelHeight : 0;

    private void Start()
    {
        if (disposed || suspended || !IsLoaded || !IsVisible) return;
        timer.Start();
        Dispatcher.BeginInvoke(RefreshFrame, DispatcherPriority.Background);
    }

    private void Stop()
    {
        timer.Stop();
        Disconnect();
    }

    private void EnsureConnected()
    {
        if (connected) return;
        if (disposed) throw new ObjectDisposedException(nameof(CameraPreviewHost));
        captureWindow = captureWindow == IntPtr.Zero
            ? CapCreateCaptureWindow("Hub camera capture", 0, 0, 0, 1, 1, IntPtr.Zero, 0)
            : captureWindow;
        if (captureWindow == IntPtr.Zero) throw new InvalidOperationException("Windows non ha potuto avviare la videocamera");
        var index = FindDriver(cameraName);
        if (index < 0 || SendMessage(captureWindow, WmCapDriverConnect, new IntPtr(index), IntPtr.Zero) == IntPtr.Zero)
            throw new InvalidOperationException("La videocamera è occupata o non disponibile");
        connected = true;
    }

    private void Disconnect()
    {
        if (!connected || captureWindow == IntPtr.Zero) return;
        SendMessage(captureWindow, WmCapDriverDisconnect, IntPtr.Zero, IntPtr.Zero);
        connected = false;
    }

    private void RefreshFrame()
    {
        if (capturing || suspended || disposed || !IsVisible) return;
        capturing = true;
        try
        {
            var frame = CaptureFrame();
            image.Source = frame.Image;
            message.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            message.Text = "Anteprima non disponibile\n" + ex.Message;
            message.TextAlignment = TextAlignment.Center;
            message.Visibility = Visibility.Visible;
        }
        finally { capturing = false; }
    }

    public void Suspend()
    {
        suspended = true;
        timer.Stop();
        Disconnect();
    }

    public void Resume()
    {
        suspended = false;
        Start();
    }

    public CameraFrameAnalysis CaptureAnalysis()
    {
        var frame = CaptureFrame();
        image.Source = frame.Image;
        message.Visibility = Visibility.Collapsed;
        return frame.Analysis;
    }

    private CameraFrame CaptureFrame()
    {
        EnsureConnected();
        var formatSize = SendMessage(captureWindow, WmCapGetVideoFormat, IntPtr.Zero, IntPtr.Zero).ToInt32();
        if (formatSize < 40) throw new IOException("La videocamera non espone il formato del fotogramma");
        var format = Marshal.AllocHGlobal(formatSize);
        try
        {
            SendMessage(captureWindow, WmCapGetVideoFormat, new IntPtr(formatSize), format);
            int width = Marshal.ReadInt32(format, 4), signedHeight = Marshal.ReadInt32(format, 8);
            int height = Math.Abs(signedHeight), bits = Marshal.ReadInt16(format, 14), bytesPerPixel = bits / 8;
            int stride = ((width * bits + 31) / 32) * 4;
            byte[]? captured = null;
            FrameCallback callback = (IntPtr _, ref VideoHeader header) =>
            {
                var reported = header.BytesUsed > 0 ? header.BytesUsed : header.BufferLength;
                var length = (int)Math.Min(reported, int.MaxValue);
                if (header.Data != IntPtr.Zero && length > 0) { captured = new byte[length]; Marshal.Copy(header.Data, captured, 0, length); }
                return IntPtr.Zero;
            };
            var callbackPointer = Marshal.GetFunctionPointerForDelegate(callback);
            SendMessage(captureWindow, WmCapSetCallbackFrame, IntPtr.Zero, callbackPointer);
            SendMessage(captureWindow, WmCapGrabFrame, IntPtr.Zero, IntPtr.Zero);
            SendMessage(captureWindow, WmCapSetCallbackFrame, IntPtr.Zero, IntPtr.Zero);
            GC.KeepAlive(callback);
            if (captured == null || captured.Length == 0)
                throw new IOException("La videocamera non ha restituito un fotogramma");

            BitmapSource previewImage;
            if (captured.Length < (long)stride * height)
            {
                using var encoded = new MemoryStream(captured, writable: false);
                var decoded = new BitmapImage();
                decoded.BeginInit(); decoded.CacheOption = BitmapCacheOption.OnLoad;
                decoded.DecodePixelWidth = Math.Min(960, Math.Max(1, width)); decoded.StreamSource = encoded; decoded.EndInit(); decoded.Freeze();
                previewImage = decoded;
            }
            else
            {
                if (width <= 0 || height <= 0 || bytesPerPixel < 3)
                    throw new IOException($"Formato videocamera non supportato ({width}×{height}, {bits} bit)");
                var raw = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, captured, stride);
                previewImage = width > 960 ? new TransformedBitmap(raw, new ScaleTransform(960d / width, 960d / width)) : raw;
                previewImage.Freeze();
            }

            var converted = new FormatConvertedBitmap(previewImage, PixelFormats.Bgr24, null, 0);
            width = converted.PixelWidth; height = converted.PixelHeight; bytesPerPixel = 3; stride = (width * 3 + 3) & ~3;
            var pixels = new byte[stride * height]; converted.CopyPixels(pixels, stride, 0);
            return new CameraFrame(previewImage, Analyze(pixels, width, height, stride, bytesPerPixel));
        }
        catch (Exception ex) when (ex is not IOException)
        {
            throw new IOException("Fotogramma videocamera non leggibile: " + ex.Message, ex);
        }
        finally { Marshal.FreeHGlobal(format); }
    }

    private static CameraFrameAnalysis Analyze(byte[] pixels, int width, int height, int stride, int bytesPerPixel)
    {
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

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; timer.Stop(); Disconnect();
        if (captureWindow != IntPtr.Zero) { DestroyWindow(captureWindow); captureWindow = IntPtr.Zero; }
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
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
}

internal readonly record struct CameraFrame(BitmapSource Image, CameraFrameAnalysis Analysis);
internal readonly record struct CameraFrameAnalysis(double MeanLuma, double Contrast, double DarkRatio, double BrightRatio,
    double MeanRed, double MeanGreen, double MeanBlue);
