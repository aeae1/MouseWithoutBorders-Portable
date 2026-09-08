# RC9 — compact settings and hover help

RC9 keeps RC8's receiving-folder and automatic-transfer-window preferences,
Installation controls, and saved settings. It changes settings presentation only.

Other Options removes the separate help/default line and nested table for each
checkbox, and groups keyboard shortcuts into compact rows. Width constraints are
updated when a section changes size, rather than during its layout pass. This
avoids repeatedly invalidating preferred-size measurements when a tab is shown.
Descriptions and defaults are available by hovering. Disabled options receive
hover help through their enabled parent, including Allow file transfers while
Share Clipboard is off. Smaller windows or larger text may still scroll.

Two rows and Start with Windows have no default text, including in their tooltips.
The machine-to-IP mapping field also has no default label. Its expanded visible
instructions explain when a manual mapping helps, one entry per line, an example,
and keeping the address current. DNS checking retains its separate tooltip.

## Validation

Windows tests exercise the original window size, repeated switches into Other
Options, saved transfer choices, tooltip contents, disabled-option help, and larger
text. The normal-font options page must fit without vertical scrolling; six warm
tab visits must complete within three seconds on the CI runner. These are automated
regression checks, not a claim of measured performance on the user's PCs.

On real PCs, check opening speed, normal display scaling, hover help, mapping
instructions, and retained shortcuts, receiving folder, automatic closing, and
startup preferences. No input, network, clipboard, transfer, or installer engine
changes are part of RC9.

See [RC8 behavior](RC8_SETTINGS.md) for the underlying preferences.
