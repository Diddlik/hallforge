using System.Globalization;

namespace HallForge.App;

/// <summary>Formats localized resource strings with the UI culture.</summary>
internal static class Text
{
    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}

/// <summary>Millimeter values are shown with '.' in every culture, because input accepts only '.'.</summary>
internal static class Mm
{
    public static string Format(decimal mm) => mm.ToString("0.00", CultureInfo.InvariantCulture);
}
