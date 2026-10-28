using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HallForge.App;

/// <summary>Shows every change before anything is written, then the sending progress and its result.</summary>
public partial class ReviewDialog : Window
{
    public ReviewDialog()
    {
        InitializeComponent();
        // Closing mid-write would hide which keys were sent; Stop ends sending cleanly between keys instead.
        Closing += (_, e) => e.Cancel = DataContext is ReviewViewModel { IsSending: true };
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
