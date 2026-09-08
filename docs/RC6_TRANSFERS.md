# RC6: folder transfers and predictable cancellation

Status: release candidate, September 8, 2026. Use **1.0.1-rc.6 on both PCs**. The automated Windows checks accompany, rather than replace, the real-PC acceptance checks below.

## Everyday use

Drag files or regular local folders across screens. A drop over an Explorer window targets the folder open in that window. Other drops open `Desktop\MouseWithoutBorders` immediately and use that receiving folder. Targeting an unopened subfolder icon, Explorer sidebar item, virtual folder, or a specific Windows 11 tab is not promised; verify the selected Explorer destination on your PCs.

Dragging `Project` into an Explorer window open to `D:\Work` copies it to `D:\Work\Project`. Dropping elsewhere copies it to `Desktop\MouseWithoutBorders\Project`. Existing receiving folders are reused as destinations, but incoming folders never merge with an existing same-name folder: `Project (2)`, `Project (3)`, etc. keep both copies. Individual filename collisions also keep both files.

Folders preserve their hierarchy, empty directories, and supported file modification timestamps. This is a contents-copy feature, not a filesystem backup: junctions, symbolic links, and reparse-point/cloud-placeholder entries are skipped with an explanation. Make a regular local copy of such contents first. Alternate streams, ACLs, and other filesystem metadata are not replicated.

There is one transfer window per PC. Four outgoing jobs can run at once, and each PC accepts four incoming jobs. Additional files wait. Folder groups expand to show individual entries. Large lists have pages: up to 50 top-level entries per page and 100 children per expanded folder page. This bounds Windows control/handle use. Expand a different folder to collapse the previous one.

Each file has Pause/Resume, Move to end, Cancel, and Retry as appropriate. Folder groups can be paused/resumed or cancelled together. Move to end waits for other runnable work, then explicitly deferred files run in order. Stages distinguish source checking, copying, and receiver verification. Copy-speed estimates are smoothed; estimates can change with network/disk activity and do not cover an unknown verification duration.

## Closing, cancellation, and recovery

- Minimize the window to let transfers continue.
- Closing a window containing unfinished work asks whether to cancel it. Confirming stops all unfinished transfers shown by that PC's transfer center, including entries on other pages, and removes local incomplete data before closing. Completed files and source files are kept.
- A cancelled folder may contain files that already completed. Cleanup removes only incomplete transfer files and empty directories owned by that incoming group; it never recursively deletes a destination tree.
- Cancellation of a transfer whose other PC is offline is saved and delivered when it reconnects. Remote temporary data cannot be removed while that PC is offline. Cleanup failures remain pending and are retried.
- Successful lists close automatically. Errors/skipped entries remain visible for review.
- Sleep, shutdown, and exiting MWB preserve unfinished work. Startup recovery leaves it paused and offers to open the transfer list. Resume after sleep also opens the list when work remains; it does not automatically resume file contents. No wake lock or sleep prevention is added.
- Short connection interruptions get up to two automatic retries. Persistent I/O interruption leaves the item paused or in error for explicit Resume/Retry or Cancel.
- Resume/Retry continues the same job and folder destination. Dragging the folder again creates a new copy with normal duplicate-name protection.

Recovery lives in `MouseWithoutBorders.transfers.json` and its backup beside the EXE. Incomplete data uses hidden `.mwb-<id>.partial` files in the receiving folders. Confirmed cancellation removes that data promptly. Small terminal/cancellation records are distinct from file contents: hidden, finished records are pruned after 10 minutes when commands and cleanup have completed. Cancelled jobs waiting for an offline PC retain their cancellation records. Unfinished data no longer expires after an arbitrary 30 days.

The receiver records whether a local drop has already accepted a manifest. An old manifest cannot create replacement jobs after receipts have expired. In that case the sender gets an error asking the user to inspect received files before dragging again, rather than risking another silent copy.

## Errors, diagnostics, and updates

Transfer support is checked automatically at a drop. Incompatible versions get an error; ordinary connection failures are also reported. No connection-test button or additional network settings are added. Mouse/keyboard packet formats and the underlying MWB encryption are unchanged. Legacy clipboard file copy/paste retains its separate transport.

