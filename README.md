# Mouse Without Borders — Standalone Vibe-Coded Fork

## What this is

This is a **ChatGPT-assisted / vibe-coded modified version of Microsoft Mouse Without Borders**.

The goal is simple:

- Make **Mouse Without Borders a true standalone Windows app again**.
- Keep the newer open-source PowerToys-era MWB fixes and improvements.
- Remove everything that is only needed by the rest of PowerToys.
- Preserve **mouse/keyboard sharing, clipboard sharing, file transfer, service/UAC support, and the classic machine-layout experience**.
- Add a few quality-of-life modifications without turning MWB into a bloated project.
- Keep the finished repository as small, clean, and easy to vibe-code as practical.

> **Not an official Microsoft build.** This fork is based on Microsoft's open-source PowerToys implementation of Mouse Without Borders and retains the upstream MIT license/copyright notices.

## Current modifications

### 2026-09-01

- Created the dedicated `mwb-standalone` development branch.
- Began extracting MWB from the larger PowerToys application.
- Removed MWB's direct dependency on `PowerToys.Interop` while preserving the same named reconnect/toggle events.
- Replaced direct PowerToys GPO integration with a small local MWB compatibility layer.
- Brought the MWB settings compatibility code into the MWB project so it does not need the PowerToys Settings project at runtime.
- Replaced PowerToys runner/telemetry plumbing with local standalone compatibility code; PowerToys telemetry is a no-op in this fork.
- Added focused Windows GitHub Actions build/test coverage for MWB.
- Changed security-key validation so **you may choose your own key**.
- Reduced the custom-key minimum from 16 characters to **4 characters**.
- Replaced the old formulaic generated-key pattern with a **10-character random, easy-to-type key**.
- Generated keys now use an unambiguous lowercase/number alphabet instead of the repeating lowercase → uppercase → number → symbol pattern.
- Added unit tests for the new key behavior.
- Added **green classic MWB branding**: the familiar old orange tray/title-bar icon has green accents in this fork so it is recognizable at a glance.
- Added AI-assisted development/commit attribution.

## Current status

**Work in progress — but the MWB code is already substantially separated from PowerToys.**

The largest remaining cleanup is the **build/project infrastructure**: the MWB projects still inherit some shared PowerToys MSBuild props/package-version infrastructure. The next milestone is making the MWB app, helper, service, and tests build from an MWB-only tree.

After that, the plan is to move the finished product into a clean repository named **`aeae1/MouseWithoutBorders`** and leave unrelated PowerToys code behind.

## Do not regress

These are core features and should remain working while the project is modified:

- Mouse sharing
- Keyboard sharing
- Clipboard text/images
- **File transfer**
- Drag/drop behavior where supported
- Machine matrix/layout
- Reconnect behavior
- Windows service mode
- UAC / secure-desktop / logon support

## Development rules

- Prefer MWB-only code over PowerToys framework dependencies.
- Delete unrelated baggage once a dependency has been proven unnecessary.
- Do **not** delete useful MWB functionality just because it is complicated.
- Preserve compatibility between machines running the same fork build.
- Warn about weak user choices when useful, but do not needlessly block them.
- Make changes in small steps and let CI catch compile/test regressions.
- Keep this README's dated modification list current.

## AI-assisted development

The custom fork modifications documented above are being performed in ChatGPT coding sessions through the repository owner's authenticated GitHub connection. The repository owner has **not manually authored the current custom code changes merely because GitHub displays the authenticated account on the commits**.

AI-assisted work is identified as:

**aeae1's vibe coding assistant — ChatGPT GPT-5.6 Sol**

Commits use an `AI-Assisted-By` trailer where practical.

## Upstream

Upstream source: **Microsoft PowerToys / Mouse Without Borders**

This development branch temporarily remains inside the PowerToys fork so dependencies can be removed safely and tested incrementally. The intended final product repository contains **only MWB and the files actually required to build, test, package, and document it**.
