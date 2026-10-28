using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HallForge.Core;

namespace HallForge.App;

public sealed record ProfileGroupRow(string Keys, string Actuation, string Release, string RapidTrigger, string RapidTriggerDistances);

/// <summary>A key on the coverage preview: whether the selected profile changes it.</summary>
public sealed record CoverageKey(double Left, double Top, double Width, double Height, string Legend, bool IsCovered);

public sealed class ProfileItem(string path, KeyboardProfile profile, bool isTemplate)
{
    public string Path { get; } = path;
    public string Name { get; } = profile.Name;
    public bool IsTemplate { get; } = isTemplate;
    public IReadOnlySet<string> KeyIds { get; } = profile.Resolve().Keys.ToHashSet();
    public string Coverage => Text.Format(Strings.ProfileCoverage, KeyIds.Count);

    public bool IsAggressive { get; } = profile.Groups.Any(g => g.Settings.ActuationMm < KeyboardEditorViewModel.RiskyActuationMm
        || g.Settings.RapidTriggerPressMm < KeyboardEditorViewModel.RiskyRapidTriggerMm
        || g.Settings.RapidTriggerReleaseMm < KeyboardEditorViewModel.RiskyRapidTriggerMm);

    public IReadOnlyList<ProfileGroupRow> Groups { get; } = profile.Groups
        .Select(g => new ProfileGroupRow(string.Join(" ", g.KeyIds), Format(g.Settings.ActuationMm), Format(g.Settings.ReleaseMm),
            g.Settings.RapidTriggerEnabled is { } on ? (on ? Strings.On : Strings.Off) : Strings.Unchanged,
            g.Settings.RapidTriggerPressMm is null && g.Settings.RapidTriggerReleaseMm is null
                ? Strings.Unchanged
                : $"{Format(g.Settings.RapidTriggerPressMm)} / {Format(g.Settings.RapidTriggerReleaseMm)}"))
        .ToList();

    private static string Format(decimal? mm) => mm is { } v ? Mm.Format(v) : Strings.Unchanged;
}

/// <summary>Templates shipped in <c>profiles/</c> plus imported files. Loading a profile only fills the draft.</summary>
public sealed partial class ProfileLibraryViewModel : ObservableObject
{
    private readonly KeyboardEditorViewModel _editor;
    private readonly Action _openPerformance;

    public ProfileLibraryViewModel(string templateDirectory, KeyboardEditorViewModel editor, Action openPerformance)
    {
        _editor = editor;
        _openPerformance = openPerformance;
        var files = Directory.Exists(templateDirectory) ? Directory.GetFiles(templateDirectory, "*.json").Order() : Enumerable.Empty<string>();
        foreach (var file in files)
            Add(file, isTemplate: true);
        Selected = Profiles.FirstOrDefault();
    }

    public ObservableCollection<ProfileItem> Profiles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CoverageKeys))]
    [NotifyCanExecuteChangedFor(nameof(LoadIntoDraftCommand))]
    public partial ProfileItem? Selected { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    public double CanvasWidth => _editor.CanvasWidth;
    public double CanvasHeight => _editor.CanvasHeight;

    public IReadOnlyList<CoverageKey> CoverageKeys => _editor.Keys
        .Select(k => new CoverageKey(k.Left, k.Top, k.Width, k.Height, k.Legend, Selected?.KeyIds.Contains(k.KeyId) == true))
        .ToList();

    partial void OnSelectedChanged(ProfileItem? value) => Status = "";

    public void Import(string path)
    {
        if (Add(path, isTemplate: false) is { } item)
            Selected = item;
    }

    private ProfileItem? Add(string path, bool isTemplate)
    {
        try
        {
            var item = new ProfileItem(path, KeyboardProfile.Load(path), isTemplate);
            Profiles.Add(item);
            return item;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Status = Text.Format(Strings.ProfileLoadFailed, ex.Message);
            return null;
        }
    }

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void LoadIntoDraft()
    {
        var loaded = _editor.LoadProfile(Selected!.Path);
        Status = loaded > 0 ? Text.Format(Strings.ProfileLoadedIntoDraft, loaded) : _editor.Status;
    }

    [RelayCommand]
    private void OpenPerformance() => _openPerformance();
}
