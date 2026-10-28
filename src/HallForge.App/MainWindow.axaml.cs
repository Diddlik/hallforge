using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HallForge.App;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;

    // Required by the Avalonia designer and XAML loader.
    public MainWindow() : this(new AppSettings())
    {
    }

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        RestorePlacement();
        Closing += (_, _) => SavePlacement();
    }

    private void OnAbout(object? sender, RoutedEventArgs e) =>
        new AboutWindow { DataContext = DataContext }.ShowDialog(this);

    private void RestorePlacement()
    {
        if (_settings.Window is not { } placement)
            return;

        Width = Math.Max(placement.Width, MinWidth);
        Height = Math.Max(placement.Height, MinHeight);
        var position = new PixelPoint(placement.X, placement.Y);
        // Skip the saved position if its monitor is gone, so the window never opens off-screen.
        if (Screens.All.Any(s => s.WorkingArea.Contains(position)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = position;
        }
        if (placement.Maximized)
            WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var maximized = WindowState == WindowState.Maximized;
        // A maximized window reports the screen size; keep the last normal size instead.
        var (width, height) = maximized && _settings.Window is { } previous ? (previous.Width, previous.Height) : (Width, Height);
        _settings.Window = new WindowPlacement(width, height, Position.X, Position.Y, maximized);
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing window placement is not worth blocking the app from closing.
        }
    }
}
