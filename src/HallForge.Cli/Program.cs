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
var stateStore = new StateStore(StateStore.DefaultRoot);
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
    var keyId = result.GetValue(keyOption)!;
    var trigger = new KeyTrigger(
        result.GetValue(actuationOption), result.GetValue(releaseOption), result.GetValue(rapidTriggerOption),
        result.GetValue(rtPressOption), result.GetValue(rtReleaseOption), result.GetValue(deadzoneOption));
    return ApplyPatches(result, new Dictionary<string, TriggerPatch> { [keyId] = TriggerPatch.From(trigger) }, $"key {keyId}", (_, _, _) => null);
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

var databaseOption = new Option<FileInfo>("--db") { Description = "Akko Cloud Driver database file.", DefaultValueFactory = _ => new FileInfo(AkkoDriverDatabase.DefaultPath) };
var replaceOption = new Option<bool>("--replace") { Description = "Replace the state HallForge already keeps for this profile." };
var importAkko = new Command("import-akko", "Import the per-key settings the Akko Cloud Driver stored, as backup and initial state.") { deviceOption, databaseOption, replaceOption };
importAkko.SetAction(result =>
{
    var target = discovery.Discover().FirstOrDefault(c => c.Interface.Id == result.GetValue(deviceOption));
    if (target is null || target.Definition!.Protocol != "rongyuan")
    {
        Console.Error.WriteLine("Device not found or not a RongYuan device. Run 'hallforge devices list'.");
        return ExitDeviceNotFound;
    }
    var definition = target.Definition;
    try
    {
        uint modelId;
        byte profile;
        using (var session = RongYuanSession.Open(transport, target))
            (modelId, profile) = (session.ModelId, session.Profile);

        if (stateStore.Load(definition.Id, profile) is not null && !result.GetValue(replaceOption))
        {
            Console.Error.WriteLine($"HallForge already keeps a state for profile {profile} ({stateStore.StatePath(definition.Id, profile)}). Use --replace to overwrite it.");
            return ExitInvalidInput;
        }

        var import = AkkoDriverDatabase.ReadProfile(result.GetValue(databaseOption)!.FullName, modelId, profile, definition);
        foreach (var warning in import.Warnings)
            Console.WriteLine($"Warning: {warning}");
        var invalid = import.Keys.Where(k => definition.Limits.Validate(k.Value).Count > 0).Select(k => k.Key).ToList();
        if (invalid.Count > 0)
        {
            Console.Error.WriteLine($"Imported values outside the model limits for: {string.Join(", ", invalid)}. Nothing was saved.");
            return ExitInvalidInput;
        }

        var state = new KeyboardState(KeyboardState.CurrentFormatVersion, definition.Id, modelId, profile, "akko-driver-import", DateTime.UtcNow, import.Keys);
        PrintState(state, definition);
        var backup = stateStore.SaveBackup(state);
        stateStore.Save(state);
        Console.WriteLine($"Backup: {backup}");
        Console.WriteLine($"{import.Keys.Count} keys imported. Keys not listed keep the driver's defaults and are unknown to HallForge.");
        return 0;
    }
    catch (DeviceNotSupportedException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return ExitUnsupported;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ProtocolException)
    {
        Console.Error.WriteLine($"Import failed: {ex.Message}");
        return ExitDeviceError;
    }
});

var profileOption = new Option<int>("--profile") { Description = "Onboard profile (0-3).", DefaultValueFactory = _ => 0 };
var stateShow = new Command("show", "Show the key settings HallForge last applied or imported.") { deviceOption, profileOption };
stateShow.SetAction(result =>
{
    var target = discovery.Discover().FirstOrDefault(c => c.Interface.Id == result.GetValue(deviceOption));
    if (target is null)
    {
        Console.Error.WriteLine("Device not found or has no device definition. Run 'hallforge devices list'.");
        return ExitDeviceNotFound;
    }
    var state = stateStore.Load(target.Definition!.Id, result.GetValue(profileOption));
    if (state is null)
    {
        Console.WriteLine("No state known yet. Run 'hallforge backup import-akko' or write keys with 'hallforge keys set'.");
        return 0;
    }
    PrintState(state, target.Definition);
    return 0;
});

var profileFileArgument = new Argument<FileInfo>("profile") { Description = "Profile JSON file." };
var profilesApply = new Command("apply", "Apply a profile: validate, preview per-key changes, confirm, write.")
{
    profileFileArgument, deviceOption, dryRunOption, yesOption, experimentalOption,
};
profilesApply.SetAction(result =>
{
    KeyboardProfile profile;
    try
    {
        profile = KeyboardProfile.Load(result.GetValue(profileFileArgument)!.FullName);
    }
    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(ex.Message);
        return ExitInvalidInput;
    }
    return ApplyPatches(result, profile.Resolve(), $"profile '{profile.Name}'", (_, _, _) => null);
});

var backupFileArgument = new Argument<FileInfo>("backup") { Description = "Backup JSON file (see 'backup import-akko')." };
var backupRestore = new Command("restore", "Write the settings of a backup back to the keyboard.")
{
    backupFileArgument, deviceOption, dryRunOption, yesOption, experimentalOption,
};
backupRestore.SetAction(result =>
{
    KeyboardState backup;
    try
    {
        backup = StateStore.Read(result.GetValue(backupFileArgument)!.FullName);
    }
    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(ex.Message);
        return ExitInvalidInput;
    }
    var patches = backup.Keys.ToDictionary(k => k.Key, k => TriggerPatch.From(k.Value));
    return ApplyPatches(result, patches, $"backup from {backup.UpdatedAtUtc:u}", (definition, modelId, profile) =>
        backup.DefinitionId != definition.Id ? $"The backup belongs to '{backup.DefinitionId}', not '{definition.Id}'."
        : backup.FirmwareModelId != modelId ? $"The backup was made for firmware model {backup.FirmwareModelId}, the device reports {modelId}."
        : backup.Profile != profile ? $"The backup is for onboard profile {backup.Profile}, but profile {profile} is active."
        : null);
});

