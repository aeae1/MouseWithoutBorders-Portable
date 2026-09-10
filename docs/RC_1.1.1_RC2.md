# 1.1.1 RC2 — smoother computer matrix

Builds on RC1, retaining its transfer recovery fixes. Mouse sharing, networking and file-transfer code are unchanged from RC1.

The Computer Matrix uses one double-buffered drawing surface for monitor artwork, status and drag previews. Computer names and checkboxes remain native Windows controls, retaining editing, validation and keyboard focus. Layout finishes before the first displayed frame. Larger text gets sufficient matrix height for usable monitor artwork. Connection statuses are computed before updating their presentation, avoiding repeated clear/reapply cycles.

Dragging follows the pointer with capture, without OLE drag/drop or the old half-second two-row swap delay. Enter the inner area of another slot to swap positions. Release inside the matrix to keep the arrangement; Escape, right click, capture loss, or release outside cancel it. Apply still saves the arrangement. Arrow keys select a monitor while the surface is focused; Ctrl+arrow moves it. Switching row mode cancels an unfinished drag.

## Validation and limits

Windows CI tests normal and enlarged geometry/text, one/two-row swaps without a timed wait, capture-loss/outside cancellation, simple clicks, restoration of native controls, editing bindings, geometry, and stale pixels during an active drag. Existing settings and transfer regressions remain required. Actual pointer feel and display scaling require owner validation; automated repaint checks do not establish a frame-rate guarantee.

## Try on your PC

1. Open Settings repeatedly and switch tabs. Check for flashing or jumping monitor artwork.
2. Drag rapidly between slots, including diagonally in two-row mode. Check immediate response and clean redraws.
3. Cancel with Escape or right click; drag outside and release; try Alt+Tab mid-drag. The previous order should return.
4. Edit an empty computer slot, toggle its checkbox, then rearrange, Apply and reopen. Check names remain attached to the right monitors and order persists.
5. Try your usual display scaling and both row modes. Names, checkboxes and statuses should fit.

Install RC2 on both PCs for a consistent test baseline. Preserve preferences and recovery files and finish/cancel unfinished work before updating.
