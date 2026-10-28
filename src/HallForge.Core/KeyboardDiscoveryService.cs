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

public sealed class KeyboardDiscoveryService(IHidTransport transport, IReadOnlyList<DeviceDefinition> definitions)
{
    // A model definition with explicit product ids wins over a vendor-wide family definition.
    private readonly IReadOnlyList<DeviceDefinition> _definitions = definitions.OrderBy(d => d.Match.ProductIds.Count == 0).ToList();

    /// <param name="includeUnmatched">Also return interfaces without a definition, needed to research new keyboards.</param>
    public IReadOnlyList<KeyboardCandidate> Discover(bool includeUnmatched = false) =>
        transport.Enumerate()
            .Select(hid => new KeyboardCandidate(hid, _definitions.FirstOrDefault(d => d.Matches(hid))))
            .Where(c => includeUnmatched || c.Definition is not null)
            .ToList();
}
