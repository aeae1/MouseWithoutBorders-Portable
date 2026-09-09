# RC10 — settings divider and multiline mappings

RC10 keeps RC9’s fast, compact settings layout and restores the horizontal divider
above keyboard shortcuts. A thin bordered control provides separation without
reintroducing nested autosizing tables.

The IP Mappings editor again has space for multiple computer entries. RC9’s new
layout measured its preferred height, which can be only one line even for a
multiline text box. RC10 disables automatic sizing for both editable and managed
mapping boxes and gives each a minimum height. Emptying the editor must not shrink
it. Existing mapping contents and save behavior are unchanged.

Visible help now explains that MWB normally finds PCs automatically, when to add a
mapping, the exact computer-name/address format, where to find a local IPv4
address, and why an address might need updating. DHCP reservation is explained as
a router setting that keeps an address from changing. DNS hover help and
Installation descriptions also use clearer wording.

Descriptions and defaults remain in tooltips. Two rows, Start with Windows, and
machine-to-IP mappings still have no default text. Receiving-folder preferences,
automatic closing, shortcut assignments, and Installation actions are unchanged.

## Validation

Windows UI coverage checks the divider’s placement, at least six visible mapping
lines while empty, three simultaneous sample entries, and retained height after
clearing them. Existing checks still cover the original-size Other Options page,
six repeated tab visits within three seconds on CI, disabled-option hover help,
saved transfer choices, and larger-font wrapping. Release x64 build, full unit
tests, and single-file packaging remain publication gates.

On a real PC, check tab-opening speed, the divider, the empty and populated mapping
box, and readability at your usual display scaling. Do not save sample computer
names or addresses unless they match actual PCs on your network. No input,
networking, clipboard, transfer-engine, or installer-engine changes are included.

See [RC9 settings](RC9_SETTINGS.md) and [RC8 preferences](RC8_SETTINGS.md).
