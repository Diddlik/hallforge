using System.Buffers.Binary;
using HallForge.Core;

namespace HallForge.Protocol.RongYuan;

/// <summary>
/// The only commands this client can send. Keeping the allowlist as an enum means no code path
/// can build a frame for SET, firmware or bootloader opcodes.
/// </summary>
public enum ReadCommand : byte
{
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
