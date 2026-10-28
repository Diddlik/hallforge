using System.Text.Json;

namespace HallForge.Core;

/// <summary>
/// What HallForge knows about one onboard profile. Needed because some firmware cannot report its settings:
/// the last applied or imported values are the only source of truth. Keys missing from <see cref="Keys"/> are unknown.
/// </summary>
/// <param name="Source">Where the values came from, e.g. "akko-driver-import" or "hallforge".</param>
public sealed record KeyboardState(
    int FormatVersion,
    string DefinitionId,
    uint FirmwareModelId,
    int Profile,
    string Source,
    DateTime UpdatedAtUtc,
    IReadOnlyDictionary<string, KeyTrigger> Keys)
{
    public const int CurrentFormatVersion = 1;
}

/// <summary>Current state per device profile plus immutable backups, as JSON files below one root directory.</summary>
public sealed class StateStore(string root)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    public static string DefaultRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HallForge");

    public string StatePath(string definitionId, int profile) => Path.Combine(root, "state", $"{definitionId}-profile{profile}.json");

    public KeyboardState? Load(string definitionId, int profile)
    {
        var path = StatePath(definitionId, profile);
        return File.Exists(path) ? Read(path) : null;
    }

    public bool HasBackup(string definitionId, int profile) =>
        Directory.Exists(Path.Combine(root, "backups"))
        && Directory.EnumerateFiles(Path.Combine(root, "backups"), $"*-{definitionId}-profile{profile}.json").Any();

    public static KeyboardState Read(string path)
    {
        try
        {
            var state = JsonSerializer.Deserialize<KeyboardState>(File.ReadAllText(path), Options);
            if (state is null || state.FormatVersion != KeyboardState.CurrentFormatVersion)
                throw new InvalidDataException($"Unsupported state file '{path}'.");
            return state;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Corrupt state file '{path}': {ex.Message}", ex);
        }
    }

    public void Save(KeyboardState state) => WriteAtomic(StatePath(state.DefinitionId, state.Profile), state);

    /// <returns>Path of the new backup file.</returns>
    public string SaveBackup(KeyboardState state)
    {
        var path = Path.Combine(root, "backups", $"{state.UpdatedAtUtc:yyyyMMdd-HHmmss}-{state.DefinitionId}-profile{state.Profile}.json");
        if (File.Exists(path))
            throw new IOException($"Backup '{path}' already exists.");
        WriteAtomic(path, state);
        return path;
    }

    /// <summary>Records one written key, keeping all other known keys.</summary>
    public KeyboardState RecordWrite(DeviceDefinition definition, uint firmwareModelId, int profile, string keyId, KeyTrigger trigger)
    {
        var keys = new Dictionary<string, KeyTrigger>(Load(definition.Id, profile)?.Keys ?? new Dictionary<string, KeyTrigger>())
        {
            [keyId] = trigger,
        };
        var state = new KeyboardState(KeyboardState.CurrentFormatVersion, definition.Id, firmwareModelId, profile, "hallforge", DateTime.UtcNow, keys);
        Save(state);
        return state;
    }

    // Temp file plus replace, so a crash never leaves a half-written state file.
    private static void WriteAtomic(string path, KeyboardState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
        File.Move(temp, path, overwrite: true);
    }
}
