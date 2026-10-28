using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HallForge.Core;
using HallForge.Protocol.RongYuan;

namespace HallForge.App;

/// <summary>Shows what the user is asked to confirm before keys are written.</summary>
public sealed record ApplyRequest(string DeviceName, IReadOnlyList<string> Changes, bool RequiresExperimentalConsent);

/// <summary>
/// Edits the trigger settings of one keyboard. Changes stay in a draft until Apply, which validates the whole draft,
/// asks for confirmation and writes through the same <see cref="RongYuanSession"/> path as the CLI.
/// </summary>
public sealed partial class KeyboardEditorViewModel : ObservableObject
{
    private static readonly Dictionary<string, string[]> Presets = new()
    {
        ["WASD"] = ["KeyW", "KeyA", "KeyS", "KeyD"],
        ["Movement"] = ["KeyW", "KeyA", "KeyS", "KeyD", "ShiftLeft", "ControlLeft", "Space"],
        ["Actions"] = ["KeyQ", "KeyE", "KeyF", "KeyR"],
        ["Numbers"] = ["Digit1", "Digit2", "Digit3", "Digit4", "Digit5"],
    };

    private readonly KeyboardCandidate _target;
    private readonly IHidTransport _transport;
    private readonly StateStore _store;
    private readonly Func<ApplyRequest, Task<bool>> _confirm;
    private readonly DeviceDefinition _definition;
    private int _profile;
    private bool _refreshingEditor;

    public KeyboardEditorViewModel(KeyboardCandidate target, KeyLegends legends, IHidTransport transport, StateStore store, Func<ApplyRequest, Task<bool>> confirm)
    {
        _target = target;
        _definition = target.Definition!;
        _transport = transport;
        _store = store;
        _confirm = confirm;
        Keys = new ObservableCollection<KeyViewModel>(_definition.Layout.Select(g => new KeyViewModel(g, legends.For(g.Key))));
        foreach (var key in Keys)
            key.PropertyChanged += OnKeyChanged;
        CanvasWidth = _definition.Layout.Max(g => g.X + g.W) * KeyViewModel.Pitch;
        CanvasHeight = _definition.Layout.Max(g => g.Y + g.H) * KeyViewModel.Pitch;
        Limits = _definition.Limits;
    }

    public ObservableCollection<KeyViewModel> Keys { get; }
    public double CanvasWidth { get; }
    public double CanvasHeight { get; }
    public TriggerLimits Limits { get; }
    public string DeviceName => _definition.Name;
    public bool IsExperimental => !_definition.Verified;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(ImportBackupCommand), nameof(LoadStateCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial bool NeedsBackup { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApplyText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(DiscardCommand))]
    public partial int PendingCount { get; set; }

    public string ApplyText => Text.Format(Strings.ApplyCount, PendingCount);

    public bool HasSelection => Keys.Any(k => k.IsSelected);

    // Editor fields show the common value of the selection, or null when it differs or is unknown.
    [ObservableProperty]
    public partial decimal? Actuation { get; set; }

    [ObservableProperty]
    public partial decimal? Release { get; set; }

    [ObservableProperty]
    public partial bool? RapidTrigger { get; set; }

    [ObservableProperty]
    public partial decimal? RapidTriggerPress { get; set; }

    [ObservableProperty]
    public partial decimal? RapidTriggerRelease { get; set; }

    [ObservableProperty]
    public partial decimal? Deadzone { get; set; }

    partial void OnActuationChanged(decimal? value) => Edit(new TriggerPatch(ActuationMm: value), value is null);
    partial void OnReleaseChanged(decimal? value) => Edit(new TriggerPatch(ReleaseMm: value), value is null);
    partial void OnRapidTriggerChanged(bool? value) => Edit(new TriggerPatch(RapidTriggerEnabled: value), value is null);
    partial void OnRapidTriggerPressChanged(decimal? value) => Edit(new TriggerPatch(RapidTriggerPressMm: value), value is null);
    partial void OnRapidTriggerReleaseChanged(decimal? value) => Edit(new TriggerPatch(RapidTriggerReleaseMm: value), value is null);
    partial void OnDeadzoneChanged(decimal? value) => Edit(new TriggerPatch(DeadzoneMm: value), value is null);

    private void Edit(TriggerPatch patch, bool cleared)
    {
        if (_refreshingEditor || cleared)
            return;
        foreach (var key in Keys.Where(k => k.IsSelected))
            key.Pending = (key.Pending ?? new TriggerPatch()).Then(patch);
        PendingCount = Keys.Count(k => k.IsPending);
    }

