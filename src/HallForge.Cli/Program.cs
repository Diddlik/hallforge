using System.CommandLine;
using System.Reflection;
using HallForge.Core;
using HallForge.Protocol.RongYuan;
using HallForge.Transport.Hid;

const int ExitDeviceNotFound = 2;
const int ExitUnsupported = 3;
const int ExitDeviceError = 4;

var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
var transport = new HidSharpTransport();
var discovery = new KeyboardDiscoveryService(
    transport,
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

var identify = new Command("identify", "Send read-only identification commands and show firmware model id and version.") { deviceOption, outputOption };
identify.SetAction(result =>
{
    var target = discovery.Discover().FirstOrDefault(c => c.Interface.Id == result.GetValue(deviceOption));
    if (target is null)
    {
        Console.Error.WriteLine("Device not found or has no device definition. Run 'hallforge devices list'.");
        return ExitDeviceNotFound;
    }
    if (target.Definition!.Protocol != "rongyuan")
    {
        Console.Error.WriteLine($"Protocol '{target.Definition.Protocol}' is not implemented.");
        return ExitUnsupported;
    }

    try
    {
        using var connection = transport.Open(target.Interface);
        var identity = new RongYuanClient(connection).Identify();
        var text = string.Join(Environment.NewLine,
            $"Firmware device id: {identity.DeviceId}",
            $"Firmware version:   {identity.FirmwareText ?? "not reported"}",
            $"Precision code:     {identity.PrecisionCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}",
            $"GET_USB_VERSION:    {Convert.ToHexString(identity.UsbVersionResponse)}",
            $"GET_FEATURE_LIST:   {(identity.FeatureListResponse is { } list ? Convert.ToHexString(list) : "not supported")}");
        Console.WriteLine(text);
        if (!target.Definition.FirmwareModelIds.Contains(identity.DeviceId))
            Console.WriteLine($"Warning: firmware model id {identity.DeviceId} is not listed in '{target.Definition.Id}'. The device is not the expected model and stays unsupported.");
        if (result.GetValue(outputOption) is { } output)
            File.WriteAllText(output.FullName, text + Environment.NewLine);
        return 0;
    }
    catch (Exception ex) when (ex is IOException or TimeoutException or ProtocolException)
    {
        Console.Error.WriteLine($"Identification failed: {ex.Message}");
        return ExitDeviceError;
    }
});

var keysOption = new Option<int>("--keys") { Description = "Matrix positions to read (1-126).", DefaultValueFactory = _ => 96 };
var magnetism = new Command("magnetism", "Read raw per-key travel tables (read-only) for protocol research.") { deviceOption, keysOption };
magnetism.SetAction(result =>
{
    var keyCount = result.GetValue(keysOption);
    if (keyCount is < 1 or > 126)
    {
        Console.Error.WriteLine("--keys must be between 1 and 126.");
        return ExitUnsupported;
    }
    var target = discovery.Discover().FirstOrDefault(c => c.Interface.Id == result.GetValue(deviceOption));
    if (target is null || target.Definition!.Protocol != "rongyuan")
    {
        Console.Error.WriteLine("Device not found or not a RongYuan device. Run 'hallforge devices list'.");
        return ExitDeviceNotFound;
    }

    try
    {
        using var connection = transport.Open(target.Interface);
        var client = new RongYuanClient(connection);
        var identity = client.Identify();
        if (!target.Definition.FirmwareModelIds.Contains(identity.DeviceId))
        {
            Console.Error.WriteLine($"Firmware model id {identity.DeviceId} is not listed in '{target.Definition.Id}'; refusing to read model-specific tables.");
            return ExitUnsupported;
        }

        var tables = new[] { MagnetismTable.PressTravel, MagnetismTable.LiftTravel, MagnetismTable.RapidTriggerPress, MagnetismTable.RapidTriggerLift }
            .Select(t => client.ReadTravelTable(t, keyCount))
            .ToList();
        Console.WriteLine("index  press  lift  rtPress  rtLift   (raw values)");
        for (var i = 0; i < keyCount; i++)
            Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{i,5}  {tables[0][i],5}  {tables[1][i],4}  {tables[2][i],7}  {tables[3][i],6}"));
        return 0;
    }
    catch (Exception ex) when (ex is IOException or TimeoutException or ProtocolException)
    {
        Console.Error.WriteLine($"Read failed: {ex.Message}");
        return ExitDeviceError;
    }
});

var root = new RootCommand($"HallForge {version} - Hall-effect keyboard configurator")
{
    new Command("devices", "Discover and inspect keyboards (read-only).") { list, probe, identify, magnetism },
};
return root.Parse(args).Invoke();

static string Format(int? value) => value is { } v ? DiagnosticReport.Hex(v) : "?";
