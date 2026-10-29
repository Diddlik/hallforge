using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HallForge.Core;
using HallForge.Protocol.RongYuan;

namespace HallForge.App;

public sealed record TravelModeItem(MagnetismMode Mode, string Name);

public sealed record OnboardProfileItem(byte Profile, string Name);

/// <summary>
/// Keyboard-wide settings that the firmware reports: travel mode (as Fn+1 to Fn+4) and the active onboard profile.
/// Each change is written right away and read back, so no review dialog is needed.
/// </summary>
/// <param name="hasDrafts">True while key changes are pending; a profile switch would apply them to another profile.</param>
/// <param name="profileSwitched">Reloads the key settings known for the new profile.</param>
public sealed partial class KeyboardSettingsViewModel(
    KeyboardCandidate target, IHidTransport transport, Func<bool> hasDrafts, Func<Task> profileSwitched) : ObservableObject
{
    private bool _showing;

    public bool RequiresExperimentalConsent => !target.Definition!.Verified;

    public static IReadOnlyList<TravelModeItem> AllModes { get; } = Enum.GetValues<MagnetismMode>()
        .Select(m => new TravelModeItem(m, Strings.ResourceManager.GetString($"TravelMode{m}", CultureInfo.CurrentUICulture) ?? m.ToString()))
        .ToList();

    public static IReadOnlyList<OnboardProfileItem> AllProfiles { get; } = Enumerable.Range(0, 4)
        .Select(p => new OnboardProfileItem((byte)p, Text.Format(Strings.OnboardProfileNumber, p + 1)))
        .ToList();


    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsLoaded { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotCustom))]
    public partial TravelModeItem? Mode { get; set; }

    /// <summary>Per-key settings take effect only in Custom mode.</summary>
    public bool IsNotCustom => Mode is { Mode: not MagnetismMode.Custom };

    [ObservableProperty]
    public partial OnboardProfileItem? Profile { get; set; }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var (mode, profile) = await Task.Run(() =>
            {
                using var session = RongYuanSession.Open(transport, target);
                return (session.Client.ReadMagnetismMode(), session.Profile);
            });
            Show(mode, profile);
            IsLoaded = true;
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or DeviceNotSupportedException or UnauthorizedAccessException)
        {
            Status = Text.Format(Strings.ConnectFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnModeChanged(TravelModeItem? oldValue, TravelModeItem? newValue)
    {
        if (_showing || oldValue is null || newValue is null)
            return;
        _ = WriteAsync(session => session.Client.WriteMagnetismMode(newValue.Mode),
            () => Show(oldValue.Mode, Profile!.Profile), Text.Format(Strings.TravelModeSwitched, newValue.Name), null);
    }

    partial void OnProfileChanged(OnboardProfileItem? oldValue, OnboardProfileItem? newValue)
    {
        if (_showing || oldValue is null || newValue is null)
            return;
        if (hasDrafts())
        {
            Status = Strings.ProfileSwitchBlockedByDrafts;
            // Deferred so the ComboBox finishes its own selection change before it is reverted.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Show(Mode!.Mode, oldValue.Profile));
            return;
        }
        _ = WriteAsync(session => session.Client.WriteActiveProfile(newValue.Profile),
            () => Show(Mode!.Mode, oldValue.Profile), Text.Format(Strings.ProfileSwitched, newValue.Profile + 1), profileSwitched);
    }

    /// <summary>
    /// Switches mode and profile for automatic switching. While key drafts exist the profile is left as it is,
    /// because switching it would show and apply the drafts against another profile.
    /// </summary>
    /// <returns>False when the keyboard is busy or not loaded, so the caller should try again later.</returns>
    public async Task<bool> SwitchToAsync(MagnetismMode mode, byte profile, string program)
    {
        if (!IsLoaded || IsBusy || Mode is not { } current || Profile is not { } currentProfile)
            return false;
        var switchMode = mode != current.Mode;
        var switchProfile = profile != currentProfile.Profile && !hasDrafts();
        if (!switchMode && !switchProfile)
            return true;
        IsBusy = true;
        try
        {
            await Task.Run(() =>
            {
                using var session = RongYuanSession.Open(transport, target);
                if (switchMode)
                    session.Client.WriteMagnetismMode(mode);
                if (switchProfile)
                    session.Client.WriteActiveProfile(profile);
            });
            Show(mode, switchProfile ? profile : currentProfile.Profile);
            Status = Text.Format(Strings.AutoSwitched, program, Mode!.Name, Profile!.Name);
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or DeviceNotSupportedException or UnauthorizedAccessException)
        {
            Status = Text.Format(Strings.KeyboardSettingFailed, ex.Message);
            switchProfile = false;
        }
        finally
        {
            IsBusy = false;
        }
        if (switchProfile)
            await profileSwitched();
        return true;
    }

    private async Task WriteAsync(Action<RongYuanSession> write, Action revert, string done, Func<Task>? then)
    {
        IsBusy = true;
        try
        {
            await Task.Run(() =>
            {
                using var session = RongYuanSession.Open(transport, target);
                write(session);
            });
            Status = done;
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or DeviceNotSupportedException or UnauthorizedAccessException)
        {
            Status = Text.Format(Strings.KeyboardSettingFailed, ex.Message);
            revert();
            return;
        }
        finally
        {
            IsBusy = false;
        }
        if (then is not null)
            await then();
    }

    private void Show(MagnetismMode mode, byte profile)
    {
        _showing = true;
        Mode = AllModes.First(m => m.Mode == mode);
        Profile = AllProfiles.First(p => p.Profile == profile);
        _showing = false;
    }
}