    private void OnKeyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(HasSelection));
            RefreshEditor();
        }
    }

    private void RefreshEditor()
    {
        var selected = Keys.Where(k => k.IsSelected).Select(k => k.Effective).ToList();
        _refreshingEditor = true;
        Actuation = Common(selected, t => t.ActuationMm);
        Release = Common(selected, t => t.ReleaseMm);
        RapidTrigger = Common(selected, t => t.RapidTrigger);
        RapidTriggerPress = Common(selected, t => t.RapidTriggerPressMm);
        RapidTriggerRelease = Common(selected, t => t.RapidTriggerReleaseMm);
        Deadzone = Common(selected, t => t.DeadzoneMm);
        _refreshingEditor = false;
    }

    private static T? Common<T>(List<KeyTrigger?> triggers, Func<KeyTrigger, T> field) where T : struct =>
        triggers.Count > 0 && triggers.All(t => t is not null) && triggers.Select(t => field(t!)).Distinct().Count() == 1
            ? field(triggers[0]!)
            : null;

    [RelayCommand]
    private void Select(string preset)
    {
        var ids = preset switch
        {
            "All" => Keys.Select(k => k.KeyId).ToHashSet(),
            "None" => [],
            _ => Presets[preset].ToHashSet(),
        };
        foreach (var key in Keys)
            key.IsSelected = ids.Contains(key.KeyId);
    }

    [RelayCommand(CanExecute = nameof(CanDiscard))]
    private void Discard()
    {
        foreach (var key in Keys)
            key.Pending = null;
        PendingCount = 0;
        RefreshEditor();
    }

    private bool CanDiscard() => PendingCount > 0;

    /// <summary>Adds a profile to the draft; nothing is written until Apply.</summary>
    public void LoadProfile(string path)
    {
        try
        {
            var patches = KeyboardProfile.Load(path).Resolve();
            var unknown = patches.Keys.Where(id => Keys.All(k => k.KeyId != id)).ToList();
            foreach (var key in Keys)
                if (patches.TryGetValue(key.KeyId, out var patch))
                    key.Pending = (key.Pending ?? new TriggerPatch()).Then(patch);
            PendingCount = Keys.Count(k => k.IsPending);
            RefreshEditor();
            Status = unknown.Count == 0
                ? Text.Format(Strings.ProfileLoaded, Path.GetFileName(path))
                : Text.Format(Strings.ProfileKeysIgnored, string.Join(", ", unknown));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Status = Text.Format(Strings.ProfileLoadFailed, ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task LoadStateAsync()
    {
        IsBusy = true;
        Status = Strings.Connecting;
        try
        {
            var (state, hasBackup) = await Task.Run(() =>
            {
                using var session = RongYuanSession.Open(_transport, _target);
                _profile = session.Profile;
                return (_store.Load(_definition.Id, session.Profile), _store.HasBackup(_definition.Id, session.Profile));
            });
            foreach (var key in Keys)
                key.Known = state?.Keys.GetValueOrDefault(key.KeyId);
            NeedsBackup = !hasBackup;
            RefreshEditor();
            Status = Text.Format(hasBackup ? Strings.StateLoaded : Strings.NoBackupYet, _profile, state?.Keys.Count ?? 0);
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or DeviceNotSupportedException or InvalidDataException or UnauthorizedAccessException)
        {
            Status = Text.Format(Strings.ConnectFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ImportBackupAsync()
    {
        IsBusy = true;
        try
        {
            var result = await Task.Run(() =>
            {
                using var session = RongYuanSession.Open(_transport, _target);
                return session.ImportAkkoDriver(AkkoDriverDatabase.DefaultPath, _store);
            });
            IsBusy = false;
            await LoadStateAsync();
            Status = Text.Format(Strings.BackupImported, result.State.Keys.Count, result.BackupPath);
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or DeviceNotSupportedException or InvalidDataException or UnauthorizedAccessException)
        {
            Status = Text.Format(Strings.BackupImportFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool IsIdle() => !IsBusy;

    private bool CanApply() => !IsBusy && PendingCount > 0;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var state = _store.Load(_definition.Id, _profile);
        var draft = Keys.Where(k => k.Pending is not null).ToDictionary(k => k.KeyId, k => k.Pending!);
        var plan = ApplyPlan.Create(draft, _definition, state);
        if (!plan.CanApply)
        {
            Status = string.Join(Environment.NewLine, plan.Errors);
            return;
        }
        if (NeedsBackup)
        {
            Status = Strings.BackupRequired;
            return;
        }
        var lines = plan.Changes.Select(c => $"{Keys.First(k => k.KeyId == c.KeyId).Legend}: {Describe(c.Before)} → {Describe(c.After)}").ToList();
        if (!await _confirm(new ApplyRequest(_definition.Name, lines, IsExperimental)))
            return;

        IsBusy = true;
        var sent = 0;
        try
        {
            var progress = new Progress<int>(n => Status = Text.Format(Strings.Sending, n, plan.Changes.Count));
            await Task.Run(() =>
            {
                using var session = RongYuanSession.Open(_transport, _target);
                if (session.Profile != _profile)
                    throw new InvalidDataException(Text.Format(Strings.ProfileChanged, session.Profile));
                session.Apply(plan, _store, _ => ((IProgress<int>)progress).Report(++sent));
            });
            Status = Text.Format(Strings.Sent, sent);
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or DeviceNotSupportedException or InvalidDataException or UnauthorizedAccessException)
        {
            Status = Text.Format(Strings.ApplyFailed, ex.Message, sent);
        }
        finally
        {
            // Keys already sent are recorded in the state, so reloading shows exactly what the keyboard received.
            var saved = _store.Load(_definition.Id, _profile);
            foreach (var key in Keys)
            {
                key.Known = saved?.Keys.GetValueOrDefault(key.KeyId);
                if (key.Pending is not null && key.Pending.ApplyTo(key.Known) == key.Known)
                    key.Pending = null;
            }
            PendingCount = Keys.Count(k => k.IsPending);
            RefreshEditor();
            IsBusy = false;
        }
    }

    private static string Describe(KeyTrigger? t) => t is null
        ? Strings.Unknown
        : Text.Format(Strings.TriggerSummary, t.ActuationMm, t.ReleaseMm,
            t.RapidTrigger ? Strings.On : Strings.Off, t.RapidTriggerPressMm, t.RapidTriggerReleaseMm, t.DeadzoneMm);
}
