using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HallForge.Core;
using HallForge.Transport.Hid;

namespace HallForge.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = AppSettings.Load();
            var discovery = new KeyboardDiscoveryService(
                new HidSharpEnumerator(),
                DeviceDefinitionLoader.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "devices")));
            var viewModel = new MainViewModel(discovery, settings);
            var window = new MainWindow(settings) { DataContext = viewModel };
            window.Opened += async (_, _) => await viewModel.StartAsync();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
