# Shared Agent Instructions

This file is the canonical instruction source for Codex, Claude Code, and GitHub Copilot CLI.

## Maintenance

- Keep this file synchronized with the repository's verified behavior.
- Update this file in the same change when build commands, validation steps, architecture constraints, workflows, or conventions change.
- Remove obsolete instructions instead of appending corrections.
- Record only durable facts that are not obvious from the codebase.
- Replace an `OPEN` entry as soon as the decision is made or the fact becomes verifiable.
- Tool-specific instruction files contain only a pointer to this file and genuine tool-specific exceptions.

## First-use onboarding

If any `[TO FILL]` entry remains, complete this onboarding before implementing the user's first task:

1. Inspect the repository and determine every project fact that can be verified from existing files and commands.
2. Do not ask the user for information that can be discovered reliably from the repository.
3. Briefly present the discovered facts, then ask guided questions only for the remaining decisions or unknowns.
4. Ask one focused question or one closely related group of no more than three questions at a time. Explain why each answer matters and offer a recommended option when useful while allowing a free-form answer.
5. Cover the remaining topics in the order of the project facts list: purpose and scope, languages, runtimes and platforms, GUI, build and validation commands, environment requirements, then entry points, architecture, and generated-file constraints.
6. After the user answers, replace the applicable placeholders with concise verified facts, remove entries that do not apply, and record unresolved decisions explicitly as `OPEN` rather than inventing an answer.
7. Summarize what was written to this file, then continue with the user's original task.

If the user cannot be asked (non-interactive run, CI, issue-driven agent, subagent), fill every verifiable fact, mark the rest `OPEN`, and proceed with the task.

## Communication

- Respond to the user in the language the user writes in, including onboarding questions.
- Project artifacts stay in English as defined below.

## Implementation

- Implement only what the current requirement needs.
- Prefer editing existing code over adding files, layers, helpers, or abstractions.
- Use the standard library and existing dependencies before writing custom implementations.
- Do not add speculative extension points, configuration, parameters, or abstractions.
- Add a dependency only when it provides a concrete benefit and does not duplicate existing functionality.
- Preserve validation, security, accessibility, error handling, and data-integrity safeguards.

## Language and comments

- Use English for source code, identifiers, comments, tests, documentation, logs, and commit messages.
- Comments explain why a decision, constraint, workaround, or non-obvious trade-off exists.
- Do not comment what readable code already expresses.
- Prefer clear naming and small functions over explanatory comments.

## Git

- Commit coherent, verified units of work such as a finished feature, fix, or refactoring step, not every individual edit.
- Group related changes into one commit; keep unrelated changes in separate commits.
- Keep commit messages limited to change- and process-related content.
- Never add AI attribution or co-author trailers such as `Co-Authored-By: Copilot`, Claude, Codex, or similar, in commits or pull requests.

## GUI applications

Apply this section when the project has or adds a graphical user interface.

Required features:

- Versioning: one single source for the version number (Semantic Versioning), shown in the UI and embedded in build artifacts.
- About dialog: application name, version, copyright, license, and credits for every third-party library with its license.
- Auto-update: check for updates over HTTPS, verify the update's signature or checksum before installing, let the user postpone or disable automatic checks, and never lose unsaved data during an update.

Design:

- Plan the layout, navigation, and main user flows before implementing screens.
- Follow the platform's current design system: Fluent 2 on Windows, Apple Human Interface Guidelines on macOS and iOS, Material 3 on Android and where no platform system applies.
- Use native or toolkit-standard controls instead of custom widgets.
- Use a consistent spacing grid (multiples of 4 or 8 px), a limited type scale, and the system font unless the brand requires otherwise.
- Support light and dark mode and follow the system setting by default.
- Keep a clear visual hierarchy: one primary action per view, secondary actions visually subordinate, generous whitespace instead of borders and boxes.
- Design empty, loading, and error states; errors say what happened and what the user can do.
- Keep the UI responsive: run long operations in the background with progress indication and cancellation.
- Prefer undo over confirmation dialogs for reversible actions.
- Adapt to window sizes and display scaling (high DPI) without clipping or overlapping content.
- Accessibility: meet WCAG 2.2 AA contrast, provide full keyboard navigation with visible focus, label controls for screen readers, and respect reduced-motion settings.
- Keep animations short and purposeful.
- Keep user-facing strings out of code logic so they can be localized.
- Remember window size, position, and user preferences between sessions.

## Working method

- Inspect the relevant implementation and existing conventions before editing.
- Search for an existing implementation before creating a new one.
- Make the smallest coherent change that fully satisfies the request.
- Preserve unrelated user changes.
- Do not perform unrelated refactoring during a focused change.
- Ask before destructive, irreversible, security-sensitive, or materially out-of-scope actions.

