using System.CommandLine;
using System.Reflection;
using HallForge.Core;
using HallForge.Protocol.RongYuan;
using HallForge.Transport.Hid;

const int ExitDeviceNotFound = 2;
const int ExitUnsupported = 3;
const int ExitDeviceError = 4;
const int ExitInvalidInput = 5;
const int ExitCancelled = 6;

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

var keyOption = new Option<string>("--key") { Description = "Key id, e.g. KeyW or AltLeft (see 'keys list').", Required = true };
var actuationOption = MillimeterOption("--actuation", "Actuation point in mm.");
var releaseOption = MillimeterOption("--release", "Release point in mm.");
var rapidTriggerOption = new Option<bool>("--rapid-trigger") { Description = "Enable rapid trigger.", Required = true };
var rtPressOption = MillimeterOption("--rt-press", "Rapid trigger press sensitivity in mm.");
var rtReleaseOption = MillimeterOption("--rt-release", "Rapid trigger release sensitivity in mm.");
var deadzoneOption = MillimeterOption("--deadzone", "Deadzone in mm.");
var dryRunOption = new Option<bool>("--dry-run") { Description = "Validate and show the report without sending it." };
var yesOption = new Option<bool>("--yes") { Description = "Do not ask for confirmation." };
var experimentalOption = new Option<bool>("--experimental") { Description = "Allow writing to a device whose definition is not verified yet (hardware research only)." };

// All values are required: the firmware cannot report the current state, so every write replaces the full key setting.
var keysSet = new Command("set", "Write the complete trigger setting of one key.")
{
    deviceOption, keyOption, actuationOption, releaseOption, rapidTriggerOption, rtPressOption, rtReleaseOption, deadzoneOption,
    dryRunOption, yesOption, experimentalOption,
};
keysSet.SetAction(result =>
{
    var target = discovery.Discover().FirstOrDefault(c => c.Interface.Id == result.GetValue(deviceOption));
    if (target is null || target.Definition!.Protocol != "rongyuan")
    {
        Console.Error.WriteLine("Device not found or not a RongYuan device. Run 'hallforge devices list'.");
        return ExitDeviceNotFound;
    }
    var definition = target.Definition;
    if (!definition.Verified && !result.GetValue(experimentalOption))
    {
        Console.Error.WriteLine($"'{definition.Name}' is not verified for writing. Use --experimental only for hardware research.");
        return ExitUnsupported;
    }
    var keyId = result.GetValue(keyOption)!;
    if (!definition.Keys.TryGetValue(keyId, out var matrixIndex))
    {
        Console.Error.WriteLine($"Unknown key '{keyId}'. Run 'hallforge keys list --device <id>'.");
        return ExitInvalidInput;
    }
    var trigger = new KeyTrigger(
        result.GetValue(actuationOption), result.GetValue(releaseOption), result.GetValue(rapidTriggerOption),
        result.GetValue(rtPressOption), result.GetValue(rtReleaseOption), result.GetValue(deadzoneOption));
    if (definition.Limits.Validate(trigger) is { Count: > 0 } errors)
    {
        foreach (var error in errors)
            Console.Error.WriteLine(error);
        return ExitInvalidInput;
    }

    try
    {
        using var connection = transport.Open(target.Interface);
        var client = new RongYuanClient(connection);
        var identity = client.Identify();
        if (!definition.FirmwareModelIds.Contains(identity.DeviceId))
        {
            Console.Error.WriteLine($"Firmware model id {identity.DeviceId} is not listed in '{definition.Id}'; refusing to write.");
            return ExitUnsupported;
        }
        var profile = client.ReadActiveProfile();
        var report = RongYuanClient.Encode(trigger, profile, matrixIndex);
        Console.WriteLine($"Device:  {definition.Name} (firmware model {identity.DeviceId}), profile {profile}");
        Console.WriteLine($"Key:     {keyId} (matrix {matrixIndex})");
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Values:  actuation {trigger.ActuationMm} mm, release {trigger.ReleaseMm} mm, rapid trigger {(trigger.RapidTrigger ? "on" : "off")} ({trigger.RapidTriggerPressMm}/{trigger.RapidTriggerReleaseMm} mm), deadzone {trigger.DeadzoneMm} mm"));
        Console.WriteLine($"Report:  {Convert.ToHexString(report.AsSpan(1, Array.FindLastIndex(report, b => b != 0)))}");
        if (result.GetValue(dryRunOption))
        {
            Console.WriteLine("Dry run: nothing was sent.");
            return 0;
        }
        if (!result.GetValue(yesOption))
        {
            Console.Write("Send this setting to the keyboard? [y/N] ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Cancelled.");
                return ExitCancelled;
            }
        }
        client.WriteKeyTrigger(trigger, profile, matrixIndex);
        Console.WriteLine("Sent. This firmware cannot report settings back, so the result is not verified. Test the key to confirm.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or TimeoutException or ProtocolException)
    {
        Console.Error.WriteLine($"Write failed: {ex.Message}");
        return ExitDeviceError;
    }
});

var keysList = new Command("list", "List configurable keys of a device definition.") { deviceOption };
keysList.SetAction(result =>
{
    var target = discovery.Discover().FirstOrDefault(c => c.Interface.Id == result.GetValue(deviceOption));
    if (target is null)
    {
        Console.Error.WriteLine("Device not found or has no device definition. Run 'hallforge devices list'.");
        return ExitDeviceNotFound;
    }
    Console.WriteLine(string.Join(" ", target.Definition!.Keys.Keys));
    return 0;
});

var root = new RootCommand($"HallForge {version} - Hall-effect keyboard configurator")
{
    new Command("devices", "Discover and inspect keyboards (read-only).") { list, probe, identify, magnetism },
    new Command("keys", "Inspect and configure key trigger settings.") { keysList, keysSet },
};
return root.Parse(args).Invoke();

static Option<decimal> MillimeterOption(string name, string description) => new(name)
{
    Description = description + " Use '.' as decimal separator.",
    Required = true,
    CustomParser = result =>
    {
        var text = result.Tokens.Single().Value;
        if (Millimeters.TryParse(text, out var mm))
            return mm;
        result.AddError($"{name}: '{text}' is not a millimeter value like 0.5.");
        return 0;
    },
};

static string Format(int? value) => value is { } v ? DiagnosticReport.Hex(v) : "?";
