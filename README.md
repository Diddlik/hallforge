# HallForge

HallForge is a cross-platform configurator for hall-effect keyboards. It lets you edit actuation and rapid trigger settings for groups of keys, and save and apply complete profiles. It runs on Windows, Linux and macOS and talks to the keyboard directly over USB HID.

**Status: Phase 1, experimental.** HallForge lists and inspects devices and can write the trigger setting of single keys on the AKKO MOD007B ISO HE. Writing to that model requires `--experimental` until it is verified on hardware.

## Build and run

The build needs the .NET 10 SDK.

```bash
dotnet build
dotnet test
dotnet run --project src/HallForge.Cli -- devices list          # keyboards with a device definition
dotnet run --project src/HallForge.Cli -- devices list --all    # every HID interface
dotnet run --project src/HallForge.Cli -- devices probe --device <id> --output report.json
dotnet run --project src/HallForge.Cli -- devices identify --device <id>   # firmware model id and version (read-only)
dotnet run --project src/HallForge.Cli -- backup import-akko --device <id>   # back up the settings the Akko Cloud Driver stored
dotnet run --project src/HallForge.Cli -- state show --device <id>
dotnet run --project src/HallForge.Cli -- profiles apply profiles/bf6-balanced.json --device <id> --dry-run
dotnet run --project src/HallForge.Cli -- backup restore <backup.json> --device <id> --dry-run
dotnet run --project src/HallForge.Cli -- keys list --device <id>
dotnet run --project src/HallForge.Cli -- keys set --device <id> --key AltLeft --actuation 1.9 --release 1.9 --rapid-trigger false --rt-press 0.2 --rt-release 0.3 --deadzone 3.7 --dry-run
dotnet run --project src/HallForge.App                          # GUI
```

`probe` writes an anonymized report. The report does not contain OS device paths or serial numbers.

## Adding a keyboard

- **The keyboard uses a protocol that HallForge already supports:** add a JSON file to `devices/`. See `devices/akko-mod007b-iso-he.json`. Run `devices identify` to get the firmware model id for `firmwareModelIds`. Set `"verified": false` until the keyboard has been tested on real hardware.
- **The keyboard uses a new protocol:** add a protocol project and give it a `protocol` id. Device definitions refer to that id.

## State and backups

The AKKO MOD007B ISO HE cannot report its key settings, so HallForge keeps what it knows:

- `backup import-akko` reads the settings that the Akko Cloud Driver stored in its local database (`%AppData%/Akko Cloud Driver/iot_db/CONFIG/db`). It saves them as an immutable backup and as the initial state. The driver only stores keys that differ from its defaults, so all other keys stay unknown.
- `keys set` records every write in the state.
- Files live in `%LocalAppData%/HallForge/state` and `%LocalAppData%/HallForge/backups`.

## Profiles

Profiles are JSON files (see `profiles/`). Each group sets values for a list of key ids (W3C `KeyboardEvent.code` names such as `KeyW` or `ShiftLeft`). Later groups win, and the optional `keys` object overrides groups per key.

- A field that is left out stays unchanged. `null` is rejected.
- The keyboard only accepts complete key settings, so missing fields come from the known state. For a key whose state is unknown, the profile must set all six values.
- `profiles apply` shows every change per key before sending, needs a backup for the onboard profile, and asks for confirmation unless `--yes` is given. `--dry-run` sends nothing.
- `backup restore` uses the same path and checks that the backup matches the device, firmware model and active onboard profile.

## Releases and updates

Releases are built by `.github/workflows/release.yml` when a tag `v<version>` is pushed. The tag must match `<Version>` in `Directory.Build.props`. Velopack packages the GUI for Windows x64, Linux x64 and macOS arm64 and uploads it to GitHub Releases.

Installed apps check for updates over HTTPS when they start. You can turn this off under About. A downloaded update is verified by its hash and installed only when you select "Restart and update".

## Safety

- Firmware and bootloader commands are never exposed.
- Devices that are unknown stay read-only. Unverified devices are written only with `--experimental` and after confirmation.
- Every write sends the complete setting of one key. Values use '.' as decimal separator; ',' is rejected.
- The AKKO MOD007B ISO HE cannot report its settings back, so writes cannot be verified by reading them.
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
