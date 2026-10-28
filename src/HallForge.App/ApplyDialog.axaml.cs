using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HallForge.App;

/// <summary>Shows every pending change before anything is written; returns true only on explicit confirmation.</summary>
public partial class ApplyDialog : Window
{
    public ApplyDialog()
    {
        InitializeComponent();
    }

    public ApplyDialog(ApplyRequest request) : this()
    {
        DataContext = request;
        Heading.Text = Text.Format(Strings.ConfirmHeading, request.DeviceName);
        // Unverified devices need an explicit acknowledgement, the GUI counterpart of the CLI's --experimental.
        ApplyButton.IsEnabled = !request.RequiresExperimentalConsent;
    }

    private void OnConsentChanged(object? sender, RoutedEventArgs e) => ApplyButton.IsEnabled = Consent.IsChecked == true;

    private void OnApply(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
