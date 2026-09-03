# Mouse Without Borders standalone development guide

This file is the working map for rapid modification of the `mwb-standalone` branch.

## Branch convention

- `main`: keep close to the upstream Microsoft PowerToys fork.
- `mwb-standalone`: standalone extraction plus intentional MWB behavior changes.
- New experiments can use short-lived branches from `mwb-standalone` when a change is invasive.

Do not casually merge `mwb-standalone` back into `main`; the point of `main` is to remain a clean upstream-sync base.

## AI-assisted commit attribution

Changes made through the connected ChatGPT coding workflow should include:

`Assisted-by: ChatGPT (GPT-5.6 Sol)`

See `ASSISTANT.md` for why GitHub still records the authenticated account (`aeae1`) as the actual author/committer.

## Compatibility guardrails

Unless a change is explicitly intentional, preserve these behaviors:

1. MWB network protocol and packet structures.
2. Clipboard text/image sharing.
3. File copy/paste and drag/drop transfer behavior.
4. Machine matrix behavior and connection discovery.
5. Service/UAC/logon-desktop operation.
6. Named IPC/event strings used by current PowerToys MWB.
7. Current PowerToys `settings.json` shape where practical.

Old Garage standalone `2.2.1.0327` protocol compatibility is **not** assumed. Use the same modern fork/current PowerToys-compatible generation on every connected machine unless old-version interoperability is explicitly tested.

## Intentional fork behavior

### Shared key UX

The fork intentionally differs from upstream:

- manually chosen keys are allowed;
- minimum manual key length: 4 characters;
- generated key length: 10 characters;
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

### Standalone compatibility layers

- `App/Core/GpoCompatibility.cs` — managed replacement for PowerToys native GPOWrapper.
- `App/Core/SettingsCompatibility.cs` — MWB-local replacement for the subset of Settings.UI.Library that MWB needs.
- `App/Core/CommandEventHandler.cs` — locally owns the exact MWB PowerToys named-event constants after removing PowerToys.Interop.

## Settings location

The standalone compatibility layer intentionally keeps the current PowerToys path by default:

`%LOCALAPPDATA%\Microsoft\PowerToys\MouseWithoutBorders\settings.json`

This minimizes surprise while the fork is still protocol/settings compatible with current PowerToys MWB. We can add an import/migration and move to a standalone-specific path later if desired.

## Build validation

The focused workflow is:

`.github/workflows/mwb-standalone-build.yml`

It is triggered by relevant pushes to `mwb-standalone` and is intended to build/test only MWB rather than all of PowerToys.

Before considering a behavior change finished:

- build x64 Release;
- run MWB unit tests;
- for networking/input/file-transfer changes, manually test between two Windows machines;
- for service changes, test normal desktop, UAC secure desktop, lock/logon screen, sleep/wake, and reconnect.

## Extraction status

Already removed from MWB executables:

- direct `PowerToys.Interop` dependency;
- native C++ `PowerToys.GPOWrapper` dependency;
- full `Settings.UI.Library` project dependency (replaced by MWB-local compatibility types).
- PowerToys runner lifecycle coupling in standalone mode;
- Microsoft PowerToys telemetry in standalone mode;
- external `ManagedCommon` runtime/project dependencies;
- root PowerToys build-system and package-version dependencies;
- PowerToys-branded app/helper/service executable identities.

Already proven by CI:

- the standalone app, helper, service, and unit tests build from an archive containing only this MWB directory;
- unit tests run successfully in that isolated directory;
- a Windows x64 development bundle is uploaded after successful builds;
- installer/uninstaller PowerShell syntax parses successfully;
- the packaged installer and uninstaller complete their non-mutating `-WhatIf` validation path;
- manual run mode is enforced by rejecting common Windows automatic-start registry/folder markers.

Still to isolate/remove:

- legacy PowerToys-only project files and the native module interface from the final clean repository;
- PowerToys-era settings-folder naming, with a safe one-time import/migration if it changes;
- real-Windows validation of service permissions, firewall behavior, upgrades, and uninstall;
- remaining cosmetic/internal PowerToys names that are safe to change without breaking IPC or compatibility.

## Preferred modification style

Keep intentional product changes separate from extraction plumbing when practical. A useful commit sequence is:

1. compatibility/extraction refactor with no intended behavior change;
2. tests proving current behavior;
3. intentional feature/UX change;
4. packaging/UI polish.

This makes upstream merges and regressions much easier to reason about.