## Verification

- Run the narrowest relevant test, build, lint, format, or executable check after the last change.
- Add or update tests for non-trivial behavior changes and bug fixes.
- Do not claim success without current verification evidence.
- Report what was verified and what could not be verified.
- Distinguish product failures from environment, permission, network, and tooling failures.

## Security

- Never commit, print, store, or document secrets, tokens, credentials, or private keys.
- Validate data at user, file, environment, process, and network boundaries.
- Preserve authentication, authorization, escaping, permission checks, and safe defaults.
- Do not weaken security controls to make tests or local execution pass.

## Project facts

- Purpose and scope: HallForge is a cross-platform configurator for hall-effect keyboards (actuation, release, rapid trigger, profiles, backups). It has a GUI and a CLI. Requirements and phases are in `AKKO_HE_Configurator_Requirements.md`. It is vendor-neutral, and AKKO (RongYuan protocol) is only the first supported family. The current state is Phase 1. The AKKO MOD007B ISO HE (model 1778) cannot read per-key settings back (see `docs/protocol-research.md`). Decision: HallForge writes complete key settings (`KeyTrigger`), keeps the last applied state itself, and imports the initial state from the vendor driver database. Writes are reported as sent, never as verified. State and backups are JSON files written by `StateStore` (Core) below `%AppData%/HallForge` (`StateStore.DefaultRoot`; never `%LocalAppData%/HallForge`, which the Velopack installer owns and replaces); the vendor database import is `AkkoDriverDatabase` (Protocol.RongYuan). The vendor driver stores only keys that differ from the factory setting, so the import fills the other keys with the definition's `factoryDefaults` (2.0 mm actuation and release, rapid trigger off, 0.2/0.3 mm, deadzone 3.7 mm on the MOD007B, as stated by the user and consistent with the imported keys). Profiles (`KeyboardProfile`, schema version 1, templates in `profiles/`) and backup restores both go through `ApplyPlan` in Core: patch semantics, explicit `null` rejected, all errors block the whole plan, and each key is recorded in the state right after it is sent. Milestones after the MVP: LED setup, keyboard layout in the app (GitHub milestones 1 and 2).
- Primary languages and runtimes: C# on .NET 10. The SDK is pinned in `global.json`. License: GPL-3.0-or-later.
- Platform-specific constraints: Windows, Linux and macOS. HID access goes through HidSharp. The USB interface number can only be parsed from Windows device paths. Linux needs udev rules that use `TAG+="uaccess"` and never `MODE=0666`.
- GUI toolkit, update mechanism, and distribution:
  - Avalonia 12 with the Fluent theme and CommunityToolkit.Mvvm.
  - User-facing strings are in `src/HallForge.App/Strings.resx` (English, neutral) and `Strings.de.resx` (German); the UI follows the OS display language. A test checks that translations keep the format placeholders of the neutral strings.
  - User preferences and window placement are stored in `%AppData%/HallForge/settings.json` (`AppSettings`). This includes the opt-in per-program switching (`AutoSwitch`, `ProgramRules`, `OtherPrograms`), run by `AutoSwitchViewModel`: it polls the foreground process (`ForegroundProgram`, Windows only) once per second, ignores HallForge itself and applies travel mode and onboard profile through `KeyboardSettingsViewModel.SwitchToAsync`. While it is enabled, closing the window hides it to the tray (`App.KeepRunningInTray`); the tray menu exits.
  - Velopack auto-update checks GitHub Releases, can be disabled in About, and restarts only when the user clicks.
  - Release: push tag `v<Version>` (must equal `<Version>` in `Directory.Build.props`). `.github/workflows/release.yml` packs the GUI with Velopack for win-x64, linux-x64 and osx-arm64, uploads it to GitHub Releases, and attaches a self-contained CLI zip per platform. Verified with v0.6.1: GUI packages and CLI zips for all three platforms. A `create` job makes the GitHub release first and the platform jobs merge into it; without it, parallel jobs created duplicate releases (v0.6.2, fixed by hand). The release job runs the tests on each platform first, so a test that only fails on Linux or macOS stops that platform after Windows has already published (happened with v0.6.0, which is Windows-only). The update path from one release to the next is not verified yet.
  - New third-party libraries must be added to `MainViewModel.ThirdPartyLibraries` and to the README.
  - The UI follows `docs/design/ui-redesign` (tokens as `Hf*` brushes in `App.axaml`, light and dark; the header button pins a theme in `AppSettings.Theme`). Pages: `PerformanceView` (`KeyboardEditorViewModel`: draft of `TriggerPatch` per key, `TriggerFieldViewModel` per mm field with a `TravelDiagram` that animates how the setting acts while the field is hovered or focused, and stays still when Windows animations are off, undo, presets; `RoarViewModel` sets Roar keys from the selection), `LightingView` (`LightingViewModel`), `DeviceView` also hosts `KeyboardSettingsViewModel` (travel mode and onboard profile, written right away and read back; a profile switch is blocked while key drafts exist and reloads the known state), `ProfilesView` (`ProfileLibraryViewModel`, templates from `profiles/`), `DeviceView`. Keys are drawn from the definition's `layout`. Every write goes through `ReviewDialog`/`ReviewViewModel`: it lists the changes, requires an acknowledgement for unverified key writes, sends with per-key progress and can stop between keys (`ApplyPlan.Execute` cancellation). Lighting writes are verified by `GET_LEDPARAM`.
  - "Restart and update" is blocked while key changes are pending, so an update never discards a draft.
  - Format localized strings with `Text.Format` (analyzer CA1863 rejects direct `string.Format` with resource strings).
