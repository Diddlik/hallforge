using System.Text.Json;

namespace HallForge.Core;

/// <summary>
/// Printed key labels for one keyboard language layout. Key ids are positional (W3C codes), so the same
/// device definition shows "Z" or "Y" on <c>KeyY</c> depending on the layout.
/// </summary>
public sealed record KeyLegends(string Id, string Name, IReadOnlyDictionary<string, string> Legends)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static KeyLegends Load(string path) =>
        JsonSerializer.Deserialize<KeyLegends>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Invalid layout '{path}'.");

    /// <summary>Falls back to the letter, digit or function key name encoded in the key id.</summary>
    public string For(string keyId) =>
        Legends.TryGetValue(keyId, out var legend) ? legend
        : keyId.StartsWith("Key", StringComparison.Ordinal) ? keyId[3..]
        : keyId.StartsWith("Digit", StringComparison.Ordinal) ? keyId[5..]
        : keyId;
}
