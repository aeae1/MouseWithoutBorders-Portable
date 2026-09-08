# RC13: transfer window and queue

RC13 keeps RC12's acknowledged drag/drop startup and bounded connection retries, and the compact RC11 settings. It changes the durable drag/drop queue and its window; mouse/input packets and the separate clipboard file-copy transport are unchanged.

## Window

- Overall percentage, combined active copying speed and estimated remaining time appear in the title bar, refreshed about once per second. Preparing, Paused, Completed, Cancelled and items needing attention have explicit titles. The separate overall metrics row is removed.
- Files and folder groups have an icon/name/control row, a full-width metrics line and a full-width progress bar. Expanded files have a small indent. Thin dividers separate entries.
- Original green glossy up/down vector arrows match the selected desktop icon style. Buttons retain native Windows interaction and accessible names. High contrast uses system colors.
- Windows shell file-type icons are looked up by extension using `SHGFI_USEFILEATTRIBUTES`, without accessing source or unfinished destination files. A background STA worker and a cache capped at 128 types prevent shell lookups in the UI refresh path. Generic file/folder artwork appears immediately and remains if lookup fails.

## Queue rules

The order belongs to the sender. Each sender/destination pair has its own visible queue; four outgoing workers are shared across destinations. Different senders do not share an authoritative queue.

| Action | Result |
| --- | --- |
| Move an unstarted file | Changes its position among unfinished entries. |
| Move a started folder | Files already copying finish. Remaining files get slots according to the folder's new position. |
| Move an active individual file | Disabled until you Pause and its worker stops. |
| Move a paused/error file | Changes position only. Does not Resume or Retry. Runnable work may pass it. |
| Move a child file | Changes order within its existing folder. No path or folder membership changes. |
| Move a collapsed folder | Same effect as moving its expanded group. |
| Complete/cancel during a move | Current identities and states are rechecked. Finished work cannot be restarted by reordering. |
| Drop more items | Appends after existing queued work. |
| Lose the sender connection | Remote arrows become unavailable. Transfers and their saved progress remain governed by the existing recovery controls. |

A receiver asks the authenticated sender to place an item before/after a specific adjacent identity. The sender rejects mismatched peers, expired/nonadjacent targets and unmovable files. Repeating the same successful placement is a no-op. The receiver displays confirmed order, not an optimistic local rearrangement; a stale/racing request can require another click.

`RootOrder` persists folder/loose-file priority; `Order` persists child priority. Resume preserves position. Older protocol-2 `queue` commands remain accepted for compatibility, but the new UI no longer sends them. Optional `QueueSupported`, `QueueState` and `MoveQueue` extend the existing authenticated transfer channel; old peers are never assumed to support the arrows. Incoming queues refresh at most once every three seconds while the transfer window is open, with one background worker per sender. A slow refresh does not blink enabled arrows. Queue snapshots carry only job identities and priorities, and cannot change file contents, paths, bytes or state.

## What happens when a transfer fails

These are the durable **drag/drop** rules, retained from RC12 unless stated otherwise. Clipboard file copy/paste uses its separate legacy transport.

| Situation | Automatic behavior | What to do |
| --- | --- | --- |
| Brief network interruption or connection timeout | A sending file has up to two retries after the initial attempt, with short backoff. Saved bytes are checked before continuing. | Often nothing. If it settles on Paused/Error, reconnect and use Resume/Retry. |
| A PC remains offline | Attempts are bounded. Outgoing files can settle on Paused; state/control acknowledgements wait for reconnection. The other window may temporarily lag behind. | Reconnect both PCs, then review and Resume/Retry or Cancel unfinished entries. Reconnection does not itself unpause recovered/paused work. |
| Sleep or restart | Sleep can interrupt a connection and follows the retry rules; app startup loads unfinished journal entries as Paused and offers to open them, without automatically resuming. | Choose Resume or Cancel after reconnecting. |
| Drop cannot finish preparing | Startup is acknowledged; missing/expired selections and preparation timeout leave a visible error rather than authorizing a delayed fresh copy. | Resolve the cause, then drag the selection again. |
| Receiver runs out of space, loses folder access, or a file cannot be read | That file shows an error/reason. Other runnable files can continue. | Fix the space/access problem and Retry. |
| Source changed since selection | File identity/size/time checks reject the old transfer. | Cancel that entry and drag the current file again. |
| Saved partial data is damaged | Resume compares checksums; invalid partial progress can be discarded and that file recopied. Chunk and final-file checks guard completion. | Retry if it stops with a checksum error. |
| Final completion reply is lost | A retained completion receipt, or a checked existing destination, can confirm the file without making another copy. | Retry the existing entry first; inspect the destination before creating a fresh drag if the old record expired. |
| Cancel | Stops unfinished work and removes owned partial data. Finished files and source files stay. Remote cancellation is retried when the peer reconnects. | Normally nothing; an offline PC cannot clean up until reachable/running again. |
| Close with unfinished work | Asks whether to cancel. On confirmation, waits for local cleanup; minimizing continues transfers. | Minimize to keep copying; confirm cancellation to discard unfinished work. |
| Same destination name already exists | Chooses a separate numbered file/folder; existing contents are not overwritten. | Keep whichever copy you need. |

Partials use hidden `.mwb-<id>.partial` files. They remain while a transfer is paused/recoverable, and are cleaned up when cancelled. Recovery metadata and its backup live beside the app; keep them and the same source/destination locations if you want to resume. Empty directories owned by a cancelled transfer are removed after cleanup; directories containing completed files remain. Errors do not qualify for automatic window closing. Skipped unsupported entries need attention too.

A folder transfer is **not all-or-nothing**: a failed child does not roll back completed children. Use Retry on that child instead of dragging the entire folder again. The Mini Log contains bounded preparation/retry/failure details; when reporting a problem, capture it on both PCs before restarting if possible.

## Validation

Windows CI covers the four-worker scheduler, moving started folders without cancelling active files, paused-position preservation, child/peer boundaries, repeated moves, new-drop append order, cancel/snapshot safety, queue endpoint scoping, title states and rendered rows at normal/larger fonts. The workflow records actual WinForms previews for inspection.

Real-PC checks remain necessary: use RC13 on both PCs and test two folders plus a large loose file; change order on both sides; pause/move/resume a child; collapse/reopen a folder; cancel while moving; disconnect/reconnect and sleep/wake with disposable files. Verify contents and existing-name preservation, plus normal mouse/keyboard/clipboard behavior.

Shell implementation reference: [Microsoft SHGetFileInfo documentation](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow). No external transfer library or extra executable was added.
