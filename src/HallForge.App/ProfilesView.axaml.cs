using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace HallForge.App;

public partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfileLibraryViewModel library || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Strings.ProfileFileType) { Patterns = ["*.json"] }],
        });
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
            library.Import(path);
    }
}
