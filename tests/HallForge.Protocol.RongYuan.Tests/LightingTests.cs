namespace HallForge.Protocol.RongYuan.Tests;

public class LightingTests
{
    // Recorded GET_LEDPARAM response from the MOD007B ISO HE (vendor driver capture, 2026-10-28).
    [Fact]
    public void Parse_RecordedMod007bResponse()
    {
        var response = new byte[64];
        Convert.FromHexString("8713020207B4B4B4").CopyTo(response, 0);

        var light = LightingState.Parse(response);

        Assert.Equal(new LightingState(LightingMode.PressActionOff, Brightness: 2, Speed: 2, Option: 0, Rainbow: true, 0xB4, 0xB4, 0xB4), light);
    }

    [Fact]
    public void Parse_FixedColorAndOption()
    {
        var response = new byte[64];
        Convert.FromHexString("8704040410FF0000").CopyTo(response, 0);

        var light = LightingState.Parse(response);

        Assert.Equal(LightingMode.Wave, light.Mode);
        Assert.Equal(0, light.Speed);
        Assert.Equal(1, light.Option);
        Assert.False(light.Rainbow);
        Assert.Equal((byte)0xFF, light.Red);
    }

    [Fact]
    public void Parse_RejectsUnknownMode()
    {
        var response = new byte[64];
        response[0] = 0x87;
        response[1] = 0x40;

        Assert.Throws<ProtocolException>(() => LightingState.Parse(response));
    }
}

public class LightingEncodeTests
{
    // SET_LEDPARAM payloads recorded from the vendor driver on the MOD007B ISO HE (2026-10-28).
    [Theory]
    [InlineData(LightingMode.AlwaysOn, 4, 0, false, 0xFF, 0x00, 0x00, "0701040408ff0000e8")]
    [InlineData(LightingMode.AlwaysOn, 4, 0, false, 0x00, 0x00, 0x00, "0701040408000000e7")]
    [InlineData(LightingMode.RainDown, 4, 2, true, 0xB4, 0xB4, 0xB4, "0711020407b4b4b4be")]
    public void Encode_MatchesVendorDriverCapture(LightingMode mode, int brightness, int speed, bool rainbow, byte r, byte g, byte b, string expected)
    {
        var report = new LightingState(mode, brightness, speed, 0, rainbow, r, g, b).Encode();

        Assert.Equal(65, report.Length);
        Assert.Equal(expected, Convert.ToHexStringLower(report, 1, 9));
        Assert.All(report.Skip(10), x => Assert.Equal(0, x));
    }

    [Fact]
    public void White_IsSentAsFirmwareWhiteAndReadBackAsWhite()
    {
        var white = new LightingState(LightingMode.AlwaysOn, 4, 0, 0, false, 0xFF, 0xFF, 0xFF);

        var report = white.Encode();
        report[1] = 0x87;

        Assert.Equal("FAFFFA", Convert.ToHexString(report, 6, 3));
        Assert.Equal(white, LightingState.Parse(report[1..]));
    }

    [Theory]
    [InlineData(LightingMode.UserPicture, 4, 0)]
    [InlineData(LightingMode.MusicFollow2, 4, 0)]
    [InlineData(LightingMode.AlwaysOn, 5, 0)]
    [InlineData(LightingMode.Wave, 4, 5)]
    public void Encode_RejectsUnsupportedModesAndRanges(LightingMode mode, int brightness, int speed) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightingState(mode, brightness, speed, 0, true, 0, 0, 0).Encode());
}
