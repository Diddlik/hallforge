# HallForge

HallForge is a cross-platform configurator for hall-effect keyboards. It lets you edit actuation and rapid trigger settings for groups of keys, and save and apply complete profiles. It runs on Windows, Linux and macOS and talks to the keyboard directly over USB HID.

**Status: Phase 1, read-only.** HallForge lists and inspects devices. For RongYuan keyboards it sends only allowlisted read commands. Nothing is ever written.

## Build and run

The build needs the .NET 10 SDK.

```bash
dotnet build
dotnet test
dotnet run --project src/HallForge.Cli -- devices list          # keyboards with a device definition
dotnet run --project src/HallForge.Cli -- devices list --all    # every HID interface
dotnet run --project src/HallForge.Cli -- devices probe --device <id> --output report.json
dotnet run --project src/HallForge.Cli -- devices identify --device <id>   # firmware model id and version (read-only)
dotnet run --project src/HallForge.App                          # GUI
```

`probe` writes an anonymized report. The report does not contain OS device paths or serial numbers.

## Adding a keyboard

- **The keyboard uses a protocol that HallForge already supports:** add a JSON file to `devices/`. See `devices/akko-mod007b-iso-he.json`. Run `devices identify` to get the firmware model id for `firmwareModelIds`. Set `"verified": false` until the keyboard has been tested on real hardware.
- **The keyboard uses a new protocol:** add a protocol project and give it a `protocol` id. Device definitions refer to that id.

## Releases and updates

Releases are built by `.github/workflows/release.yml` when a tag `v<version>` is pushed. The tag must match `<Version>` in `Directory.Build.props`. Velopack packages the GUI for Windows x64, Linux x64 and macOS arm64 and uploads it to GitHub Releases.

Installed apps check for updates over HTTPS when they start. You can turn this off under About. A downloaded update is verified by its hash and installed only when you select "Restart and update".

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
| CommunityToolkit.Mvvm | MIT |
| HidSharp | Apache-2.0 |
| System.CommandLine | MIT |
| Velopack | MIT |
| xUnit (tests only) | Apache-2.0 |
