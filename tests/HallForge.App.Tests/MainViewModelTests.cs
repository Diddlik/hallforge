using HallForge.Core;

namespace HallForge.App.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private static readonly TravelRange Travel = new(0.1m, 4.0m, 0.1m);

    private static readonly DeviceDefinition Definition = new(
        "test", "Test Board", "rongyuan", false,
        new DeviceMatch([0x3151], [0x4035], 0xFFFF, 0x02, [0x4011]), [1778],
        new TriggerLimits(Travel, Travel, Travel),
        new Dictionary<string, int> { ["KeyW"] = 14 },
        [new KeyGeometry("KeyW", 2.5, 2, 1, 1)]);

    private static readonly HidInterfaceInfo Cable = new("cable", 0x3151, 0x4035, 0x0103, null, null, 0xFFFF, 0x02, 2, 0, 0, 65);
    private static readonly HidInterfaceInfo Receiver = new("receiver", 0x3151, 0x4011, 0x0103, null, null, 0xFFFF, 0x02, 2, 0, 0, 65);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "hallforge-app-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class Usb : IHidTransport
    {
        public List<HidInterfaceInfo> Plugged { get; } = [];
        public IReadOnlyList<HidInterfaceInfo> Enumerate() => [.. Plugged];
        public IHidConnection Open(HidInterfaceInfo hid) => throw new IOException("No device in tests.");
    }

    [Fact]
    public async Task CheckDevices_FollowsCableAndKeepsDraftsWhenUnplugged()
    {
        // MainViewModel creates an UpdateManager, which needs the Velopack locator the app sets up in Main.
        Velopack.VelopackApp.Build().Run();
        var usb = new Usb();
        var main = new MainViewModel(new KeyboardDiscoveryService(usb, [Definition]), usb, new StateStore(_root),
            new KeyLegends("test", "Test", new Dictionary<string, string>()), new AppSettings());

        usb.Plugged.Add(Receiver);
        await main.CheckDevicesAsync();
        Assert.Null(main.Keyboard);
        Assert.Contains("Test Board", main.Status, StringComparison.Ordinal);

        usb.Plugged.Add(Cable);
        await main.CheckDevicesAsync();
        Assert.True(main.IsKeyboardConnected);

        var keyboard = main.Keyboard!;
        keyboard.Keys.Single().IsSelected = true;
        keyboard.Actuation.Input = "1.0";
        usb.Plugged.Remove(Cable);
        await main.CheckDevicesAsync();
        Assert.Same(keyboard, main.Keyboard);
        Assert.True(main.IsKeyboardLost);

        usb.Plugged.Add(Cable);
        await main.CheckDevicesAsync();
        Assert.True(main.IsKeyboardConnected);

        keyboard.DiscardCommand.Execute(null);
        usb.Plugged.Remove(Cable);
        await main.CheckDevicesAsync();
        Assert.Null(main.Keyboard);
        Assert.False(main.IsKeyboardConnected);
    }
}
