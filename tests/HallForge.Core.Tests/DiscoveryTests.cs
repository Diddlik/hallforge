using HallForge.Core;

namespace HallForge.Core.Tests;

public class DiscoveryTests
{
    private const string Definition = """
        {
          "id": "test-board",
          "name": "Test Board",
          "protocol": "rongyuan",
          "verified": false,
          "match": { "vendorIds": ["0x3151"], "productIds": [], "usagePage": "0xFFFF", "usage": "0x0002" }
        }
        """;

    private static HidInterfaceInfo Hid(string path, int vid = 0x3151, int pid = 0x5030, int? usagePage = 0xFFFF, int? usage = 0x02) =>
        new(path, vid, pid, 0x0100, "Vendor", "Board", usagePage, usage, 2, 0, 0, 65);

    private sealed class FakeEnumerator(params HidInterfaceInfo[] interfaces) : IHidEnumerator
    {
        public IReadOnlyList<HidInterfaceInfo> Enumerate() => interfaces;
    }

    [Fact]
    public void Parse_ReadsHexIdentifiers()
    {
        var definition = DeviceDefinitionLoader.Parse(Definition);

        Assert.Equal([0x3151], definition.Match.VendorIds);
        Assert.Equal(0xFFFF, definition.Match.UsagePage);
        Assert.False(definition.Verified);
    }

    [Theory]
    [InlineData("12625")]
    [InlineData("\"3151\"")]
    [InlineData("\"0x10000\"")]
    public void Parse_RejectsNonHexOrOutOfRangeIds(string vendorId) =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => DeviceDefinitionLoader.Parse(Definition.Replace("\"0x3151\"", vendorId, StringComparison.Ordinal)));

    [Fact]
    public void Parse_RejectsUnknownFields() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => DeviceDefinitionLoader.Parse(Definition.Replace("\"verified\"", "\"writeEnabled\": true, \"verified\"", StringComparison.Ordinal)));

    [Fact]
    public void Discover_MatchesOnlyTheConfigInterface()
    {
        var config = Hid("config");
        var keyboardInput = Hid("input", usagePage: 0x01, usage: 0x06);
        var otherVendor = Hid("other", vid: 0x1234);
        var service = new KeyboardDiscoveryService(new FakeEnumerator(config, keyboardInput, otherVendor), [DeviceDefinitionLoader.Parse(Definition)]);

        var candidate = Assert.Single(service.Discover());

        Assert.Same(config, candidate.Interface);
        Assert.Equal(SupportLevel.Unverified, candidate.Support);
    }

    [Fact]
    public void Discover_WithUnmatched_ReportsUnknownDevices()
    {
        var service = new KeyboardDiscoveryService(new FakeEnumerator(Hid("config"), Hid("other", vid: 0x1234)), [DeviceDefinitionLoader.Parse(Definition)]);

        var candidates = service.Discover(includeUnmatched: true);

        Assert.Equal([SupportLevel.Unverified, SupportLevel.Unknown], candidates.Select(c => c.Support));
    }

    [Fact]
    public void DiagnosticReport_DoesNotContainDevicePath()
    {
        const string path = @"\\?\hid#vid_3151&pid_5030&mi_02#secret-instance";
        var report = DiagnosticReport.Create([new KeyboardCandidate(Hid(path), null)], "0.1.0");

        Assert.DoesNotContain("secret-instance", report, StringComparison.Ordinal);
        Assert.Contains("\"vendorId\": \"0x3151\"", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippedDeviceDefinitions_AreValid()
    {
        var definitions = DeviceDefinitionLoader.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "devices"));

        Assert.NotEmpty(definitions);
        Assert.Equal(definitions.Count, definitions.Select(d => d.Id).Distinct().Count());
    }
}
