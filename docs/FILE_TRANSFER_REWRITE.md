# File transfer replacement — first RC

The user requested a simple, visible, cancellable transfer experience and reported
that sustained file copies make ordinary mouse control lag. This RC replaces the
file-copy loop and adds a transfer session/UI layer. It retains the authenticated
MWB peer connection and existing encrypted byte framing.

## Design and reuse research

- Windows IFileOperation provides Shell copy/progress operations over accessible
  Shell items. It does not itself expose a remote MWB file as a Shell item:
  https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperation
- Windows IProgressDialog provides reusable progress and cancellation UI, not a
  transfer transport:
  https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nn-shlobj_core-iprogressdialog
- BITS provides background HTTP/SMB transfers and automatic recovery, but would
  require a compatible server endpoint and integration with MWB authentication:
  https://learn.microsoft.com/en-us/windows/win32/bits/background-intelligent-transfer-service-portal
- LocalSend separates preparation, session/file identity, upload, cancellation,
  and optional checksum validation. This is a useful reference for a future
  versioned transport with completion receipts:
  https://github.com/localsend/protocol

No third-party source or artwork was copied. The implementation reuses .NET
streams, cancellation, Windows Forms controls, and the existing MWB peer channel.
LocalSend's Flutter app is not a drop-in library for this Windows Forms project.

## Included

- Independent transfer snapshots observed by a 200 ms UI timer; no synchronous
  UI dispatch from the file-copy loop.
- 32 KiB bounded-memory reads/writes with 64-bit lengths and exact byte counts.
- One process-wide pacing budget for both directions: 2 MiB/s by default, with
  10 MiB/s and unlimited choices for comparison. This is not adaptive QoS.
- Cancellation disposes the transfer socket to wake blocked network I/O. A
  cancelled or interrupted receive never commits its staging file.
- Atomic no-overwrite moves choose numbered names on collisions and return the
  actual path to the clipboard/post-transfer action.
- Existing 100 MB copy/paste restriction removed; ordinary clipboard text/images
  retain their prior path.
- Send and receive checks for file-sharing policy, including during streaming.

The sender does not receive a durable completion receipt under the existing
protocol. Its UI therefore says Sent and directs the user to check the receiver;
only the receiver marks its file complete after committing the destination.

## Follow-up design

A negotiated transfer protocol should add session IDs, checksums and receiver
commit acknowledgements before implementing resumable chunks. Resume must bind
partial data to an unchanged source and destination, not merely reuse a filename.
Folder-window drop targeting needs separate Shell/OLE work, with Desktop fallback.
Pause/resume, automatic retry, folders, and a transfer queue are not in this RC.
The UI and byte-copy module are deliberately separate so they can stay while the
transport is replaced. The immediate goal is to obtain real-PC evidence for the
progress/cancel experience and pacing choice using a normal GitHub prerelease.

## Validation

Windows tests cover exact copy boundaries, short input, counters beyond 2 GB,
cancelled staging cleanup, duplicate-name preservation, blocked-socket cancellation,
and the non-cancellable atomic commit transition. Existing receive/crypto/settings
tests remain. Real-PC testing must cover both directions, large files, cancellation,
Wi-Fi loss, mouse responsiveness, clipboard, reconnect, and sleep/wake. See the
release notes for the user checklist.
