# RC11 — visible divider and correct page height

RC10’s two-pixel 3D border existed as a control but did not produce a visible
separator on the user’s PC. RC11 fills the divider with a contrasting system grey
color instead. Its existing compact height is retained.

SettingsStack previously measured a child before assigning its new width. A
nested section could then wrap differently and change height while the parent
continued positioning rows from the earlier measurement. RC11 applies the width
first and advances to each child’s actual bottom edge. This targets the empty
space and unnecessary scrollbar reported after settings layout changes.

## Validation

The existing compact-layout, repeated-opening, multiline mapping, hover-help,
and preference checks remain. Additional checks use the actual settings form’s
Load and machine/layout initialization, omitting its network polling timer. They
exercise 100%, 150%, and 200% scaled geometry/text, all tab transitions, and a
wide/narrow/original-width sequence. They check each section’s actual content
height, the scroll range, and whether the normal-size page fits without scrolling.
A rendered image must contain a contrasting divider across the page; a 150%
rendering is also recorded in CI for visual inspection. These simulated scales
do not replace physical per-monitor Windows DPI testing.

On your PC:

- Look for the grey line immediately above Enable keyboard shortcuts.
- Switch among tabs repeatedly and check that Other Options has no unnecessary
  scrollbar or large empty scrolling area.
- Check at your normal display scaling. Scrolling remains available when content
  genuinely does not fit; it should end near the last setting.
- Confirm the IP Mappings editor still shows multiple lines.

RC10’s help, mapping editor, saved preferences, and transfer behavior are retained.
No input, network, transfer-engine, or installer-engine changes are included.
