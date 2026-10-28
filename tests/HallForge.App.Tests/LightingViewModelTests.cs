using HallForge.Core;
using HallForge.Protocol.RongYuan;

namespace HallForge.App.Tests;

public class LightingViewModelTests
{
    private static LightingViewModel Lighting()
    {
        var hid = new HidInterfaceInfo("path", 0x3151, 0x4035, 0x0103, null, null, 0xFFFF, 0x02, 2, 0, 0, 65);
        var range = new TravelRange(0.1m, 4.0m, 0.1m);
        var definition = new DeviceDefinition("test", "Test", "rongyuan", false, new DeviceMatch([0x3151], [], 0xFFFF, 2), [1778],
            new TriggerLimits(range, range, range), new Dictionary<string, int>(), []);
        return new LightingViewModel(new KeyboardCandidate(hid, definition), new NoDevice(), _ => Task.FromResult(false));
    }

    private sealed class NoDevice : IHidTransport
    {
        public IReadOnlyList<HidInterfaceInfo> Enumerate() => [];
        public IHidConnection Open(HidInterfaceInfo hid) => throw new IOException("No device in tests.");
    }

    [Fact]
    public void Modes_OfferOnlyWritableEffects()
    {
        var modes = Lighting().Modes.Select(m => m.Mode).ToList();

        Assert.Contains(LightingMode.AlwaysOn, modes);
        Assert.DoesNotContain(LightingMode.UserPicture, modes);
        Assert.DoesNotContain(LightingMode.ScreenColor, modes);
    }

    [Theory]
    [InlineData("ff8800", "#FF8800")]
    [InlineData("FF88", null)]
    [InlineData("GG0000", null)]
    public void ColorPreview_OnlyForValidHexColors(string text, string? expected)
    {
        var lighting = Lighting();

        lighting.ColorHex = text;

        Assert.Equal(expected, lighting.ColorPreview);
    }

    [Fact]
    public void Apply_IsDisabledUntilLightingWasRead() =>
        Assert.False(Lighting().ApplyCommand.CanExecute(null));
}
