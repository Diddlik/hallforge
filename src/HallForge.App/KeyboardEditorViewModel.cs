using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HallForge.Core;
using HallForge.Protocol.RongYuan;

namespace HallForge.App;

public enum KeySource
{
    AppState,
    Draft,
    Unknown,
}

/// <summary>Old value and, when the draft changes it, the new one.</summary>
public sealed record ValueChange(string Old, string? New)
{
    public bool IsChanged => New is not null;
}

public sealed record ValueFieldItem(KeyValueField Field, string Name);

public sealed partial class PresetItem(string id, string label) : ObservableObject
{
    public string Id { get; } = id;
    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsActive { get; set; }
}

public sealed record SelectionRow(string Key, ValueChange Actuation, ValueChange Release, ValueChange RapidTrigger, KeySource Source)
{
    public string SourceText => Source switch
    {
        KeySource.Draft => Strings.SourceDraft,
        KeySource.Unknown => Strings.SourceUnknown,
        _ => Strings.SourceAppState,
    };

    public string SourceHelp => Source switch
    {
        KeySource.Draft => Strings.SourceDraftHelp,
        KeySource.Unknown => Strings.SourceUnknownHelp,
        _ => Strings.SourceAppStateHelp,
    };

    public bool IsDraft => Source == KeySource.Draft;
    public bool IsUnknown => Source == KeySource.Unknown;
}

/// <summary>
/// Edits the trigger settings of one keyboard. Changes stay in a draft until Review &amp; Apply, which validates the
/// whole draft, shows every change and writes through the same <see cref="RongYuanSession"/> path as the CLI.
/// </summary>
public sealed partial class KeyboardEditorViewModel : ObservableObject
{
    private const int UndoLimit = 50;
    // Below these distances accidental or repeated presses become likely while typing.
    internal const decimal RiskyActuationMm = 0.6m;
    internal const decimal RiskyRapidTriggerMm = 0.2m;

    private static readonly Dictionary<string, string[]> Presets = new()
    {
        ["WASD"] = ["KeyW", "KeyA", "KeyS", "KeyD"],
        ["Movement"] = ["KeyW", "KeyA", "KeyS", "KeyD", "ShiftLeft", "ControlLeft", "Space"],
        ["Actions"] = ["KeyQ", "KeyE", "KeyR", "KeyF", "KeyG", "KeyC", "KeyV"],
        ["Numbers"] = ["Digit1", "Digit2", "Digit3", "Digit4", "Digit5"],
    };

    private static readonly Dictionary<string, TriggerPatch> Templates = new()
    {
        ["Comfort"] = new TriggerPatch(2.0m, 2.0m, false),
        ["Sensitive"] = new TriggerPatch(1.2m, 1.2m, true, 0.3m, 0.3m),
        ["Gaming"] = new TriggerPatch(0.6m, 0.6m, true, 0.2m, 0.2m),
    };

    private readonly KeyboardCandidate _target;
    private readonly IHidTransport _transport;
    private readonly StateStore _store;
    private readonly Func<ReviewViewModel, Task> _review;
    private readonly DeviceDefinition _definition;
    private readonly Stack<Dictionary<string, TriggerPatch?>> _undo = new();
    private string? _lastEdit;
    private KeyViewModel? _anchor;
    private bool _bulkSelect;

