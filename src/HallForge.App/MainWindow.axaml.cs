using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HallForge.Core;
using HallForge.Transport.Hid;

namespace HallForge.App;

public partial class MainWindow : Window
{
    private readonly KeyboardDiscoveryService _discovery = new(
        new HidSharpEnumerator(),
        DeviceDefinitionLoader.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "devices")));

    public MainWindow()
    {
        InitializeComponent();
        var version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Title = $"HallForge {version.Split('+')[0]}";
        Opened += (_, _) => _ = RefreshAsync();
    }

    private void OnRefresh(object? sender, RoutedEventArgs e) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Searching for keyboards…";
        try
        {
            // HID enumeration blocks; keep the UI thread responsive.
            var candidates = await Task.Run(() => _discovery.Discover());
            DeviceList.ItemsSource = candidates.Select(c =>
                $"{c.Interface.ProductName ?? "Unknown device"}  ({DiagnosticReport.Hex(c.Interface.VendorId)}:{DiagnosticReport.Hex(c.Interface.ProductId)})  {c.Support}, read-only");
            StatusText.Text = candidates.Count == 0
                ? "No supported keyboard found. Connect the keyboard via USB cable and select Refresh."
                : $"{candidates.Count} configuration interface(s) found.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not list HID devices: {ex.Message}. Check device permissions and select Refresh.";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }
}
