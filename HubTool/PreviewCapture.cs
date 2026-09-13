using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HubTool;

/// <summary>Renders the app's own WPF visual tree for repeatable layout inspection.</summary>
internal static class PreviewCapture
{
    public static async Task TryCaptureAsync(MainWindow window)
    {
        if (App.PreviewDirectory == null) return;
        var tabs = (TabControl)window.FindName("Pages");
        var content = (FrameworkElement)window.Content;
        for (int i = 0; i < tabs.Items.Count; i++)
        {
            tabs.SelectedIndex = i;
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
                drawing.DrawRectangle(window.Background, null, bounds);
                drawing.DrawRectangle(new VisualBrush(content), null, bounds);
            }
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(App.PreviewDirectory, "page-" + i + ".png"));
            encoder.Save(stream);
        }
        File.WriteAllText(Path.Combine(App.PreviewDirectory, "result.json"), JsonSerializer.Serialize(new { Pages = tabs.Items.Count, Completed = true }));
        window.Hide();
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var started = System.Diagnostics.Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(15));
        process.Refresh();
        File.WriteAllText(Path.Combine(App.PreviewDirectory, "idle-metrics.json"), JsonSerializer.Serialize(new
        {
            SampleSeconds = started.Elapsed.TotalSeconds,
            CpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds,
            WorkingSetMiB = process.WorkingSet64 / 1048576.0,
            PrivateMemoryMiB = process.PrivateMemorySize64 / 1048576.0,
            Note = "After rendering four pages; isolated preferences; hidden window; no forced GC or working-set trimming"
        }));
        System.Windows.Application.Current.Shutdown();
    }
}
