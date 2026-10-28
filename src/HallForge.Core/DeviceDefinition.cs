using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HallForge.Core;

/// <summary>
/// Data-driven description of a keyboard model. Adding a keyboard that speaks an already
/// implemented protocol only requires a new JSON file in the devices directory.
/// </summary>
/// <param name="FirmwareModelIds">
/// Model ids the firmware reports. Vendors reuse one USB product id for many boards, so only this id
/// confirms the model; a device whose firmware reports another id must not be configured with this definition.
/// </param>
/// <param name="Keys">Configurable keys by W3C <c>KeyboardEvent.code</c> name, mapped to the device's matrix position.</param>
public sealed record DeviceDefinition(
    string Id,
    string Name,
    string Protocol,
    bool Verified,
    DeviceMatch Match,
    IReadOnlyList<uint> FirmwareModelIds,
    TriggerLimits Limits,
    IReadOnlyDictionary<string, int> Keys,
    IReadOnlyList<KeyGeometry> Layout,
    KeyTrigger? FactoryDefaults = null)
{
    public bool Matches(HidInterfaceInfo hid) =>
        Match.VendorIds.Contains(hid.VendorId)
        && (Match.ProductIds.Count == 0 || Match.ProductIds.Contains(hid.ProductId))
        && hid.UsagePage == Match.UsagePage
        && hid.Usage == Match.Usage;
}

/// <summary>Identifies the vendor configuration interface. An empty product id list matches any product of the vendor.</summary>
public sealed record DeviceMatch(
    [property: JsonConverter(typeof(HexIntListConverter))] IReadOnlyList<int> VendorIds,
    [property: JsonConverter(typeof(HexIntListConverter))] IReadOnlyList<int> ProductIds,
    [property: JsonConverter(typeof(HexIntConverter))] int UsagePage,
    [property: JsonConverter(typeof(HexIntConverter))] int Usage);

public static class DeviceDefinitionLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static IReadOnlyList<DeviceDefinition> LoadDirectory(string directory) =>
        Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal).Select(LoadFile).ToList();

    public static DeviceDefinition LoadFile(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid device definition '{path}': {ex.Message}", ex);
        }
    }

    public static DeviceDefinition Parse(string json)
    {
        var definition = JsonSerializer.Deserialize<DeviceDefinition>(json, Options)
            ?? throw new JsonException("Definition is null.");
        if (string.IsNullOrWhiteSpace(definition.Id) || definition.Match.VendorIds.Count == 0)
            throw new JsonException("Definition requires an id and at least one vendor id.");
        if (definition.Keys.Values.Distinct().Count() != definition.Keys.Count)
            throw new JsonException("Two keys share a matrix position.");
        if (definition.Layout.FirstOrDefault(g => !definition.Keys.ContainsKey(g.Key)) is { } stray)
            throw new JsonException($"Layout key '{stray.Key}' is not in keys.");
        if (definition.Keys.Values.Any(i => i is < 0 or >= TriggerLimits.MatrixSize))
            throw new JsonException($"Matrix positions must be between 0 and {TriggerLimits.MatrixSize - 1}.");
        if (definition.FactoryDefaults is { } defaults && definition.Limits.Validate(defaults) is { Count: > 0 } errors)
            throw new JsonException($"Factory defaults: {string.Join(" ", errors)}");
        return definition;
    }
}

/// <summary>USB identifiers are conventionally written in hex, so definitions accept "0x3151" strings.</summary>
internal sealed class HexIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (text is null || !text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)
            || value > 0xFFFF)
            throw new JsonException($"Expected a 16-bit hex string like \"0x3151\", got \"{text}\".");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteStringValue($"0x{value:X4}");
}

internal sealed class HexIntListConverter : JsonConverter<IReadOnlyList<int>>
{
    private static readonly HexIntConverter Item = new();

    public override IReadOnlyList<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected an array of hex strings.");
        var values = new List<int>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            values.Add(Item.Read(ref reader, typeof(int), options));
        return values;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<int> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
            Item.Write(writer, item, options);
        writer.WriteEndArray();
    }
}

/// <summary>Physical key position in key units (1 u = width of a letter key), origin at the top left.</summary>
public sealed record KeyGeometry(string Key, double X, double Y, double W, double H);
