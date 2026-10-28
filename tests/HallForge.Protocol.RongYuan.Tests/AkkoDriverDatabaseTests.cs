using System.Text;
using HallForge.Core;

namespace HallForge.Protocol.RongYuan.Tests;

public class AkkoDriverDatabaseTests
{
    private static readonly TravelRange Range = new(0.1m, 4.0m, 0.1m);

    private static readonly DeviceDefinition Definition = new(
        "test", "Test Board", "rongyuan", false,
        new DeviceMatch([0x3151], [0x4035], 0xFFFF, 0x02), [1778],
        new TriggerLimits(Range, Range, Range),
        new Dictionary<string, int> { ["KeyW"] = 14, ["AltLeft"] = 23 });

    private static string Mode(int usage, int travel, bool fire = false, string option = "normal") =>
        $$"""{"mode":"normal","option":"{{option}}","travel":{{travel}},"liftTravel":{{travel}},"firePressTravel":2,"fireLiftTravel":3,"all":false,"original":{{usage}},"fire":{{(fire ? "true" : "false")}},"deadZoneTravel":3.7}""";

    // Mimics the sled log: binary headers around each version, the newest appended last.
    private static byte[] Database(params string[] versions)
    {
        var data = new List<byte>([0x00, 0xF9, 0x10]);
        foreach (var modes in versions)
        {
            data.AddRange(Encoding.ASCII.GetBytes("\"devConfig_1778\""));
            data.AddRange([0xF9, 0xE9, 0x64]);
            data.AddRange(Encoding.ASCII.GetBytes($$"""[{"isShared":false,"magnetismModes":[{{modes}}]},{"isShared":false}]"""));
            data.AddRange([0xCE, 0x4F, 0x0A]);
        }
        return [.. data];
    }

    [Fact]
    public void ParseProfile_UsesNewestVersionAndConvertsTenths()
    {
        var database = Database(Mode(26, 9), $"{Mode(26, 4, fire: true)},{Mode(226, 19)}");

        var import = AkkoDriverDatabase.ParseProfile(database, 1778, 0, Definition);

        Assert.Equal(new KeyTrigger(0.4m, 0.4m, true, 0.2m, 0.3m, 3.7m), import.Keys["KeyW"]);
        Assert.Equal(1.9m, import.Keys["AltLeft"].ActuationMm);
        Assert.Empty(import.Warnings);
    }

    [Fact]
    public void ParseProfile_SkipsTruncatedNewerVersion()
    {
        var database = Database(Mode(26, 9));
        var truncated = Encoding.ASCII.GetBytes("\"devConfig_1778\"ù[{\"isShared\":false,\"magnetismModes\":[{\"mo");

        var import = AkkoDriverDatabase.ParseProfile([.. database, .. truncated], 1778, 0, Definition);

        Assert.Equal(0.9m, import.Keys["KeyW"].ActuationMm);
    }

    [Fact]
    public void ParseProfile_WarnsAboutUnknownKeysAndSpecialModes()
    {
        var database = Database($"{Mode(4, 9)},{Mode(26, 9, option: "dks")}");

        var import = AkkoDriverDatabase.ParseProfile(database, 1778, 0, Definition);

        Assert.Empty(import.Keys);
        Assert.Equal(2, import.Warnings.Count);
    }

    [Fact]
    public void ParseProfile_FailsWithoutConfigForModel() =>
        Assert.Throws<InvalidDataException>(() => AkkoDriverDatabase.ParseProfile(Database(Mode(26, 9)), 847, 0, Definition));

    [Fact]
    public void ParseProfile_FailsForMissingProfile() =>
        Assert.Throws<InvalidDataException>(() => AkkoDriverDatabase.ParseProfile(Database(Mode(26, 9)), 1778, 2, Definition));
}
