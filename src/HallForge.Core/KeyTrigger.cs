namespace HallForge.Core;

/// <summary>
/// Complete trigger configuration of one key. Some firmware (e.g. AKKO model 1778) cannot read these values
/// back and only accepts all fields at once, so a write always carries the full state, never a partial patch.
/// </summary>
/// <param name="RapidTriggerPressMm">Rapid trigger sensitivity, not an absolute position.</param>
public sealed record KeyTrigger(
    decimal ActuationMm,
    decimal ReleaseMm,
    bool RapidTrigger,
    decimal RapidTriggerPressMm,
    decimal RapidTriggerReleaseMm,
    decimal DeadzoneMm);

public sealed record TravelRange(decimal MinMm, decimal MaxMm, decimal StepMm)
{
    public bool Allows(decimal mm) => mm >= MinMm && mm <= MaxMm && (mm - MinMm) % StepMm == 0;

    // Invariant so messages use the same '.' separator that input requires.
    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{MinMm}-{MaxMm} mm in steps of {StepMm} mm");
}

public sealed record TriggerLimits(TravelRange Travel, TravelRange RapidTrigger, TravelRange Deadzone)
{
    /// <summary>Largest switch matrix supported by known firmware (21 columns x 6 rows).</summary>
    public const int MatrixSize = 126;

    public IReadOnlyList<string> Validate(KeyTrigger trigger)
    {
        var errors = new List<string>();
        Check(Travel, trigger.ActuationMm, "Actuation");
        Check(Travel, trigger.ReleaseMm, "Release");
        Check(RapidTrigger, trigger.RapidTriggerPressMm, "Rapid trigger press");
        Check(RapidTrigger, trigger.RapidTriggerReleaseMm, "Rapid trigger release");
        Check(Deadzone, trigger.DeadzoneMm, "Deadzone");
        return errors;

        void Check(TravelRange range, decimal value, string name)
        {
            if (!range.Allows(value))
                errors.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{name} {value} mm is outside {range}."));
        }
    }
}

public static class Millimeters
{
    /// <summary>
    /// Parses user input with '.' as decimal separator regardless of the OS culture. A ',' is rejected
    /// instead of guessed, because "0,2" read as a group separator would silently become 2 mm.
    /// </summary>
    public static bool TryParse(string text, out decimal mm) =>
        decimal.TryParse(text, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out mm);
}
