# Mouse Without Borders — Standalone Vibe-Coded Fork

## What this is

This is a **ChatGPT-assisted, vibe-coded fork of Microsoft Mouse Without Borders**.

The goal is simple:

- Keep **Mouse Without Borders as a standalone Windows app**.
- Remove the need to install or run the rest of Microsoft PowerToys.
- Preserve the parts that make MWB great: **mouse/keyboard sharing, clipboard sharing, file transfer, service/UAC support, and the classic machine layout experience**.
- Make small quality-of-life changes where the stock version is unnecessarily restrictive or annoying.
- Keep the project easy to modify and build with focused MWB-only CI.

This is **not an official Microsoft build**. It is based on Microsoft's open-source PowerToys implementation of Mouse Without Borders and retains the upstream MIT-licensed source notices.

## Current custom changes

### 2026-09-01

- Created the dedicated `mwb-standalone` development branch.
- Began separating MWB from PowerToys-only infrastructure.
- Removed MWB's direct dependency on `PowerToys.Interop` while preserving the same named reconnect/toggle events.
- Added focused Windows GitHub Actions build/test coverage for MWB.
- Changed security-key validation so **you may choose your own key**.
- Reduced the custom-key minimum from 16 characters to **4 characters**.
- Replaced the old formulaic generated-key pattern with a **10-character random, easy-to-type key**.
- Generated keys now use an unambiguous lowercase/number alphabet instead of the repeating lowercase → uppercase → number → symbol pattern.
- Added unit tests for the new key behavior.
- Began documenting AI-assisted development and commit attribution.
- Planned visual fork branding: **the original MWB icon with its orange accents changed to green** so this build is easy to recognize at a glance.

## Current status

**Work in progress.**

The code is still being extracted from the larger PowerToys source tree. The end goal is a true standalone MWB project with its own build, service, settings, packaging, and releases.

### Do not regress

These features are considered core and should remain working while the project is modified:

- Mouse sharing
- Keyboard sharing
- Clipboard text/images
- **File transfer**
- Drag/drop behavior where supported
- Machine matrix/layout
- Reconnect behavior
- Windows service mode
- UAC / secure-desktop / logon support

## Development philosophy

Keep it simple.

- Prefer MWB-only code over PowerToys framework dependencies.
- Preserve compatibility between machines running this fork.
- Warn about weak user choices when useful, but do not needlessly block them.
- Make changes in small testable steps.
- Keep this README's **Current custom changes** section updated as functionality changes.

## AI-assisted development

Most custom fork work is being performed through the repository owner's GitHub connection with assistance from:

**aeae1's vibe coding assistant — ChatGPT GPT-5.6 Sol**

GitHub therefore records repository writes under the authenticated owner account. AI-assisted commits use an `AI-Assisted-By` trailer where practical.

## Upstream

Original project: **Microsoft PowerToys / Mouse Without Borders**

This branch currently lives inside a PowerToys fork while the standalone extraction is completed. Once MWB no longer relies on the surrounding PowerToys tree, the repository can be aggressively pruned or moved into a clean MWB-only repository.
