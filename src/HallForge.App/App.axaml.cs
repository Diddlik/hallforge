using Avalonia;
using Avalonia.Controls;
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
            if (desktop.Args?.Contains(Autostart.MinimizedArgument) == true)
                window.WindowState = Avalonia.Controls.WindowState.Minimized;
            window.Opened += async (_, _) => await viewModel.StartAsync();
            HidSharp.DeviceList.Local.Changed += (_, _) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(async () => await viewModel.CheckDevicesAsync());
            desktop.MainWindow = window;
            KeepRunningInTray(window, viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// With close to tray or automatic switching enabled, closing the window hides it to the tray.
    /// "Exit" in the tray menu ends the app.
    /// </summary>
    private void KeepRunningInTray(Window window, MainViewModel viewModel)
    {
        void ShowWindow()
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        }

        var open = new NativeMenuItem(Strings.TrayOpen);
        open.Click += (_, _) => ShowWindow();
        var exit = new NativeMenuItem(Strings.TrayExit);
        exit.Click += (_, _) => window.Close();
        var tray = new TrayIcon
        {
            Icon = window.Icon,
            ToolTipText = Strings.TrayToolTip,
            IsVisible = viewModel.KeepsRunningInTray,
            Menu = new NativeMenu { open, exit },
        };
        tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, [tray]);
        viewModel.AutoSwitch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AutoSwitchViewModel.IsEnabled))
                tray.IsVisible = viewModel.KeepsRunningInTray;
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.KeepsRunningInTray))
                tray.IsVisible = viewModel.KeepsRunningInTray;
        };
        // Programmatic closes (tray "Exit") and system shutdown still end the app.
        window.Closing += (_, e) =>
        {
            if (viewModel.KeepsRunningInTray && !e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
                window.Hide();
            }
        };
    }
}
