# Mouse Without Borders — Standalone Green Fork

## Vibe-coded with ChatGPT

This is a ChatGPT-modified, standalone-only fork of Microsoft PowerToys Mouse Without Borders. The goal is to keep the actively maintained Mouse Without Borders app while removing the need to install or build the rest of PowerToys.

**Last modified: September 2, 2026**

> [!IMPORTANT]
> This fork is still in development. The automated Windows build and tests pass, but the final installer and real two-computer testing are not finished yet.

## What has changed

- Uses the classic Mouse Without Borders look with a green icon and green accents.
- Runs as its own app, helper, and Windows service instead of as a PowerToys module.
- Builds from the Mouse Without Borders source folder alone; the surrounding PowerToys source tree is not required.
- Removes PowerToys telemetry from the standalone build.
- Accepts a manually chosen security key with 4 or more characters.
- Generates easy-to-type 10-character keys without easily confused characters.
- Preserves modern keyboard/mouse sharing, clipboard sharing, file transfer, encryption, and service-mode code.
- Produces a downloadable Windows x64 development bundle after a successful automated build.

## Progress

The extraction is about **70% complete**.

Finished:

- standalone app project;
- standalone helper and service projects;
- clean standalone program names;
- PowerToys-free build configuration;
- automated x64 build and unit tests from an isolated MWB-only folder;
- green classic branding and friendlier key generation.

Still to do:

- create and test the installer, including Windows service and firewall setup;
- move the finished MWB-only tree into the clean `aeae1/MouseWithoutBorders` repository;
- test input, clipboard, file transfer, reconnect, UAC, sign-in screen, and sleep/wake behavior between real Windows computers.

## Current build layout

The standalone solution has three programs that work together:

- `MouseWithoutBorders.exe` — the main tray app and settings window;
- `MouseWithoutBordersHelper.exe` — the elevated helper used for privileged tasks;
- `MouseWithoutBordersService.exe` — the Windows service used for UAC and sign-in-screen support.

The focused build workflow is `.github/workflows/mwb-standalone-build.yml`. It copies only this Mouse Without Borders directory to a clean location, builds all three programs, runs the unit tests there, and uploads the x64 development bundle.

## Safety and compatibility

Use the same fork build on every connected computer while this work is being tested. Compatibility with the old Garage standalone release `2.2.1.0327` is not assumed.

The 4-character minimum is an intentional convenience option. A longer, randomly generated key is safer, and the built-in generator remains the recommended choice.

## Project notes

- Detailed extraction status: [`Standalone/README.md`](Standalone/README.md)
- Development and compatibility guide: [`Standalone/DEVELOPMENT.md`](Standalone/DEVELOPMENT.md)
- AI-assistance disclosure: [`Standalone/AI_ASSISTANCE.md`](Standalone/AI_ASSISTANCE.md)

## License and attribution

This work is derived from Microsoft PowerToys Mouse Without Borders and remains under the repository's MIT License. Microsoft and the original PowerToys contributors retain attribution for the upstream work. The standalone extraction and fork-specific changes are developed through ChatGPT coding sessions for repository owner `aeae1`.
