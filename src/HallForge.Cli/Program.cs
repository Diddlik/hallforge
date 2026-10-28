using System.CommandLine;
using System.Reflection;
using HallForge.Core;
using HallForge.Transport.Hid;

const int ExitDeviceNotFound = 2;

var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
var discovery = new KeyboardDiscoveryService(
    new HidSharpEnumerator(),
    DeviceDefinitionLoader.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "devices")));

var allOption = new Option<bool>("--all") { Description = "Include HID interfaces without a matching device definition." };
var list = new Command("list", "List keyboard configuration interfaces.") { allOption };
list.SetAction(result =>
{
    foreach (var c in discovery.Discover(result.GetValue(allOption)))
    {
        var hid = c.Interface;
        Console.WriteLine(
            $"{hid.Id}  {DiagnosticReport.Hex(hid.VendorId)}:{DiagnosticReport.Hex(hid.ProductId)}  " +
            $"usage {Format(hid.UsagePage)}/{Format(hid.Usage)}  if {hid.InterfaceNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}  " +
            $"{c.Support,-10}  {c.Definition?.Name ?? hid.ProductName ?? "(no name)"}");
    }
});

var deviceOption = new Option<string>("--device") { Description = "Device id from 'devices list'.", Required = true };
var outputOption = new Option<FileInfo?>("--output") { Description = "Write an anonymized diagnostic report to this file." };
var probe = new Command("probe", "Show read-only details of a device and all interfaces of the same USB device.") { deviceOption, outputOption };
probe.SetAction(result =>
{
    var all = discovery.Discover(includeUnmatched: true);
    var target = all.FirstOrDefault(c => c.Interface.Id == result.GetValue(deviceOption));
    if (target is null)
    {
        Console.Error.WriteLine("Device not found. Run 'hallforge devices list --all' to see available ids.");
        return ExitDeviceNotFound;
    }

    var siblings = all.Where(c => c.Interface.VendorId == target.Interface.VendorId && c.Interface.ProductId == target.Interface.ProductId);
    var report = DiagnosticReport.Create(siblings, version);
    Console.WriteLine(report);
    if (result.GetValue(outputOption) is { } output)
        File.WriteAllText(output.FullName, report);
    return 0;
});

var root = new RootCommand($"HallForge {version} - Hall-effect keyboard configurator")
{
    new Command("devices", "Discover and inspect keyboards (read-only).") { list, probe },
};
return root.Parse(args).Invoke();

static string Format(int? value) => value is { } v ? DiagnosticReport.Hex(v) : "?";
