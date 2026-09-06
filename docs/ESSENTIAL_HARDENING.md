# Essential hardening

This change retains the one-EXE product, classic UI, shared-key policy, network
protocol, named IPC endpoints, and existing drag/drop behavior. It sets the process to normal priority and does not add new transfer features.

## Changes

- Both local RPC pipe endpoints and the clipboard client restrict access to the
  current Windows user with an explicit user SID ACL and server-owner check. The existing endpoint
  names and external integration remain available to that identity. This avoids
  .NET 10 CurrentUserOnly using an Administrators group owner when elevated
  (dotnet/runtime#123903).
- Preferences are validated before adoption. Invalid JSON and explicit nulls in
  required properties are rejected; missing optional properties retain upgrade defaults. Missing key/layout fields are rejected.
- Atomic preference replacement keeps a `.bak` containing the previous valid
  document. Startup offers restoration of a valid backup, preserving the damaged
  original in a uniquely named `.corrupt-*` file. With no usable backup, startup
  stops with an explanation instead of resetting the shared key or layout.
- Background saves use one worker and serialized immutable documents. A synchronous
  save supersedes pending writes. Own-write notifications do not replace newer
  in-memory settings. Invalid live reloads retain the current configuration.
- A key change that cannot be persisted leaves the old key active and reports the
  error. Background save failures appear in the local log and a notification.
- Installation validates preferences first, stages the EXE, and retains rollback
  copies of affected files/shortcuts plus the previous startup value until setup
  succeeds. A failed rollback leaves its recovery file available and logs the error.
- Migration retains source portable preferences for recovery. The user may remove
  the old portable copy after confirming the installed copy works.
- Shortcut removal verifies the target executable, protecting other installations.
  Uninstall does not proceed if the main process has not exited, or remove prefs
  after failing to remove the executable. Deleting prefs also removes `.bak`.
  Preserved `.corrupt-*` recovery files are not automatically deleted.
- Clipboard deduplication is case-sensitive.
- Release metadata must match the source version and existing tag commit. CI also
  checks the published EXE's product version. Existing releases cannot have assets
  replaced, and stable tags are no longer automatically classified as prereleases.

## Validation

Focused Windows tests cover damaged/malformed preferences, recovery, backup
preservation, queued and synchronous saves, failed key saves, independent clones,
clipboard capitalization, installation file rollback, shortcut ownership, and
pipe ACLs. Python tests cover release version/channel validation.

Before a stable release, test on two Windows PCs:

1. Run both app and clipboard helper normally; reconnect, copy text/images/files,
   and confirm the current-user pipe restriction does not disrupt helper startup.
2. Test normal desktop, lock/unlock, sleep/wake, and reconnect.
3. Apply settings and keys, immediately exit, and restart; test a temporarily
   unwritable preferences folder and confirm recovery after restoring access.
4. With the app stopped, damage a test prefs copy; restore its backup. Repeat
   without a valid backup and confirm that the original is preserved.
5. Install into a new folder, upgrade an existing copy after closing it, migrate
   in place, test startup/shortcuts, and uninstall an older copy after creating a
   newer installation. Confirm the newer shortcuts survive.
6. Run two Windows accounts and verify they cannot use each other's RPC pipes.
   Automated tests inspect the ACL; they do not impersonate a second account.

Windows unit tests do not establish real two-PC runtime compatibility. Keep this
change in review until the hardware checklist is completed. The 1.0.1-rc.1
prerelease is the downloadable vehicle for that testing.

## RC follow-up fixes

Invalid-data failures now reach recovery/error handlers. Generated keys use the
same durable-save path as typed keys. Option handlers request a save; controlled
exit waits up to two seconds for a final save and keeps the app open on ordinary
exit failure. Helper shutdown respects its timeout. Settings RPC shutdown no
longer kills unrelated processes sharing the executable name. The process uses
Normal priority; the dedicated input thread keeps its existing priority.

## Still proposed

Transfer-policy enforcement across all send/receive paths, collision-safe incoming
filenames, and robust IPC listener cancellation/recovery remain follow-up work.
Progress UI, resumable large files, and folder-window drop targeting are not in
this RC. Do not interpret automated tests as proof of those behaviors.
