using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HubTool;

internal static class PreviewCapture
{
    private static void Capture(Window window, string name)
    {
        var content = (FrameworkElement)window.Content;
        content.UpdateLayout();
        var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, bounds);
            drawing.DrawRectangle(new VisualBrush(content), null, bounds);
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(App.PreviewDirectory!, name + ".png")); encoder.Save(stream);
    }

    public static async Task TryCaptureAsync(MainWindow window)
    {
        if (App.PreviewDirectory == null) return;
        if (!App.BenchmarkOnly)
        {
            var tabs = (TabControl)window.FindName("Pages");
            for (int i = 0; i < tabs.Items.Count; i++)
            {
                tabs.SelectedIndex = i;
                if (i == 0 && window.FindName("DeviceList") is ListBox devices)
                {
                    var diagnostic = devices.Items.Cast<Device>().FirstOrDefault(d => d.Id.StartsWith("light:") && d.Controls.Count > 0)
                        ?? devices.Items.Cast<Device>().FirstOrDefault(d => d.Id.StartsWith("camera:") && d.Controls.Count > 0)
                        ?? devices.Items.Cast<Device>().FirstOrDefault(d => d.Controls.Any(c => AudioTopologyControls.IsSidetone(c.Id)));
                    if (diagnostic != null) devices.SelectedItem = diagnostic;
                }
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture(window, "page-" + i);
            }
            tabs.SelectedIndex = 0; window.Width = 900; window.Height = 620;
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(window, "compact-layout");
            using var overlay = new DiagnosticOverlay(window.CreateDiagnosticOverlay());
            overlay.Window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(overlay.Window, "overlay-badge");
            if (overlay.Window.Width != 52 || overlay.Window.Height != 52) throw new InvalidOperationException("Badge non compatto");
            var hwnd = new WindowInteropHelper(overlay.Window).Handle;
            if ((Native.WindowStyle(hwnd, -16) & 0x00C00000) != 0) throw new InvalidOperationException("Overlay con barra del titolo");
            var anchor = new Point(overlay.Window.Left, overlay.Window.Top);
            ((Button)overlay.Window.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!overlay.Window.Expanded) throw new InvalidOperationException("Il clic non apre i moduli");
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(overlay.Window, "overlay-modules");
            overlay.Window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(overlay.Window), 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            if (overlay.Window.Expanded || overlay.Window.Width != 52) throw new InvalidOperationException("Escape non comprime l’overlay");
            if (Math.Abs(overlay.Window.Left - anchor.X) > 1 || Math.Abs(overlay.Window.Top - anchor.Y) > 1) throw new InvalidOperationException("Il badge cambia posizione alla chiusura");
            File.WriteAllText(Path.Combine(App.PreviewDirectory, "result.json"), JsonSerializer.Serialize(new
            { Pages = tabs.Items.Count, OverlayBadge = "52x52", NativeCaption = false, ClickExpanded = true, EscapeCollapsed = true, AnchorPreserved = true, Completed = true }));
        }
        window.Hide();
        // Exclude startup/JIT and screenshot work from the idle sample.
        await Task.Delay(TimeSpan.FromSeconds(8));
        var process = System.Diagnostics.Process.GetCurrentProcess(); process.Refresh();
        var cpu = process.TotalProcessorTime; var started = System.Diagnostics.Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(20)); process.Refresh();
        File.WriteAllText(Path.Combine(App.PreviewDirectory, "idle-metrics.json"), JsonSerializer.Serialize(new
        {
            SampleSeconds = started.Elapsed.TotalSeconds, CpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds,
            WorkingSetMiB = process.WorkingSet64 / 1048576.0, PrivateMemoryMiB = process.PrivateMemorySize64 / 1048576.0,
            ScreenshotWork = !App.BenchmarkOnly, Note = "8s warm-up; hidden window; no forced GC or working-set trimming"
        }));
        System.Windows.Application.Current.Shutdown();
    }

    private sealed class DiagnosticOverlay(OverlayWindow window) : IDisposable
    {
        public OverlayWindow Window => window;
        public void Dispose() => window.Close();
    }
}
