using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HallForge.Core;

/// <summary>Settings a profile changes for a key. A null field means "leave unchanged".</summary>
public sealed record TriggerPatch(
    decimal? ActuationMm = null,
    decimal? ReleaseMm = null,
    bool? RapidTriggerEnabled = null,
    decimal? RapidTriggerPressMm = null,
    decimal? RapidTriggerReleaseMm = null,
    decimal? DeadzoneMm = null)
{
    public TriggerPatch Then(TriggerPatch later) => new(
        later.ActuationMm ?? ActuationMm,
        later.ReleaseMm ?? ReleaseMm,
        later.RapidTriggerEnabled ?? RapidTriggerEnabled,
        later.RapidTriggerPressMm ?? RapidTriggerPressMm,
        later.RapidTriggerReleaseMm ?? RapidTriggerReleaseMm,
        later.DeadzoneMm ?? DeadzoneMm);

    /// <returns>Null when a field is neither in the patch nor in <paramref name="current"/>.</returns>
    public KeyTrigger? ApplyTo(KeyTrigger? current)
    {
        var actuation = ActuationMm ?? current?.ActuationMm;
        var release = ReleaseMm ?? current?.ReleaseMm;
        var rapidTrigger = RapidTriggerEnabled ?? current?.RapidTrigger;
        var rtPress = RapidTriggerPressMm ?? current?.RapidTriggerPressMm;
        var rtRelease = RapidTriggerReleaseMm ?? current?.RapidTriggerReleaseMm;
        var deadzone = DeadzoneMm ?? current?.DeadzoneMm;
        return actuation is { } a && release is { } r && rapidTrigger is { } rt && rtPress is { } p && rtRelease is { } l && deadzone is { } d
            ? new KeyTrigger(a, r, rt, p, l, d)
            : null;
    }

    public static TriggerPatch From(KeyTrigger trigger) => new(
        trigger.ActuationMm, trigger.ReleaseMm, trigger.RapidTrigger,
        trigger.RapidTriggerPressMm, trigger.RapidTriggerReleaseMm, trigger.DeadzoneMm);
}

public sealed record ProfileGroup(IReadOnlyList<string> KeyIds, TriggerPatch Settings);

/// <summary>Portable key settings. Groups apply in order; <see cref="Keys"/> overrides groups.</summary>
public sealed record KeyboardProfile(
    int SchemaVersion,
    string Name,
    IReadOnlyList<ProfileGroup> Groups,
    IReadOnlyDictionary<string, TriggerPatch>? Keys = null)
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static KeyboardProfile Parse(string json)
    {
        // Omitted means "unchanged"; an explicit null is ambiguous and therefore rejected.
        if (FindNull(JsonNode.Parse(json), "$") is { } path)
            throw new JsonException($"{path} is null. Omit a field to leave it unchanged.");
        var profile = JsonSerializer.Deserialize<KeyboardProfile>(json, Options) ?? throw new JsonException("Profile is null.");
        if (profile.SchemaVersion != CurrentSchemaVersion)
            throw new JsonException($"Unsupported schemaVersion {profile.SchemaVersion}; expected {CurrentSchemaVersion}.");
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new JsonException("Profile requires a name.");
        return profile;
    }

    public static KeyboardProfile Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid profile '{path}': {ex.Message}", ex);
        }
    }

    /// <summary>The combined patch per key after groups and overrides.</summary>
    public IReadOnlyDictionary<string, TriggerPatch> Resolve()
    {
        var patches = new Dictionary<string, TriggerPatch>();
        foreach (var group in Groups)
            foreach (var keyId in group.KeyIds)
                patches[keyId] = patches.GetValueOrDefault(keyId, new TriggerPatch()).Then(group.Settings);
        foreach (var (keyId, patch) in Keys ?? new Dictionary<string, TriggerPatch>())
            patches[keyId] = patches.GetValueOrDefault(keyId, new TriggerPatch()).Then(patch);
        return patches;
    }

    private static string? FindNull(JsonNode? node, string path) => node switch
    {
        null => path,
        JsonObject obj => obj.Select(p => FindNull(p.Value, $"{path}.{p.Key}")).FirstOrDefault(p => p is not null),
        JsonArray array => array.Select((item, i) => FindNull(item, $"{path}[{i}]")).FirstOrDefault(p => p is not null),
        _ => null,
    };
}

/// <param name="Before">Null when HallForge does not know the key's current setting.</param>
public sealed record KeyChange(string KeyId, int MatrixIndex, KeyTrigger? Before, KeyTrigger After);

public sealed record ApplyPlan(IReadOnlyList<KeyChange> Changes, IReadOnlyList<string> Errors, int UnchangedKeys)
{
    public bool CanApply => Errors.Count == 0;

    /// <summary>Validates every patch against the device and builds the full per-key writes. Nothing is partial: any error blocks the plan.</summary>
    public static ApplyPlan Create(IReadOnlyDictionary<string, TriggerPatch> patches, DeviceDefinition definition, KeyboardState? state)
    {
        var changes = new List<KeyChange>();
        var errors = new List<string>();
        var unchanged = 0;
        foreach (var (keyId, patch) in patches.OrderBy(p => definition.Keys.GetValueOrDefault(p.Key)))
        {
            if (!definition.Keys.TryGetValue(keyId, out var matrixIndex))
            {
                errors.Add($"{keyId}: not a configurable key of {definition.Name}.");
                continue;
            }
            var before = state?.Keys.GetValueOrDefault(keyId);
            if (patch.ApplyTo(before) is not { } after)
            {
                errors.Add($"{keyId}: current setting unknown, so the profile must set all six values for this key.");
                continue;
            }
            foreach (var error in definition.Limits.Validate(after))
                errors.Add($"{keyId}: {error}");
            if (after == before)
                unchanged++;
            else
                changes.Add(new KeyChange(keyId, matrixIndex, before, after));
        }
        return new ApplyPlan(changes, errors, unchanged);
    }

    /// <summary>
    /// Writes changes one by one and records each in the state right after it is sent, so an interruption leaves the
    /// state matching what was actually sent.
    /// </summary>
    public void Execute(Action<KeyChange> write, Action<KeyChange> recorded)
    {
        if (!CanApply)
            throw new InvalidOperationException("The plan has errors.");
        foreach (var change in Changes)
        {
            write(change);
            recorded(change);
        }
    }
}
