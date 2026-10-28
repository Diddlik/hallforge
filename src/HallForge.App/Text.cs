using System.Globalization;

namespace HallForge.App;

/// <summary>Formats localized resource strings with the UI culture.</summary>
internal static class Text
{
    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
