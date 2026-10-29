using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HallForge.App;

/// <summary>The setting a <see cref="TravelDiagram"/> illustrates.</summary>
public enum TravelKind
{
    Actuation,
    Release,
    RapidTriggerPress,
    RapidTriggerRelease,
    Deadzone,
}

/// <summary>
/// Side view of the key travel that shows where a setting acts: the line marks the value, the bar is the key and
/// lights up while the key registers. It animates one press only while <see cref="IsPlaying"/> is set and the
/// system allows animations; otherwise it shows a still frame. Purely decorative, so screen readers skip it.
/// </summary>
public sealed class TravelDiagram : Control
{
    public static readonly StyledProperty<TravelKind> KindProperty = AvaloniaProperty.Register<TravelDiagram, TravelKind>(nameof(Kind));
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<TravelDiagram, double>(nameof(Value));
    public static readonly StyledProperty<double> MaxTravelProperty = AvaloniaProperty.Register<TravelDiagram, double>(nameof(MaxTravel), 4);
    public static readonly StyledProperty<bool> IsPlayingProperty = AvaloniaProperty.Register<TravelDiagram, bool>(nameof(IsPlaying));
    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<TravelDiagram, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> KeyBrushProperty = AvaloniaProperty.Register<TravelDiagram, IBrush?>(nameof(KeyBrush));
    public static readonly StyledProperty<IBrush?> AccentBrushProperty = AvaloniaProperty.Register<TravelDiagram, IBrush?>(nameof(AccentBrush));

    private static readonly TimeSpan Cycle = TimeSpan.FromSeconds(2.4);
    private const double Inset = 5;
    private const double CapHeight = 5;
    // Rapid trigger is shown around this share of the travel, where a key usually is during fast repeats.
    private const double RapidTriggerDepth = 0.55;
    private TimeSpan? _start;
    private double? _phase;

    static TravelDiagram()
    {
        AffectsRender<TravelDiagram>(KindProperty, ValueProperty, MaxTravelProperty, TrackBrushProperty, KeyBrushProperty, AccentBrushProperty);
        Avalonia.Automation.AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<TravelDiagram>(Avalonia.Automation.AccessibilityView.Raw);
    }

