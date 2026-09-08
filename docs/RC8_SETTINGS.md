# RC8 — settings and transfer preferences

RC8 retains the RC7 installer, compact transfer window, cancellation synchronization,
and immediate preparation feedback. Use RC8 on both PCs for the same interface.

## Settings layout

- **Other Options:** mouse/screen switching, clipboard/file transfers, and keyboard
  shortcuts. Rows wrap and grow with their text; smaller windows scroll.
- **IP Mappings:** manual computer-name/address mappings and **Check computer names
  against DNS**. DNS checking can prevent a connection if name records are wrong.
- **Installation:** current mode and paths, Open folder, Install for me for portable
  copies, and Start with Windows / Uninstall for installed copies.
- The tray contains Settings, File transfers, About, and Exit.

Service-only settings are removed from the visible interface. Optional clipboard/
network status popups are disabled; actual errors and Mini Log remain available.
The removed Same subnet only checkbox used a coarse IPv4 prefix comparison; it no
longer affects unmanaged portable connections. Explicit administrator policy still
applies. Old JSON fields remain readable for compatibility.

**Default** labels describe initial values, not the current selection. Existing
supported preferences, keys, layouts, and shortcut assignments are preserved.

| Setting | Default |
| --- | --- |
| Wrap mouse | Off |
| Share clipboard | On |
| Allow file transfers | On |
| Hide mouse at screen edge | On |
| Show a fallback cursor | On |
| Block screen saver on other machines | On |
| Relative mouse movement | Off |
| Block switching at screen corners | Off |
| Switch computers at screen edge | On |
| Edge activation | Always |
| Keyboard shortcuts | Off; individual assignments None |
| Check computer names against DNS | Off |
| Start with Windows | Off |
| Two-row machine layout | Off |
| Default receiving folder | Desktop / MouseWithoutBorders |
| Automatically close finished transfers | On |

Allow file transfers controls both drag/drop and clipboard file copying. It requires
Share Clipboard. Disabling clipboard sharing makes the file-transfer control
unavailable while retaining its checked/unchecked choice for when sharing is restored.

The fallback cursor is a replacement pointer for cases where Windows reports its
cursor hidden; it is not the normal Windows pointer. It remains optional because it
can conflict with deliberate cursor hiding. Relative movement sends motion deltas
instead of normalized screen positions; it can change pointer feel but does not fix
network congestion.

## Default receiving folder

Choose an ordinary, writable local folder under Other Options on the receiving PC.
The selected directory is checked for linked paths and write access before saving.
Use default restores Desktop / MouseWithoutBorders. The choice survives restarts and
self-installation in the existing adjacent preferences file.

- Dropping into an Explorer folder still targets that folder.
- Otherwise, a new durable drag/drop transfer uses the receiving PC's selected
  folder, which opens immediately.
- A missing custom folder produces an error. MWB does not create it again or silently
  send the files to the desktop instead. Restore the folder/drive or select another.
- Existing transfers retain their recorded destination, including after recovery.
- The separate legacy clipboard file-copy path is unchanged.
- Choosing or restoring a default does not move or delete existing received files.

## Automatic closing

This preference belongs to each PC independently. On closes completed/cancelled lists
after local cleanup; Off leaves the list available for review. Errors stay visible.
Changing this setting affects an already open transfer window too.

An explicit title-bar close still asks to cancel unfinished work and waits for local
cleanup. Minimize continues copying. Offline cancellation requests remain durable;
remote temporary data can only be cleaned after that PC reconnects.

## Installation controls

Start with Windows manages the installed copy's per-user startup entry. Uninstall
retains its confirmation and offers to keep preferences, with No selected by default
for deleting them. Cancelling either confirmation does not uninstall the app.

## Focused real-PC checks

1. Upgrade RC7 on both PCs and check automatic launch, version, key, layout, and
   existing preferences.
2. Review every settings section at your normal display scale and with a larger
   window. Check default labels, wrapping, scrolling, and dropdowns.
3. Turn file transfers off, reopen Settings, and toggle clipboard sharing off/on.
   The file-transfer choice should stay off. Re-enable it for the following tests.
4. Choose a custom receiving folder. Test a desktop drop and an Explorer-folder
   drop, restart MWB, then test Use default. Existing files must remain untouched.
5. Rename the selected custom folder and repeat a desktop drop. Expect an error
   without a copy elsewhere; then restore the folder or change the setting.
6. Disable automatic closing on one PC and finish/cancel a transfer. That window
   should stay open. Re-enable it and check closing; errors must remain visible.
7. Test Installation → Start with Windows and Open folder. Use a disposable
   installation for uninstall testing, first cancelling, then keeping preferences.
8. Recheck mouse, keyboard, clipboard, pause/cancel, duplicate protection, recovery,
   and sleep/reconnect between two Windows machines.

Windows automated tests cover preference migration/round trips, installation copying,
failed-save rollback, missing destinations, settings control behavior and larger-font
layout, and a running transfer-window timer with automatic closing off/on. These do
not replace real Explorer, startup/uninstall, or physical two-PC testing.
