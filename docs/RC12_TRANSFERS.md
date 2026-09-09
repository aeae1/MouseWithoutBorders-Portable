# RC12: transfer startup, timeouts and overall progress

Release candidate, September 8, 2026. Update **both PCs to 1.0.1-rc.12**. Physical two-PC validation remains necessary.

## Changes and evidence

A reported drag reached the receiving PC's version handshake but did not queue files on the sender. MWB and mouse sharing stayed running. The old ClipboardAsk start request had no acknowledgement, with silent returns for stale selections or unavailable peer identity. The exact trigger of that intermittent incident is not established.

New drops use an explicit StartOffer request on the authenticated encrypted transfer connection. Hello advertises support; older peers get an update message before the start is sent. The sender resolves only a locally recorded selection, consumes it once, and returns success after the file list is accepted, or an error if preparation fails. Working replies keep that request alive during scanning. Preparation has a two-minute cancellation deadline. No network-supplied source paths are accepted. Cancellation still prevents a late manifest from authorizing new receiving jobs. A lost final reply after manifest acceptance does not resurrect the finished preparation.

A separate report showed one file failing approximately ten seconds after starting with “The operation was canceled”, while other files completed. The connection helper's private ten-second deadline raised a cancellation exception which bypassed I/O retry handling. That is a strong match, not a captured stack-trace proof of the incident. RC12 translates that private timeout to an explanatory I/O error so it receives the existing two automatic retries. User cancellation stays cancellation and does not retry. Connection attempts now also observe the transfer's cancellation token.

Folder headings and the overall transfer list show copied/total size, combined active copying speed and an approximate ETA. Paused/error/skipped/cancelled work suppresses a whole-batch ETA. Completed lists do not show stale speed. Verification time is not predicted. Per-file controls and keep-both destination protection remain unchanged.

Mini Log includes at most ten visible preparation entries, their stage and error, plus existing bounded job/event history. Retry events include the underlying reason. Raw user diagnostics are not stored in this repository.

## Test on both PCs

1. Update both EXEs. Repeatedly drag a multi-file selection and a folder with many small files into disposable destinations. Check both windows and compare received contents/counts.
2. Include several large files; check group and overall speed, size and ETA. Pause one and confirm the overall ETA no longer promises a completion time for the paused batch.
3. Cancel during preparation, then drag again. The cancelled attempt must not start later.
4. Briefly interrupt the connection during a disposable transfer. Check the bounded retries, explicit failure/pause if needed, and Retry without duplicate or overwritten destination files.
5. Recheck duplicates, individual cancellation, clipboard, input, lock/unlock and sleep/wake. If one file needs attention, capture its error and both Mini Logs before clearing it.

Automated checks cover connection-deadline versus user-cancellation semantics, single-use multi-file offers, expired offers, cancelled preparation, error replies, capability negotiation, late acknowledgement handling and aggregate progress. Windows UI regression tests cover the retained settings layout. Real Explorer drops and Wi-Fi timing need the above manual tests.

## Broader transfer-software references

These are design references, not copied implementations or newly bundled dependencies:

- [WinSCP resume](https://winscp.net/eng/docs/resume) and [temporary-file handling](https://winscp.net/eng/docs/ui_pref_resume): reconnect/resume and promote temporary files only after successful transfer. MWB already has checked partial files and keeps existing destinations; explicit cancellation cleanup remains our product choice.
- [rclone retry documentation](https://rclone.org/docs/#low-level-retries-int): distinguish retryable operations from a whole-job retry. RC12 fixes the timeout classification at the connection boundary rather than retrying user cancellation or safety validation errors.
- [Syncthing block exchange](https://docs.syncthing.net/specs/bep-v1.html): explicit requests/responses, bounded blocks and hashes. Its permanent synchronization model is broader than MWB's manual one-off copying.

A useful next investigation is connection reuse for batches of small files: MWB currently creates a separate authenticated data connection per file. That may reduce overhead, but would need careful isolation of per-file cancellation and failed connections. It is not changed in RC12, and no replacement engine has been selected.