    public TravelKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    /// <summary>The setting in millimeters.</summary>
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public double MaxTravel { get => GetValue(MaxTravelProperty); set => SetValue(MaxTravelProperty, value); }
    public bool IsPlaying { get => GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? KeyBrush { get => GetValue(KeyBrushProperty); set => SetValue(KeyBrushProperty, value); }
    public IBrush? AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    /// <summary>False when the user turned animations off in Windows ("Show animations in Windows").</summary>
    public static bool AnimationsAllowed { get; } = !OperatingSystem.IsWindows()
        || (SystemParametersInfo(GetClientAreaAnimation, 0, out var enabled, 0) && enabled);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsPlayingProperty)
            return;
        _start = null;
        _phase = null;
        InvalidateVisual();
        RequestFrame();
    }

    private void RequestFrame()
    {
        if (IsPlaying && AnimationsAllowed && TopLevel.GetTopLevel(this) is { } top)
            top.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan time)
    {
        if (!IsPlaying)
            return;
        _start ??= time;
        _phase = (time - _start.Value).TotalMilliseconds % Cycle.TotalMilliseconds / Cycle.TotalMilliseconds;
        InvalidateVisual();
        RequestFrame();
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var max = MaxTravel > 0 ? MaxTravel : 4;
        var value = Math.Clamp(Value, 0, max);
        var top = Inset;
        var height = Bounds.Height - 2 * Inset;
        double Y(double mm) => top + Math.Clamp(mm, 0, max) / max * height;

        var trackLeft = width * 0.28;
        var trackWidth = width * 0.26;
        context.DrawRectangle(TrackBrush, null, new RoundedRect(new Rect(trackLeft, top, trackWidth, height), 3));

        var accentPen = new Pen(AccentBrush, 1.5);
        if (Kind == TravelKind.Deadzone)
        {
            using (context.PushOpacity(0.35))
                context.DrawRectangle(AccentBrush, null, new Rect(trackLeft - 3, Y(value), trackWidth + 6, Y(max) - Y(value)));
        }
        // Rapid trigger sensitivities are distances, not positions, so they get only the bracket.
        if (Kind is not (TravelKind.RapidTriggerPress or TravelKind.RapidTriggerRelease))
            context.DrawLine(accentPen, new Point(1, Y(value)), new Point(trackLeft + trackWidth + 5, Y(value)));

        var (position, pressed, bracket) = Frame(value, max);
        if (bracket is var (from, to))
        {
            var x = trackLeft + trackWidth + 5;
            // Keep a very small distance visible; 0.1 mm is about one pixel at this size.
            var (y1, y2) = (Y(from), Math.Max(Y(to), Y(from) + 3));
            context.DrawLine(accentPen, new Point(x, y1), new Point(x, y2));
            context.DrawLine(accentPen, new Point(x - 3, y1), new Point(x, y1));
            context.DrawLine(accentPen, new Point(x - 3, y2), new Point(x, y2));
        }

        // Inside the deadzone the key moves but nothing registers.
        var ignored = Kind == TravelKind.Deadzone && position > value;
        using (context.PushOpacity(ignored ? 0.45 : 1))
            context.DrawRectangle(pressed ? AccentBrush : KeyBrush, null,
                new RoundedRect(new Rect(trackLeft - 4, Y(position) - CapHeight, trackWidth + 8, CapHeight), 2));
        context.DrawEllipse(pressed ? AccentBrush : TrackBrush, null, new Point(width - 4, top + 3), 3, 3);
    }

    /// <returns>Key depth in mm, whether it registers, and the distance to mark with a bracket.</returns>
    private (double Position, bool Pressed, (double From, double To)? Bracket) Frame(double value, double max)
    {
        var depth = max * RapidTriggerDepth;
        var sensitivity = Math.Min(value, depth);
        var rapidBracket = (depth - sensitivity, depth);
        if (_phase is not { } t)
        {
            return Kind switch
            {
                TravelKind.Actuation => (value, true, null),
                TravelKind.Release => (value, false, null),
                TravelKind.Deadzone => (max, true, null),
                _ => (depth, true, rapidBracket),
            };
        }

        switch (Kind)
        {
            case TravelKind.Actuation:
            {
                // Slow press past the line, quick return.
                var p = t < 0.6 ? Ease(t / 0.6) * max : t < 0.7 ? max : t < 0.85 ? (1 - Ease((t - 0.7) / 0.15)) * max : 0;
                return (p, p >= value, null);
            }
            case TravelKind.Release:
            {
                // Quick press, slow release past the line.
                var p = t < 0.15 ? Ease(t / 0.15) * max : t < 0.25 ? max : t < 0.85 ? (1 - Ease((t - 0.25) / 0.6)) * max : 0;
                return (p, p > value, null);
            }
            case TravelKind.Deadzone:
            {
                var p = t < 0.5 ? Ease(t / 0.5) * max : t < 0.6 ? max : t < 0.85 ? (1 - Ease((t - 0.6) / 0.25)) * max : 0;
                return (p, p >= max * 0.4, null);
            }
            default:
            {
                // Press, lift by the sensitivity (releases), press again by the sensitivity (registers again), return.
                var low = depth - sensitivity;
                return t switch
                {
                    < 0.25 => (Ease(t / 0.25) * depth, Ease(t / 0.25) * depth >= depth * 0.5, rapidBracket),
                    < 0.35 => (depth, true, rapidBracket),
                    < 0.5 => (depth - Ease((t - 0.35) / 0.15) * sensitivity, t < 0.49, rapidBracket),
                    < 0.6 => (low, false, rapidBracket),
                    < 0.75 => (low + Ease((t - 0.6) / 0.15) * sensitivity, t >= 0.74, rapidBracket),
                    < 0.8 => (depth, true, rapidBracket),
                    < 0.95 => ((1 - Ease((t - 0.8) / 0.15)) * depth, t < 0.82, rapidBracket),
                    _ => (0, false, rapidBracket),
                };
            }
        }
    }

    private static double Ease(double t) => t * t * (3 - 2 * t);

    private const uint GetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out bool value, uint winIni);
}
