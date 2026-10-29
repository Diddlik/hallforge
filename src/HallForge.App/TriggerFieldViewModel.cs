using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HallForge.Core;

namespace HallForge.App;

/// <summary>
/// One millimeter setting of the selected keys: typed value, slider and step buttons. It shows the common value,
/// or explains a mixed or unknown selection. A typed value or slider position is applied to every selected key;
/// the step buttons move each key from its own value, so mixed selections can be shifted together.
/// </summary>
public sealed partial class TriggerFieldViewModel(
    string label, string help, TravelRange range, Func<KeyTrigger, decimal> read, Func<decimal, TriggerPatch> patch,
    Action<TriggerPatch, string> edit, Action<TriggerFieldViewModel, int> step)
    : ObservableObject
{
    private bool _refreshing;

    public string Label { get; } = label;
    public TravelKind Kind { get; init; }
    public double MaxTravel { get; init; } = 4;
    public string Help { get; } = help;
    public TravelRange Range { get; } = range;
    public double Minimum => (double)Range.MinMm;
    public double Maximum => (double)Range.MaxMm;
    public double Step => (double)Range.StepMm;
    public decimal Read(KeyTrigger trigger) => read(trigger);
    public TriggerPatch Patch(decimal mm) => patch(mm);

    public string RangeText => $"{Mm.Format(Range.MinMm)}–{Mm.Format(Range.MaxMm)}";
    public string DecreaseName => Text.Format(Strings.DecreaseField, Label, Mm.Format(Range.StepMm));
    public string IncreaseName => Text.Format(Strings.IncreaseField, Label, Mm.Format(Range.StepMm));

    /// <summary>The value shared by all selected keys; null when nothing is selected, values differ or are unknown.</summary>
    [ObservableProperty]
    public partial decimal? Value { get; set; }

    /// <summary>At least one selected key has a known value, so the step buttons have something to move.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(IncreaseCommand), nameof(DecreaseCommand))]
    public partial bool CanStep { get; set; }

    [ObservableProperty]
    public partial string Input { get; set; } = "";

    [ObservableProperty]
    public partial double SliderValue { get; set; }

    [ObservableProperty]
    public partial string Placeholder { get; set; } = "—";

    [ObservableProperty]
    public partial string Note { get; set; } = "";

    [ObservableProperty]
    public partial bool IsWarning { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial bool IsInactive { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    /// <param name="selected">Effective settings of the selected keys; null entries are unknown.</param>
    /// <param name="inactive">The setting has no effect for this selection (rapid trigger sensitivity while it is off).</param>
    /// <param name="keepInput">Keep text the user is still typing instead of replacing it with the stored value.</param>
    internal void Refresh(IReadOnlyList<KeyTrigger?> selected, bool inactive, bool keepInput)
    {
        _refreshing = true;
        var known = selected.Where(t => t is not null).Select(t => read(t!)).ToList();
        var unknown = selected.Count - known.Count;
        var distinct = known.Distinct().ToList();
        Value = distinct.Count == 1 && unknown == 0 ? distinct[0] : null;
        CanStep = known.Count > 0;
        IsEnabled = selected.Count > 0;
        IsInactive = inactive && Value is not null;
        IsWarning = false;
        Placeholder = "—";
        if (selected.Count == 0)
            Note = Strings.SelectKeysToEdit;
        else if (known.Count == 0)
        {
            Placeholder = "?";
            Note = Strings.FieldUnknown;
            IsWarning = true;
        }
        else if (Value is null)
        {
            Placeholder = Strings.Mixed;
            var spread = known.Min() == known.Max() ? Mm.Format(known.Min()) : $"{Mm.Format(known.Min())}–{Mm.Format(known.Max())}";
            Note = unknown > 0 ? Text.Format(Strings.FieldMixedUnknown, spread, unknown) : Text.Format(Strings.FieldMixed, spread);
            IsWarning = true;
        }
        else
            Note = IsInactive ? Strings.FieldInactive : selected.Count > 1 ? Text.Format(Strings.FieldSame, selected.Count) : "";

        if (keepInput && HasError)
            Note = Text.Format(Strings.FieldRangeError, Mm.Format(Range.MinMm), Mm.Format(Range.MaxMm), Mm.Format(Range.StepMm));
        else
        {
            HasError = false;
            // Leave typed text alone while it already means the stored value, so the caret does not jump.
            if (!(Millimeters.TryParse(Input, out var typed) && typed == Value))
                Input = Value is { } v ? Mm.Format(v) : "";
        }
        SliderValue = (double)(Value ?? (known.Count > 0 ? known.Min() : Range.MinMm));
        _refreshing = false;
    }

    partial void OnInputChanged(string value)
    {
        if (_refreshing)
            return;
        if (!Millimeters.TryParse(value, out var mm) || !Range.Allows(mm))
        {
            HasError = true;
            Note = Text.Format(Strings.FieldRangeError, Mm.Format(Range.MinMm), Mm.Format(Range.MaxMm), Mm.Format(Range.StepMm));
            return;
        }
        HasError = false;
        if (mm != Value)
            edit(patch(mm), Label);
    }

    partial void OnSliderValueChanged(double value)
    {
        if (_refreshing)
            return;
        var mm = Snap((decimal)value);
        if (mm != Value)
            edit(patch(mm), Label);
    }

    [RelayCommand(CanExecute = nameof(CanStep))]
    private void Increase() => step(this, 1);

    [RelayCommand(CanExecute = nameof(CanStep))]
    private void Decrease() => step(this, -1);

    public decimal Snap(decimal mm) =>
        Math.Clamp(Range.MinMm + Math.Round((mm - Range.MinMm) / Range.StepMm) * Range.StepMm, Range.MinMm, Range.MaxMm);
}
