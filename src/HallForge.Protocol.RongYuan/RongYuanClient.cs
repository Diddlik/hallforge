using System.Buffers.Binary;
using HallForge.Core;

namespace HallForge.Protocol.RongYuan;

/// <summary>
/// The only commands this client can send. Keeping the allowlist as an enum means no code path
/// can build a frame for SET, firmware or bootloader opcodes.
/// </summary>
public enum ReadCommand : byte
{
    GetProfile = 0x84,
    GetLedParam = 0x87,
    GetUsbVersion = 0x8F,
    GetFeatureList = 0xE6,
}

/// <summary>Per-key tables readable via GET_MULTI_MAGNETISM (0xE5). Values are 16-bit unless noted.</summary>
public enum MagnetismTable : byte
{
    PressTravel = 0x00,
    LiftTravel = 0x01,
    RapidTriggerPress = 0x02,
    RapidTriggerLift = 0x03,
    /// <summary>One byte per key.</summary>
    KeyMode = 0x07,
}

public class ProtocolException(string message) : Exception(message);

/// <summary>The firmware returned the request unchanged, which is how it answers commands it does not implement.</summary>
public sealed class CommandNotSupportedException(string message) : ProtocolException(message);

/// <summary>Wired RongYuan configuration protocol over 64-byte feature reports (report id 0).</summary>
public sealed class RongYuanClient(IHidConnection connection)
{
    public const int ReportLength = 65;
    public const int PageLength = 64;
    private const byte GetMultiMagnetism = 0xE5;
    // The only write this client can send: one key's trigger settings (vendor name FEA_CMD_SET_MAGNETISM_MODE).
    private const byte SetKeyTriggerCommand = 0x1A;
    private const byte NormalMode = 0x01;
    private const byte RapidTriggerFlag = 0x80;
    // Onboard profiles 0-3; the firmware does not bounds-check the profile byte.
    private const byte MaxProfile = 3;
    // The vendor driver leaves at least this much time between per-key writes.
    private static readonly TimeSpan WriteSpacing = TimeSpan.FromMilliseconds(30);
    private const int Attempts = 5;
    // Until the firmware has prepared a response, reading returns the request unchanged. Simple queries
    // answer within milliseconds; table reads may take longer, so the wait grows before giving up.
    private static readonly int[] PollDelaysMs = [10, 20, 50, 100, 200, 400];

    public static byte[] Encode(ReadCommand command)
    {
        // C# allows casting any byte to an enum, so the allowlist must be enforced at runtime.
        if (!Enum.IsDefined(command))
            throw new ArgumentOutOfRangeException(nameof(command), $"0x{(byte)command:X2} is not an allowed command.");
        return Frame((byte)command, []);
    }

    /// <param name="page">Each page holds 64 bytes: 32 keys for 16-bit tables, 64 keys for <see cref="MagnetismTable.KeyMode"/>.</param>
    public static byte[] Encode(MagnetismTable table, byte page)
    {
        if (!Enum.IsDefined(table))
            throw new ArgumentOutOfRangeException(nameof(table), $"0x{(byte)table:X2} is not an allowed table.");
        // 126 matrix positions x 2 bytes fit in 4 pages; the firmware does not check page numbers itself.
        if (page > 3)
            throw new ArgumentOutOfRangeException(nameof(page));
        return Frame(GetMultiMagnetism, [(byte)table, 0x01, page]);
    }

    /// <summary>
    /// Encodes a complete normal-mode trigger setting for one key. Values are sent in 0.1 mm units;
    /// the deadzone is sent as <c>mm * 10 - 1</c>. Layout verified against a USB capture of the vendor driver.
    /// </summary>
    public static byte[] Encode(KeyTrigger trigger, byte profile, int matrixIndex)
    {
        if (profile > MaxProfile)
            throw new ArgumentOutOfRangeException(nameof(profile));
        if (matrixIndex is < 0 or >= TriggerLimits.MatrixSize)
            throw new ArgumentOutOfRangeException(nameof(matrixIndex));

        var report = Frame(SetKeyTriggerCommand, [
            profile,
            NormalMode,
            trigger.RapidTrigger ? RapidTriggerFlag : (byte)0,
            Tenths(trigger.ActuationMm),
            0,
            (byte)matrixIndex,
        ]);
        report[9] = Tenths(trigger.ReleaseMm);
        report[10] = Tenths(trigger.RapidTriggerPressMm);
        report[11] = Tenths(trigger.RapidTriggerReleaseMm);
        report[26] = (byte)(Tenths(trigger.DeadzoneMm) - 1);
        return report;
    }

    private static byte Tenths(decimal mm)
    {
        var tenths = mm * 10;
        if (tenths != decimal.Truncate(tenths) || tenths is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(mm), $"{mm} mm cannot be encoded in 0.1 mm steps.");
        return (byte)tenths;
    }

    private static byte[] Frame(byte command, ReadOnlySpan<byte> parameters)
    {
        var report = new byte[ReportLength];
        report[1] = command;
        parameters.CopyTo(report.AsSpan(2));
        report[8] = Checksum(report.AsSpan(1, 7));
        return report;
    }

