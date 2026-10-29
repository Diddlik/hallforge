using HallForge.Core;

namespace HallForge.Protocol.RongYuan.Tests;

public class RongYuanClientTests
{
    /// <summary>
    /// Replies with queued responses and records every report sent. Like the firmware's report buffer,
    /// it keeps returning the last response once the queue is empty.
    /// </summary>
    private sealed class FakeConnection(params byte[][] responses) : IHidConnection
    {
        private readonly Queue<byte[]> _responses = new(responses);
        private byte[] _last = [];
        public List<byte[]> Sent { get; } = [];

        public void SetFeature(byte[] report) => Sent.Add(report.ToArray());

        public void GetFeature(byte[] report)
        {
            if (_responses.Count > 0)
                _last = _responses.Dequeue();
            _last.CopyTo(report.AsSpan(1));
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
    public void Encode_MagnetismPage_UsesDocumentedLayout()
    {
        var report = RongYuanClient.Encode(MagnetismTable.LiftTravel, 2);

        Assert.Equal([0x00, 0xE5, 0x01, 0x01, 0x02, 0, 0, 0], report.Take(8));
        Assert.Equal(RongYuanClient.Checksum([0xE5, 0x01, 0x01, 0x02, 0, 0, 0]), report[8]);
    }

    [Theory]
    [InlineData(0x04, 0)] // DKS travel: not allowlisted
    [InlineData(0x00, 4)] // page beyond the matrix
    public void Encode_MagnetismPage_RejectsOutsideBounds(byte table, byte page) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RongYuanClient.Encode((MagnetismTable)table, page));

    [Fact]
    public void ReadPage_AcceptsOnlyTwoIdenticalConsecutiveReads()
    {
        var stale = Response(0x8F, 0xF2);
        var page = Response(0x14, 0x00, 0x1E, 0x00);
        var connection = new FakeConnection(stale, page, page);

        var result = new RongYuanClient(connection).ReadPage(MagnetismTable.PressTravel, 0);

        Assert.Equal(page, result);
        Assert.Equal(3, connection.Sent.Count);
    }

    [Fact]
    public void ReadTravelTable_DecodesLittleEndianAcrossPages()
    {
        var page0 = Response([.. Enumerable.Repeat<byte>(0, 62), 0x2C, 0x01]);
        var page1 = Response(0x14, 0x00);
        var connection = new FakeConnection(page0, page0, page1, page1);

        var values = new RongYuanClient(connection).ReadTravelTable(MagnetismTable.PressTravel, 33);

        Assert.Equal(300, values[31]);
        Assert.Equal(20, values[32]);
    }

