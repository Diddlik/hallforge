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

public sealed class ProtocolException(string message) : Exception(message);

/// <summary>Wired RongYuan configuration protocol over 64-byte feature reports (report id 0).</summary>
public sealed class RongYuanClient(IHidConnection connection)
{
    public const int ReportLength = 65;
    private const int Attempts = 5;
    // The firmware needs a short gap before the response is ready; reading too early returns the previous one.
    private static readonly TimeSpan ReadGap = TimeSpan.FromMilliseconds(10);

    public static byte[] Encode(ReadCommand command)
    {
        // C# allows casting any byte to an enum, so the allowlist must be enforced at runtime.
        if (!Enum.IsDefined(command))
            throw new ArgumentOutOfRangeException(nameof(command), $"0x{(byte)command:X2} is not an allowed command.");
        var report = new byte[ReportLength];
        report[1] = (byte)command;
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
            connection.SetFeature(request);
            Thread.Sleep(ReadGap);
            var response = new byte[ReportLength];
            connection.GetFeature(response);
            if (response[1] == (byte)command)
                return response[1..];
        }
        throw new ProtocolException($"No matching response to 0x{(byte)command:X2} after {Attempts} attempts.");
    }

    public RongYuanIdentity Identify() =>
        RongYuanIdentity.Parse(Query(ReadCommand.GetUsbVersion), Query(ReadCommand.GetFeatureList));
}

/// <param name="DeviceId">Firmware model id; identifies the exact keyboard model, unlike the shared USB product id.</param>
/// <param name="PrecisionCode">Raw precision byte from the feature list, or null if the firmware marks the list invalid.</param>
public sealed record RongYuanIdentity(uint DeviceId, ushort FirmwareVersion, byte? PrecisionCode, byte[] UsbVersionResponse, byte[] FeatureListResponse)
{
    private const byte ValidMarker = 0xAA;

    // Vendor notation: 0x0408 is written "v408".
    public string FirmwareText => $"v{FirmwareVersion >> 8}{FirmwareVersion & 0xFF:00}";

    public static RongYuanIdentity Parse(byte[] usbVersion, byte[] featureList)
    {
        if (usbVersion.Length < 9 || usbVersion[0] != (byte)ReadCommand.GetUsbVersion)
            throw new ProtocolException("Malformed GET_USB_VERSION response.");
        if (featureList.Length < 3 || featureList[0] != (byte)ReadCommand.GetFeatureList)
            throw new ProtocolException("Malformed GET_FEATURE_LIST response.");

        return new RongYuanIdentity(
            BinaryPrimitives.ReadUInt32LittleEndian(usbVersion.AsSpan(1)),
            BinaryPrimitives.ReadUInt16LittleEndian(usbVersion.AsSpan(7)),
            featureList[1] == ValidMarker ? featureList[2] : null,
            usbVersion,
            featureList);
    }
}
