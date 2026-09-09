# 1.1.1 RC1 — transfer recovery and matrix dragging

This candidate builds on 1.1.0. Install the same candidate on both PCs.

- Recovery write failures pause transfers visibly. Resume/Retry rechecks storage; cancellation remains available. Initialization is published only after the first checkpoint succeeds.
- A missing main journal can recover its backup. Recovery from a corrupt primary preserves the valid backup.
- New loose-file jobs remember their destination directory identity, like folder transfers. Older records acquire an identity when first used; their historical identity cannot be reconstructed.
- Temporarily unreadable regular files remain retryable. A metadata-only rescan is acknowledged before sending bytes. Unsupported links and unreadable directory trees remain skipped and require a fresh drag.
- Finished hidden rows become compact peer-scoped receipts, retained up to one day with a 32,768 receipt bound. Expired identities cannot authorize a new destination.
- Space checks include the current file and other active receiving work, excluding paused/failed backlog.
- Receiving attempts reuse bounded buffers. Redundant sender checksum/completion and receiver completion writes are removed; final rename checkpoints and per-chunk flushes remain.
- Matrix dragging moves one bitmap instead of live text boxes and checkboxes. Release or cancellation restores the normal controls.

## Validation

The regression suite includes real loopback encrypted transfer interruption/resume, replayed completion, failed journal writes, backup recovery, destination replacement, metadata rescan, space admission and compact receipts. Windows CI gates the release. Hardware testing remains necessary; no claim of exhaustive two-PC validation is made.

## Owner checks

1. Rearrange tiles repeatedly in one-row and two-row mode, including leaving the window and cancelling a drag.
2. Copy a folder and loose files together; pause/resume and cancel individual files.
3. Hold a regular source file open in another application with exclusive access, drag it, release the lock and Retry that entry.
4. Disconnect/reconnect a PC during copying, then Resume or Cancel. Check both windows and destination contents.
5. Pause a loose-file transfer, rename its receiving directory and replace it with a new directory of the old name. Resume must report the changed folder; restore the original directory before cleaning up.

Do not remove preferences or recovery files when updating. Source files and existing destination files must remain intact.
