using HallForge.Core;

namespace HallForge.App.Tests;

public class RoarViewModelTests
{
    /// <summary>Answers identify, active profile and a Roar read with KeyW (matrix 14) enabled.</summary>
    private sealed class FakeKeyboard : IHidTransport, IHidConnection
    {
        private byte[] _request = [];

        public IReadOnlyList<HidInterfaceInfo> Enumerate() => [];
        public IHidConnection Open(HidInterfaceInfo hid) => this;
        public void SetFeature(byte[] report) => _request = report.ToArray();

        public void GetFeature(byte[] report)
        {
            byte[] response = _request[1] switch
            {
                0x8F => [0x8F, 0xF2, 0x06, 0, 0, 0, 0, 1, 2],
                0x85 => [0x85, 0],
                0x9F => Convert.FromHexString("9F00000000000060010E"),
                var command => [command, 1],
            };
            response.CopyTo(report, 1);
        }

        public void Dispose()
        {
        }
    }

    private static (RoarViewModel Roar, List<ReviewViewModel> Reviews) Roar(params string[] selected)
    {
        var hid = new HidInterfaceInfo("path", 0x3151, 0x4035, 0x0103, null, null, 0xFFFF, 0x02, 2, 0, 0, 65);
        var range = new TravelRange(0.1m, 4.0m, 0.1m);
        var keys = new Dictionary<string, int> { ["KeyW"] = 14, ["KeyA"] = 9, ["KeyS"] = 15, ["KeyD"] = 21, ["KeyQ"] = 8 };
        var definition = new DeviceDefinition("test", "Test", "rongyuan", false, new DeviceMatch([0x3151], [], 0xFFFF, 2), [1778],
            new TriggerLimits(range, range, range), keys, []);
        var reviews = new List<ReviewViewModel>();
        var roar = new RoarViewModel(new KeyboardCandidate(hid, definition), new FakeKeyboard(),
            r => { reviews.Add(r); return Task.CompletedTask; }, () => selected, id => id);
        return (roar, reviews);
    }

    [Fact]
    public async Task Load_ShowsEnabledKeys()
    {
        var (roar, _) = Roar();

        await roar.LoadCommand.ExecuteAsync(null);

        Assert.Equal(Strings.RoarCurrent.Replace("{0}", "KeyW", StringComparison.Ordinal), roar.KeysText);
        Assert.True(roar.TurnOffCommand.CanExecute(null));
    }

    [Fact]
    public void Commands_AreDisabledUntilRoarWasRead() =>
        Assert.False(Roar("KeyW").Roar.UseSelectionCommand.CanExecute(null));

    [Fact]
    public async Task UseSelection_AcceptsOneToFourKeysOnly()
    {
        var (roar, reviews) = Roar("KeyW", "KeyA", "KeyS", "KeyD", "KeyQ");
        await roar.LoadCommand.ExecuteAsync(null);

        await roar.UseSelectionCommand.ExecuteAsync(null);

        Assert.Equal(Strings.RoarSelectKeys, roar.Status);
        Assert.Empty(reviews);
    }

    [Fact]
    public async Task UseSelection_SameKeysAreUnchanged()
    {
        var (roar, reviews) = Roar("KeyW");
        await roar.LoadCommand.ExecuteAsync(null);

        await roar.UseSelectionCommand.ExecuteAsync(null);

        Assert.Equal(Strings.RoarUnchanged, roar.Status);
        Assert.Empty(reviews);
    }

    [Fact]
    public async Task UseSelection_ReviewsTheChange()
    {
        var (roar, reviews) = Roar("KeyD", "KeyA");
        await roar.LoadCommand.ExecuteAsync(null);

        await roar.UseSelectionCommand.ExecuteAsync(null);

        var row = Assert.Single(Assert.Single(reviews).Rows);
        Assert.Equal("KeyW → KeyA, KeyD", row.Changes);
        Assert.True(reviews[0].NeedsAcknowledgement);
    }
}
