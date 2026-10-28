using System.Security.Cryptography;
using System.Text;

namespace HallForge.Core;

/// <summary>One HID interface as reported by the operating system. Read-only metadata, no device I/O.</summary>
public sealed record HidInterfaceInfo(
    string Path,
    int VendorId,
    int ProductId,
    int ReleaseNumberBcd,
    string? Manufacturer,
    string? ProductName,
    int? UsagePage,
    int? Usage,
    int? InterfaceNumber,
    int MaxInputReportLength,
    int MaxOutputReportLength,
    int MaxFeatureReportLength)
{
    // Derived from the OS path so the id is stable per port but does not expose the path itself.
    public string Id { get; } = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path)))[..10];
}

public interface IHidEnumerator
{
    IReadOnlyList<HidInterfaceInfo> Enumerate();
}
