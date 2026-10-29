namespace HallForge.Protocol.RongYuan.Tests;

public class RoarTests
{
    private static byte[] Response(string hex)
    {
        var response = new byte[64];
        Convert.FromHexString(hex).CopyTo(response, 0);
        return response;
    }

    // GET_MAGNETISM_FAST response recorded from the MOD007B ISO HE (ws/usb2): W, A, D, S enabled.
    [Fact]
    public void Parse_RecordedMod007bResponse() =>
        Assert.Equal(
            [new RoarSlot(14, true), new RoarSlot(9, true), new RoarSlot(21, true), new RoarSlot(15, true)],
            Roar.Parse(Response("9F00000000000060010E01090115010F")));

    [Fact]
    public void Parse_EmptyAndDisabledSlots() =>
        Assert.Equal([null, new RoarSlot(14, false), null, null], Roar.Parse(Response("9F00000000000060" + "0000000E00000000")));

    [Theory]
    [InlineData("9F00000000000060020E000000000000")]
    [InlineData("9F00000000000060017E000000000000")]
    [InlineData("8700000000000060010E000000000000")]
    public void Parse_RejectsUnexpectedData(string hex) => Assert.Throws<ProtocolException>(() => Roar.Parse(Response(hex)));

    // SET_MAGNETISM_FAST payloads recorded from the vendor driver (ws/usb5): W turned off, then on again.
    [Theory]
    [InlineData(false, "1f000000000000e0000e01090115010f")]
    [InlineData(true, "1f000000000000e0010e01090115010f")]
    public void Encode_MatchesVendorDriverCapture(bool w, string expected)
    {
        var report = Roar.Encode([new RoarSlot(14, w), new RoarSlot(9, true), new RoarSlot(21, true), new RoarSlot(15, true)]);

        Assert.Equal(65, report.Length);
        Assert.Equal(expected, Convert.ToHexStringLower(report, 1, 16));
        Assert.All(report.Skip(17), x => Assert.Equal(0, x));
    }

    [Fact]
    public void Encode_RejectsDuplicateKeysAndWrongSlotCount()
    {
        Assert.Throws<ArgumentException>(() => Roar.Encode([new RoarSlot(14, true), new RoarSlot(14, false), null, null]));
        Assert.Throws<ArgumentException>(() => Roar.Encode([new RoarSlot(14, true)]));
    }

    // GET_KBOPTION response recorded from the MOD007B ISO HE: backlight off, Fn layout 1.
    [Fact]
    public void KeyboardOptions_ChangeOnlyTheBacklightBit()
    {
        var options = KeyboardOptions.Parse(Response("86001001"));

        Assert.True(options.LedOff);
        Assert.Equal(new KeyboardOptions(0x00, 0x01), options.WithLedOff(false));
        Assert.Equal(options, options.WithLedOff(false).WithLedOff(true));
    }

    [Fact]
    public void KeyboardOptions_RejectsUnknownBytes() =>
        Assert.Throws<ProtocolException>(() => KeyboardOptions.Parse(Response("8600100101")));
}
