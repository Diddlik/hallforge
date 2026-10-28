namespace HallForge.Protocol.RongYuan;

/// <summary>Lighting effects in firmware order (vendor driver names). The value is the mode byte of GET/SET_LEDPARAM.</summary>
public enum LightingMode : byte
{
    Off = 0,
    AlwaysOn = 1,
    Breath = 2,
    Neon = 3,
    Wave = 4,
    Ripple = 5,
    Raindrop = 6,
    Snake = 7,
    PressAction = 8,
    Converge = 9,
    SineWave = 10,
    Kaleidoscope = 11,
    LineWave = 12,
    UserPicture = 13,
    Laser = 14,
    CircleWave = 15,
    Dazzling = 16,
    RainDown = 17,
    Meteor = 18,
    PressActionOff = 19,
    MusicFollow3 = 20,
    ScreenColor = 21,
    MusicFollow2 = 22,
    Train = 23,
    Fireworks = 24,
}

/// <param name="Speed">0 (slowest) to 4; the firmware stores 4 minus this value.</param>
/// <param name="Option">Effect variant, e.g. direction for Wave; meaning depends on the mode.</param>
/// <param name="Rainbow">Cycles through colors instead of using <paramref name="Red"/>/<paramref name="Green"/>/<paramref name="Blue"/>.</param>
public sealed record LightingState(LightingMode Mode, int Brightness, int Speed, int Option, bool Rainbow, byte Red, byte Green, byte Blue)
{
    public const int MaxSpeed = 4;
    public const int MaxBrightness = 4;
    private const byte SetLedParam = 0x07;
    private const int RainbowColorMode = 7;
    private const int FixedColorMode = 8;

    // The vendor driver never sends pure white; it substitutes #FAFFFA and maps it back when reading.
    private static readonly (byte R, byte G, byte B) White = (0xFF, 0xFF, 0xFF);
    private static readonly (byte R, byte G, byte B) FirmwareWhite = (0xFA, 0xFF, 0xFA);

    /// <summary>
    /// Modes that need data or a host process this client does not provide (user picture upload, music and
    /// screen color streaming) are not writable.
    /// </summary>
    public static bool IsWritable(LightingMode mode) =>
        Enum.IsDefined(mode) && mode is not (LightingMode.UserPicture or LightingMode.MusicFollow2 or LightingMode.MusicFollow3 or LightingMode.ScreenColor);

    /// <summary>Decodes a GET_LEDPARAM (0x87) response; byte 0 is the echoed command.</summary>
    public static LightingState Parse(byte[] response)
    {
        if (response.Length < 8 || !Enum.IsDefined((LightingMode)response[1]))
            throw new ProtocolException("Malformed GET_LEDPARAM response.");
        var color = (response[5], response[6], response[7]) == FirmwareWhite ? White : (response[5], response[6], response[7]);
        return new LightingState(
            (LightingMode)response[1],
            response[3],
            MaxSpeed - response[2],
            response[4] >> 4,
            (response[4] & 0x0F) == RainbowColorMode,
            color.Item1, color.Item2, color.Item3);
    }

    /// <summary>Encodes SET_LEDPARAM (0x07) with the "Bit8" checksum. Layout verified against a vendor driver capture.</summary>
    public byte[] Encode()
    {
        if (!IsWritable(Mode))
            throw new ArgumentOutOfRangeException(nameof(Mode), $"Lighting mode {Mode} cannot be set.");
        if (Brightness is < 0 or > MaxBrightness)
            throw new ArgumentOutOfRangeException(nameof(Brightness));
        if (Speed is < 0 or > MaxSpeed)
            throw new ArgumentOutOfRangeException(nameof(Speed));
        if (Option is < 0 or > 15)
            throw new ArgumentOutOfRangeException(nameof(Option));

        var color = (Red, Green, Blue) == White ? FirmwareWhite : (Red, Green, Blue);
        var report = new byte[RongYuanClient.ReportLength];
        report[1] = SetLedParam;
        report[2] = (byte)Mode;
        report[3] = (byte)(MaxSpeed - Speed);
        report[4] = (byte)Brightness;
        report[5] = (byte)(Option << 4 | (Rainbow ? RainbowColorMode : FixedColorMode));
        (report[6], report[7], report[8]) = color;
        report[9] = RongYuanClient.Checksum(report.AsSpan(1, 8));
        return report;
    }
}
