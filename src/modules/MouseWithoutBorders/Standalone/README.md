# Mouse Without Borders standalone extraction

This branch is intended to produce a standalone Windows build of Mouse Without Borders while keeping the MWB protocol and behavior compatible with the PowerToys version.

## Goals

- Preserve keyboard and mouse sharing behavior.
- Preserve clipboard sharing and file transfer.
- Preserve compatibility with existing PowerToys MWB peers where practical.
- Preserve service/UAC/logon-desktop support.
- Remove runtime dependence on the PowerToys runner and unrelated PowerToys modules.
- Keep the changes structured so upstream MWB fixes can still be merged from `microsoft/PowerToys`.

## Extraction strategy

Do not copy the entire MWB codebase into a separate subtree. Instead, remove PowerToys-only dependencies from the existing MWB project one at a time and introduce small local compatibility abstractions only where needed. This minimizes divergence from upstream.

## PowerToys-specific dependencies currently identified

### `PowerToys.Interop`

Used by MWB only for the named Command Palette / Settings UI event names. The event-name literals are now kept locally in `Core/CommandEventHandler.cs`, preserving the same names so compatibility is retained. The direct project reference has been removed.

### `PowerToys.GPOWrapper`

Used for enterprise policy checks affecting MWB enablement, service mode, clipboard sharing, file transfer, and screen-saver behavior. Standalone work should replace this with a small MWB policy abstraction that defaults to `NotConfigured` while optionally reading the same policy registry values for compatibility.

### `Settings.UI.Library`

This is currently the largest coupling. MWB uses it for settings models, hotkey settings, JSON settings storage/watchers, attributes, and a few utility helpers. The standalone version should move only the MWB-specific settings contracts/helpers into the MWB module rather than depending on the full PowerToys settings library.

### `ManagedCommon` / PowerToys telemetry

Startup logging, PowerToys-runner shutdown integration, process helpers, IPC helpers, and telemetry are PowerToys integration concerns. The standalone build should use MWB-local logging/process helpers and omit Microsoft telemetry.

### Service/helper integration

The existing `App/Service` and `App/Helper` projects are valuable and should be retained. Their PowerToys-specific policy/startup assumptions need to be separated from the underlying service/session-launch behavior rather than rewritten from scratch.

## Work order

1. Remove narrow PowerToys dependencies that can be replaced without behavior changes.
2. Isolate enterprise-policy access behind an MWB-local abstraction.
3. Extract MWB settings contracts and persistence from `Settings.UI.Library`.
4. Remove PowerToys runner/telemetry startup coupling.
5. Make the app, helper, and service build without unrelated PowerToys projects.
6. Add a focused standalone build/CI path.
7. Package a standalone installer/portable build.

## Compatibility rule

Unless intentionally changed, protocol constants, named IPC objects, settings migration behavior, and network/file-transfer semantics should remain compatible with Microsoft's PowerToys MWB implementation.
