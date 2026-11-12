using HallForge.Core;

namespace HallForge.Protocol.RongYuan.Tests;

public class RongYuanReceiverTests
{
    private sealed class Receiver(params byte[] status) : IHidConnection
    {
        public List<byte[]> Sent { get; } = [];
        public void SetFeature(byte[] report) => Sent.Add(report.ToArray());
        public void GetFeature(byte[] report) => status.CopyTo(report.AsSpan(1));
        public void Dispose()
        {
        }
    }

    [Fact]
    public void ReadBattery_SendsStatusQueryAndReadsPercent()
    {
        // Captured from the MOD007B receiver with the keyboard at 100 %.
        var receiver = new Receiver(0x00, 0x64, 0x00, 0x00, 0x01, 0x01, 0x01);

        Assert.Equal(100, RongYuanReceiver.ReadBattery(receiver));
        Assert.Equal([0x00, 0xF7], Assert.Single(receiver.Sent)[..2]);
    }

    [Fact]
    public void ReadBattery_IsUnknownBeforeTheKeyboardReported() =>
        Assert.Null(RongYuanReceiver.ReadBattery(new Receiver(0x00, 0x00, 0x00, 0x01, 0x01, 0x01)));
}
