using Avalonia;
using Velopack;

namespace HallForge.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: Velopack handles install/update hooks and may exit the process.
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
