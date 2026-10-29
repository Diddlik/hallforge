using Avalonia.Controls;
using Avalonia.Input;

namespace HallForge.App;

public partial class DeviceView : UserControl
{
    public DeviceView()
    {
        InitializeComponent();
    }

    // Programs start and stop all the time, so the suggestions are refreshed whenever the user starts typing.
    private void OnProgramBoxFocused(object? sender, FocusChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.AutoSwitch.RefreshProgramsCommand.Execute(null);
    }
}
