using HallForge.Core;

namespace HallForge.App.Tests;

public sealed class KeyboardEditorViewModelTests : IDisposable
{
    private static readonly TravelRange Travel = new(0.1m, 4.0m, 0.1m);
    private static readonly KeyTrigger Fast = new(0.4m, 0.4m, true, 0.5m, 0.5m, 3.7m);

    private static readonly DeviceDefinition Definition = new(
        "test", "Test Board", "rongyuan", false,
        new DeviceMatch([0x3151], [0x4035], 0xFFFF, 0x02), [1778],
        new TriggerLimits(Travel, Travel, Travel),
        new Dictionary<string, int> { ["KeyW"] = 14, ["KeyA"] = 9, ["KeyQ"] = 8 },
        [new KeyGeometry("KeyQ", 1.5, 2, 1, 1), new KeyGeometry("KeyW", 2.5, 2, 1, 1), new KeyGeometry("KeyA", 1.75, 3, 1, 1)]);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "hallforge-app-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class NoDevice : IHidTransport
    {
        public IReadOnlyList<HidInterfaceInfo> Enumerate() => [];
        public IHidConnection Open(HidInterfaceInfo hid) => throw new IOException("No device in tests.");
    }

    private KeyboardEditorViewModel Editor()
    {
        var hid = new HidInterfaceInfo("path", 0x3151, 0x4035, 0x0103, null, null, 0xFFFF, 0x02, 2, 0, 0, 65);
        var editor = new KeyboardEditorViewModel(
            new KeyboardCandidate(hid, Definition), new KeyLegends("test", "Test", new Dictionary<string, string>()),
            new NoDevice(), new StateStore(_root), _ => Task.FromResult(false));
        foreach (var key in editor.Keys.Where(k => k.KeyId != "KeyQ"))
            key.Known = Fast;
        return editor;
    }

    [Fact]
    public void Selection_ShowsCommonValuesAndMixedOnes()
    {
        var editor = Editor();
        editor.Keys.Single(k => k.KeyId == "KeyW").Known = Fast with { ReleaseMm = 0.6m };

        editor.SelectCommand.Execute("All");

        Assert.Null(editor.Actuation); // KeyQ is unknown
        editor.Keys.Single(k => k.KeyId == "KeyQ").IsSelected = false;
        Assert.Equal(0.4m, editor.Actuation);
        Assert.Null(editor.Release);
        Assert.True(editor.RapidTrigger);
    }

    [Fact]
    public void EditingAField_PatchesOnlyThatFieldOfSelectedKeys()
    {
        var editor = Editor();
        editor.Keys.Single(k => k.KeyId == "KeyW").IsSelected = true;

        editor.Actuation = 1.0m;

        var w = editor.Keys.Single(k => k.KeyId == "KeyW");
        Assert.Equal(new TriggerPatch(ActuationMm: 1.0m), w.Pending);
        Assert.Equal(Fast with { ActuationMm = 1.0m }, w.Effective);
        Assert.Equal(1, editor.PendingCount);
        Assert.Null(editor.Keys.Single(k => k.KeyId == "KeyA").Pending);
    }

    [Fact]
    public void ChangingSelection_DoesNotCreatePendingChanges()
    {
        var editor = Editor();

        editor.SelectCommand.Execute("All");
        editor.SelectCommand.Execute("None");

        Assert.Equal(0, editor.PendingCount);
    }

    [Fact]
    public void Discard_ClearsAllPendingChanges()
    {
        var editor = Editor();
        editor.SelectCommand.Execute("All");
        editor.Deadzone = 3.0m;

        editor.DiscardCommand.Execute(null);

        Assert.Equal(0, editor.PendingCount);
        Assert.All(editor.Keys, k => Assert.Null(k.Pending));
    }

    [Fact]
    public void LoadProfile_AddsToDraftAndReportsUnknownKeys()
    {
        var editor = Editor();
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "profile.json");
        File.WriteAllText(path, """
            { "schemaVersion": 1, "name": "x", "groups": [ { "keyIds": ["KeyW", "Digit9"], "settings": { "actuationMm": 0.2 } } ] }
            """);

        editor.LoadProfile(path);

        Assert.Equal(1, editor.PendingCount);
        Assert.Contains("Digit9", editor.Status, StringComparison.Ordinal);
    }
}
