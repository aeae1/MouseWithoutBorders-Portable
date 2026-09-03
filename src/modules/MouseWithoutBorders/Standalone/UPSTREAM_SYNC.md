# Upstream synchronization record

This file records how the portable fork tracks Microsoft PowerToys Mouse Without Borders. It exists so future updates can be reviewed deliberately instead of either missing important fixes or merging PowerToys-only dependencies back into the product.

## Current audit position

| Item | Value |
| --- | --- |
| Fork source baseline | PowerToys `becc96f59cf18f3128fedbd6856a5248104216dd` |
| Baseline date | August 14, 2026 |
| Last upstream audit | September 3, 2026 |
| Latest audited MWB commit | [`103d376`](https://github.com/microsoft/PowerToys/commit/103d376c0a987cf350d4594bb3f8d71282fddfd6) |
| New MWB commits found after the baseline | One |
| Adoption status | Incorporated into the portable fork |

The July 31, 2026 PowerToys change increasing PBKDF2 key derivation from 50,000 to 100,000 iterations predates the baseline and is already present in this fork.

## September 2 receive-safety update

The upstream update changed `Clipboard.cs` and `Launch.cs`, added `ReceivedDestinationFile.cs`, and added focused unit tests. This fork adopted the complete patch because its non-service protections directly benefit portable clipboard and file transfer.

Adopted behavior:

- only one incoming clipboard/file receive may write at a time;
- a second overlapping receive is rejected rather than racing the active transfer;
- incoming disk files are written to a uniquely named partial file in the destination folder;
- the partial file replaces the requested destination only after the expected byte count arrives;
- interrupted or failed transfers delete their partial file;
- a pre-existing destination remains intact if its replacement transfer fails;
- logged-on-user impersonation always attempts to revert and always closes native token handles;
- tests cover receive serialization, padded payloads, incomplete transfers, replacement behavior, maximum-length destination names, and impersonation cleanup.

The impersonation changes primarily protect PowerToys service mode. They remain in the shared source for upstream alignment, but the distributed portable product neither installs nor launches that service.

## Deliberate differences from PowerToys

Do not overwrite these choices during a future sync:

- one distributed `MouseWithoutBorders.exe` rather than app, helper, and service executables;
- the clipboard helper runs as a hidden mode of the same EXE;
- `MouseWithoutBorders.prefs.json` lives beside the EXE;
- first launch offers portable use or a per-user self-install;
- an already-configured portable copy can install itself later and move its prefs safely;
- installation offers a desktop shortcut by default;
- no Windows service, protected-UAC control, or sign-in-screen control;
- no PowerToys runner lifecycle dependency or PowerToys telemetry;
- local settings, policy, logging, and runtime compatibility layers instead of PowerToys project references;
- green classic branding;
- manually chosen keys require at least four characters and generated keys use ten easy-to-type characters.
- keys do not expire and the legacy timed generated-key/expiry enforcement is disabled.

## Future review procedure

1. List Microsoft PowerToys commits after the latest audited marker that touch `src/modules/MouseWithoutBorders`.
2. Inspect every changed MWB file plus any shared PowerToys dependency the change relies upon.
3. Classify each change as portable-app relevant, service-only, PowerToys-integration-only, or conflicting with an intentional fork behavior.
4. Port applicable fixes narrowly while preserving packet formats, named IPC objects, encryption behavior, and settings compatibility.
5. Add or retain upstream tests and add fork-specific tests where packaging or behavior differs.
6. Build and test from the isolated MWB-only source archive, then publish and inspect the single-EXE artifact.
7. For input, networking, clipboard, or file-transfer changes, complete physical two-computer tests before calling the sync fully validated.