// Shared by profile apply and backup restore so both follow the same validation, preview and write path.
int ApplyPatches(ParseResult result, IReadOnlyDictionary<string, TriggerPatch> patches, string title, Func<DeviceDefinition, uint, int, string?> precondition)
{
    var target = discovery.Discover().FirstOrDefault(c => c.Interface.Id == result.GetValue(deviceOption));
    if (target is null || target.Definition!.Protocol != "rongyuan")
    {
        Console.Error.WriteLine("Device not found or not a RongYuan device. Run 'hallforge devices list'.");
        return ExitDeviceNotFound;
    }
    var definition = target.Definition;
    var dryRun = result.GetValue(dryRunOption);
    if (!dryRun && !definition.Verified && !result.GetValue(experimentalOption))
    {
        Console.Error.WriteLine($"'{definition.Name}' is not verified for writing. Use --dry-run to preview, or --experimental only for hardware research.");
        return ExitUnsupported;
    }

    var sent = new List<string>();
    try
    {
        using var session = RongYuanSession.Open(transport, target);
        var (modelId, profile) = (session.ModelId, session.Profile);
        if (precondition(definition, modelId, profile) is { } problem)
        {
            Console.Error.WriteLine(problem);
            return ExitInvalidInput;
        }

        var plan = ApplyPlan.Create(patches, definition, stateStore.Load(definition.Id, profile));
        Console.WriteLine($"Applying {title} to {definition.Name}, onboard profile {profile}");
        foreach (var change in plan.Changes)
            Console.WriteLine($"  {change.KeyId,-14} {Describe(change.Before)}  ->  {Describe(change.After)}");
        Console.WriteLine($"{plan.Changes.Count} key(s) change, {plan.UnchangedKeys} already match.");
        if (!plan.CanApply)
        {
            foreach (var error in plan.Errors)
                Console.Error.WriteLine($"Error: {error}");
            Console.Error.WriteLine("Nothing was sent.");
            return ExitInvalidInput;
        }
        if (dryRun || plan.Changes.Count == 0)
        {
            Console.WriteLine(dryRun ? "Dry run: nothing was sent." : "Nothing to send.");
            return 0;
        }
        if (!stateStore.HasBackup(definition.Id, profile))
        {
            Console.Error.WriteLine("No backup exists for this profile yet. Run 'hallforge backup import-akko' first.");
            return ExitInvalidInput;
        }
        if (!result.GetValue(yesOption))
        {
            Console.Write($"Send {plan.Changes.Count} key setting(s) to the keyboard? [y/N] ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Cancelled.");
                return ExitCancelled;
            }
        }

        session.Apply(plan, stateStore, change => sent.Add(change.KeyId));
        Console.WriteLine($"Sent {sent.Count} key setting(s). This firmware cannot report settings back, so the result is not verified.");
        return 0;
    }
    catch (DeviceNotSupportedException ex)
    {
        Console.Error.WriteLine($"{ex.Message} Refusing to write.");
        return ExitUnsupported;
    }
    catch (Exception ex) when (ex is IOException or TimeoutException or ProtocolException or UnauthorizedAccessException or InvalidDataException)
    {
        Console.Error.WriteLine($"Apply failed: {ex.Message}");
        if (sent.Count > 0)
            Console.Error.WriteLine($"Already sent: {string.Join(", ", sent)}. Restore with 'hallforge backup restore <backup> --device <id>'.");
        return ExitDeviceError;
    }
}

var root = new RootCommand($"HallForge {version} - Hall-effect keyboard configurator")
{
    new Command("devices", "Discover and inspect keyboards (read-only).") { list, probe, identify, magnetism },
    new Command("keys", "Inspect and configure key trigger settings.") { keysList, keysSet },
    new Command("backup", "Create and restore backups of key settings.") { importAkko, backupRestore },
    new Command("profiles", "Apply portable key setting profiles.") { profilesApply },
    new Command("state", "Key settings known to HallForge (the device cannot report them).") { stateShow },
};
return root.Parse(args).Invoke();

static string Describe(KeyTrigger? t) => t is null
    ? "unknown"
    : string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"{t.ActuationMm}/{t.ReleaseMm} mm, RT {(t.RapidTrigger ? "on" : "off")} {t.RapidTriggerPressMm}/{t.RapidTriggerReleaseMm}, dz {t.DeadzoneMm}");

static void PrintState(KeyboardState state, DeviceDefinition definition)
{
    Console.WriteLine($"{definition.Name}, profile {state.Profile}, source {state.Source}, updated {state.UpdatedAtUtc:u}");
    Console.WriteLine("key              actuation  release  RT   press  release  deadzone");
    foreach (var (keyId, t) in state.Keys.OrderBy(k => definition.Keys.GetValueOrDefault(k.Key)))
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{keyId,-16} {t.ActuationMm,9}  {t.ReleaseMm,7}  {(t.RapidTrigger ? "on" : "off"),-3}  {t.RapidTriggerPressMm,5}  {t.RapidTriggerReleaseMm,7}  {t.DeadzoneMm,8}"));
}

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
