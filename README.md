# HallForge

HallForge is a cross-platform configurator for hall-effect keyboards. It lets you edit actuation and rapid trigger settings for groups of keys, and save and apply complete profiles. It runs on Windows, Linux and macOS and talks to the keyboard directly over USB HID.

**Status: Phase 0, read-only.** HallForge only lists and inspects devices. It does not send any command to a keyboard yet.

## Build and run

The build needs the .NET 10 SDK.

```bash
dotnet build
dotnet test
dotnet run --project src/HallForge.Cli -- devices list          # keyboards with a device definition
dotnet run --project src/HallForge.Cli -- devices list --all    # every HID interface
dotnet run --project src/HallForge.Cli -- devices probe --device <id> --output report.json
dotnet run --project src/HallForge.App                          # GUI
```

`probe` writes an anonymized report. The report does not contain OS device paths or serial numbers.

## Adding a keyboard

- **The keyboard uses a protocol that HallForge already supports:** add a JSON file to `devices/`. See `devices/rongyuan-akko-monsgeek.json`. Set `"verified": false` until the keyboard has been tested on real hardware.
- **The keyboard uses a new protocol:** add a protocol project and give it a `protocol` id. Device definitions refer to that id.

## Safety

- Firmware and bootloader commands are never exposed.
- Devices that are unknown or not yet verified stay read-only.
- HallForge opens only the vendor configuration interface, never the normal keyboard input interface.

## License

GPL-3.0-or-later. See [LICENSE](LICENSE).

Protocol research is based on [monsgeek-akko-linux](https://github.com/echtzeit-solutions/monsgeek-akko-linux) (GPL-3.0).

Third-party libraries:

| Library | License |
|---|---|
| Avalonia | MIT |
| HidSharp | Apache-2.0 |
| System.CommandLine | MIT |
| xUnit (tests only) | Apache-2.0 |
