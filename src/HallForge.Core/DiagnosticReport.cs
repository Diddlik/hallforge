using System.Runtime.InteropServices;
using System.Text.Json;

namespace HallForge.Core;

/// <summary>
/// Shareable report for hardware research. Omits OS device paths and serial numbers,
/// which can identify the user's machine.
/// </summary>
public static class DiagnosticReport
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string Create(IEnumerable<KeyboardCandidate> candidates, string appVersion) =>
        JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            createdAtUtc = DateTime.UtcNow,
            appVersion,
            os = RuntimeInformation.OSDescription,
            interfaces = candidates.Select(c => new
            {
                id = c.Interface.Id,
                vendorId = Hex(c.Interface.VendorId),
                productId = Hex(c.Interface.ProductId),
                releaseNumberBcd = Hex(c.Interface.ReleaseNumberBcd),
                manufacturer = c.Interface.Manufacturer,
                productName = c.Interface.ProductName,
                usagePage = c.Interface.UsagePage is { } page ? Hex(page) : null,
                usage = c.Interface.Usage is { } usage ? Hex(usage) : null,
                interfaceNumber = c.Interface.InterfaceNumber,
                maxInputReportLength = c.Interface.MaxInputReportLength,
                maxOutputReportLength = c.Interface.MaxOutputReportLength,
                maxFeatureReportLength = c.Interface.MaxFeatureReportLength,
                definition = c.Definition?.Id,
                support = c.Support.ToString(),
            }),
        }, Options);

    public static string Hex(int value) => $"0x{value:X4}";
}