The existing Mini Log gains a compact transfer snapshot (at most 20 visible entries), protocol version, and counts of active/unfinished/cleanup jobs. Transfer lifecycle events identify the job and peer without logging every chunk. The report retains its existing bounded recent-log section and key redaction; filenames and computer names may appear, so review before sharing.

Disk-space checks budget the remaining bytes of accepted transfers on a destination volume, including saved partial progress, and recheck before chunk writes. Other applications can consume disk space, so a mid-copy error still preserves partial data for Retry. Full-speed copying can still affect Wi-Fi mouse responsiveness.

**About → Check for updates** makes a GitHub request only when clicked. It displays the newer release with a GitHub link or reports that the app is up to date. It never downloads or replaces the EXE. Stable versions exclude RCs; RCs may offer a newer RC or the corresponding/newer stable release. Failed checks leave a manual GitHub link.

Limits: 256 selected top-level items per drag, 4096 expanded entries (including directories) and unfinished jobs, 8192 retained job records, a 4 MiB manifest/frame limit, relative paths of at most 1024 characters/64 components, and a 16 MiB journal. Filesystem limitations can be stricter. Large/deep batches that exceed these limits should be split into smaller selections.

## Acceptance checks on two Windows PCs

1. Exit MWB on both PCs and back up the EXE/preferences. Cancel obsolete RC5 jobs on both PCs before upgrading. If already upgraded, cancel the old entries separately on BOTH PCs and redrag; RC6 cannot resume or synchronize cancellation of old-protocol jobs. Replace only the EXE in its existing directory and confirm About shows RC6.
2. Copy a disposable folder containing files, nested folders, and empty folders into an Explorer window. Verify the complete hierarchy and file contents. Repeat with a desktop drop and check the default MWB folder opens immediately.
3. Pre-create a same-name destination folder containing a sentinel file. Copy the folder twice. Verify the original sentinel is unchanged and separate, numbered sibling folders contain the copies.
4. Copy at least six files and expand folder rows. Check the four-file limit, individual Pause/Resume, Move to end, Cancel, and group Pause/Cancel. Try a folder with more than 100 entries to exercise paging.
5. Close the transfer window during copying: decline once, then confirm. Verify completed files survive and incomplete files disappear. Minimize in another attempt and verify copying continues.
6. Disconnect the receiving PC, cancel from the sender, then reconnect. Verify it does not resume copying and the receiver cleans up its partial data. Repeat sleep/restart recovery without cancelling; expect paused jobs requiring a decision.
7. Try a destination with insufficient free space and a source file changed after selection. Verify a useful error and preserved existing destination contents. Free space and Retry where applicable.
8. Open Mini Log and copy it after a failure; confirm the relevant job/event is easy to find. In About, click Check for updates; only a manual GitHub link should be offered.
9. Verify mouse/keyboard switching, typing/cursor hiding, drag feedback, text/image clipboard, legacy single-file copy/paste, lock/unlock, and sleep/wake. Full-speed Wi-Fi lag remains possible.

## Implementation notes

`TransferFolders.cs` scans a bounded tree and supplies Windows directory/file-handle checks. Incoming group roots use exclusive directory creation. Directory identities and non-reparse ancestors are checked and held through writes, preventing a checked folder from being replaced while in use. Final file moves prohibit overwrite. Empty-directory cancellation cleanup uses a verified native handle and refuses non-empty directories.

`DurableTransfers.Folders.cs` validates a complete manifest before reserving receiving roots; local drop acceptance controls creation and replay. `TransferWire.cs` keeps bounded 64-byte-aligned framing inside the existing encrypted clipboard connection and adds explicit transfer protocol 2. `DurableTransfers.Commands.cs` validates peer-scoped batches so folder controls share a request and journal save. `TransferCenter.cs` owns the close-confirmation and paged folder UI; progress refresh scans each displayed group once rather than rescanning the journal for every child. `ManualUpdates.cs` is invoked only by the About button.

Windows copy semantics informed the keep-both policy, using Microsoft's documented [rename-on-collision behavior](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags). This is an original network implementation using Windows/.NET APIs, not an embedded Explorer/SMB copy engine.
