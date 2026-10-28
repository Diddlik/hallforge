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

- Purpose and scope: HallForge is a cross-platform configurator for hall-effect keyboards (actuation, release, rapid trigger, profiles, backups). It has a GUI and a CLI. Requirements and phases are in `AKKO_HE_Configurator_Requirements.md`. It is vendor-neutral, and AKKO (RongYuan protocol) is only the first supported family. The current state is Phase 1. The AKKO MOD007B ISO HE (model 1778) cannot read per-key settings back (see `docs/protocol-research.md`). Decision: HallForge writes complete key settings (`KeyTrigger`), keeps the last applied state itself, and imports the initial state from the vendor driver database. Writes are reported as sent, never as verified.
- Primary languages and runtimes: C# on .NET 10. The SDK is pinned in `global.json`. License: GPL-3.0-or-later.
- Platform-specific constraints: Windows, Linux and macOS. HID access goes through HidSharp. The USB interface number can only be parsed from Windows device paths. Linux needs udev rules that use `TAG+="uaccess"` and never `MODE=0666`.
- GUI toolkit, update mechanism, and distribution:
  - Avalonia 12 with the Fluent theme and CommunityToolkit.Mvvm.
  - User-facing strings are in `src/HallForge.App/Strings.resx`, which generates the strongly typed `Strings` class at build time.
  - User preferences and window placement are stored in `%LocalAppData%/HallForge/settings.json` (`AppSettings`).
  - Velopack auto-update checks GitHub Releases, can be disabled in About, and restarts only when the user clicks.
  - Release: push tag `v<Version>` and `.github/workflows/release.yml` packs and uploads. The release pipeline is OPEN until the first tag run verifies it.
  - New third-party libraries must be added to `MainViewModel.ThirdPartyLibraries` and to the README.
- Build command: `dotnet build`
- Test command: `dotnet test`. It uses xUnit v3 on Microsoft.Testing.Platform, configured in `global.json`.
- Lint and format command: analyzers run during the build (`AnalysisLevel` latest-recommended, `TreatWarningsAsErrors`). Format with `dotnet format`.
- Local run command: `dotnet run --project src/HallForge.Cli -- devices list --all` and `dotnet run --project src/HallForge.App`.
- Required environment: .NET 10 SDK. Hardware tests need a keyboard connected by USB cable. The first device is AKKO VID `0x3151` PID `0x4035`.
- Important entry points: `src/HallForge.Cli/Program.cs` (CLI `hallforge`), `src/HallForge.App` (GUI, `MainViewModel`), `src/HallForge.Core/KeyboardDiscoveryService.cs`, `src/HallForge.Transport.Hid/HidSharpTransport.cs`, `src/HallForge.Protocol.RongYuan/RongYuanClient.cs` (read-only command allowlist).
- Architecture constraints:
  - `Core` is vendor-neutral and has no HidSharp, UI or protocol specifics.
  - Keyboards are described as data in `devices/*.json`, one file per model. Each file names a `protocol` id, the USB match, the `firmwareModelIds` and a `verified` flag. Vendors reuse USB product ids across models, so only the firmware model id confirms the model.
  - A new keyboard on a known protocol needs only a JSON file. A new protocol gets its own `HallForge.Protocol.<Name>` project.
  - Unknown or unverified devices are never written to.
  - Firmware, bootloader, keymatrix, macro and FN commands are never exposed.
  - Every SET command goes through an allowlist. The only write is the per-key trigger setting (0x1A) in `RongYuanClient`. Unverified definitions need `--experimental`.
  - Millimeter input is parsed with `Millimeters.TryParse` (invariant '.', ',' rejected), never with the current culture.
  - The GUI and the CLI share the same services. Version numbers come only from `Directory.Build.props`, and package versions come only from `Directory.Packages.props`.
- Generated files: `bin/` and `obj/`.
- Files or directories not to edit manually: `LICENSE` (verbatim GPL-3.0 text).

## Definition of done

A change is complete when:

- The requested behavior is implemented.
- Relevant verification passes after the final edit.
- Appropriate error paths and edge cases are handled.
- Documentation and this file reflect changed behavior or workflows.
- No unrelated files, abstractions, or dependencies were introduced.
- Remaining limitations and unverified points are stated clearly.
