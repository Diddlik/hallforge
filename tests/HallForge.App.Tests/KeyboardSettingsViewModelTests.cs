using HallForge.Core;
using HallForge.Protocol.RongYuan;

namespace HallForge.App.Tests;

public class KeyboardSettingsViewModelTests
{
    /// <summary>Keeps travel mode and active profile like the firmware: SET commands change them, GET commands report them.</summary>
    private sealed class FakeKeyboard : IHidTransport, IHidConnection
    {
        private byte[] _request = [];
        public byte Mode { get; set; } = 3;
        public byte Profile { get; set; }
        public int Writes { get; private set; }

        public IReadOnlyList<HidInterfaceInfo> Enumerate() => [];
        public IHidConnection Open(HidInterfaceInfo hid) => this;

        public void SetFeature(byte[] report)
        {
            _request = report.ToArray();
            switch (report[1])
            {
                case 0x1D:
                    Mode = report[2];
                    Writes++;
                    break;
                case 0x05:
                    Profile = report[2];
                    Writes++;
                    break;
            }
        }

        public void GetFeature(byte[] report)
        {
            byte[] response = _request[1] switch
            {
                0x8F => [0x8F, 0xF2, 0x06, 0, 0, 0, 0, 1, 2],
                0x85 => [0x85, Profile],
                0x9D => [0x9D, Mode, 0, 0, 0, 0, 0, 1],
                var command => [command, 1],
            };
            response.CopyTo(report, 1);
        }

        public void Dispose()
        {
        }
    }

