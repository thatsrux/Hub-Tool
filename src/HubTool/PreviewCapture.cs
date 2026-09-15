using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
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
            CameraFrameAnalysis? cameraAnalysis = null;
            string cameraAnalysisError = "";
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
            if (window.FindName("DeviceList") is ListBox cameraList)
            {
                var camera = cameraList.Items.Cast<Device>().FirstOrDefault(d => d.Id.StartsWith("camera:") && d.Controls.Count > 0);
                if (camera != null)
                {
                    cameraList.SelectedItem = camera; await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Capture(window, "camera-detail");
                    await Task.Delay(1200);
                    try { cameraAnalysis = window.CaptureCameraAnalysisForDiagnostics(); } catch (Exception ex) { cameraAnalysisError = ex.Message; }
                }
            }
            using var overlay = new DiagnosticOverlay(window.CreateDiagnosticOverlay());
            overlay.Window.Show();
            overlay.Window.KeepOnScreen();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(overlay.Window, "overlay-icons");
            if (overlay.Window.Expanded || overlay.Window.IconCount < 1 || overlay.Window.Width <= overlay.Window.Height)
                throw new InvalidOperationException("Barra icone compatta non valida");
            var hwnd = new WindowInteropHelper(overlay.Window).Handle;
            if ((Native.WindowStyle(hwnd, -16) & 0x00C00000) != 0) throw new InvalidOperationException("Overlay con barra del titolo");
            var anchor = new Point(overlay.Window.Left, overlay.Window.Top);
            overlay.Window.OpenFirstModuleForDiagnostics();
            if (!overlay.Window.Expanded) throw new InvalidOperationException("Il clic non apre i moduli");
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(overlay.Window, "overlay-modules");
            overlay.Window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(overlay.Window), 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            if (overlay.Window.Expanded || overlay.Window.Width <= overlay.Window.Height) throw new InvalidOperationException("Escape non comprime l’overlay");
            if (Math.Abs(overlay.Window.Left - anchor.X) > 1 || Math.Abs(overlay.Window.Top - anchor.Y) > 1)
                throw new InvalidOperationException($"La barra cambia posizione alla chiusura: {anchor.X:0},{anchor.Y:0} → {overlay.Window.Left:0},{overlay.Window.Top:0}");
            window.SetOverlayOrientationForDiagnostics("Vertical"); overlay.Window.RefreshAppearance();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (overlay.Window.Width >= overlay.Window.Height) throw new InvalidOperationException("Barra icone verticale non valida");
            if (!overlay.Window.CompactContentFits) throw new InvalidOperationException("La barra compatta ritaglia i pulsanti delle icone");
            Capture(overlay.Window, "overlay-icons-vertical");
            var stableHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(App.PreviewDirectory, "overlay-icons-vertical.png"))));
            for (var pass = 0; pass < 12; pass++)
            {
                overlay.Window.RefreshModules();
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture(overlay.Window, "overlay-icons-repeat");
                var repeated = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(App.PreviewDirectory, "overlay-icons-repeat.png"))));
                if (repeated != stableHash) throw new InvalidOperationException("Il rendering delle icone cambia fra due frame identici");
            }
            var firstIcon = FindVisualChild<Button>((DependencyObject)overlay.Window.Content);
            if (firstIcon == null) throw new InvalidOperationException("Icona overlay non trovata");
            GetCursorPos(out var previousPointer);
            try
            {
                var hover = firstIcon.PointToScreen(new Point(firstIcon.ActualWidth / 2, firstIcon.ActualHeight / 2));
                SetCursorPos((int)Math.Round(hover.X), (int)Math.Round(hover.Y));
                await Task.Delay(100); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Input);
                if (!firstIcon.IsMouseOver || !overlay.Window.CompactContentFits) throw new InvalidOperationException("Il bordo hover dell’icona non è interamente visibile");
                Capture(overlay.Window, "overlay-icons-hover");
            }
            finally { SetCursorPos(previousPointer.X, previousPointer.Y); }
            var verticalLeft = overlay.Window.Left; var compactRight = overlay.Window.Left + overlay.Window.Width;
            overlay.Window.OpenFirstModuleForDiagnostics();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (!overlay.Window.Expanded || overlay.Window.Left >= verticalLeft - 1 || Math.Abs(overlay.Window.Left + overlay.Window.Width - compactRight) > 1)
                throw new InvalidOperationException("L’overlay ancorato a destra non si apre direttamente verso sinistra");
            Capture(overlay.Window, "overlay-right-open");
            File.WriteAllText(Path.Combine(App.PreviewDirectory, "result.json"), JsonSerializer.Serialize(new
            { Pages = tabs.Items.Count, CompactIcons = overlay.Window.IconCount, HorizontalLayout = true, VerticalLayout = true, StableIconPasses = 12,
                HoverBorderVisible = true, RightDockOpensLeft = true, NativeCaption = false, ClickExpanded = true, EscapeCollapsed = true, AnchorPreserved = true,
                CameraFrameAnalyzed = cameraAnalysis.HasValue, CameraMeanLuma = cameraAnalysis?.MeanLuma, CameraAnalysisError = cameraAnalysisError, Completed = true }));
        }
        window.Hide();
        if (App.FastPreview) { System.Windows.Application.Current.Shutdown(); return; }
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

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var nested = FindVisualChild<T>(child); if (nested != null) return nested;
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
}
