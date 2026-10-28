using HallForge.Core;

namespace HallForge.Protocol.RongYuan.Tests;

public class RongYuanClientTests
{
    /// <summary>Replies with queued responses and records every report sent.</summary>
    private sealed class FakeConnection(params byte[][] responses) : IHidConnection
    {
        private readonly Queue<byte[]> _responses = new(responses);
        public List<byte[]> Sent { get; } = [];

        public void SetFeature(byte[] report) => Sent.Add(report.ToArray());

        public void GetFeature(byte[] report)
        {
            var next = _responses.Count > 0 ? _responses.Dequeue() : [];
            next.CopyTo(report.AsSpan(1));
        }

        public void Dispose()
        {
        }
    }

    private static byte[] Response(params byte[] payload)
    {
        var response = new byte[64];
        payload.CopyTo(response, 0);
        return response;
    }

    [Fact]
    public void Checksum_MatchesDocumentedExample() =>
        Assert.Equal(0x08, RongYuanClient.Checksum([0xF7, 0, 0, 0, 0, 0, 0]));

    [Fact]
    public void Encode_BuildsFeatureReportWithReportIdAndChecksum()
    {
        var report = RongYuanClient.Encode(ReadCommand.GetUsbVersion);

        Assert.Equal(65, report.Length);
        Assert.Equal(0x00, report[0]);
        Assert.Equal(0x8F, report[1]);
        Assert.Equal(0x70, report[8]);
        Assert.All(report.Skip(9), b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData(0x7F)] // ENTER_BOOTLOADER
    [InlineData(0x65)] // SET_MULTI_MAGNETISM
    public void Encode_RejectsCommandsOutsideAllowlist(byte opcode) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RongYuanClient.Encode((ReadCommand)opcode));

    [Fact]
    public void Query_RetriesUntilResponseEchoesCommand()
    {
        var connection = new FakeConnection(Response(0xE6), Response(0x8F, 1, 2));

        var response = new RongYuanClient(connection).Query(ReadCommand.GetUsbVersion);

        Assert.Equal(2, connection.Sent.Count);
        Assert.Equal([0x8F, 1, 2], response.Take(3));
    }

    [Fact]
    public void Query_FailsAfterRetriesWithoutMatchingResponse()
    {
        var connection = new FakeConnection();

        Assert.Throws<ProtocolException>(() => new RongYuanClient(connection).Query(ReadCommand.GetFeatureList));
        Assert.Equal(5, connection.Sent.Count);
    }

    [Fact]
    public void Identify_ParsesDeviceIdVersionAndPrecision()
    {
        var connection = new FakeConnection(
            Response(0x8F, 0x55, 0x06, 0x00, 0x00, 0x00, 0x00, 0x08, 0x04),
            Response(0xE6, 0xAA, 0x02));

        var identity = new RongYuanClient(connection).Identify();

        Assert.Equal(1621u, identity.DeviceId);
        Assert.Equal("v408", identity.FirmwareText);
        Assert.Equal((byte)2, identity.PrecisionCode);
    }

    // Recorded from an AKKO MOD007B ISO HE (USB 0x3151:0x4035) on 2026-10-28.
    [Fact]
    public void Identify_ParsesRecordedMod007bResponses()
    {
        var usbVersion = Response(Convert.FromHexString("8FF206000000007000"));
        var featureList = Response(Convert.FromHexString("E6000000000000190000"));

        var identity = RongYuanIdentity.Parse(usbVersion, featureList);

        Assert.Equal(1778u, identity.DeviceId);
        Assert.Equal(0x0070, identity.FirmwareVersion);
        Assert.Null(identity.PrecisionCode);
    }

    [Fact]
    public void Identify_TreatsFeatureListWithoutMarkerAsUnknownPrecision()
    {
        var identity = RongYuanIdentity.Parse(Response(0x8F), Response(0xE6, 0x00, 0x02));

        Assert.Null(identity.PrecisionCode);
    }
}
