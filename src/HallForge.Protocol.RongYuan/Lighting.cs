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
    private const int MaxSpeed = 4;
    private const int RainbowColorMode = 7;

    /// <summary>Decodes a GET_LEDPARAM (0x87) response; byte 0 is the echoed command.</summary>
    public static LightingState Parse(byte[] response)
    {
        if (response.Length < 8 || !Enum.IsDefined((LightingMode)response[1]))
            throw new ProtocolException("Malformed GET_LEDPARAM response.");
        return new LightingState(
            (LightingMode)response[1],
            response[3],
            MaxSpeed - response[2],
            response[4] >> 4,
            (response[4] & 0x0F) == RainbowColorMode,
            response[5], response[6], response[7]);
    }
}
