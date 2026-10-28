namespace HallForge.Core.Tests;

public sealed class StateStoreTests : IDisposable
{
    private static readonly TravelRange Range = new(0.1m, 4.0m, 0.1m);
    private static readonly KeyTrigger Fast = new(0.4m, 0.4m, true, 0.5m, 0.5m, 3.7m);
    private static readonly KeyTrigger Slow = new(1.9m, 1.9m, false, 0.2m, 0.3m, 3.7m);

    private static readonly DeviceDefinition Definition = new(
        "test", "Test Board", "rongyuan", false,
        new DeviceMatch([0x3151], [0x4035], 0xFFFF, 0x02), [1778],
        new TriggerLimits(Range, Range, Range),
        new Dictionary<string, int> { ["KeyW"] = 14, ["AltLeft"] = 23 },
        []);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "hallforge-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Load_ReturnsNullWhenNothingIsKnown() =>
        Assert.Null(new StateStore(_root).Load("test", 0));

    [Fact]
    public void RecordWrite_KeepsOtherKnownKeys()
    {
        var store = new StateStore(_root);
        store.Save(new KeyboardState(1, "test", 1778, 0, "akko-driver-import", DateTime.UtcNow, new Dictionary<string, KeyTrigger> { ["KeyW"] = Fast }));

        store.RecordWrite(Definition, 1778, 0, "AltLeft", Slow);

        var state = store.Load("test", 0)!;
        Assert.Equal(Fast, state.Keys["KeyW"]);
        Assert.Equal(Slow, state.Keys["AltLeft"]);
    }

    [Fact]
    public void SaveBackup_NeverOverwritesAnExistingBackup()
    {
        var store = new StateStore(_root);
        var state = new KeyboardState(1, "test", 1778, 0, "akko-driver-import", new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc), new Dictionary<string, KeyTrigger>());

        store.SaveBackup(state);

        Assert.Throws<IOException>(() => store.SaveBackup(state));
    }

    [Fact]
    public void Load_RejectsCorruptStateFile()
    {
        var store = new StateStore(_root);
        var path = store.StatePath("test", 0);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        Assert.Throws<InvalidDataException>(() => store.Load("test", 0));
    }

    [Theory]
    [InlineData(0x1A, "KeyW")]
    [InlineData(0x1E, "Digit1")]
    [InlineData(0x27, "Digit0")]
    [InlineData(0x45, "F12")]
    [InlineData(0x64, "IntlBackslash")]
    [InlineData(0xE2, "AltLeft")]
    public void KeyCodes_MapHidUsageToW3cCode(int usage, string code) =>
        Assert.Equal(code, KeyCodes.FromHidUsage(usage));

    [Theory]
    [InlineData("KeyZ", "Y")]
    [InlineData("KeyY", "Z")]
    [InlineData("KeyA", "A")]
    [InlineData("Digit1", "1")]
    [InlineData("F5", "F5")]
    [InlineData("IntlBackslash", "<")]
    public void KeyLegends_IsoGermanShowsPrintedLabels(string keyId, string legend) =>
        Assert.Equal(legend, KeyLegends.Load(Path.Combine(AppContext.BaseDirectory, "layouts", "iso-de.json")).For(keyId));
}
