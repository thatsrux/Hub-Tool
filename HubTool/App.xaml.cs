using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace HubTool;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    public static string? PreviewDirectory { get; private set; }
    public static bool BenchmarkOnly { get; private set; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] is "--preview" or "--benchmark")
        {
            BenchmarkOnly = e.Args[0] == "--benchmark";
            PreviewDirectory = Path.GetFullPath(e.Args[1]);
            Directory.CreateDirectory(PreviewDirectory);
            Settings.DataDirectoryOverride = Path.Combine(PreviewDirectory, "isolated-data-" + Guid.NewGuid().ToString("N"));
        }
        else
        {
            instance = new Mutex(true, "Local\\HubTool.Desktop", out bool created);
            if (!created)
            {
                PostMessage(FindWindow(null, "Hub Tool"), 0x8001, IntPtr.Zero, IntPtr.Zero);
                Shutdown(); return;
            }
        }
        DispatcherUnhandledException += (_, args) =>
        {
            Directory.CreateDirectory(Settings.Folder);
            File.AppendAllText(Path.Combine(Settings.Folder, "errors.log"), DateTime.UtcNow + " " + args.Exception + Environment.NewLine);
            if (PreviewDirectory == null) MessageBox.Show("Hub Tool ha incontrato un errore. Dettagli in " + Settings.Folder + "\\errors.log", "Hub Tool");
            args.Handled = true;
            Shutdown(1);
        };
        var window = new MainWindow();
        MainWindow = window;
        if (PreviewDirectory != null)
        {
            window.ShowActivated = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + 100;
        }
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
