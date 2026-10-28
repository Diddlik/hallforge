using Microsoft.Win32;

namespace HallForge.App;

/// <summary>Starts HallForge minimized when the user signs in to Windows, through the per-user Run key.</summary>
public static class Autostart
{
    public const string MinimizedArgument = "--minimized";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HallForge";

    /// <summary>
    /// The Velopack launcher one folder above the app survives updates, while the app folder is replaced;
    /// only installed copies have it, so portable and development builds cannot enable autostart.
    /// </summary>
    private static string? Launcher =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "HallForge.exe")) is var path && File.Exists(path) ? path : null;

    public static bool IsSupported => OperatingSystem.IsWindows() && Launcher is not null;

    public static bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows())
                return false;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        set
        {
            if (!OperatingSystem.IsWindows() || Launcher is not { } launcher)
                return;
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(ValueName, $"\"{launcher}\" {MinimizedArgument}");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
