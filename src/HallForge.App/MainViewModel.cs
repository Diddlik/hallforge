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

public sealed partial class MainViewModel(
    KeyboardDiscoveryService discovery, IHidTransport transport, StateStore store, KeyLegends legends, AppSettings settings) : ObservableObject
{
    private const string RepositoryUrl = "https://github.com/Diddlik/hallforge";

    private readonly UpdateManager _updates = new(new GithubSource(RepositoryUrl, null, prerelease: false));
    private UpdateInfo? _pendingUpdate;

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

    /// <summary>Set by the view; asks the user to confirm a write.</summary>
    public Func<ApplyRequest, Task<bool>> ConfirmApply { get; set; } = _ => Task.FromResult(false);

    [ObservableProperty]
    public partial KeyboardEditorViewModel? Keyboard { get; set; }

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

    public async Task StartAsync()
    {
        await RefreshAsync();
        if (settings.AutoCheckForUpdates && _updates.IsInstalled)
            await CheckForUpdatesAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Status = Strings.Searching;
        Keyboard = null;
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
            Keyboard = new KeyboardEditorViewModel(target, legends, transport, store, request => ConfirmApply(request));
            await Keyboard.LoadStateCommand.ExecuteAsync(null);
            await Keyboard.Lighting.LoadCommand.ExecuteAsync(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = Format(Strings.ListFailed, ex.Message);
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
