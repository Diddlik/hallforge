using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HallForge.App;

/// <summary>Name of the program that owns the foreground window. Windows only.</summary>
public static partial class ForegroundProgram
{
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <returns>The process name without ".exe", or null when unknown or not on Windows.</returns>
    public static string? Current()
    {
        if (!IsSupported)
            return null;
        var window = GetForegroundWindow();
        if (window == 0 || GetWindowThreadProcessId(window, out var processId) == 0)
            return null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            // The process exited between the two calls.
            return null;
        }
    }

    /// <summary>Programs with a visible window, offered when adding a rule.</summary>
    public static IReadOnlyList<string> WithWindows()
    {
        var own = Environment.ProcessId;
        return Process.GetProcesses()
            .Select(p =>
            {
                using (p)
                {
                    try
                    {
                        return p.Id != own && p.MainWindowHandle != 0 ? p.ProcessName : null;
                    }
                    catch (InvalidOperationException)
                    {
                        // The process exited while it was listed.
                        return null;
                    }
                }
            })
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
