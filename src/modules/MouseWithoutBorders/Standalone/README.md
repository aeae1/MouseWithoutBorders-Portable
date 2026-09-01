# Mouse Without Borders standalone extraction

This branch is intended to produce a standalone Windows build of Mouse Without Borders while keeping the MWB protocol and behavior compatible with the actively maintained PowerToys version.

## Current status

- `mwb-standalone` is intentionally kept as a small delta from the fork's `main` branch.
- Direct `PowerToys.Interop` dependency: **removed**.
- Native C++/WinRT `PowerToys.GPOWrapper` dependency: **removed from the MWB app, helper, and service** and replaced by an MWB-local managed compatibility shim that reads the same policy registry values.
- Focused Windows x64 build/test workflow: **added** at `.github/workflows/mwb-standalone-build.yml`.
- Human-friendly shared-key policy: **added**. The classic UI accepts user-chosen keys of 4+ characters and generated keys are 10 cryptographically random characters from an easy-to-type lowercase/digit alphabet.
- `Settings.UI.Library`: **next major extraction target**.
- PowerToys runner / telemetry glue: **still to remove after settings extraction**.

## Goals

- Preserve keyboard and mouse sharing behavior.
- Preserve clipboard sharing and file transfer.
- Preserve compatibility with existing PowerToys MWB peers where practical.
- Preserve service/UAC/logon-desktop support.
- Remove runtime dependence on the PowerToys runner and unrelated PowerToys modules.
- Keep the changes structured so upstream MWB fixes can still be merged from `microsoft/PowerToys`.

## Deliberate fork behavior

### Friendlier shared keys

Upstream PowerToys MWB currently requires at least 16 characters and its generator produces characters in a repeating lowercase / uppercase / digit / symbol class sequence. This fork intentionally changes that UX:

- users may type their own key;
- minimum accepted length is 4 characters;
- the classic UI warns that short custom keys are less secure;
- generated keys are 10 characters long;
- generated characters come from `abcdefghjkmnpqrstuvwxyz23456789` to avoid ambiguous characters and keyboard-layout-hostile punctuation;
- every generated character position is selected independently with `RandomNumberGenerator` rather than following a character-class formula.

This does **not** weaken the underlying PowerToys encryption implementation for a given shared secret. The current MWB code still derives AES-256 keys with PBKDF2-SHA512 and uses fresh per-connection random salt and IV values. Choosing a very short custom shared secret is intentionally allowed for convenience but makes guessing attacks easier.

## Extraction strategy

Do not copy the entire MWB codebase into a separate subtree. Instead, remove PowerToys-only dependencies from the existing MWB project one at a time and introduce small local compatibility abstractions only where needed. This minimizes divergence from upstream.

## PowerToys-specific dependencies

### `PowerToys.Interop` — removed

MWB used it only for named Command Palette / Settings UI event names. The exact event-name literals are now kept locally in `Core/CommandEventHandler.cs`, preserving the same names so compatibility is retained.

### `PowerToys.GPOWrapper` — removed from MWB executables

MWB uses enterprise policies for utility enablement, service mode, clipboard sharing, file transfer, networking restrictions, UI selection, and screen-saver behavior. `Core/GpoCompatibility.cs` now supplies the same API from managed code and reads the same `HKLM` / `HKCU\SOFTWARE\Policies\PowerToys` values, with machine policy taking precedence.

This avoids carrying the native C++/WinRT GPO projection into the eventual standalone package while preserving policy behavior and keeping the upstream call sites nearly unchanged.

### `Settings.UI.Library` — in progress

This is currently the largest remaining coupling. MWB uses it for settings models, hotkey settings, JSON settings storage/watchers, attributes, and utility helpers. The standalone version should move only the MWB-specific settings contracts/helpers into the MWB module rather than depending on the full PowerToys settings library.

### `ManagedCommon` / PowerToys telemetry — pending

Startup logging, PowerToys-runner shutdown integration, process helpers, IPC helpers, and telemetry are PowerToys integration concerns. The standalone build should use MWB-local logging/process helpers and omit Microsoft telemetry.

### Service/helper integration — preserve

The existing `App/Service` and `App/Helper` projects are valuable and should be retained. Their PowerToys-specific startup assumptions should be separated from the underlying service/session-launch behavior rather than rewritten from scratch.

## Work order

1. ~~Remove narrow PowerToys dependencies that can be replaced without behavior changes.~~
2. ~~Isolate enterprise-policy access behind an MWB-local abstraction.~~
3. Extract MWB settings contracts and persistence from `Settings.UI.Library`.
4. Remove PowerToys runner/telemetry startup coupling.
5. Make the app, helper, and service build without unrelated PowerToys projects.
6. ~~Add a focused standalone build/CI path.~~
7. Package a standalone installer/portable build.

## AI-assisted development

See `ASSISTANT.md`. Commits made through the connected GitHub workflow use the authenticated repository owner's GitHub identity and include `Assisted-by: ChatGPT (GPT-5.6 Sol)` when the change was produced through this coding session.

## Compatibility rule

Unless intentionally changed, protocol constants, named IPC objects, settings migration behavior, and network/file-transfer semantics should remain compatible with Microsoft's PowerToys MWB implementation.

Because Microsoft has changed the MWB wire protocol since the old Garage standalone build, do not assume this fork will interoperate with `2.2.1.0327`. Run the same fork/current PowerToys-compatible generation on all connected machines unless compatibility has been explicitly tested.
