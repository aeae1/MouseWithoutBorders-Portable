# 1.0.1-rc.4 file-transfer changes

The RC3 user logs show receiver rejection at 20:19:05.157 and 20:19:07.881,
followed by sender reset errors roughly 0.33 seconds later. The old receive guard
waited three seconds then closed overlapping connections. A single global source
path and helper extraction of only the first selected file also prevented reliable
multi-file drag/drop.

RC4 deliberately introduces a fork-specific drag/drop batch protocol. Install RC4
on both PCs. Stable main remains available while hardware validation is pending.

## Design

The helper sends the entire selection. A bounded, expiring offer table stores a
snapshot of source paths, sizes, and modification times under a random offer ID.
The existing drag announcement carries that ID and a format marker in unused
payload fields. The destination requests that specific offer with ClipboardAsk /
QueuedFiles (post-action 3). Requests must name a connected peer with a matching
machine ID. Offers are consumed once, never resolved via the latest clipboard path.

The sender connects using the existing encrypted clipboard handshake. Mouse and
keyboard packet types, key derivation, and AES stream construction are unchanged.
After the handshake, the batch has a 1024-byte UTF-16 envelope, then one 1024-byte
header per file. The envelope name is MWB-PORTABLE-FILES-1 and its length is the
file count (1–256). File headers contain a file\ prefix followed by a validated
leaf name and a 64-bit byte length. No source directory is sent.

The receiver acknowledges the manifest before waiting in its ordered queue. The
sender can then arrange another drag so both PCs show waiting files. Waiting
receivers send a 64-byte control block each second, avoiding inactivity timeouts.
Control blocks contain the little-endian marker 0x4D574234, a one-byte state
(0 waiting, 1 ready/saved, 2 manifest accepted), and zero reserved bytes.

Only the head receiver copies files. Each file uses the existing bounded-memory
copy engine and zero padding to a 64-byte boundary. The receiver writes a unique
partial file, flushes and closes it, commits without overwriting existing files,
and sends a saved acknowledgement. A later failure preserves earlier completed
files. Sender success requires that saved acknowledgement. This is a save receipt,
not a cryptographic content-hash check.

Cancellation interrupts socket operations and queue waits. A cancelled queue entry
cannot release a later entry before its predecessor finishes. Controlled exit tracks
all file sessions through cleanup. Files changed while queued are rejected rather
than silently copying a different-sized version. A file can still change before its
read handle is acquired without a change to its reported metadata; no snapshot or
hash-based source identity is claimed.

## Window and drag feedback

One window accumulates batches and shows file/status rows, current-file progress,
and Cancel all. New transfers restore and bring it forward. Timer updates do not
activate the window, rewrite tooltips, or relayout changing progress labels.
Successful queues close after cleanup and a one-second grace period. Errors remain
visible. Speed choices and intentional rate limiting are removed.

The drag overlay is requested immediately upon announcement, and delayed move
messages cannot resurrect it after dropping. Clipboard connection acceptance no
longer clears drag state while the user may be dragging the next batch. The source
clears its matching drag when its offer is requested. These are targeted fixes;
physical Windows cursor and focus behavior still needs user verification.

## Validation and remaining scope

Windows CI covers the existing suite plus encrypted batch receive, exact file bytes,
duplicate preservation, empty files, truncated later files, manifest rejection,
queue cancellation order, late socket cancellation, and a real loopback receiver
held active for over three seconds while a second connection queues and cancels.
See the RC release checklist for two-PC testing. Passing automation does not prove
Explorer drag capture, foreground restrictions, or cursor visibility on hardware.

Folder transfer, direct Explorer destination targeting, pause/resume, automatic
retry, and hashes remain future work. Legacy clipboard copy/paste retains its
single-file path and receive guard. Active copies run at maximum speed and can
increase input lag on Wi-Fi. This change does not introduce Windows network QoS.
