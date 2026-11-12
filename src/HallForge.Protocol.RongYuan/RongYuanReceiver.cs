using HallForge.Core;

namespace HallForge.Protocol.RongYuan;

/// <summary>
/// Queries the 2.4 GHz receiver answers itself. Commands for the keyboard get no answer over the receiver
/// (see docs/protocol-research.md), so this is the only thing read from it.
/// </summary>
public static class RongYuanReceiver
{
    // GET_DONGLE_STATUS in monsgeek-akko-linux; the vendor driver polls it without a checksum.
    private const byte GetStatus = 0xF7;
    private static readonly TimeSpan AnswerDelay = TimeSpan.FromMilliseconds(15);

    /// <returns>Keyboard battery in percent, or null while the receiver has not heard from the keyboard.</returns>
    public static int? ReadBattery(IHidConnection connection)
    {
        var request = new byte[RongYuanClient.ReportLength];
        request[1] = GetStatus;
        connection.SetFeature(request);
        Thread.Sleep(AnswerDelay);
        var response = new byte[RongYuanClient.ReportLength];
        connection.GetFeature(response);
        // The receiver reports 0 until the keyboard has sent its level.
        return response[2] is > 0 and <= 100 ? response[2] : null;
    }
}
