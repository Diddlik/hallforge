using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HallForge.Protocol.RongYuan;

namespace HallForge.App;

public sealed partial class ProgramRuleViewModel(string program, TravelModeItem mode, OnboardProfileItem profile, Action changed) : ObservableObject
{
    public string Program { get; } = program;
    public string RemoveName => Text.Format(Strings.AutoSwitchRemove, Program);

    [ObservableProperty]
    public partial TravelModeItem Mode { get; set; } = mode;

    [ObservableProperty]
    public partial OnboardProfileItem Profile { get; set; } = profile;

    partial void OnModeChanged(TravelModeItem value) => changed();
    partial void OnProfileChanged(OnboardProfileItem value) => changed();
}

/// <summary>
/// Opt-in automatic switching: when another program comes to the foreground, the travel mode and onboard profile
/// of its rule (or of "other programs") are applied. Each switch is one or two short commands that are read back,
/// never a rewrite of key settings, so it does not interrupt a game.
/// </summary>
/// <param name="keyboard">Settings of the connected keyboard, or null while none is connected.</param>
/// <param name="busy">True while another write to the keyboard is running.</param>
/// <param name="foreground">Name of the program in the foreground.</param>
/// <param name="save">Stores <paramref name="settings"/> after every change, so the tray "Exit" path keeps them too.</param>
public sealed partial class AutoSwitchViewModel : ObservableObject
{
    private static readonly string OwnProgram = Process.GetCurrentProcess().ProcessName;
    private readonly AppSettings _settings;
    private readonly Func<KeyboardSettingsViewModel?> _keyboard;
    private readonly Func<bool> _busy;
    private readonly Func<string?> _foreground;
    private readonly Action _save;
    private string? _lastProgram;
    private bool _loading;

    public AutoSwitchViewModel(AppSettings settings, Func<KeyboardSettingsViewModel?> keyboard, Func<bool> busy, Func<string?> foreground, Action save)
    {
        _settings = settings;
        _keyboard = keyboard;
        _busy = busy;
        _foreground = foreground;
        _save = save;
        // Settings come from a file; rules with values outside the known modes and profiles are dropped.
        Rules = new ObservableCollection<ProgramRuleViewModel>((settings.ProgramRules ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Program) && IsValid(r))
            .Select(r => new ProgramRuleViewModel(r.Program, ModeItem(r.Mode), ProfileItem(r.Profile), Persist)));
        var other = settings.OtherPrograms is { } o && IsValid(o) ? o : new ProgramRule("", MagnetismMode.Custom, 0);
        _loading = true;
        OtherMode = ModeItem(other.Mode);
        OtherProfile = ProfileItem(other.Profile);
        IsEnabled = settings.AutoSwitch;
        _loading = false;
    }

    public static bool IsSupported => ForegroundProgram.IsSupported;
    public static bool IsUnsupported => !IsSupported;

    /// <summary>Enabled and able to run; the setting is kept on other systems but has no effect there.</summary>
    public bool IsActive => IsEnabled && IsSupported;
    public ObservableCollection<ProgramRuleViewModel> Rules { get; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial TravelModeItem OtherMode { get; set; }

    [ObservableProperty]
    public partial OnboardProfileItem OtherProfile { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewProgram { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<string> RunningPrograms { get; set; } = [];

    partial void OnIsEnabledChanged(bool value) => Persist();
    partial void OnOtherModeChanged(TravelModeItem value) => Persist();
    partial void OnOtherProfileChanged(OnboardProfileItem value) => Persist();

    [RelayCommand]
    private void RefreshPrograms() => RunningPrograms = ForegroundProgram.WithWindows();

    private bool CanAdd() => Normalize(NewProgram) is { Length: > 0 } program
        && !Rules.Any(r => r.Program.Equals(program, StringComparison.OrdinalIgnoreCase));

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        Rules.Add(new ProgramRuleViewModel(Normalize(NewProgram), ModeItem(MagnetismMode.Custom), ProfileItem(0), Persist));
        NewProgram = "";
        Persist();
    }

    [RelayCommand]
    private void Remove(ProgramRuleViewModel rule)
    {
        Rules.Remove(rule);
        AddCommand.NotifyCanExecuteChanged();
        Persist();
    }

    /// <summary>Called periodically; applies the rule of a newly focused program.</summary>
    public async Task CheckAsync()
    {
        if (!IsEnabled || _keyboard() is not { } keyboard || _busy())
            return;
        var program = _foreground();
        // With HallForge in front the setting stays, so the user can edit rules without switching back and forth.
        if (program is null || program.Equals(OwnProgram, StringComparison.OrdinalIgnoreCase) || program == _lastProgram)
            return;
        var rule = Rules.FirstOrDefault(r => r.Program.Equals(program, StringComparison.OrdinalIgnoreCase));
        var (mode, profile) = rule is null ? (OtherMode, OtherProfile) : (rule.Mode, rule.Profile);
        // A failed write is not retried until another program comes to the front, so errors do not repeat every second.
        if (await keyboard.SwitchToAsync(mode.Mode, profile.Profile, program))
            _lastProgram = program;
    }

    private void Persist()
    {
        if (_loading)
            return;
        _settings.AutoSwitch = IsEnabled;
        _settings.ProgramRules = Rules.Select(r => new ProgramRule(r.Program, r.Mode.Mode, r.Profile.Profile)).ToList();
        _settings.OtherPrograms = new ProgramRule("", OtherMode.Mode, OtherProfile.Profile);
        // Re-evaluate the foreground program with the changed rules.
        _lastProgram = null;
        _save();
    }

    // Users may type the file name as shown in the task manager.
    private static string Normalize(string program)
    {
        var name = program.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static bool IsValid(ProgramRule rule) => Enum.IsDefined(rule.Mode) && rule.Profile is >= 0 and <= 3;
    private static TravelModeItem ModeItem(MagnetismMode mode) => KeyboardSettingsViewModel.AllModes.First(m => m.Mode == mode);
    private static OnboardProfileItem ProfileItem(int profile) => KeyboardSettingsViewModel.AllProfiles.First(p => p.Profile == profile);
}
