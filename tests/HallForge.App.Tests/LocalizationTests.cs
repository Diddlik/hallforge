using System.Globalization;
using System.Resources;

namespace HallForge.App.Tests;

public class LocalizationTests
{
    private static readonly ResourceManager Resources = new("HallForge.App.Strings", typeof(Strings).Assembly);

    [Fact]
    public void German_UiCulture_UsesGermanStrings() =>
        Assert.Equal("Anwenden", Resources.GetString(nameof(Strings.Apply), new CultureInfo("de-DE")));

    [Fact]
    public void German_FormatStrings_KeepThePlaceholdersOfTheNeutralStrings()
    {
        var neutral = Resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!;
        var german = Resources.GetResourceSet(new CultureInfo("de"), createIfNotExists: true, tryParents: false)!;

        foreach (System.Collections.DictionaryEntry entry in german)
        {
            var key = (string)entry.Key;
            Assert.Equal(Placeholders(neutral.GetString(key)!), Placeholders((string)entry.Value!));
        }

        static string Placeholders(string text) => string.Join(",", System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order());
    }
}
