# AGENTS.md — Mouse Without Borders standalone fork

These instructions apply to `src/modules/MouseWithoutBorders/**` on the `mwb-standalone` branch.

## Product intent

This branch turns Microsoft PowerToys Mouse Without Borders into a clean standalone Windows application while staying close enough to upstream MWB that fixes can continue to be merged.

Do not treat this as a greenfield rewrite. Prefer small compatibility layers and focused changes over replacing mature input/network/file-transfer code.

## Branches

- `main` is the upstream-sync base. Keep it close to Microsoft PowerToys.
- `mwb-standalone` is the standalone product branch.
- Do not merge standalone-specific changes into `main` unless explicitly requested.

## Compatibility-sensitive behavior

Unless the requested feature deliberately changes it, preserve:

- MWB packet/wire protocol and network constants;
- keyboard and mouse capture/injection behavior;
- clipboard text/image sharing;
- file copy/paste and drag/drop transfer;
- machine matrix and peer discovery;
- normal-desktop behavior without requiring an installed service;
- named IPC/event strings used by modern PowerToys MWB;
- current PowerToys MWB `settings.json` shape where practical.

Do not assume compatibility with the old Garage standalone `2.2.1.0327`; explicitly test it before claiming it.

## Deliberate fork behavior: security key UX

This fork intentionally differs from upstream:

- users may type their own shared key;
- minimum manual key length is 4 characters;
- generated keys are 10 characters;
- generated alphabet is `abcdefghjkmnpqrstuvwxyz23456789`;
- generated positions are independently selected with `RandomNumberGenerator`;
- do not restore the upstream forced lower/upper/digit/symbol position pattern;
- UI should warn that short manually chosen keys are easier to guess, but should not block them solely for strength.

Do not weaken the underlying PBKDF2/AES stream encryption unless explicitly requested and security-reviewed.

## Deliberate fork behavior: portable product

The distributed product is one self-contained `MouseWithoutBorders.exe` with
`MouseWithoutBorders.prefs.json` beside it. The same EXE also runs the hidden
clipboard-helper mode; do not reintroduce a distributed helper executable.

If no prefs file exists, first launch offers portable use or a per-user
self-install. The default install location is
`%LOCALAPPDATA%\Programs\Mouse Without Borders`, and Start with Windows is
optional and off by default.

The portable product deliberately does not install a Windows service. Protected
UAC prompts and the Windows sign-in screen are unsupported; do not claim those
scenarios work unless the product direction changes and they are tested again.

## Standalone compatibility layers

Prefer these instead of reintroducing PowerToys project dependencies:

- `App/Core/GpoCompatibility.cs` — managed policy access replacing native `PowerToys.GPOWrapper`.
- `App/Core/SettingsCompatibility.cs` — local subset of `Settings.UI.Library` used by MWB.
- `App/Core/PowerToysRuntimeCompatibility.cs` — standalone logging plus no-op PowerToys runner/telemetry APIs.
- `App/Core/CommandEventHandler.cs` — owns preserved MWB named-event strings locally.

PowerToys telemetry must remain a no-op in the standalone product.

## Important source areas

Treat these as high-risk and test behavior after editing:

- `App/Class/InputHook.cs`
- `App/Class/InputSimulation.cs`
- `App/Class/SocketStuff.cs`
- `App/Class/TcpServer.cs`
- `App/Class/MachinePool.cs`
- `App/Core/Encryption.cs`
- `App/Core/Clipboard.cs`
- `App/Core/DragDrop.cs`
- `App/Class/IClipboardHelper.cs`
- `App/Core/Service.cs`
- `App/Service/**`

For standalone-only UI tweaks, prefer narrowly scoped `*.Standalone.cs` partial files when that avoids unnecessary upstream diffs.

## Build/test loop

Use `.github/workflows/mwb-standalone-build.yml` as the focused Windows x64 validation path.

Before calling a change complete:

1. Build Release x64.
2. Run MWB unit tests.
3. If input/network/clipboard/file transfer changed, test between two Windows machines.
4. If service/session code changed, test normal desktop, UAC secure desktop, lock/logon, sleep/wake, and reconnect.

A compile-only success does not prove file-transfer or service correctness.

## Commit attribution

For changes produced through the connected ChatGPT coding workflow, include:

`Assisted-by: ChatGPT (GPT-5.6 Sol)`

GitHub will still show the authenticated account (`aeae1`) as the actual author/committer. See `Standalone/ASSISTANT.md`.

## More context

Read:

- `Standalone/README.md`
- `Standalone/DEVELOPMENT.md`
- `Standalone/ASSISTANT.md`
