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
          "match": { "vendorIds": ["0x3151"], "productIds": [], "usagePage": "0xFFFF", "usage": "0x0002" },
          "firmwareModelIds": [1778],
          "limits": {
            "travel": { "minMm": 0.1, "maxMm": 4.0, "stepMm": 0.1 },
            "rapidTrigger": { "minMm": 0.1, "maxMm": 2.5, "stepMm": 0.1 },
            "deadzone": { "minMm": 0.1, "maxMm": 4.0, "stepMm": 0.1 }
          },
          "keys": { "KeyW": 14, "AltLeft": 23 }
        }
        """;

    private static HidInterfaceInfo Hid(string path, int vid = 0x3151, int pid = 0x5030, int? usagePage = 0xFFFF, int? usage = 0x02) =>
        new(path, vid, pid, 0x0100, "Vendor", "Board", usagePage, usage, 2, 0, 0, 65);

    private sealed class FakeTransport(params HidInterfaceInfo[] interfaces) : IHidTransport
    {
        public IReadOnlyList<HidInterfaceInfo> Enumerate() => interfaces;
        public IHidConnection Open(HidInterfaceInfo hid) => throw new NotSupportedException();
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
    public void Parse_RejectsDuplicateMatrixPositions() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => DeviceDefinitionLoader.Parse(Definition.Replace("\"AltLeft\": 23", "\"AltLeft\": 14", StringComparison.Ordinal)));

    [Fact]
    public void Parse_RejectsMatrixPositionOutsideMatrix() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => DeviceDefinitionLoader.Parse(Definition.Replace("\"AltLeft\": 23", "\"AltLeft\": 126", StringComparison.Ordinal)));

    [Fact]
    public void Limits_RejectValuesOutsideRangeOrStep()
    {
        var limits = DeviceDefinitionLoader.Parse(Definition).Limits;

        Assert.Empty(limits.Validate(new KeyTrigger(1.9m, 1.9m, false, 0.2m, 0.3m, 3.7m)));
        Assert.Equal(3, limits.Validate(new KeyTrigger(4.1m, 0.05m, true, 2.6m, 0.3m, 3.7m)).Count);
    }

    [Theory]
    [InlineData("0.2", true, 0.2)]
    [InlineData("1.9", true, 1.9)]
    [InlineData("0,2", false, 0)]
    [InlineData("1,000", false, 0)]
    [InlineData("-1", false, 0)]
    public void Millimeters_ParseIndependentOfCulture(string text, bool valid, double expected)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            Assert.Equal(valid, Millimeters.TryParse(text, out var mm));
            if (valid)
                Assert.Equal((decimal)expected, mm);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Discover_MatchesOnlyTheConfigInterface()
    {
        var config = Hid("config");
        var keyboardInput = Hid("input", usagePage: 0x01, usage: 0x06);
        var otherVendor = Hid("other", vid: 0x1234);
        var service = new KeyboardDiscoveryService(new FakeTransport(config, keyboardInput, otherVendor), [DeviceDefinitionLoader.Parse(Definition)]);

        var candidate = Assert.Single(service.Discover());

        Assert.Same(config, candidate.Interface);
        Assert.Equal(SupportLevel.Unverified, candidate.Support);
    }

    [Fact]
    public void Discover_PrefersModelDefinitionOverFamily()
    {
        var family = DeviceDefinitionLoader.Parse(Definition);
        var model = DeviceDefinitionLoader.Parse(Definition.Replace("\"test-board\"", "\"model\"", StringComparison.Ordinal).Replace("[]", "[\"0x5030\"]", StringComparison.Ordinal));
        var service = new KeyboardDiscoveryService(new FakeTransport(Hid("config")), [family, model]);

        Assert.Same(model, Assert.Single(service.Discover()).Definition);
    }

    [Fact]
    public void Discover_WithUnmatched_ReportsUnknownDevices()
    {
        var service = new KeyboardDiscoveryService(new FakeTransport(Hid("config"), Hid("other", vid: 0x1234)), [DeviceDefinitionLoader.Parse(Definition)]);

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
