using System.Text;
using System.Text.Json;
using HallForge.Core;

namespace HallForge.Protocol.RongYuan;

public sealed record AkkoImport(IReadOnlyDictionary<string, KeyTrigger> Keys, IReadOnlyList<string> Warnings);

/// <summary>
/// Reads per-key trigger settings that the Akko Cloud Driver keeps locally, because model 1778 cannot report them.
/// The driver stores them in a sled database as JSON under the key <c>devConfig_&lt;modelId&gt;</c>: an array with one
/// entry per onboard profile, each listing only the keys that differ from the driver's defaults.
/// </summary>
public static class AkkoDriverDatabase
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Akko Cloud Driver", "iot_db", "CONFIG", "db");

    public static AkkoImport ReadProfile(string databasePath, uint modelId, int profile, DeviceDefinition definition)
    {
        // The driver may hold the file open; read without locking it.
        using var stream = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return ParseProfile(memory.ToArray(), modelId, profile, definition);
    }

    public static AkkoImport ParseProfile(byte[] database, uint modelId, int profile, DeviceDefinition definition)
    {
        var profiles = LatestConfig(database, modelId)
            ?? throw new InvalidDataException($"The driver database has no configuration for model {modelId}.");
        if (profile >= profiles.GetArrayLength())
            throw new InvalidDataException($"The driver database has no onboard profile {profile} for model {modelId}.");

        var keys = new Dictionary<string, KeyTrigger>();
        var warnings = new List<string>();
        var skipped = new HashSet<string>();
        var modes = profiles[profile].TryGetProperty("magnetismModes", out var stored) ? stored.EnumerateArray().ToList() : [];

        foreach (var mode in modes)
        {
            var usage = mode.GetProperty("original").GetInt32();
            var keyId = KeyCodes.FromHidUsage(usage);
            if (keyId is null || !definition.Keys.ContainsKey(keyId))
            {
                warnings.Add($"Skipped HID usage 0x{usage:X2}: not a configurable key of {definition.Name}.");
                continue;
            }
            if (mode.GetProperty("mode").GetString() != "normal" || mode.GetProperty("option").GetString() != "normal")
            {
                warnings.Add($"Skipped {keyId}: special key modes (DKS, mod-tap, toggle) are not supported.");
                skipped.Add(keyId);
                continue;
            }
            keys[keyId] = new KeyTrigger(
                Tenths(mode, "travel"),
                Tenths(mode, "liftTravel"),
                mode.GetProperty("fire").GetBoolean(),
                Tenths(mode, "firePressTravel"),
                Tenths(mode, "fireLiftTravel"),
                mode.GetProperty("deadZoneTravel").GetDecimal());
        }

        // The driver stores only keys that differ from the factory setting, so missing keys have the factory values.
        if (definition.FactoryDefaults is { } defaults)
        {
            var missing = definition.Keys.Keys.Where(k => !keys.ContainsKey(k) && !skipped.Contains(k)).ToList();
            foreach (var keyId in missing)
                keys[keyId] = defaults;
            if (missing.Count > 0)
                warnings.Add($"{missing.Count} keys were not stored by the driver and are set to the factory defaults of {definition.Name}.");
        }
        return new AkkoImport(keys, warnings);
    }

    private static decimal Tenths(JsonElement mode, string name) => mode.GetProperty(name).GetDecimal() / 10;

    /// <summary>
    /// The log-structured database keeps older versions of a value; the newest one is appended last.
    /// Versions that do not parse (e.g. partially overwritten) are skipped.
    /// </summary>
    private static JsonElement? LatestConfig(byte[] database, uint modelId)
    {
        var key = Encoding.ASCII.GetBytes($"\"devConfig_{modelId}\"");
        JsonElement? latest = null;
        for (var at = database.AsSpan().IndexOf(key); at >= 0;)
        {
            var start = database.AsSpan(at + key.Length).IndexOf((byte)'[');
            if (start is >= 0 and < 8 && TryParseArray(database.AsMemory(at + key.Length + start), out var config))
                latest = config;
            var next = database.AsSpan(at + key.Length).IndexOf(key);
            at = next < 0 ? -1 : at + key.Length + next;
        }
        return latest;
    }

    private static bool TryParseArray(ReadOnlyMemory<byte> data, out JsonElement array)
    {
        var reader = new Utf8JsonReader(data.Span);
        try
        {
            if (JsonElement.TryParseValue(ref reader, out var element) && element.Value.ValueKind == JsonValueKind.Array)
            {
                array = element.Value;
                return true;
            }
        }
        catch (JsonException)
        {
            // An older version partially overwritten by newer data; the caller keeps looking.
        }
        array = default;
        return false;
    }
}
