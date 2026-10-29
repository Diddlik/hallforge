using HallForge.Core;

namespace HallForge.Protocol.RongYuan;

public sealed record RoarSlot(int MatrixIndex, bool Enabled);

/// <summary>
/// Per-profile keyboard options (GET 0x86 / SET 0x06). Bits of <paramref name="Flags"/> per the vendor driver:
/// 0 Win lock, 2 Mac mode, 3 WASD/arrow swap, 4 backlight off, 5 side light off, 6 keyboard mode, 7 lock.
/// HallForge changes only bit 4 and writes everything else back as read.
/// </summary>
public sealed record KeyboardOptions(byte Flags, byte FnLayout)
{
    private const byte LedOffFlag = 0x10;

    public bool LedOff => (Flags & LedOffFlag) != 0;

    public KeyboardOptions WithLedOff(bool off) => this with { Flags = (byte)(off ? Flags | LedOffFlag : Flags & ~LedOffFlag) };

    /// <summary>Byte 0 is the echoed command. Refuses unknown data, because a write sends the parsed values back.</summary>
    public static KeyboardOptions Parse(byte[] response)
    {
        // Recorded on model 1778: 86 00 10 01, all later bytes zero. Some driver classes add a power save byte at 4.
        if (response.Length < 8 || response.AsSpan(4, 3).ContainsAnyExcept((byte)0))
            throw new ProtocolException($"Unexpected GET_KBOPTION response {Convert.ToHexString(response, 0, Math.Min(8, response.Length))}.");
        return new KeyboardOptions(response[2], response[3]);
    }
}

/// <summary>
/// Roar mode (vendor name "guansu", FEA_CMD_GET/SET_MAGNETISM_FAST): up to four keys that the firmware reports
/// with the fastest response. The setting is global, not per onboard profile. Layout from the vendor driver,
/// verified against the capture <c>ws/usb5</c> on model 1778.
/// </summary>
public static class Roar
{
    public const int SlotCount = 4;
    public const byte GetCommand = 0x9F;
    private const byte SetCommand = 0x1F;
    // Slot n is [enabled, matrix index] at payload offset 8 + 2n, after the checksum.
    private const int SlotOffset = 8;

    /// <summary>Decodes a GET_MAGNETISM_FAST response; byte 0 is the echoed command. Empty slots are null.</summary>
    public static IReadOnlyList<RoarSlot?> Parse(byte[] response)
    {
        if (response.Length < SlotOffset + 2 * SlotCount || response[0] != GetCommand)
            throw new ProtocolException("Malformed GET_MAGNETISM_FAST response.");
        var slots = new RoarSlot?[SlotCount];
        for (var n = 0; n < SlotCount; n++)
        {
            var (enabled, index) = (response[SlotOffset + 2 * n], response[SlotOffset + 2 * n + 1]);
            if (enabled > 1 || index >= TriggerLimits.MatrixSize)
                throw new ProtocolException($"Unexpected Roar slot {n}: {enabled:X2} {index:X2}.");
            // The vendor driver writes an empty slot as 00 00, so a disabled slot on matrix index 0 reads as empty.
            slots[n] = enabled == 0 && index == 0 ? null : new RoarSlot(index, enabled == 1);
        }
        return slots;
    }

    public static byte[] Encode(IReadOnlyList<RoarSlot?> slots)
    {
        if (slots.Count != SlotCount)
            throw new ArgumentException($"Roar has exactly {SlotCount} slots.", nameof(slots));
        var keys = slots.OfType<RoarSlot>().ToList();
        if (keys.Any(s => s.MatrixIndex is < 0 or >= TriggerLimits.MatrixSize))
            throw new ArgumentOutOfRangeException(nameof(slots));
        if (keys.DistinctBy(s => s.MatrixIndex).Count() != keys.Count)
            throw new ArgumentException("A key can be in only one Roar slot.", nameof(slots));

        var report = new byte[RongYuanClient.ReportLength];
        report[1] = SetCommand;
        // The checksum covers only the command and the six empty parameter bytes, not the slots (see capture).
        report[8] = RongYuanClient.Checksum(report.AsSpan(1, 7));
        for (var n = 0; n < SlotCount; n++)
        {
            if (slots[n] is not { } slot)
                continue;
            report[1 + SlotOffset + 2 * n] = slot.Enabled ? (byte)1 : (byte)0;
            report[1 + SlotOffset + 2 * n + 1] = (byte)slot.MatrixIndex;
        }
        return report;
    }
}
