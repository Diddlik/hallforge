using System.Text.Json;

namespace HallForge.Core.Tests;

public class ProfileTests
{
    private static readonly TravelRange Travel = new(0.1m, 4.0m, 0.1m);
    private static readonly TravelRange Rapid = new(0.1m, 2.5m, 0.1m);
    private static readonly KeyTrigger Known = new(0.9m, 0.9m, false, 0.2m, 0.3m, 3.7m);

    private static readonly DeviceDefinition Definition = new(
        "test", "Test Board", "rongyuan", false,
        new DeviceMatch([0x3151], [0x4035], 0xFFFF, 0x02), [1778],
        new TriggerLimits(Travel, Rapid, Travel),
        new Dictionary<string, int> { ["KeyW"] = 14, ["KeyQ"] = 8, ["Digit1"] = 7 });

    private static KeyboardState State(params (string Key, KeyTrigger Trigger)[] keys) =>
        new(1, "test", 1778, 0, "test", DateTime.UtcNow, keys.ToDictionary(k => k.Key, k => k.Trigger));

    private static ApplyPlan Plan(string json, KeyboardState? state) =>
        ApplyPlan.Create(KeyboardProfile.Parse(json).Resolve(), Definition, state);

    [Fact]
    public void Parse_RejectsExplicitNull()
    {
        var ex = Assert.Throws<JsonException>(() => KeyboardProfile.Parse("""
            { "schemaVersion": 1, "name": "x", "groups": [ { "keyIds": ["KeyW"], "settings": { "actuationMm": null } } ] }
            """));
        Assert.Contains("$.groups[0].settings.actuationMm", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 2, "name": "x", "groups": [] }""")]
    [InlineData("""{ "schemaVersion": 1, "name": "x", "groups": [], "target": {} }""")]
    [InlineData("""{ "schemaVersion": 1, "name": " ", "groups": [] }""")]
    public void Parse_RejectsInvalidProfiles(string json) =>
        Assert.ThrowsAny<JsonException>(() => KeyboardProfile.Parse(json));

    [Fact]
    public void Resolve_LaterGroupsAndKeyOverridesWin()
    {
        var profile = KeyboardProfile.Parse("""
            {
              "schemaVersion": 1, "name": "x",
              "groups": [
                { "keyIds": ["KeyW", "KeyQ"], "settings": { "actuationMm": 1.0, "rapidTriggerEnabled": true } },
                { "keyIds": ["KeyQ"], "settings": { "actuationMm": 2.0 } }
              ],
              "keys": { "KeyW": { "actuationMm": 0.5 } }
            }
            """);

        var patches = profile.Resolve();

        Assert.Equal(new TriggerPatch(ActuationMm: 0.5m, RapidTriggerEnabled: true), patches["KeyW"]);
        Assert.Equal(new TriggerPatch(ActuationMm: 2.0m, RapidTriggerEnabled: true), patches["KeyQ"]);
    }

    [Fact]
    public void Plan_PartialPatchKeepsKnownFields()
    {
        var plan = Plan("""{ "schemaVersion": 1, "name": "x", "groups": [ { "keyIds": ["KeyQ"], "settings": { "actuationMm": 1.0 } } ] }""", State(("KeyQ", Known)));

        var change = Assert.Single(plan.Changes);
        Assert.Equal(Known with { ActuationMm = 1.0m }, change.After);
        Assert.Equal(Known, change.Before);
        Assert.Equal(8, change.MatrixIndex);
    }

    [Fact]
    public void Plan_CountsKeysThatAlreadyMatch()
    {
        var plan = Plan("""{ "schemaVersion": 1, "name": "x", "groups": [ { "keyIds": ["KeyQ"], "settings": { "actuationMm": 0.9 } } ] }""", State(("KeyQ", Known)));

        Assert.Empty(plan.Changes);
        Assert.Equal(1, plan.UnchangedKeys);
        Assert.True(plan.CanApply);
    }

    [Fact]
    public void Plan_UnknownKeyNeedsAllFields()
    {
        var plan = Plan("""{ "schemaVersion": 1, "name": "x", "groups": [ { "keyIds": ["Digit1"], "settings": { "actuationMm": 1.5 } } ] }""", State());

        Assert.False(plan.CanApply);
        Assert.Contains("Digit1", Assert.Single(plan.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_RejectsUnknownKeyIdsAndLimitViolations()
    {
        var plan = Plan("""
            { "schemaVersion": 1, "name": "x", "groups": [
              { "keyIds": ["KeyZ"], "settings": { "actuationMm": 1.0 } },
              { "keyIds": ["KeyQ"], "settings": { "rapidTriggerPressMm": 3.0 } } ] }
            """, State(("KeyQ", Known)));

        Assert.Equal(2, plan.Errors.Count);
        Assert.Throws<InvalidOperationException>(() => plan.Execute(_ => { }, _ => { }));
    }

    [Fact]
    public void Execute_RecordsEachKeyRightAfterItIsSent()
    {
        var plan = Plan("""{ "schemaVersion": 1, "name": "x", "groups": [ { "keyIds": ["KeyQ", "KeyW"], "settings": { "actuationMm": 1.0 } } ] }""",
            State(("KeyQ", Known), ("KeyW", Known)));
        var log = new List<string>();

        var ex = Assert.Throws<IOException>(() => plan.Execute(
            change => { if (change.KeyId == "KeyW") throw new IOException("unplugged"); log.Add($"sent {change.KeyId}"); },
            change => log.Add($"recorded {change.KeyId}")));

        Assert.Equal(["sent KeyQ", "recorded KeyQ"], log);
        Assert.Equal("unplugged", ex.Message);
    }

    [Theory]
    [InlineData("bf6-balanced.json")]
    [InlineData("fps-conservative.json")]
    [InlineData("fps-fast.json")]
    public void ShippedProfiles_ApplyToMod007bWithoutKnownState(string file)
    {
        var definition = DeviceDefinitionLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "devices", "akko-mod007b-iso-he.json"));
        var profile = KeyboardProfile.Load(Path.Combine(AppContext.BaseDirectory, "profiles", file));

        var plan = ApplyPlan.Create(profile.Resolve(), definition, state: null);

        Assert.Empty(plan.Errors);
        Assert.NotEmpty(plan.Changes);
    }
}
