using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HallForge.Core;

namespace HallForge.App;

/// <summary>One key on the keyboard view: position, label, known setting and pending change.</summary>
public sealed partial class KeyViewModel(KeyGeometry geometry, string legend) : ObservableObject
{
    public const double Pitch = 56;
    private const double Gap = 4;

    public string KeyId { get; } = geometry.Key;
    public string Legend { get; } = legend;
    public double Left { get; } = geometry.X * Pitch;
    public double Top { get; } = geometry.Y * Pitch;
    public double Width { get; } = geometry.W * Pitch - Gap;
    public double Height { get; } = geometry.H * Pitch - Gap;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Last applied or imported setting; null when unknown because the device cannot report it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Effective), nameof(ValueText), nameof(IsRapidTrigger), nameof(AccessibleName))]
    public partial KeyTrigger? Known { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Effective), nameof(ValueText), nameof(IsRapidTrigger), nameof(IsPending), nameof(AccessibleName))]
    public partial TriggerPatch? Pending { get; set; }

    public bool IsPending => Pending is not null;

    /// <summary>The setting after applying the pending change, or null while it is incomplete or unknown.</summary>
    public KeyTrigger? Effective => Pending is null ? Known : Pending.ApplyTo(Known);

    public string ValueText => Effective is { } t ? t.ActuationMm.ToString("0.0#", CultureInfo.CurrentCulture) : "?";

    public bool IsRapidTrigger => Effective?.RapidTrigger == true;

    public string AccessibleName => Text.Format(Strings.KeyAccessibleName, Legend,
        Effective is { } t ? Text.Format(Strings.KeyAccessibleValue, t.ActuationMm, t.RapidTrigger ? Strings.On : Strings.Off) : Strings.Unknown);
}
