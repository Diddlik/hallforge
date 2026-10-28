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
