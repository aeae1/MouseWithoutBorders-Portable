# File transfers — 1.1.1 candidate

Use the same release on every PC. The controls below apply to **drag-and-drop**; clipboard file copy/paste retains its separate transport.

## Where files go

Drag selected files or folders across the screen edge and release them over an open Explorer folder to copy there. Otherwise, MWB uses this PC's receiving-folder preference under Settings → Other Options. The initial destination is **Desktop / MouseWithoutBorders**, and that folder opens when preparation starts.

A selected folder is copied as a folder beneath the destination, preserving its contents and subfolders. If that name already exists, MWB reserves a separate numbered copy rather than merging into or overwriting the existing folder. Loose files follow the same separate-copy rule. Source files stay in place.

A missing or inaccessible custom receiving folder produces an error. MWB does not silently redirect that transfer elsewhere. Changing the preference affects later drops, not transfers already in the list.

## Before dropping

The receiver shows a preview beside the pointer. A single item has its type icon and name. Multiple items show up to three representative types, including folders when selected, and a total such as **Copy 27 items**. Same-type selections use a small stack. The count covers selected items, not every file inside a selected folder.

Windows supplies icons from the receiving PC's file associations without opening source files or transferring thumbnails. Right-click during the cross-PC drag to cancel it before copying starts. Once a transfer has been accepted into the list, use its Cancel control instead.

## The transfer window

The window appears with preparation feedback, then lists files and folder groups. Expand a folder to inspect its children. Each row has progress and controls; the title bar shows overall percentage, speed and estimated remaining time. Additional drops join the same window.

You can pause, resume, retry or cancel individual entries. A file needing attention stays visible; other runnable work can continue. ETA is an estimate of remaining copying, not a guarantee about verification or recovery time.

The up/down arrows change priority. The sender confirms the order, so the other window may take a few seconds to catch up.

| Action | Behavior |
| --- | --- |
| Move a waiting file | Changes its place among unfinished entries. |
| Move a folder that has started | Files already copying finish; the remaining files follow the new priority. |
| Move a copying file | Pause it first and wait for its worker to stop. |
| Move a paused or failed entry | Changes position without resuming or retrying it. Runnable work can pass it. |
| Move a child file | Reorders it within the same folder, without changing its destination. |
| Drop more items | Appends them after existing queued work. |

Each sender/destination pair has its own queue. Four outgoing file workers are shared across that sender's destinations. Transfers coming from different senders do not share one global priority order.

## Interruptions and failures

| Situation | What happens |
| --- | --- |
| Brief connection failure | A sending file can make two retries after its initial attempt, with short delays. Saved progress is checked before continuing. |
| A PC stays offline | Attempts are bounded. Reconnect, review unfinished work, then Resume/Retry or Cancel if it has stopped. The windows may temporarily disagree while a peer is unreachable. |
| Sleep or restart | Sleep can interrupt copying. App startup restores unfinished journal entries as Paused and offers to open them, without automatically resuming. |
| Preparation fails or expires | A visible error remains. Fix the cause and drag the selection again. |
| Disk full, folder inaccessible, or unreadable file | That file reports an error. Fix the problem and Retry; other files can continue. |
| Source file changed | Cancel its old entry and drag the current version again. |
| Partial data is damaged | Checksums prevent accepting invalid saved progress; that file may need to be recopied. |
| Final acknowledgement is lost | A saved completion receipt or a checked existing destination can confirm completion. Retry the existing entry before starting a fresh drag. |
| Cancel | Unfinished work stops and owned partial data is removed. Completed files and source files stay. Remote cancellation is retried after reconnection. |

A folder transfer is **not all-or-nothing**. A failed child does not roll back completed children. Retry that child instead of dragging the whole folder again unless you want another separate folder copy.

Hidden `.mwb-<id>.partial` files remain while work is paused/recoverable. Cancelling removes owned partials and empty directories created by that transfer; directories containing completed files remain. An offline PC cannot clean up until it is running and reachable. Keep the adjacent recovery metadata and the original source/destination locations if you want to resume after restarting.

## Closing and cleanup

Minimize the window to keep copying. Closing unfinished work asks whether to cancel and waits for local cleanup after confirmation. Completed/cancelled lists close after local cleanup by default; **Automatically close finished transfers** in Other Options can keep them visible. Errors remain visible either way. This closing preference is local to each PC.

## Reporting a problem

Open **Mini Log** on both PCs as soon as practical. Include which PC was sending, the versions, what you dragged, the error, and whether either PC slept or disconnected. Logs contain names, addresses and paths, so review them before sharing publicly.

Busy Wi-Fi can still make mouse movement lag during full-speed transfers. Relative mouse movement changes pointer behavior, not network capacity.

Temporarily unreadable regular files can be rescanned with Retry in 1.1.1. Links and unreadable directory trees remain skipped. Paused transfers do not reserve the receiving drive’s free space; space is checked again when resumed. Recovery storage errors pause work visibly until storage is writable and Resume/Retry is requested.
