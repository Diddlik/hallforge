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
            new NoDevice(), new StateStore(_root), _ => Task.CompletedTask);
        foreach (var key in editor.Keys.Where(k => k.KeyId != "KeyQ"))
            key.Known = Fast;
        return editor;
    }

    private static KeyViewModel Key(KeyboardEditorViewModel editor, string id) => editor.Keys.Single(k => k.KeyId == id);

    [Fact]
    public void Selection_ShowsCommonValuesAndMixedOnes()
    {
        var editor = Editor();
        Key(editor, "KeyW").Known = Fast with { ReleaseMm = 0.6m };

        editor.SelectCommand.Execute("All");

        Assert.Null(editor.Actuation.Value); // KeyQ is unknown
        Assert.True(editor.Actuation.IsWarning);
        Key(editor, "KeyQ").IsSelected = false;
        Assert.Equal(0.4m, editor.Actuation.Value);
        Assert.Equal("0.40", editor.Actuation.Input);
        Assert.Null(editor.Release.Value);
        Assert.Equal(Strings.Mixed, editor.Release.Placeholder);
        Assert.True(editor.RapidTrigger);
    }

    [Fact]
    public void TypingAValue_PatchesOnlyThatFieldOfSelectedKeys()
    {
        var editor = Editor();
        Key(editor, "KeyW").IsSelected = true;

        editor.Actuation.Input = "1.0";

        var w = Key(editor, "KeyW");
        Assert.Equal(new TriggerPatch(ActuationMm: 1.0m), w.Pending);
        Assert.Equal(Fast with { ActuationMm = 1.0m }, w.Effective);
        Assert.Equal(1, editor.PendingCount);
        Assert.Equal(1, editor.FieldChangeCount);
        Assert.Null(Key(editor, "KeyA").Pending);
    }

    [Theory]
    [InlineData("1,0")]
    [InlineData("9.9")]
    [InlineData("1.05")]
    public void InvalidInput_IsReportedAndNotApplied(string text)
    {
        var editor = Editor();
        Key(editor, "KeyW").IsSelected = true;

        editor.Actuation.Input = text;

        Assert.True(editor.Actuation.HasError);
        Assert.True(editor.HasInputError);
        Assert.Equal(0, editor.PendingCount);
    }

    [Fact]
    public void SettingTheKnownValueAgain_LeavesNoDraft()
    {
        var editor = Editor();
        Key(editor, "KeyW").IsSelected = true;
        editor.Actuation.Input = "1.0";

        editor.Actuation.Input = "0.4";

        Assert.Null(Key(editor, "KeyW").Pending);
        Assert.Equal(0, editor.PendingCount);
    }

    [Fact]
    public void Undo_RevertsSliderDragAsOneStep()
    {
        var editor = Editor();
        Key(editor, "KeyW").IsSelected = true;
        editor.RapidTriggerOffCommand.Execute(null);

        editor.Actuation.SliderValue = 1.0;
        editor.Actuation.SliderValue = 1.5;
        editor.Actuation.SliderValue = 2.0;
        editor.UndoCommand.Execute(null);

        Assert.Equal(new TriggerPatch(RapidTriggerEnabled: false), Key(editor, "KeyW").Pending);
        editor.UndoCommand.Execute(null);
        Assert.Null(Key(editor, "KeyW").Pending);
        Assert.False(editor.UndoCommand.CanExecute(null));
    }

    [Fact]
    public void StepButtons_MoveByTheDeviceStep()
    {
        var editor = Editor();
        Key(editor, "KeyW").IsSelected = true;

        editor.Release.IncreaseCommand.Execute(null);

        Assert.Equal(0.5m, Key(editor, "KeyW").Pending?.ReleaseMm);
    }

    [Fact]
    public void StepButtons_ShiftMixedKeysFromTheirOwnValues()
    {
        var editor = Editor();
        Key(editor, "KeyA").Known = Fast with { ActuationMm = 1.0m };
        editor.SelectCommand.Execute("All");

        editor.Actuation.IncreaseCommand.Execute(null);
        editor.Actuation.IncreaseCommand.Execute(null);

        Assert.Equal(0.6m, Key(editor, "KeyW").Pending?.ActuationMm);
        Assert.Equal(1.2m, Key(editor, "KeyA").Pending?.ActuationMm);
        Assert.Null(Key(editor, "KeyQ").Pending); // unknown keys are not guessed
        editor.UndoCommand.Execute(null);
        Assert.Equal(0, editor.PendingCount);
    }

    [Fact]
    public void SelectArea_SelectsOverlappingKeysAndCanAdd()
    {
        var editor = Editor();
        var pitch = KeyViewModel.Pitch;

        editor.SelectArea(1.5 * pitch, 2 * pitch, 1.2 * pitch, 0.5 * pitch, add: false); // Q and W row
        Assert.Equal(["KeyQ", "KeyW"], editor.Keys.Where(k => k.IsSelected).Select(k => k.KeyId));

        editor.SelectArea(1.8 * pitch, 3.2 * pitch, 0.2 * pitch, 0.2 * pitch, add: true);
        Assert.All(editor.Keys, k => Assert.True(k.IsSelected));
    }

    [Fact]
    public void ShownValue_ChangesTheValuePrintedOnKeys()
    {
        var editor = Editor();

        editor.ShownValue = editor.ValueFields.Single(f => f.Field == KeyValueField.RapidTriggerPress);

        Assert.Equal("0.50", Key(editor, "KeyW").ValueText);
        Assert.True(Key(editor, "KeyW").IsRapidTrigger);
        Assert.Equal("?", Key(editor, "KeyQ").ValueText);
    }

    [Fact]
    public void Pick_SelectsTogglesAndExtends()
    {
        var editor = Editor();

        editor.Pick(Key(editor, "KeyQ"), toggle: false, extend: false);
        editor.Pick(Key(editor, "KeyA"), toggle: false, extend: true);
        Assert.All(editor.Keys, k => Assert.True(k.IsSelected));

        editor.Pick(Key(editor, "KeyW"), toggle: true, extend: false);
        Assert.False(Key(editor, "KeyW").IsSelected);

        editor.Pick(Key(editor, "KeyW"), toggle: false, extend: false);
        Assert.Equal(["KeyW"], editor.Keys.Where(k => k.IsSelected).Select(k => k.KeyId));
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
    public void RiskNote_FlagsShortDistances()
    {
        var editor = Editor();
        Key(editor, "KeyW").IsSelected = true;

        Assert.True(editor.HasRisk); // Fast uses 0.4 mm actuation
        editor.ApplyTemplateCommand.Execute("Comfort");
        Assert.False(editor.HasRisk);
    }

    [Fact]
    public void Discard_ClearsAllPendingChangesAndCanBeUndone()
    {
        var editor = Editor();
        editor.SelectCommand.Execute("All");
        editor.Deadzone.Input = "3.0";

        editor.DiscardCommand.Execute(null);
        Assert.Equal(0, editor.PendingCount);
        Assert.All(editor.Keys, k => Assert.Null(k.Pending));

        editor.UndoCommand.Execute(null);
        Assert.Equal(3, editor.PendingCount);
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

        Assert.Equal(1, editor.LoadProfile(path));

        Assert.Equal(1, editor.PendingCount);
        Assert.Contains("Digit9", editor.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_IsBlockedUntilConnected()
    {
        var editor = Editor();
        Key(editor, "KeyW").IsSelected = true;
        editor.Actuation.Input = "1.0";

        editor.ReviewCommand.Execute(null);

        Assert.Equal(Strings.NotConnected, editor.Status);
    }
}
