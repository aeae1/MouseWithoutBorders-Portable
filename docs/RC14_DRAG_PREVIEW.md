# RC14 — drag preview and cancellation

RC14 keeps the RC13 transfer window and queue controls and improves the small preview beside the receiving PC's mouse pointer. Install RC14 on both PCs.

## Preview behavior

- One item shows its file-type or folder icon and filename.
- Multiple items show up to three distinct types and a total such as **Copy 27 items**. Folders get one of the slots whenever folders are selected; the other slots use the first distinct file types in the selection.
- Multiple items of one type show a small stack of the same icon. The count still covers the whole selection.
- Counts describe selected files and folders, without scanning all the contents of selected folders before showing feedback.
- Icons use the receiving PC's Windows file associations. They do not require opening the source file, sending thumbnails or waiting for the file to arrive.

Windows shell image lists provide jumbo or extra-large artwork where available. The preview measures the actual artwork inside the icon canvas and avoids stretching a small legacy icon. It uses a layered window with premultiplied per-pixel alpha, rather than a magenta transparency key. Icon lookup happens on a background STA thread, with a bounded type cache. Moving the visible preview does not rerun shell lookup or redraw the bitmap for every mouse event.

## Cancel a drag

While a cross-PC drag is active, right-click to cancel it. The click is intercepted before the normal right-click menu is opened. The drag preview and pending drop are cleared, and the other PC is notified. The matching right-button release and remaining left-button release are consumed so they cannot open a menu or trigger a drop after cancellation. A new left-button press starts normal interaction again.

Cancellation identifies the original sending PC and offer. Short-lived, bounded cancellation records prevent a late begin message for that offer from bringing the preview back; a delayed cancel for an earlier offer does not dismiss another drag. Revoking a selection does not delete source files or cancel transfers already accepted into the transfer window. Use that window's Cancel controls for copying that has already started.

The existing drag-end packet carries an optional cancellation marker; ordinary unmarked end packets retain their screen-handoff behavior. Preview replies retain the old name list and add optional file/folder metadata. Mixed RC versions cannot provide the complete new behavior, so update both PCs.

## Validation

Windows CI builds Release x64 and runs the unit suite. RC14 adds checks for selection grouping, preview metadata, cancellation and button-release handling, revoked offers, late messages, native shell artwork, alpha pixels at 100/125/150/200% scaling, nonactivating/click-through window styles, and actual Windows alpha compositing. CI also records a preview sheet over light and dark backgrounds for visual inspection.

Real-PC acceptance still matters:

1. Drag one file, many same-type files, and mixed files/folders. Check the icons and selected-item count.
2. Move the preview over light and dark windows and across monitors with your usual display scaling.
3. Right-click mid-drag, release the buttons, and check both PCs: no frozen preview, no menu and no transfer. Repeat while moving between screens.
4. Start another drag immediately and complete it. Check ordinary right-click menus still work outside dragging.
5. Recheck input sharing, clipboard, transfer recovery, lock/unlock, sleep/wake and reconnect.

See [RC13_TRANSFERS.md](RC13_TRANSFERS.md) for transfer progress, queue ordering, interruption recovery and failure behavior.
