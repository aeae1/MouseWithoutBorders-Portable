# Mouse Without Borders portable development guide

This file is the working map for modification of the `mwb-standalone` branch. The branch, a few imported source filenames, and the `STANDALONE` build symbol remain internal compatibility identifiers; public documentation and release titles call the product **Portable**.

## Branch convention

- `main`: keep close to the upstream Microsoft PowerToys fork.
- `mwb-standalone`: portable extraction plus intentional MWB behavior changes.
- New experiments can use short-lived branches from `mwb-standalone` when a change is invasive.

Do not casually merge `mwb-standalone` back into `main`; the point of `main` is to remain a clean upstream-sync base.

## AI-assisted commit attribution

Changes made through the connected ChatGPT coding workflow should include:

`Assisted-by: ChatGPT (GPT-5.6 Sol)`

See `AI_ASSISTANCE.md` for why GitHub still records the authenticated account (`aeae1`) as the actual author/committer.

## Compatibility guardrails

Unless a change is explicitly intentional, preserve these behaviors:

1. MWB network protocol and packet structures.
2. Clipboard text/image sharing.
3. File copy/paste and drag/drop transfer behavior.
4. Machine matrix behavior and connection discovery.
5. Normal-desktop operation without an installed Windows service.
6. Named IPC/event strings used by current PowerToys MWB.
7. Current PowerToys settings shape where practical inside the portable prefs file.

Old Garage standalone `2.2.1.0327` protocol compatibility is **not** assumed. Use the same modern fork/current PowerToys-compatible generation on every connected machine unless old-version interoperability is explicitly tested.

## Upstream maintenance

Use `UPSTREAM_SYNC.md` as the durable upstream audit record. Do not merge PowerToys `main` wholesale into the product branch. Review new upstream commits touching Microsoft's `src/modules/MouseWithoutBorders`, classify each change against the deliberate portable divergences, port the applicable pieces into this repository's `App` tree, and rerun the Windows build and physical compatibility tests.

The current audited upstream marker is Microsoft PowerToys commit `103d376c0a987cf350d4594bb3f8d71282fddfd6`, reviewed September 3, 2026.

## Intentional fork behavior

### Shared key UX

The fork intentionally differs from upstream:

- manually chosen keys are allowed;
- minimum manual key length: 4 characters;
- generated key length: 12 characters;
- generated alphabet: `abcdefghjkmnpqrstuvwxyz23456789`;
- each generated character is independently selected with `RandomNumberGenerator`;
- no forced lowercase/uppercase/digit/symbol position pattern;
- short manually chosen keys are allowed but the UI warns they are easier to guess.

The underlying modern MWB AES/PBKDF2 implementation remains intact.

## High-value code map

### Input

- `App/Class/InputHook.cs` — low-level keyboard/mouse capture.
- `App/Class/InputSimulation.cs` — remote input injection.
- `App/Core/Common.cs` — hotkey matching and shared runtime helpers.

### Network / protocol

- `App/Class/SocketStuff.cs` — main MWB networking and transport behavior.
- `App/Class/TcpServer.cs` — inbound TCP listener.
- `App/Class/MachinePool.cs` — peer state.
- `App/Core/Encryption.cs` — key validation/generation and stream encryption.

### Clipboard / file transfer

- `App/Core/Clipboard.cs` — clipboard serialization/transport.
- `App/Core/DragDrop.cs` — drag/drop transfer behavior.
- `App/Class/IClipboardHelper.cs` — helper IPC contract.

Treat these files as compatibility-sensitive.

### UI / machine layout

- `App/Form/frmMatrix.cs` — classic machine matrix/settings UI.
- `App/Form/frmMatrix.Standalone.cs` — standalone-only UI overrides.
- `App/Control/Machine.cs` — machine tile UI.

Prefer adding narrowly scoped `*.Standalone.cs` partials where that keeps upstream files cleaner.

### Portable compatibility layers

- `App/Core/GpoCompatibility.cs` — managed replacement for PowerToys native GPOWrapper.
- `App/Core/SettingsCompatibility.cs` — MWB-local replacement for the subset of Settings.UI.Library that MWB needs.
- `App/Core/CommandEventHandler.cs` — locally owns the exact MWB PowerToys named-event constants after removing PowerToys.Interop.

## Portable product behavior

The distributed product is one self-contained `MouseWithoutBorders.exe`. It runs a hidden second copy of itself in clipboard-helper mode rather than shipping `MouseWithoutBordersHelper.exe`.

Preferences are stored beside the executable as:

`MouseWithoutBorders.prefs.json`

If the prefs file is absent, first launch offers a portable mode or a per-user self-install. The default install directory is `%LOCALAPPDATA%\Programs\Mouse Without Borders`; Start with Windows is optional and off by default. The product does not import PowerToys settings automatically.

