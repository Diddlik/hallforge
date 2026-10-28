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
            var transport = new HidSharpTransport();
            var discovery = new KeyboardDiscoveryService(
                transport,
                DeviceDefinitionLoader.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "devices")));
            // ISO German is the only shipped legend set so far; other layouts are added as files in layouts/.
            var legends = KeyLegends.Load(Path.Combine(AppContext.BaseDirectory, "layouts", "iso-de.json"));
            var viewModel = new MainViewModel(discovery, transport, new StateStore(StateStore.DefaultRoot), legends, settings);
            var window = new MainWindow(settings) { DataContext = viewModel };
            window.Opened += async (_, _) => await viewModel.StartAsync();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
