# Mouse Without Borders — Standalone Green Fork

## Vibe-coded with ChatGPT

This is a ChatGPT-modified, standalone-only fork of Microsoft PowerToys Mouse Without Borders. The goal is to keep the actively maintained Mouse Without Borders app as one portable EXE without requiring the rest of PowerToys.

**Last modified: September 3, 2026**

> [!IMPORTANT]
> This fork is still in development. Automated Windows builds and tests are in place, but the new single-EXE first-launch, self-install, startup, self-uninstall, and two-computer behavior still require real-Windows testing.

## What has changed

- Uses the classic Mouse Without Borders look with a green icon and green accents.
- Runs as its own app instead of as a PowerToys module.
- Builds from the Mouse Without Borders source folder alone; the surrounding PowerToys source tree is not required.
- Removes PowerToys telemetry from the standalone build.
- Accepts a manually chosen security key with 4 or more characters.
- Generates easy-to-type 10-character keys without easily confused characters.
- Preserves modern keyboard/mouse sharing, clipboard sharing, file transfer, and encryption for normal Windows desktops.
- Includes Microsoft's September 2, 2026 receive-safety update: incoming files are staged before replacing their destination, incomplete transfers are discarded, received lengths are checked, and overlapping transfers are handled safely.
- Runs its clipboard helper as a hidden second mode of the same EXE, so no companion program is required.
- Produces one self-contained Windows x64 EXE after a successful automated build.
- Produces a clean, repo-ready source archive with PowerToys-only project files omitted.
- Creates `MouseWithoutBorders.prefs.json` beside the EXE on first launch.
- Offers either portable use or a per-user self-install, with an optional Start with Windows setting that is off by default.

## Upstream tracking

This fork started from PowerToys commit `becc96f59cf18f3128fedbd6856a5248104216dd` dated August 14, 2026. Microsoft PowerToys `main` was audited again on September 3, 2026. The one newer MWB-specific change, upstream commit [`103d376`](https://github.com/microsoft/PowerToys/commit/103d376c0a987cf350d4594bb3f8d71282fddfd6), has been incorporated.

Upstream synchronization is reviewed rather than merged blindly so that useful MWB fixes are retained without restoring PowerToys runtime dependencies, telemetry, multi-program packaging, or service installation. The detailed audit record and deliberate fork differences are in [`Standalone/UPSTREAM_SYNC.md`](Standalone/UPSTREAM_SYNC.md).

## Progress

The extraction is about **80% complete**.

Finished:

- standalone app project;
- standalone app, helper, and service comparison projects;
- clean standalone program names;
- PowerToys-free build configuration;
- automated x64 build and unit tests from an isolated MWB-only folder;
- green classic branding and friendlier key generation;
- single-file publish script and first-launch portable/self-install experience;
- installed-mode tray controls for Start with Windows and self-uninstall.

Still to do:

- finish real-Windows testing of portable preferences, self-install, startup, firewall prompting, and self-uninstall;
- move the finished MWB-only tree into the clean `aeae1/MouseWithoutBorders` repository;
- test input, clipboard, file transfer, reconnect, and sleep/wake behavior between real Windows computers.

## Running the portable app

The downloadable build contains exactly one program: `MouseWithoutBorders.exe`.

On first launch, it offers two choices:

- **Install for me** copies the EXE to `%LOCALAPPDATA%\Programs\Mouse Without Borders`, creates the adjacent prefs file and a Start menu shortcut, and optionally enables Start with Windows.
- **Run portable here** creates the prefs file beside the current EXE and runs without installing anything.

No Windows service is installed. This portable edition therefore does not control protected UAC prompts or the Windows sign-in screen. Normal-desktop input, clipboard, and file-transfer behavior remains in scope.

## Building from source

Prerequisites:

- Windows 10 version 2004 or newer;
- Visual Studio Build Tools with MSBuild and the .NET desktop workload;
- .NET 10 SDK.

Open a Visual Studio Developer PowerShell window in the repository directory, then run:

```powershell
.\build.ps1 -Configuration Release -Platform x64 -RunTests
```

The output is written to `artifacts/standalone/x64/Release`.

To publish the single-file app:

```powershell
.\publish-portable.ps1 -Configuration Release -Platform x64 -Destination .\portable
```

## Safety and compatibility

Use the same fork build on every connected computer while this work is being tested. Compatibility with the old Garage standalone release `2.2.1.0327` is not assumed.

The 4-character minimum is an intentional convenience option. A longer, randomly generated key is safer, and the built-in generator remains the recommended choice.

## Project notes

- Detailed extraction status: [`Standalone/README.md`](Standalone/README.md)
- Development and compatibility guide: [`Standalone/DEVELOPMENT.md`](Standalone/DEVELOPMENT.md)
- AI-assistance disclosure: [`Standalone/AI_ASSISTANCE.md`](Standalone/AI_ASSISTANCE.md)

## License and attribution

This work is derived from Microsoft PowerToys Mouse Without Borders and remains under the repository's MIT License. Microsoft and the original PowerToys contributors retain attribution for the upstream work. The standalone extraction and fork-specific changes are developed through ChatGPT coding sessions for repository owner `aeae1`.