After that choice, portable builds bypass the legacy `SetupPage` wizard and open `FrmMatrix` directly. The first matrix view reveals the generated key. Applying the matrix validates every checked tile before changing the key or settings: checked names must be nonblank and unique, including the local computer. A changed key must update `Setting.Values.MyKey` as well as `Encryption.MyKey`, must be compared case-sensitively, and must be saved synchronously before sockets reopen; otherwise the UI can appear to apply a key while a restart restores the old JSON value. Machine tiles summarize connection state in plain language while the form is open. The removed reconfigure link must not be restored unless it leads to a maintained portable flow.

The first-launch install flow creates a Start Menu shortcut, offers a desktop shortcut checked by default, and optionally enables Start with Windows. A portable user may install later from the **Portable** tab in `FrmMatrix`. That path must synchronously save current settings, write a validated installed-mode copy in the destination, defer removal of the source prefs until the old process exits, then restart the installed EXE. Never delete or overwrite the only valid preferences copy before the destination write succeeds. Because `FrmMatrix` is always on top, its modal configured-copy installer must use `CenterParent`, remain topmost while open, and avoid a redundant taskbar button; otherwise the disabled Settings owner can cover the only usable dialog.

The portable build deliberately reconstructs the tray context menu in `frmScreen.Portable.cs`. A portable copy exposes Settings, About, and Exit. An installed copy inserts Start with Windows and Uninstall between Settings and About. Do not restore the legacy screen-capture, all-computers broadcast, live machine-switching, generated-log, or empty Help entries without an explicit product decision; their supporting engine code remains available even though the commands are not shown.

The **Mini Log** link remains available in the Settings window as a support aid and opens one reusable, resizable/maximizable modeless `MiniLogForm`; do not use `ShowDialog`, because a modal owned form disables the main Settings window and looks like a hang even though connection and tray processing continue. `DiagnosticLog.Create` combines the existing configuration/connection snapshot with version, portable mode, paths, OS/runtime/process facts, key length/checksum, and at most the latest 96 KiB of the on-disk log. The report must redact the actual current key and warn that names, IP addresses, and paths can appear. Its read-only multiline text box supports scrolling, selection, Ctrl+C, and an explicit **Copy all** action. Repeated clicks refresh and activate the existing viewer rather than stacking windows. Opening the report must not overwrite the clipboard. The modeless Close button was deliberately removed because `DialogResult` does not close a modeless form; use the normal title-bar X.

`MouseWithoutBordersProperties` defaults `WrapMouse` to `false` for newly created preferences. This affects only new JSON files: loading or upgrading an existing preferences file must preserve its saved value.

Keyboard shortcuts are opt-in through the persisted `KeyboardShortcutsEnabled` master flag, which defaults to false when creating or loading preferences that predate the flag. Individual direct F-key/number switching and lock, reconnect, all-PC, and Easy Mouse toggle assignments remain stored while the master switch is off. Both local `InputHook` handling and the remotely injected lock-hotkey path must check the master flag; do not implement global disable by erasing assignments. The portable `FrmMatrix` owns these supported controls directly; do not restore the stale PowerToys Settings tooltip or blanket-disable loop. Letter selections represent `Ctrl+Alt+letter` and must save through the local settings model. The obsolete Show Settings, Exit, and custom screen-capture shortcut rows are hidden because their backing settings/actions are not active in this build. The visible shortcut panel uses None for an unassigned individual command and forms four responsive rows: the master switch followed by three assignment rows. Their vertical positions must be recalculated from the shortcut group's current scaled height so resizing or DPI scaling cannot bunch them at the top, while maximum spacing is capped for readable maximized windows.

Easy Mouse is not itself a keyboard shortcut. The portable UI presents it in Other Options as **Switch computers at screen edge**, enabled by default, with Always, Hold Ctrl, and Hold Shift activation choices. Its optional toggle hotkey remains in Keyboard Shortcuts and is subject to the master switch. Preserve the engine's `EasyMouseOption.Enable`, `Ctrl`, `Shift`, and `Disable` values when translating between the checkbox/activation selector and persisted settings.

Do not depend on tooltips to explain disabled WinForms controls; disabled controls do not normally receive the hover events needed to show them. The portable form hides the deprecated `Use Key Mappings` checkbox because its setting and handler are disconnected. The unavailable sign-in/Ctrl+Alt+Del controls name their omitted-service requirement inline. When Share Clipboard is off, the disabled Transfer File label names that dependency and returns to its normal text when sharing is restored.

The portable build intentionally excludes the legacy one-minute security-key enforcement block. User-chosen keys are valid indefinitely; the app must not reopen Settings, close sockets, or display expiry/regeneration nags merely because a key is old or manually chosen.

Startup must not access `Setting.Values` before `PortableApplication.PrepareFirstLaunch()` returns successfully. The settings singleton creates a default prefs file when none exists, so initializing it before the first-launch choice would bypass setup and could leave the first process hidden. `StandaloneBootstrap.InitializeAfterFirstLaunch()` deliberately runs immediately afterward.

