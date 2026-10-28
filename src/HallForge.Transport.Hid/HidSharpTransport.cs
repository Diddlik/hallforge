using System.Globalization;
using System.Text.RegularExpressions;
using HallForge.Core;
using HidSharp;

namespace HallForge.Transport.Hid;

/// <summary>HID access via HidSharp. Enumeration reads metadata only; streams are opened explicitly via <see cref="Open"/>.</summary>
public sealed partial class HidSharpTransport : IHidTransport
{
    public IReadOnlyList<HidInterfaceInfo> Enumerate() =>
        DeviceList.Local.GetHidDevices().Select(ToInfo).ToList();

    public IHidConnection Open(HidInterfaceInfo hid)
    {
        var device = DeviceList.Local.GetHidDevices().FirstOrDefault(d => d.DevicePath == hid.Path)
            ?? throw new IOException("The device is no longer connected.");
        if (!device.TryOpen(out var stream))
            throw new IOException("The device could not be opened. Another program, such as the vendor driver, may be using it.");
        stream.ReadTimeout = 1000;
        stream.WriteTimeout = 1000;
        return new Connection(stream);
    }

    private sealed class Connection(HidStream stream) : IHidConnection
    {
        public void SetFeature(byte[] report) => stream.SetFeature(report);
        public void GetFeature(byte[] report) => stream.GetFeature(report);
        public void Dispose() => stream.Dispose();
    }

    private static HidInterfaceInfo ToInfo(HidDevice device)
    {
        var (usagePage, usage) = TryGet(() => PrimaryUsage(device));
        return new HidInterfaceInfo(
            device.DevicePath,
            device.VendorID,
            device.ProductID,
            device.ReleaseNumberBcd,
            TryGet(device.GetManufacturer),
            TryGet(device.GetProductName),
            usagePage,
            usage,
            InterfaceNumber(device.DevicePath),
            TryGet(device.GetMaxInputReportLength),
            TryGet(device.GetMaxOutputReportLength),
            TryGet(device.GetMaxFeatureReportLength));
    }

    private static (int?, int?) PrimaryUsage(HidDevice device)
    {
        var item = device.GetReportDescriptor().DeviceItems.FirstOrDefault();
        if (item is null || item.Usages.GetAllValues().FirstOrDefault() is not (var value and not 0))
            return (null, null);
        return ((int)(value >> 16), (int)(value & 0xFFFF));
    }

    // Only Windows device paths carry the USB interface number ("&mi_02"); elsewhere it stays unknown.
    private static int? InterfaceNumber(string path) =>
        InterfacePattern().Match(path) is { Success: true } match
            ? int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : null;

    [GeneratedRegex("mi_([0-9a-f]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex InterfacePattern();

    // Descriptors and strings are optional metadata; a device that denies access or lacks them is still listed.
    private static T? TryGet<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            return default;
        }
    }
}
