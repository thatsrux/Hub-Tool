using Microsoft.Win32;

namespace HubTool;

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HubTool";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (!enabled) { key.DeleteValue(ValueName, throwOnMissingValue: false); return; }
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Avvia HubTool.exe per attivare l’avvio con Windows");
        key.SetValue(ValueName, BuildCommand(executable), RegistryValueKind.String);
    }

    internal static string BuildCommand(string executable) => $"\"{executable}\" --startup";
}