`App/Icon/notify_default.bmp` is the canonical 32×32 legacy-shape reference. `App/ClassicGreen.svg` expresses that exact pixel grid after a mechanical orange-to-green recolor; transparent pixels, black edging, and pale highlights stay unchanged. `App/ClassicGreen.ico` contains nearest-neighbor 16/20/24/32/40/48/64/128/256 pixel images with 32-bit transparency. MSBuild embeds the ICO in `MouseWithoutBorders.exe`, and title bars and the tray read the embedded product icon at runtime. When the green mapping changes, update the SVG and ICO together and verify the small tray sizes as well as the 256-pixel Explorer view. Do not repeat the rejected Test 5 experiment of replacing the smallest frames with a simplified silhouette; it removed black pixels that are part of the classic design. The accepted ICO was restored from the pre-Test-5 asset.

The large monitor illustrations in the machine matrix are separate from that product icon. `App/Icon/MachineEnabled.png` and `App/Icon/MachineDisabled.png` are geometrically aligned 360×288 true-alpha sources. Portable-only layout code uses `PictureBoxSizeMode.Zoom`, condenses the legacy two-label status footer into one visible line, expands one-row tiles into available height, and calculates both two-row bounds from the matrix's current DPI-scaled client area. Preserve alpha and native proportions; do not flatten onto a background or restore the legacy width-based row spacing. Verify both states and both layouts at 100%, 125%, 150%, and 200% display scaling. Replacing these illustrations must not alter `ClassicGreen.svg`, `ClassicGreen.ico`, tray behavior, or machine connection-state logic.

No Windows service is registered, so protected UAC and Windows sign-in-screen input are intentionally unsupported in the portable product.

## Build validation

The focused workflow is:

`.github/workflows/build.yml`

It is triggered by relevant pushes to `mwb-standalone` and is intended to build/test only MWB rather than all of PowerToys.

### GitHub release publishing

Tags beginning with `mwb-v` run the focused build and then attach the tested `MouseWithoutBorders.exe` and its SHA-256 checksum to the matching GitHub release. A branch commit whose message contains `[release]` does the same using the reviewed metadata in `.github/release-request.json`; this lets a coding session request a release without asking the repository owner to build or upload files manually. The release job has `contents: write`; ordinary branch and pull-request builds retain read-only repository access and never publish releases.

Public versions use semantic versioning. The `0.1.0-test.*` series recorded exploratory hardware tests. `1.0.0-rc.*` identifies feature-complete release candidates undergoing final real-PC validation, and `1.0.0` will be the first stable portable release. Keep `Directory.Build.props`, the Git tag, release title, and release notes aligned so About, diagnostics, Explorer metadata, and GitHub all identify the same build.

Temporary workflow copies of the portable EXE use a one-day retention period. They exist only to carry a tested binary into the release job and support immediate diagnosis; GitHub Release assets are the durable distribution channel.

After publishing an RC, the release job retains the two newest tags matching `mwb-vX.Y.Z-rc.N` on the Releases page and deletes older matching release entries and their assets. It deliberately leaves the corresponding Git tags and commit history intact. Test-series and stable releases do not match this cleanup rule.

The workflow validates that the downloadable package contains exactly one compiled program before publishing it.

Before considering a behavior change finished:

- build x64 Release;
- run MWB unit tests;
- for networking/input/file-transfer changes, manually test between two Windows machines;
- test first launch, portable mode, self-install, startup toggle, self-uninstall, and firewall prompting on a real Windows machine;
- test sleep/wake and reconnect on real machines.

## Extraction status

Already removed from MWB executables:

- direct `PowerToys.Interop` dependency;
- native C++ `PowerToys.GPOWrapper` dependency;
- full `Settings.UI.Library` project dependency (replaced by MWB-local compatibility types).
- PowerToys runner lifecycle coupling in portable mode;
- Microsoft PowerToys telemetry in portable mode;
- external `ManagedCommon` runtime/project dependencies;
- root PowerToys build-system and package-version dependencies;
- PowerToys-branded app/helper/service executable identities;
- the surrounding PowerToys source tree, native module interface, service executable project, and comparison-only project files.

Already proven by CI:

- the portable app and unit tests build directly from this MWB-only repository;
- unit tests run successfully on the focused Windows workflow;
- a single self-contained Windows x64 EXE is uploaded after successful builds;
- the portable prefs path, same-EXE clipboard-helper mode, and per-user startup controls compile in the cleaned repository build.

Remaining validation and cautious cleanup:

- real-Windows validation of portable settings, firewall behavior, startup, upgrades, and self-uninstall;
- optional pruning of dormant MWB-internal setup code and cosmetic names, only after a passing build and focused runtime tests prove each removal safe.

## Preferred modification style

Keep intentional product changes separate from extraction plumbing when practical. A useful commit sequence is:

1. compatibility/extraction refactor with no intended behavior change;
2. tests proving current behavior;
3. intentional feature/UX change;
4. packaging/UI polish.

This makes upstream merges and regressions much easier to reason about.