    private static (KeyboardSettingsViewModel Settings, FakeKeyboard Keyboard, List<string> Reloads) Settings(bool drafts = false)
    {
        var hid = new HidInterfaceInfo("path", 0x3151, 0x4035, 0x0103, null, null, 0xFFFF, 0x02, 2, 0, 0, 65);
        var range = new TravelRange(0.1m, 4.0m, 0.1m);
        var definition = new DeviceDefinition("test", "Test", "rongyuan", false, new DeviceMatch([0x3151], [], 0xFFFF, 2), [1778],
            new TriggerLimits(range, range, range), new Dictionary<string, int>(), []);
        var keyboard = new FakeKeyboard();
        var reloads = new List<string>();
        var settings = new KeyboardSettingsViewModel(new KeyboardCandidate(hid, definition), keyboard, () => drafts,
            () => { reloads.Add("reload"); return Task.CompletedTask; });
        return (settings, keyboard, reloads);
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++)
            await Task.Delay(20);
    }

    [Fact]
    public async Task Load_ShowsModeAndProfile()
    {
        var (settings, _, _) = Settings();

        await settings.LoadCommand.ExecuteAsync(null);

        Assert.Equal(MagnetismMode.Custom, settings.Mode!.Mode);
        Assert.Equal(0, settings.Profile!.Profile);
        Assert.False(settings.IsNotCustom);
    }

    [Fact]
    public async Task ChangingTheMode_WritesItAndWarnsOutsideCustom()
    {
        var (settings, keyboard, _) = Settings();
        await settings.LoadCommand.ExecuteAsync(null);

        settings.Mode = KeyboardSettingsViewModel.AllModes.Single(m => m.Mode == MagnetismMode.Comfort);
        await Until(() => !settings.IsBusy && keyboard.Writes > 0);

        Assert.Equal(0, keyboard.Mode);
        Assert.True(settings.IsNotCustom);
    }

    [Fact]
    public async Task SwitchingTheProfile_WritesItAndReloadsKeySettings()
    {
        var (settings, keyboard, reloads) = Settings();
        await settings.LoadCommand.ExecuteAsync(null);

        settings.Profile = KeyboardSettingsViewModel.AllProfiles[1];
        await Until(() => reloads.Count > 0);

        Assert.Equal(1, keyboard.Profile);
        Assert.Single(reloads);
    }

    private static async Task<(AutoSwitchViewModel Auto, KeyboardSettingsViewModel Settings, FakeKeyboard Keyboard, Func<string?, Task> Focus, AppSettings Stored)> AutoSwitch(bool drafts = false)
    {
        var (settings, keyboard, _) = Settings(drafts);
        await settings.LoadCommand.ExecuteAsync(null);
        var stored = new AppSettings
        {
            AutoSwitch = true,
            ProgramRules = [new ProgramRule("bf6", MagnetismMode.Gaming, 1), new ProgramRule("bad", (MagnetismMode)9, 0)],
            OtherPrograms = new ProgramRule("", MagnetismMode.Comfort, 0),
        };
        string? foreground = null;
        var auto = new AutoSwitchViewModel(stored, () => settings, () => false, () => foreground, () => { });
        return (auto, settings, keyboard, async program =>
        {
            foreground = program;
            await auto.CheckAsync();
        }, stored);
    }

    [Fact]
    public async Task AutoSwitch_AppliesTheRuleOfTheForegroundProgram()
    {
        var (_, settings, keyboard, focus, _) = await AutoSwitch();

        await focus("BF6");

        Assert.Equal((byte)MagnetismMode.Gaming, keyboard.Mode);
        Assert.Equal(1, keyboard.Profile);
        Assert.Equal(MagnetismMode.Gaming, settings.Mode!.Mode);
    }

    [Fact]
    public async Task AutoSwitch_UsesOtherProgramsWithoutARuleAndDropsInvalidRules()
    {
        var (auto, _, keyboard, focus, _) = await AutoSwitch();

        await focus("notepad");

        Assert.Equal((byte)MagnetismMode.Comfort, keyboard.Mode);
        Assert.Equal("bf6", Assert.Single(auto.Rules).Program);
    }

    [Fact]
    public async Task AutoSwitch_KeepsTheSettingWhileHallForgeOrTheSameProgramIsInFront()
    {
        var (_, _, keyboard, focus, _) = await AutoSwitch();
        await focus("bf6");
        var writes = keyboard.Writes;

        await focus(System.Diagnostics.Process.GetCurrentProcess().ProcessName);
        await focus("bf6");

        Assert.Equal(writes, keyboard.Writes);
    }

    [Fact]
    public async Task AutoSwitch_DoesNothingWhenDisabled()
    {
        var (auto, _, keyboard, focus, stored) = await AutoSwitch();
        auto.IsEnabled = false;

        await focus("bf6");

        Assert.Equal(0, keyboard.Writes);
        Assert.False(stored.AutoSwitch);
    }

    [Fact]
    public async Task AutoSwitch_LeavesTheProfileWhileKeyChangesArePending()
    {
        var (_, _, keyboard, focus, _) = await AutoSwitch(drafts: true);

        await focus("bf6");

        Assert.Equal((byte)MagnetismMode.Gaming, keyboard.Mode);
        Assert.Equal(0, keyboard.Profile);
    }

    [Fact]
    public async Task AutoSwitch_AddNormalizesTheNameAndRejectsDuplicates()
    {
        var (auto, _, _, _, stored) = await AutoSwitch();

        auto.NewProgram = " Notepad.exe ";
        auto.AddCommand.Execute(null);
        auto.NewProgram = "notepad";

        Assert.Contains(stored.ProgramRules, r => r.Program == "Notepad");
        Assert.False(auto.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task SwitchingTheProfile_IsBlockedWhileKeyChangesArePending()
    {
        var (settings, keyboard, reloads) = Settings(drafts: true);
        await settings.LoadCommand.ExecuteAsync(null);

        settings.Profile = KeyboardSettingsViewModel.AllProfiles[1];

        Assert.Equal(Strings.ProfileSwitchBlockedByDrafts, settings.Status);
        Assert.Equal(0, keyboard.Writes);
        Assert.Empty(reloads);
    }
}