- Build command: `dotnet build`
- Test command: `dotnet test`. It uses xUnit v3 on Microsoft.Testing.Platform, configured in `global.json`. View model tests live in `tests/HallForge.App.Tests`.
- Lint and format command: analyzers run during the build (`AnalysisLevel` latest-recommended, `TreatWarningsAsErrors`). Format with `dotnet format`.
- Local run command: `dotnet run --project src/HallForge.Cli -- devices list --all` and `dotnet run --project src/HallForge.App`.
- Required environment: .NET 10 SDK. Hardware tests need a keyboard connected by USB cable. The first device is AKKO VID `0x3151` PID `0x4035`.
- Important entry points: `src/HallForge.Cli/Program.cs` (CLI `hallforge`), `src/HallForge.App` (GUI, `MainViewModel`), `src/HallForge.Core/KeyboardDiscoveryService.cs`, `src/HallForge.Transport.Hid/HidSharpTransport.cs`, `src/HallForge.Protocol.RongYuan/RongYuanClient.cs` (command allowlist), `src/HallForge.Protocol.RongYuan/RongYuanSession.cs` (identify, model check and apply, shared by GUI and CLI).
- Architecture constraints:
  - `Core` is vendor-neutral and has no HidSharp, UI or protocol specifics.
  - Keyboards are described as data in `devices/*.json`, one file per model. Each file names a `protocol` id, the USB match, the `firmwareModelIds` and a `verified` flag. Vendors reuse USB product ids across models, so only the firmware model id confirms the model.
  - A new keyboard on a known protocol needs only a JSON file (match, firmware model ids, limits, `keys` matrix map, `layout` geometry in key units). Printed labels per language are in `layouts/*.json` (`KeyLegends`), separate from devices. A new protocol gets its own `HallForge.Protocol.<Name>` project.
  - Unknown or unverified devices are never written to.
  - Firmware, bootloader, keymatrix, macro and FN commands are never exposed.
  - Every SET command goes through an allowlist. The writes are the per-key trigger setting (0x1A, unverifiable), the lighting effect (SET_LEDPARAM 0x07, verified by reading it back) and Roar mode (0x1F, verified by 0x9F, plus only the LED-off bit of SET_KBOPTION 0x06), the travel mode (0x1D, verified by 0x9D) and the active profile (SET_PROFILE 0x05, verified by GET_PROFILE 0x85; 0x84 is GET_REPORT) in `RongYuanClient`/`LightingState`/`Roar`. Unverified definitions need `--experimental`.
  - Millimeter input is parsed with `Millimeters.TryParse` (invariant '.', ',' rejected), never with the current culture.
  - The GUI and the CLI share the same services. Version numbers come only from `Directory.Build.props`, and package versions come only from `Directory.Packages.props`.
- Generated files: `bin/` and `obj/`.
- The app icon source is `assets/icon.svg`; `hallforge.ico`, `hallforge.icns` and the PNGs are rendered from it (ImageMagick). README images live in `docs/images` (`infographic.svg` is the source of `infographic.png`).
- Local USB captures go to `ws/` (git-ignored; they may contain keystrokes).
- Files or directories not to edit manually: `LICENSE` (verbatim GPL-3.0 text).

## Definition of done

A change is complete when:

- The requested behavior is implemented.
- Relevant verification passes after the final edit.
- Appropriate error paths and edge cases are handled.
- Documentation and this file reflect changed behavior or workflows.
- No unrelated files, abstractions, or dependencies were introduced.
- Remaining limitations and unverified points are stated clearly.
