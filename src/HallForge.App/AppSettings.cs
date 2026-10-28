using System.Text.Json;
using HallForge.Core;

namespace HallForge.App;

public sealed record WindowPlacement(double Width, double Height, int X, int Y, bool Maximized);

/// <summary>User preferences, stored next to the state in <see cref="StateStore.DefaultRoot"/>.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        StateStore.DefaultRoot, "settings.json");

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public bool AutoCheckForUpdates { get; set; } = true;
    public WindowPlacement? Window { get; set; }
    public bool NavigationCollapsed { get; set; }

    /// <summary>"Light" or "Dark"; null follows the system setting.</summary>
    public string? Theme { get; set; }

    // Missing or corrupt settings must never prevent the app from starting; defaults are used instead.
    public static AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // Write to a temp file first so a crash cannot leave a half-written settings file.
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
