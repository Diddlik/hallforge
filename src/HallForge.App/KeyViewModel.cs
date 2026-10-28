using CommunityToolkit.Mvvm.ComponentModel;
using HallForge.Core;

namespace HallForge.App;

/// <summary>One key on the keyboard view: position, label, known setting and pending change.</summary>
public sealed partial class KeyViewModel(KeyGeometry geometry, string legend) : ObservableObject
{
    public const double Pitch = 52;
    private const double Gap = 5;

    public string KeyId { get; } = geometry.Key;
    public string Legend { get; } = legend;
    public double Left { get; } = geometry.X * Pitch;
    public double Top { get; } = geometry.Y * Pitch;
    public double Width { get; } = geometry.W * Pitch - Gap;
    public double Height { get; } = geometry.H * Pitch - Gap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    public partial bool IsSelected { get; set; }

    /// <summary>Last applied or imported setting; null when unknown because the device cannot report it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Effective), nameof(ValueText), nameof(IsUnknown), nameof(AccessibleName))]
    public partial KeyTrigger? Known { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Effective), nameof(ValueText), nameof(IsPending), nameof(IsUnknown), nameof(AccessibleName))]
    public partial TriggerPatch? Pending { get; set; }

    public bool IsPending => Pending is not null;

    /// <summary>Neither HallForge's state nor a draft says what this key is set to.</summary>
    public bool IsUnknown => Known is null && Pending is null;

    /// <summary>The setting after applying the pending change, or null while it is incomplete or unknown.</summary>
    public KeyTrigger? Effective => Pending is null ? Known : Pending.ApplyTo(Known);

    public string ValueText => (Pending?.ActuationMm ?? Effective?.ActuationMm) is { } mm ? Mm.Format(mm) : "?";

    public string AccessibleName
    {
        get
        {
            var parts = new List<string> { Legend.Length > 0 ? Legend : KeyId };
            if (IsSelected)
                parts.Add(Strings.KeySelected);
            if (IsPending)
                parts.Add(Strings.KeyModified);
            parts.Add(Effective is { } t
                ? Text.Format(Strings.KeyAccessibleValue, Mm.Format(t.ActuationMm), Mm.Format(t.ReleaseMm), t.RapidTrigger ? Strings.On : Strings.Off)
                : Strings.KeyUnknown);
            return string.Join(", ", parts);
        }
    }
}
