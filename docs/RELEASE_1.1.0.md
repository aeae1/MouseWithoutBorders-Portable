# Version 1.1.0 — stable

Version 1.1.0 promotes the application code from **1.0.1-rc.14** into the stable release and `main`. This is a feature release after 1.0.0; the RC series kept its development version numbers, while the finished transfer and settings improvements are released as 1.1.0.

The promotion changes version metadata, documentation and the release-note heading. Application code, dependencies, preferences and transfer-journal formats are the same as RC14.

## What's new since 1.0.0

- **File and folder drag-and-drop:** Explorer-folder destinations, a configurable receiving folder, and immediate preparation feedback.
- **One transfer window:** per-file and folder-group progress, overall speed and ETA in the title bar, individual pause/resume/retry/cancel, and queue arrows.
- **Safer recovery:** bounded startup and connection attempts, checked saved progress, completion receipts, and cleanup of cancelled partial files. Existing destination names get separate copies.
- **Clear drag previews:** larger Windows file-type artwork where available, clean alpha edges, up to three representative types, and a selected-item count. Right-click cancels an active cross-PC drag.
- **Simpler settings:** compact Other Options, hover descriptions/defaults, a visible shortcut divider, a large IP mapping editor, receiving-folder and automatic-closing preferences, and Installation controls.
- **Installation and diagnostics:** updating a running installation and relaunching, a plain tray icon, bounded useful logs, and a manual update check that links to GitHub.

## Upgrading

Use **1.1.0 on every connected PC**.

1. Finish or cancel active transfers before updating.
2. For a portable copy, exit MWB and replace `MouseWithoutBorders.exe` in its existing folder. Keep `MouseWithoutBorders.prefs.json` and any adjacent transfer-recovery metadata and backups.
3. For an installed copy, run the downloaded EXE and choose **Install for me**, using the existing installation folder. The setup flow handles the running copy and launches the installed app afterward. Keep the same key and machine layout.
4. Check About shows 1.1.0 on both PCs, then try normal mouse/keyboard sharing and a small file transfer.

RC14 settings and recovery data use the same formats in 1.1.0. Recovery entries load paused after restarting; review them and choose Resume or Cancel. Updating does not change an existing transfer's destination. When upgrading from older versions, saved supported preferences remain; the complete new transfer feature set requires current peers.

## Validation and scope

RC14 passed **138 automated Windows tests with one existing skip**, and its drag preview rendering was inspected at 100%, 125%, 150% and 200% scaling. The owner tested the candidate series on real PCs and reported no major bugs in recent use. That is practical release feedback, not a claim that every edge case was tested.

The stable release repeats the Windows Release x64 build, unit suite and single-file packaging checks. CI verifies the EXE's product version matches the source, then publishes the EXE and SHA-256 checksum. The previous stable release and RC source tags remain recovery points.

## Limitations

- This is an unsigned, unofficial Windows x64 build. No Windows service is installed; protected UAC prompts and sign-in screens are unsupported.
- Transfer progress/recovery controls apply to drag-and-drop. Clipboard file copy/paste retains its separate transport.
- Busy Wi-Fi can still add mouse lag during full-speed copying.
- A folder can partially complete. Completed children are kept when another child fails or is cancelled.
- Remote cleanup and state synchronization require the other PC to be reachable. Restarted/recovered paused work does not resume automatically.
- Icons come from the receiving PC's installed file associations. Some types only have small artwork available.

See the [file-transfer guide](FILE_TRANSFERS.md) for destinations, queue behavior, interruption recovery and cancellation. Historical [RC13](RC13_TRANSFERS.md) and [RC14](RC14_DRAG_PREVIEW.md) notes preserve implementation and test details.
