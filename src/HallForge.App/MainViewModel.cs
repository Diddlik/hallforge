using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HallForge.Core;
using HallForge.Protocol.RongYuan;
using Velopack;
using Velopack.Sources;

namespace HallForge.App;

public sealed record ThirdPartyLibrary(string Name, string License);

/// <summary>Navigation targets; the values are the positions in the navigation list.</summary>
public enum Page
{
    Performance = 0,
    KeyMapping = 1,
    Lighting = 2,
    Profiles = 3,
    Device = 4,
}

public sealed partial class MainViewModel(
    KeyboardDiscoveryService discovery, IHidTransport transport, StateStore store, KeyLegends legends, AppSettings settings) : ObservableObject
{
    private const string RepositoryUrl = "https://github.com/Diddlik/hallforge";

    private readonly UpdateManager _updates = new(new GithubSource(RepositoryUrl, null, prerelease: false));
    private UpdateInfo? _pendingUpdate;
    private Avalonia.Threading.DispatcherTimer? _autoSwitchTimer;

    public static string Version { get; } = typeof(MainViewModel).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    public static string Title { get; } = Format(Strings.WindowTitle, Version);

    public static IReadOnlyList<ThirdPartyLibrary> ThirdPartyLibraries { get; } =
    [
        new("Avalonia", "MIT"),
        new("CommunityToolkit.Mvvm", "MIT"),
        new("HidSharp", "Apache-2.0"),
        new("Velopack", "MIT"),
    ];

    /// <summary>Set by the view; shows the review dialog, which also sends, and completes when it closes.</summary>
    public Func<ReviewViewModel, Task> ShowReview { get; set; } = _ => Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeyboard), nameof(IsNoDevice), nameof(ShowDevicePage), nameof(ShowPerformancePage), nameof(ShowLightingPage), nameof(ShowProfilesPage))]
    public partial KeyboardEditorViewModel? Keyboard { get; set; }

    [ObservableProperty]
    public partial ProfileLibraryViewModel? Profiles { get; set; }

    public bool HasKeyboard => Keyboard is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNoDevice))]
    public partial bool IsSearching { get; set; }

    public bool IsNoDevice => !IsSearching && Keyboard is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDevicePage), nameof(ShowPerformancePage), nameof(ShowLightingPage), nameof(ShowProfilesPage))]
    public partial int PageIndex { get; set; } = (int)Page.Device;

    // Without a keyboard every page shows the device page, which explains how to connect one.
    public bool ShowDevicePage => Keyboard is null || PageIndex == (int)Page.Device;
    public bool ShowPerformancePage => Keyboard is not null && PageIndex == (int)Page.Performance;
    public bool ShowLightingPage => Keyboard is not null && PageIndex == (int)Page.Lighting;
    public bool ShowProfilesPage => Keyboard is not null && PageIndex == (int)Page.Profiles;

    public bool IsNavigationCollapsed
    {
        get => settings.NavigationCollapsed;
        set
        {
            settings.NavigationCollapsed = value;
            OnPropertyChanged();
        }
    }

    public static bool CanStartWithWindows => Autostart.IsSupported;

    public bool StartWithWindows
    {
        get => Autostart.IsEnabled;
        set
        {
            try
            {
                Autostart.IsEnabled = value;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                UpdateMessage = Format(Strings.AutostartFailed, ex.Message);
            }
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void ToggleNavigation() => IsNavigationCollapsed = !IsNavigationCollapsed;

    [RelayCommand]
    private void ShowDevice() => PageIndex = (int)Page.Device;

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial string? UpdateMessage { get; set; }

    [ObservableProperty]
    public partial bool IsUpdateReady { get; set; }

    public bool AutoCheckForUpdates
    {
        get => settings.AutoCheckForUpdates;
        set
        {
            settings.AutoCheckForUpdates = value;
            OnPropertyChanged();
        }
    }

    public AutoSwitchViewModel AutoSwitch => field ??= new AutoSwitchViewModel(settings, () => Keyboard?.Settings,
        () => Keyboard is { } k && (k.IsBusy || k.Lighting.IsBusy || k.Roar.IsBusy), ForegroundProgram.Current, SaveSettings);

    private void SaveSettings()
    {
        try
        {
            settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The settings are saved again when the window closes.
        }
    }

    public async Task StartAsync()
    {
        // Polling is simpler than a WinEvent hook and a one-second delay is not noticeable when switching programs.
        _autoSwitchTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromSeconds(1), Avalonia.Threading.DispatcherPriority.Background,
            async (_, _) => await AutoSwitch.CheckAsync());
        await RefreshAsync();
        if (settings.AutoCheckForUpdates && _updates.IsInstalled)
            await CheckForUpdatesAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Status = Strings.Searching;
        // Drafts exist only in memory; replacing the editor would silently drop them.
        if (Keyboard is { PendingCount: > 0 })
        {
            Status = Strings.RefreshBlockedByPending;
            return;
        }
        Keyboard = null;
        Profiles = null;
        IsSearching = true;
        try
        {
            // HID enumeration blocks; keep the UI thread responsive.
            var candidates = await Task.Run(() => discovery.Discover());
            var target = candidates.FirstOrDefault(c => c.Definition!.Protocol == "rongyuan" && c.Definition.Layout.Count > 0);
            if (target is null)
            {
                Status = Strings.NoKeyboardFound;
                return;
            }
            Status = "";
            Keyboard = new KeyboardEditorViewModel(target, legends, transport, store, review => ShowReview(review));
            Profiles = new ProfileLibraryViewModel(Path.Combine(AppContext.BaseDirectory, "profiles"), Keyboard, () => PageIndex = (int)Page.Performance);
            if (PageIndex == (int)Page.Device)
                PageIndex = (int)Page.Performance;
            await Keyboard.LoadStateCommand.ExecuteAsync(null);
            await Keyboard.Lighting.LoadCommand.ExecuteAsync(null);
            await Keyboard.Roar.LoadCommand.ExecuteAsync(null);
            await Keyboard.Settings.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = Format(Strings.ListFailed, ex.Message);
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (!_updates.IsInstalled)
        {
            UpdateMessage = Strings.UpdatesNotInstalled;
            return;
        }

        try
        {
            _pendingUpdate = await _updates.CheckForUpdatesAsync();
            if (_pendingUpdate is null)
            {
                UpdateMessage = Strings.UpToDate;
                return;
            }

            UpdateMessage = Strings.UpdateDownloading;
            // Velopack verifies the package hash from the release manifest before it can be applied.
            await _updates.DownloadUpdatesAsync(_pendingUpdate);
            UpdateMessage = Format(Strings.UpdateAvailable, _pendingUpdate.TargetFullRelease.Version);
            IsUpdateReady = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            UpdateMessage = Format(Strings.UpdateFailed, ex.Message);
        }
    }

    // Only on explicit user request: a restart must never interrupt work in progress.
    [RelayCommand]
    private void InstallUpdate()
    {
        // Pending key changes exist only in memory; restarting would lose them.
        if (Keyboard is { PendingCount: > 0 })
        {
            UpdateMessage = Strings.UpdateBlockedByPending;
            return;
        }
        if (_pendingUpdate is not null)
            _updates.ApplyUpdatesAndRestart(_pendingUpdate);
    }

    [RelayCommand]
    private void PostponeUpdate()
    {
        IsUpdateReady = false;
        UpdateMessage = null;
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