    /// <summary>"Bit7" checksum: 255 minus the low byte of the sum of command and parameter bytes.</summary>
    public static byte Checksum(ReadOnlySpan<byte> commandAndParameters)
    {
        var sum = 0;
        foreach (var b in commandAndParameters)
            sum += b;
        return (byte)(255 - (sum & 0xFF));
    }

    /// <returns>The 64-byte response; byte 0 echoes the command.</returns>
    public byte[] Query(ReadCommand command)
    {
        var request = Encode(command);
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var response = Exchange(request);
            if (response[0] == (byte)command)
                return response;
        }
        throw new ProtocolException($"No matching response to 0x{(byte)command:X2} after {Attempts} attempts.");
    }

    /// <summary>
    /// Magnetism pages carry no command echo, so a stale response would be indistinguishable from data.
    /// A page is accepted only after two consecutive reads return identical bytes.
    /// </summary>
    public byte[] ReadPage(MagnetismTable table, byte page)
    {
        var request = Encode(table, page);
        byte[]? previous = null;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var current = Exchange(request);
            if (previous is not null && current.AsSpan().SequenceEqual(previous))
                return current;
            previous = current;
        }
        throw new ProtocolException($"Page {page} of table {table} did not return a stable response after {Attempts} attempts.");
    }

    public IReadOnlyList<ushort> ReadTravelTable(MagnetismTable table, int keyCount)
    {
        if (table == MagnetismTable.KeyMode)
            throw new ArgumentException("Key mode is an 8-bit table.", nameof(table));
        var data = new List<byte>();
        for (var page = 0; page * PageLength < keyCount * 2; page++)
            data.AddRange(ReadPage(table, (byte)page));
        var bytes = data.ToArray();
        return Enumerable.Range(0, keyCount).Select(i => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2))).ToList();
    }

    public byte ReadActiveProfile()
    {
        var profile = Query(ReadCommand.GetProfile)[1];
        return profile <= MaxProfile ? profile : throw new ProtocolException($"Unexpected active profile {profile}.");
    }

    /// <summary>Sends one key's settings. The firmware does not acknowledge or report them back, so success cannot be verified.</summary>
    public void WriteKeyTrigger(KeyTrigger trigger, byte profile, int matrixIndex)
    {
        connection.SetFeature(Encode(trigger, profile, matrixIndex));
        Thread.Sleep(WriteSpacing);
    }

    public LightingState ReadLighting() => LightingState.Parse(Query(ReadCommand.GetLedParam));

    public RongYuanIdentity Identify()
    {
        var usbVersion = Query(ReadCommand.GetUsbVersion);
        byte[]? featureList;
        try
        {
            featureList = Query(ReadCommand.GetFeatureList);
        }
        catch (CommandNotSupportedException)
        {
            featureList = null;
        }
        return RongYuanIdentity.Parse(usbVersion, featureList);
    }

    /// <returns>The 64-byte response without the report id.</returns>
    private byte[] Exchange(byte[] request)
    {
        connection.SetFeature(request);
        foreach (var delay in PollDelaysMs)
        {
            Thread.Sleep(delay);
            var response = new byte[ReportLength];
            connection.GetFeature(response);
            // Compares command, parameters and checksum; real responses overwrite at least one of these bytes.
            if (!response.AsSpan(1, 8).SequenceEqual(request.AsSpan(1, 8)))
                return response[1..];
        }
        throw new CommandNotSupportedException($"The firmware does not implement command 0x{request[1]:X2} with these parameters (request echoed).");
    }
}

/// <param name="DeviceId">Firmware model id; identifies the exact keyboard model, unlike the shared USB product id.</param>
/// <param name="FirmwareVersion">Null when the firmware leaves the version bytes as sent (observed on model 1778).</param>
/// <param name="PrecisionCode">Raw precision byte from the feature list, or null if the list is missing or marked invalid.</param>
public sealed record RongYuanIdentity(uint DeviceId, ushort? FirmwareVersion, byte? PrecisionCode, byte[] UsbVersionResponse, byte[]? FeatureListResponse)
{
    private const byte ValidMarker = 0xAA;

    // Vendor notation: 0x0408 is written "v408".
    public string? FirmwareText => FirmwareVersion is { } v ? $"v{v >> 8}{v & 0xFF:00}" : null;

    public static RongYuanIdentity Parse(byte[] usbVersion, byte[]? featureList)
    {
        if (usbVersion.Length < 9 || usbVersion[0] != (byte)ReadCommand.GetUsbVersion)
            throw new ProtocolException("Malformed GET_USB_VERSION response.");
        if (featureList is not null && (featureList.Length < 3 || featureList[0] != (byte)ReadCommand.GetFeatureList))
            throw new ProtocolException("Malformed GET_FEATURE_LIST response.");

        var request = RongYuanClient.Encode(ReadCommand.GetUsbVersion);
        var versionEchoed = usbVersion.AsSpan(7, 2).SequenceEqual(request.AsSpan(8, 2));
        return new RongYuanIdentity(
            BinaryPrimitives.ReadUInt32LittleEndian(usbVersion.AsSpan(1)),
            versionEchoed ? null : BinaryPrimitives.ReadUInt16LittleEndian(usbVersion.AsSpan(7)),
            featureList is not null && featureList[1] == ValidMarker ? featureList[2] : null,
            usbVersion,
            featureList);
    }
}