    public KeyboardEditorViewModel(KeyboardCandidate target, KeyLegends legends, IHidTransport transport, StateStore store, Func<ReviewViewModel, Task> review)
    {
        _target = target;
        _definition = target.Definition!;
        _transport = transport;
        _store = store;
        _review = review;
        Keys = new ObservableCollection<KeyViewModel>(_definition.Layout.Select(g => new KeyViewModel(g, legends.For(g.Key))));
        foreach (var key in Keys)
            key.PropertyChanged += OnKeyChanged;
        CanvasWidth = _definition.Layout.Max(g => g.X + g.W) * KeyViewModel.Pitch;
        CanvasHeight = _definition.Layout.Max(g => g.Y + g.H) * KeyViewModel.Pitch;
        var limits = _definition.Limits;
        var maxTravel = (double)limits.Travel.MaxMm;
        Actuation = new TriggerFieldViewModel(Strings.Actuation, Strings.ActuationHelp, limits.Travel,
            t => t.ActuationMm, mm => new TriggerPatch(ActuationMm: mm), Edit, Step) { Kind = TravelKind.Actuation, MaxTravel = maxTravel };
        Release = new TriggerFieldViewModel(Strings.Release, Strings.ReleaseHelp, limits.Travel,
            t => t.ReleaseMm, mm => new TriggerPatch(ReleaseMm: mm), Edit, Step) { Kind = TravelKind.Release, MaxTravel = maxTravel };
        RapidTriggerPress = new TriggerFieldViewModel(Strings.RapidTriggerPress, Strings.RapidTriggerPressHelp, limits.RapidTrigger,
            t => t.RapidTriggerPressMm, mm => new TriggerPatch(RapidTriggerPressMm: mm), Edit, Step) { Kind = TravelKind.RapidTriggerPress, MaxTravel = maxTravel };
        RapidTriggerRelease = new TriggerFieldViewModel(Strings.RapidTriggerRelease, Strings.RapidTriggerReleaseHelp, limits.RapidTrigger,
            t => t.RapidTriggerReleaseMm, mm => new TriggerPatch(RapidTriggerReleaseMm: mm), Edit, Step) { Kind = TravelKind.RapidTriggerRelease, MaxTravel = maxTravel };
        Deadzone = new TriggerFieldViewModel(Strings.Deadzone, Strings.DeadzoneHelp, limits.Deadzone,
            t => t.DeadzoneMm, mm => new TriggerPatch(DeadzoneMm: mm), Edit, Step) { Kind = TravelKind.Deadzone, MaxTravel = maxTravel };
        foreach (var field in Fields)
            field.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TriggerFieldViewModel.HasError))
                    HasInputError = Fields.Any(f => f.HasError);
            };
        Lighting = new LightingViewModel(target, transport, review);
        Settings = new KeyboardSettingsViewModel(target, transport, () => PendingCount > 0, () => LoadStateCommand.ExecuteAsync(null));
        Roar = new RoarViewModel(target, transport, review, () => Selected.Select(k => k.KeyId).ToList(),
            id => Keys.FirstOrDefault(k => k.KeyId == id) is { } key ? Name(key) : id);
        ShownValue = ValueFields[0];
        RefreshEditor(keepInput: false);
    }

    public LightingViewModel Lighting { get; }
    public RoarViewModel Roar { get; }
    public KeyboardSettingsViewModel Settings { get; }

    public ObservableCollection<KeyViewModel> Keys { get; }
    public double CanvasWidth { get; }
    public double CanvasHeight { get; }
    public string DeviceName => _definition.Name;
    public bool IsExperimental => !_definition.Verified;
    public string UsbId => $"{_target.Interface.VendorId:X4}:{_target.Interface.ProductId:X4}";
    public string ModelIds => string.Join(", ", _definition.FirmwareModelIds);
    public string LayoutSummary => Text.Format(Strings.LayoutSummary, Keys.Count);

    public TriggerFieldViewModel Actuation { get; }
    public TriggerFieldViewModel Release { get; }
    public TriggerFieldViewModel RapidTriggerPress { get; }
    public TriggerFieldViewModel RapidTriggerRelease { get; }
    public TriggerFieldViewModel Deadzone { get; }
    public IReadOnlyList<TriggerFieldViewModel> TravelFields => [Actuation, Release];
    public IReadOnlyList<TriggerFieldViewModel> RapidTriggerFields => [RapidTriggerPress, RapidTriggerRelease];
    private IEnumerable<TriggerFieldViewModel> Fields => [Actuation, Release, RapidTriggerPress, RapidTriggerRelease, Deadzone];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReviewCommand), nameof(ImportBackupCommand), nameof(LoadStateCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileText), nameof(HeaderInfo))]
    public partial int? Profile { get; set; }

    public string ProfileText => Profile is { } p ? Text.Format(Strings.OnboardProfile, p + 1) : Strings.OnboardProfileUnknown;
    public string HeaderInfo => $"{ProfileText} · {Strings.HeaderCapabilities}";

    [ObservableProperty]
    public partial bool NeedsBackup { get; set; }

    [ObservableProperty]
    public partial bool ShowValues { get; set; } = true;

    public IReadOnlyList<ValueFieldItem> ValueFields { get; } =
    [
        new(KeyValueField.Actuation, Strings.Actuation), new(KeyValueField.Release, Strings.Release),
        new(KeyValueField.RapidTriggerPress, Strings.RapidTriggerPress), new(KeyValueField.RapidTriggerRelease, Strings.RapidTriggerRelease),
        new(KeyValueField.Deadzone, Strings.Deadzone),
    ];

    [ObservableProperty]
    public partial ValueFieldItem? ShownValue { get; set; }

    partial void OnShownValueChanged(ValueFieldItem? value)
    {
        foreach (var key in Keys)
            key.ShownField = value?.Field ?? KeyValueField.Actuation;
    }

    // Draft summary for the pending-changes bar.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDrafts), nameof(DraftTitle))]
    [NotifyCanExecuteChangedFor(nameof(ReviewCommand), nameof(DiscardCommand))]
    public partial int PendingCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DraftTitle))]
    public partial int FieldChangeCount { get; set; }

    public bool HasDrafts => PendingCount > 0;
    public string DraftTitle => Text.Format(Strings.DraftSummary, PendingCount, FieldChangeCount);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial int UndoDepth { get; set; }

    // Selection panel.
    public ObservableCollection<SelectionRow> SelectionRows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetSelectionCommand), nameof(RapidTriggerOnCommand), nameof(RapidTriggerOffCommand), nameof(ApplyTemplateCommand))]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    public partial string SelectionTitle { get; set; } = "";

    [ObservableProperty]
    public partial string SelectionCounts { get; set; } = "";

    [ObservableProperty]
    public partial string ScopeText { get; set; } = "";

    public IReadOnlyList<PresetItem> QuickSelect { get; } =
    [
        new("WASD", Strings.PresetWasd), new("Movement", Strings.PresetMovement), new("Actions", Strings.PresetActions),
        new("Numbers", Strings.PresetNumbers), new("All", Strings.PresetAll), new("None", Strings.PresetNone),
    ];

    public IReadOnlyList<PresetItem> TemplateItems { get; } =
    [
        new("Comfort", Strings.TemplateComfort), new("Sensitive", Strings.TemplateSensitive), new("Gaming", Strings.TemplateGaming),
    ];

    // Rapid trigger state of the selection: true, false, or null when mixed, unknown or nothing is selected.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRapidTriggerOn), nameof(IsRapidTriggerOff))]
    public partial bool? RapidTrigger { get; set; }

    public bool IsRapidTriggerOn => RapidTrigger == true;
    public bool IsRapidTriggerOff => RapidTrigger == false;

    [ObservableProperty]
    public partial string RapidTriggerNote { get; set; } = "";

    [ObservableProperty]
    public partial bool RapidTriggerNoteIsWarning { get; set; }

    [ObservableProperty]
    public partial string RiskText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasRisk { get; set; }

    [ObservableProperty]
    public partial bool HasInputError { get; set; }

    private IEnumerable<KeyViewModel> Selected => Keys.Where(k => k.IsSelected);

    private void OnKeyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyViewModel.IsSelected) && !_bulkSelect)
        {
            _lastEdit = null;
            RefreshEditor(keepInput: false);
        }
    }

    /// <summary>Click selects one key, <paramref name="toggle"/> adds or removes it, <paramref name="extend"/> selects the range from the last clicked key.</summary>
    public void Pick(KeyViewModel key, bool toggle, bool extend)
    {
        if (extend && _anchor is not null)
        {
            var from = Keys.IndexOf(_anchor);
            var to = Keys.IndexOf(key);
            var range = Keys.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1).ToHashSet();
            SetSelection(k => k.IsSelected || range.Contains(k));
            return;
        }
        _anchor = key;
        if (toggle)
            key.IsSelected = !key.IsSelected;
        else
            SetSelection(k => k == key);
    }

    /// <summary>Selects the keys that overlap a dragged rectangle, in keyboard canvas coordinates.</summary>
    /// <param name="add">Keep the current selection and add the keys, instead of replacing it.</param>
    public void SelectArea(double x, double y, double width, double height, bool add)
    {
        bool Overlaps(KeyViewModel k) => k.Left < x + width && k.Left + k.Width > x && k.Top < y + height && k.Top + k.Height > y;
        _anchor = Keys.FirstOrDefault(Overlaps) ?? _anchor;
        SetSelection(k => (add && k.IsSelected) || Overlaps(k));
    }

    [RelayCommand]
    private void Select(string preset)
    {
        var ids = preset switch
        {
            "All" => Keys.Select(k => k.KeyId).ToHashSet(),
            "None" => [],
            _ => Presets[preset].ToHashSet(),
        };
        _anchor = Keys.FirstOrDefault(k => ids.Contains(k.KeyId));
        SetSelection(k => ids.Contains(k.KeyId));
    }

    private void SetSelection(Func<KeyViewModel, bool> selected)
    {
        _bulkSelect = true;
        foreach (var key in Keys)
            key.IsSelected = selected(key);
        _bulkSelect = false;
        _lastEdit = null;
        RefreshEditor(keepInput: false);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RapidTriggerOn() => Edit(new TriggerPatch(RapidTriggerEnabled: true), null);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RapidTriggerOff() => Edit(new TriggerPatch(RapidTriggerEnabled: false), null);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ApplyTemplate(string name) => Edit(Templates[name], null);

    /// <param name="field">Consecutive edits of the same field on the same selection form one undo step, so dragging a slider is undone at once.</param>
    private void Edit(TriggerPatch patch, string? field)
    {
        var selected = Selected.ToList();
        if (selected.Count == 0)
            return;
        if (field is null || field != _lastEdit)
            Remember();
        _lastEdit = field;
        foreach (var key in selected)
            key.Pending = Trim((key.Pending ?? new TriggerPatch()).Then(patch), key.Known);
        RefreshEditor(keepInput: true);
    }

    /// <summary>Moves every selected key with a known value one step, each from its own value; unknown keys are left alone.</summary>
    private void Step(TriggerFieldViewModel field, int direction)
    {
        var known = Selected.Where(k => k.Effective is not null).ToList();
        if (known.Count == 0)
            return;
        var undoKey = field.Label + "±";
        if (undoKey != _lastEdit)
            Remember();
        _lastEdit = undoKey;
        foreach (var key in known)
        {
            var value = field.Snap(field.Read(key.Effective!) + direction * field.Range.StepMm);
            key.Pending = Trim((key.Pending ?? new TriggerPatch()).Then(field.Patch(value)), key.Known);
        }
        RefreshEditor(keepInput: false);
    }

    /// <summary>Drops fields that equal the known setting, so a draft only holds real changes.</summary>
    private static TriggerPatch? Trim(TriggerPatch patch, KeyTrigger? known)
    {
        if (known is not null)
            patch = new TriggerPatch(
                patch.ActuationMm == known.ActuationMm ? null : patch.ActuationMm,
                patch.ReleaseMm == known.ReleaseMm ? null : patch.ReleaseMm,
                patch.RapidTriggerEnabled == known.RapidTrigger ? null : patch.RapidTriggerEnabled,
                patch.RapidTriggerPressMm == known.RapidTriggerPressMm ? null : patch.RapidTriggerPressMm,
                patch.RapidTriggerReleaseMm == known.RapidTriggerReleaseMm ? null : patch.RapidTriggerReleaseMm,
                patch.DeadzoneMm == known.DeadzoneMm ? null : patch.DeadzoneMm);
        return patch == new TriggerPatch() ? null : patch;
    }

    private static int FieldCount(TriggerPatch? p) => p is null ? 0 :
        new object?[] { p.ActuationMm, p.ReleaseMm, p.RapidTriggerEnabled, p.RapidTriggerPressMm, p.RapidTriggerReleaseMm, p.DeadzoneMm }.Count(v => v is not null);

    private void Remember()
    {
        if (_undo.Count == UndoLimit)
        {
            var kept = _undo.Reverse().Skip(1).ToList();
            _undo.Clear();
            foreach (var snapshot in kept)
                _undo.Push(snapshot);
        }
        _undo.Push(Keys.ToDictionary(k => k.KeyId, k => k.Pending));
        UndoDepth = _undo.Count;
    }

    private bool CanUndo() => UndoDepth > 0;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        var snapshot = _undo.Pop();
        UndoDepth = _undo.Count;
        foreach (var key in Keys)
            key.Pending = snapshot.GetValueOrDefault(key.KeyId);
        _lastEdit = null;
        RefreshEditor(keepInput: false);
    }

    [RelayCommand(CanExecute = nameof(HasDrafts))]
    private void Discard()
    {
        Remember();
        foreach (var key in Keys)
            key.Pending = null;
        _lastEdit = null;
        RefreshEditor(keepInput: false);
    }

    private bool CanResetSelection() => Selected.Any(k => k.IsPending);

    [RelayCommand(CanExecute = nameof(CanResetSelection))]
    private void ResetSelection()
    {
        Remember();
        foreach (var key in Selected)
            key.Pending = null;
        _lastEdit = null;
        RefreshEditor(keepInput: false);
    }

    private void RefreshEditor(bool keepInput)
    {
        var selected = Selected.ToList();
        var triggers = selected.Select(k => k.Effective).ToList();
        HasSelection = selected.Count > 0;

        var rapidTrigger = triggers.Select(t => t?.RapidTrigger).ToList();
        RapidTrigger = rapidTrigger.Count > 0 && rapidTrigger.All(v => v is not null) && rapidTrigger.Distinct().Count() == 1 ? rapidTrigger[0] : null;
        RapidTriggerNoteIsWarning = false;
        if (selected.Count == 0)
            RapidTriggerNote = Strings.SelectKeysToEdit;
        else if (RapidTrigger is { } on)
            RapidTriggerNote = on ? Strings.RapidTriggerOnNote : Strings.RapidTriggerOffNote;
        else
        {
            var unknown = rapidTrigger.Count(v => v is null);
            RapidTriggerNote = unknown == rapidTrigger.Count
                ? Strings.FieldUnknown
                : Text.Format(Strings.RapidTriggerMixed, rapidTrigger.Count(v => v == true), rapidTrigger.Count(v => v == false), unknown);
            RapidTriggerNoteIsWarning = true;
        }

        Actuation.Refresh(triggers, false, keepInput);
        Release.Refresh(triggers, false, keepInput);
        RapidTriggerPress.Refresh(triggers, RapidTrigger == false, keepInput);
        RapidTriggerRelease.Refresh(triggers, RapidTrigger == false, keepInput);
        Deadzone.Refresh(triggers, false, keepInput);

        var risky = selected.Count(k => k.Effective is { } t
            && (t.ActuationMm < RiskyActuationMm || (t.RapidTrigger && (t.RapidTriggerPressMm < RiskyRapidTriggerMm || t.RapidTriggerReleaseMm < RiskyRapidTriggerMm))));
        HasRisk = risky > 0;
        RiskText = Text.Format(Strings.RiskWarning, risky);

        SelectionRows.Clear();
        foreach (var key in selected)
            SelectionRows.Add(Row(key));
        var names = string.Join(", ", selected.Take(6).Select(k => Name(k))) + (selected.Count > 6 ? $" +{selected.Count - 6}" : "");
        SelectionTitle = selected.Count == 1 ? Text.Format(Strings.OneKeySelected, names) : Text.Format(Strings.KeysSelected, selected.Count, names);
        SelectionCounts = Text.Format(Strings.SelectionCounts, selected.Count(k => k.Known is null && !k.IsPending));
        ScopeText = selected.Count == 0 ? Strings.NoKeysSelected : Text.Format(Strings.SelectionScope, selected.Count);
        var ids = selected.Select(k => k.KeyId).ToHashSet();
        foreach (var preset in QuickSelect)
            preset.IsActive = preset.Id == "All" ? ids.Count == Keys.Count : Presets.TryGetValue(preset.Id, out var keys) && ids.SetEquals(keys);

        PendingCount = Keys.Count(k => k.IsPending);
        FieldChangeCount = Keys.Sum(k => FieldCount(k.Pending));
        ResetSelectionCommand.NotifyCanExecuteChanged();
    }

    private static string Name(KeyViewModel key) => key.Legend.Length > 0 ? key.Legend : key.KeyId;

    private static SelectionRow Row(KeyViewModel key)
    {
        string Old<T>(Func<KeyTrigger, T> field, Func<T, string> format) => key.Known is { } k ? format(field(k)) : Strings.Unknown;
        string? New<T>(T? value, Func<T, string> format) where T : struct => value is { } v ? format(v) : null;
        var p = key.Pending;
        return new SelectionRow(Name(key),
            new ValueChange(Old(t => t.ActuationMm, Mm.Format), New(p?.ActuationMm, Mm.Format)),
            new ValueChange(Old(t => t.ReleaseMm, Mm.Format), New(p?.ReleaseMm, Mm.Format)),
            new ValueChange(Old(t => t.RapidTrigger, OnOff), New(p?.RapidTriggerEnabled, OnOff)),
            key.IsPending ? KeySource.Draft : key.Known is null ? KeySource.Unknown : KeySource.AppState);
    }

    private static string OnOff(bool on) => on ? Strings.On : Strings.Off;

    /// <summary>Adds a profile to the draft; nothing is written until Review &amp; Apply.</summary>
    /// <returns>Number of keys the profile changed in the draft.</returns>
    public int LoadProfile(string path)
    {
        try
        {
            var patches = KeyboardProfile.Load(path).Resolve();
            var unknown = patches.Keys.Where(id => Keys.All(k => k.KeyId != id)).ToList();
            Remember();
            _lastEdit = null;
            var loaded = 0;
            foreach (var key in Keys)
                if (patches.TryGetValue(key.KeyId, out var patch))
                {
                    key.Pending = Trim((key.Pending ?? new TriggerPatch()).Then(patch), key.Known);
                    loaded++;
                }
            RefreshEditor(keepInput: false);
            Status = unknown.Count == 0
                ? Text.Format(Strings.ProfileLoaded, Path.GetFileName(path))
                : Text.Format(Strings.ProfileKeysIgnored, string.Join(", ", unknown));
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Status = Text.Format(Strings.ProfileLoadFailed, ex.Message);
            return 0;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task LoadStateAsync()
    {
        IsBusy = true;
        Status = Strings.Connecting;
        try
        {
            var (profile, state, hasBackup) = await Task.Run(() =>
            {
                using var session = RongYuanSession.Open(_transport, _target);
                return (session.Profile, _store.Load(_definition.Id, session.Profile), _store.HasBackup(_definition.Id, session.Profile));
            });
            Profile = profile;
            foreach (var key in Keys)
                key.Known = state?.Keys.GetValueOrDefault(key.KeyId);
            NeedsBackup = !hasBackup;
            RefreshEditor(keepInput: false);
            Status = hasBackup ? "" : Strings.NoBackupYet;
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

    private bool CanReview() => !IsBusy && PendingCount > 0;

    [RelayCommand(CanExecute = nameof(CanReview))]
    private async Task ReviewAsync()
    {
        if (HasInputError)
        {
            Status = Strings.FixInput;
            return;
        }
        if (Profile is not { } profile)
        {
            Status = Strings.NotConnected;
            return;
        }
        var state = _store.Load(_definition.Id, profile);
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
        if (plan.Changes.Count == 0)
        {
            Status = Strings.NothingToApply;
            return;
        }

        var changes = plan.Changes.Select(c => (Change: c, Fields: ChangedFields(c.Before, c.After))).ToList();
        var rows = changes.Select(c => new ReviewRow(Name(Keys.First(k => k.KeyId == c.Change.KeyId)), string.Join(" · ", c.Fields), Untouched(c.Change.Before, c.Change.After))).ToList();
        var notes = new List<string> { Strings.ReviewNoReadback, Strings.ReviewBackup };
        if (plan.Changes.Any(c => c.Before is null))
            notes.Add(Strings.ReviewUnknownKeys);
        var acknowledgement = IsExperimental ? Strings.ReviewAcknowledgeExperimental : Strings.ReviewAcknowledge;
        var review = new ReviewViewModel(Text.Format(Strings.ReviewTarget, DeviceName, profile + 1), rows,
            Text.Format(Strings.ReviewSummary, plan.Changes.Count, changes.Sum(c => c.Fields.Count)),
            notes, acknowledgement, Text.Format(Strings.ReviewSend, plan.Changes.Count),
            (r, cancellation) => SendAsync(plan, profile, r, cancellation));
        await _review(review);
    }

    private async Task SendAsync(ApplyPlan plan, int profile, ReviewViewModel review, CancellationToken cancellation)
    {
        IsBusy = true;
        var sent = 0;
        var opened = false;
        var progress = new Progress<int>(_ => review.Report(ReviewRowStatus.Sent));
        try
        {
            await Task.Run(() =>
            {
                using var session = RongYuanSession.Open(_transport, _target);
                if (session.Profile != profile)
                    throw new InvalidDataException(Text.Format(Strings.ProfileChanged, session.Profile + 1));
                opened = true;
                session.Apply(plan, _store, _ => ((IProgress<int>)progress).Report(Interlocked.Increment(ref sent)), cancellation);
            }, CancellationToken.None);
            review.Finish(sent, ReviewRowStatus.Sent, false, Text.Format(Strings.Sent, sent));
            Status = Text.Format(Strings.Sent, sent);
        }
        catch (OperationCanceledException)
        {
            review.Finish(sent, ReviewRowStatus.Sent, false, Text.Format(Strings.SendStopped, sent));
            Status = Text.Format(Strings.SendStopped, sent);
        }
        catch (Exception ex) when (ex is IOException or ProtocolException or DeviceNotSupportedException or InvalidDataException or UnauthorizedAccessException)
        {
            // A failed write may or may not have reached the keyboard; its draft stays so it can be retried.
            review.Finish(sent, ReviewRowStatus.Sent, opened, Text.Format(Strings.ApplyFailed, ex.Message, sent));
            Status = Text.Format(Strings.ApplyFailed, ex.Message, sent);
        }
        finally
        {
            // Keys already sent are recorded in the state, so reloading shows exactly what the keyboard received.
            var saved = _store.Load(_definition.Id, profile);
            foreach (var key in Keys)
            {
                key.Known = saved?.Keys.GetValueOrDefault(key.KeyId);
                key.Pending = key.Pending is null ? null : Trim(key.Pending, key.Known);
            }
            _undo.Clear();
            UndoDepth = 0;
            RefreshEditor(keepInput: false);
            IsBusy = false;
        }
    }

    private static List<string> ChangedFields(KeyTrigger? before, KeyTrigger after)
    {
        var changes = new List<string>();
        void Add(string name, string? old, string now)
        {
            if (old != now)
                changes.Add(old is null ? $"{name} {now}" : $"{name} {old} → {now}");
        }
        Add(Strings.Actuation, before is null ? null : Mm.Format(before.ActuationMm), Mm.Format(after.ActuationMm));
        Add(Strings.Release, before is null ? null : Mm.Format(before.ReleaseMm), Mm.Format(after.ReleaseMm));
        Add(Strings.RapidTrigger, before is null ? null : OnOff(before.RapidTrigger), OnOff(after.RapidTrigger));
        Add(Strings.RapidTriggerPress, before is null ? null : Mm.Format(before.RapidTriggerPressMm), Mm.Format(after.RapidTriggerPressMm));
        Add(Strings.RapidTriggerRelease, before is null ? null : Mm.Format(before.RapidTriggerReleaseMm), Mm.Format(after.RapidTriggerReleaseMm));
        Add(Strings.Deadzone, before is null ? null : Mm.Format(before.DeadzoneMm), Mm.Format(after.DeadzoneMm));
        return changes;
    }

    private static string Untouched(KeyTrigger? before, KeyTrigger after)
    {
        if (before is null)
            return "—";
        var same = new List<string>();
        if (before.ActuationMm == after.ActuationMm) same.Add(Strings.Actuation);
        if (before.ReleaseMm == after.ReleaseMm) same.Add(Strings.Release);
        if (before.RapidTrigger == after.RapidTrigger) same.Add(Strings.RapidTrigger);
        if (before.RapidTriggerPressMm == after.RapidTriggerPressMm) same.Add(Strings.RapidTriggerPress);
        if (before.RapidTriggerReleaseMm == after.RapidTriggerReleaseMm) same.Add(Strings.RapidTriggerRelease);
        if (before.DeadzoneMm == after.DeadzoneMm) same.Add(Strings.Deadzone);
        return same.Count == 0 ? "—" : string.Join(", ", same);
    }
}