    // Payloads recorded from the vendor driver writing AltLeft (matrix index 23) on a MOD007B ISO HE.
    [Theory]
    [InlineData(1.9, 1.9, true, "1a0001801300173a130203")]
    [InlineData(1.4, 1.9, true, "1a0001800e00173f130203")]
    [InlineData(1.9, 1.9, false, "1a000100130017ba130203")]
    public void Encode_KeyTrigger_MatchesVendorDriverCapture(double actuation, double release, bool rapidTrigger, string expectedPrefix)
    {
        var trigger = new KeyTrigger((decimal)actuation, (decimal)release, rapidTrigger, 0.2m, 0.3m, 3.7m);

        var report = RongYuanClient.Encode(trigger, profile: 0, matrixIndex: 23);

        Assert.Equal(65, report.Length);
        Assert.Equal(expectedPrefix, Convert.ToHexStringLower(report, 1, 11));
        Assert.Equal(0x24, report[26]);
        Assert.All(report.Skip(12).Take(14).Concat(report.Skip(27)), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Encode_KeyTrigger_RejectsValuesThatCannotBeEncoded()
    {
        var valid = new KeyTrigger(1.9m, 1.9m, false, 0.2m, 0.3m, 3.7m);

        Assert.Throws<ArgumentOutOfRangeException>(() => RongYuanClient.Encode(valid with { ActuationMm = 0.25m }, 0, 23));
        Assert.Throws<ArgumentOutOfRangeException>(() => RongYuanClient.Encode(valid with { RapidTriggerPressMm = 0m }, 0, 23));
        Assert.Throws<ArgumentOutOfRangeException>(() => RongYuanClient.Encode(valid, 4, 23));
        Assert.Throws<ArgumentOutOfRangeException>(() => RongYuanClient.Encode(valid, 0, 126));
    }

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

    // Recorded from an AKKO MOD007B ISO HE (USB 0x3151:0x4035) on 2026-10-28. The firmware fills in the
    // model id but echoes everything else, including our checksum where other models report the version.
    [Fact]
    public void Identify_RecordedMod007b_ReportsModelIdOnly()
    {
        var connection = new FakeConnection(
            Response(Convert.FromHexString("8FF206000000007000")),
            Response(Convert.FromHexString("E6000000000000190000")));

        var identity = new RongYuanClient(connection).Identify();

        Assert.Equal(1778u, identity.DeviceId);
        Assert.Null(identity.FirmwareVersion);
        Assert.Null(identity.FeatureListResponse);
        Assert.Null(identity.PrecisionCode);
    }

    // Recorded from the same keyboard: GET_MULTI_MAGNETISM comes back as the unchanged request.
    [Fact]
    public void ReadPage_RecordedMod007bEcho_IsReportedAsNotSupported()
    {
        var echo = Response(Convert.FromHexString("E50001000000001900"));

        Assert.Throws<CommandNotSupportedException>(() => new RongYuanClient(new FakeConnection(echo)).ReadPage(MagnetismTable.PressTravel, 0));
    }

    // With no Roar keys the response equals the request, so it must not be taken for an unsupported command.
    [Fact]
    public void ReadRoar_AcceptsEmptyResponseThatLooksLikeAnEcho()
    {
        var empty = Response(Convert.FromHexString("9F000000000000600000"));

        Assert.All(new RongYuanClient(new FakeConnection(empty, empty)).ReadRoar(), Assert.Null);
    }

    [Fact]
    public void ReadRoar_SkipsStaleResponseOfAnotherCommand()
    {
        var roar = Response(Convert.FromHexString("9F00000000000060010E"));

        var slots = new RongYuanClient(new FakeConnection(Response(0x87, 0x13), roar, roar)).ReadRoar();

        Assert.Equal(new RoarSlot(14, true), slots[0]);
    }

    [Fact]
    public void WriteRoar_VerifiesSlotsAndTurnsBacklightOffWhileEnabled()
    {
        var roar = Response(Convert.FromHexString("9F00000000000060010E"));
        var connection = new FakeConnection(roar, roar, Response(0x86, 0x00, 0x00, 0x01), Response(0x86, 0x00, 0x10, 0x01));

        new RongYuanClient(connection).WriteRoar([new RoarSlot(14, true), null, null, null], profile: 0);

        Assert.Equal("1f000000000000e0010e", Convert.ToHexStringLower(connection.Sent[0], 1, 10));
        Assert.Contains(connection.Sent, r => Convert.ToHexStringLower(r, 1, 8) == "06001001000000e8");
    }

    [Fact]
    public void WriteRoar_FailsWhenTheKeyboardReportsOtherSlots()
    {
        var empty = Response(Convert.FromHexString("9F000000000000600000"));

        Assert.Throws<ProtocolException>(() =>
            new RongYuanClient(new FakeConnection(empty, empty)).WriteRoar([new RoarSlot(14, true), null, null, null], profile: 0));
    }

    [Fact]
    public void Identify_TreatsFeatureListWithoutMarkerAsUnknownPrecision()
    {
        var identity = RongYuanIdentity.Parse(Response(0x8F), Response(0xE6, 0x00, 0x02));

        Assert.Null(identity.PrecisionCode);
    }
}
