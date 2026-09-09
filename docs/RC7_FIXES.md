# RC7: installation and transfer-window fixes

RC7 is a release candidate. Use it on both PCs for the complete set of fixes. Its file-transfer protocol and recovery journal remain compatible with RC6. The RC5 migration caution in the [folder guide](RC6_TRANSFERS.md) still applies to older jobs.

## Installing or upgrading

Open the downloaded EXE from a separate folder and choose **Install for me**, using your existing installation folder. Setup identifies processes using that exact destination EXE in your Windows session, verifies the settings pipe's owner and server process, requests a normal shutdown, and waits for the app and its helper to exit before replacing files. It does not terminate every process with the same name or force-kill a copy that cannot shut down safely.

The installed copy saves preferences and pauses unfinished transfers before exiting. Setup preserves existing installed preferences and recovery data when installing a fresh download. Installing an already configured portable copy from Settings retains the existing preference-migration behavior. Recoverable backups protect file replacement and synchronous setup steps.

The installed EXE handles its own launch handoff: it waits for the setup/source process to exit, checking the process start time to avoid PID reuse, then launches normal MWB. No completion dialog needs an extra click. The original portable EXE/preferences remain available. If Windows blocks the running copy or relaunch, a clear error asks you to exit it or launch from the Start menu; setup does not silently force termination. Uninstall retains its existing cleanup mechanism.

## Transfer window

- The window opens with **Preparing transfer…** while the connection and selected files are checked. Incoming preparations remain visible until the manifest arrives, fails, times out, or is cancelled. Cancelling invalidates the pending receiving drop so a late manifest cannot silently begin copying.
- Ordinary file rows use two compact lines: name and controls, then a thin progress bar and status. An additional line appears for errors, cleanup, or pending command details. Long text has a tooltip. Button dimensions use the current font and DPI; the footer sizes itself around its controls.
- Folder summaries include cancelled counts and cleanup/cancellation information instead of showing only an incomplete fraction with disabled buttons.
- Cancel/Pause commands have priority and can be sent while the data connection is stopping. Command attempts are bounded and retried; failure details appear in Mini Log. Late replies cannot change a cancelled job back to paused/error or override a newer local control.
- Completed/cancelled lists close after local work and cleanup finish. An offline cancellation remains saved after the window closes and is delivered on reconnect. Skipped/error entries stay available for review. Minimize continues copying; closing unfinished work still asks to cancel it.

The tray always uses the plain green icon. Connection/clipboard/error dots, borders, and animation overlays are disabled. Settings and Mini Log still provide status information.

## Real-PC checks

1. Upgrade over running RC6 and confirm RC7 launches automatically with the same key, layout, and options. Repeat a fresh install and installation from a configured portable copy's Settings.
2. Verify the plain tray icon during connection changes and clipboard activity.
3. Drop a large folder and confirm immediate preparation feedback, compact rows, paging, and unclipped controls at your normal scaling. Resize the window and move it between displays with different scaling if available.
4. Cancel a file/folder from each side, then cancel during preparation. Both PCs should settle on cancellation, retain completed files, remove local incomplete data, and close the cancelled list.
5. Try rapid Pause/Resume/Cancel, an offline cancellation followed by reconnection, and an app restart with unfinished work. No cancelled item should restart; recovered unfinished work should wait for a manual decision.

Automated tests cover reply ordering, cancellation persistence and closure, preparation cancellation, verified shutdown IPC, launch-parent identity/waiting, and compact row/button layout. Actual Windows focus, Explorer targeting, installed-app startup, and two-PC networking still need these acceptance checks. Full-speed Wi-Fi lag remains possible.
