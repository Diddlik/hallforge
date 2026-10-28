namespace HallForge.Core;

public enum SupportLevel
{
    /// <summary>No device definition matches; never written to.</summary>
    Unknown,
    /// <summary>Matches a definition that is not yet verified on real hardware; read-only.</summary>
    Unverified,
    Supported,
}

public sealed record KeyboardCandidate(HidInterfaceInfo Interface, DeviceDefinition? Definition)
{
    public SupportLevel Support => Definition switch
    {
        null => SupportLevel.Unknown,
        { Verified: false } => SupportLevel.Unverified,
        _ => SupportLevel.Supported,
    };
}

public sealed class KeyboardDiscoveryService(IHidEnumerator enumerator, IReadOnlyList<DeviceDefinition> definitions)
{
    /// <param name="includeUnmatched">Also return interfaces without a definition, needed to research new keyboards.</param>
    public IReadOnlyList<KeyboardCandidate> Discover(bool includeUnmatched = false) =>
        enumerator.Enumerate()
            .Select(hid => new KeyboardCandidate(hid, definitions.FirstOrDefault(d => d.Matches(hid))))
            .Where(c => includeUnmatched || c.Definition is not null)
            .ToList();
}
