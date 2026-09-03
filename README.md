<p align="center">
  <img src="src/modules/MouseWithoutBorders/App/ClassicGreen.svg" width="180" alt="Green Mouse Without Borders standalone icon">
</p>

<h1 align="center">Mouse Without Borders — Standalone</h1>

<p align="center">
  <strong>Modern Mouse Without Borders, separated from PowerToys and packaged as one portable Windows EXE.</strong><br>
  Vibe-coded with ChatGPT from Microsoft's open-source implementation.
</p>

<p align="center">
  <a href="https://github.com/aeae1/PowerToys/releases">Download a test release</a>
  ·
  <a href="https://github.com/aeae1/PowerToys/actions/workflows/mwb-standalone-build.yml">Windows build status</a>
</p>

> [!IMPORTANT]
> This is an unofficial test build, not a Microsoft release. Automated Windows builds and unit tests pass, but the new first-launch, self-install, and two-computer behavior is still being tested on real PCs.

## What this project is

This fork keeps the maintained PowerToys-era Mouse Without Borders engine while turning it back into a focused standalone application. It is intended for people who want MWB without installing or running the rest of PowerToys.

The finished product is deliberately small from a user's perspective:

- one self-contained `MouseWithoutBorders.exe`;
- one adjacent `MouseWithoutBorders.prefs.json` settings file;
- no installer package and no required PowerToys installation;
- optional per-user self-install and Start with Windows support;
- the classic MWB identity recolored green so this fork is easy to recognize.

## Fork-specific changes

- Runs independently of the PowerToys runner and Settings application.
- Keeps normal-desktop mouse, keyboard, clipboard, file-transfer, drag/drop, machine-layout, reconnect, and encryption behavior.
- Includes Microsoft's September 2, 2026 incoming-file safety improvements.
- Folds the clipboard helper into a hidden second mode of the same EXE.
- Stores preferences beside the EXE for genuinely portable operation.
- Offers **Install for me** or **Run portable here** when no preferences file exists.
- Uses a per-user install folder by default, so installation does not require administrator access.
- Allows custom security keys of four or more characters and generates easy-to-type random ten-character keys.
- Removes PowerToys telemetry from the standalone build.
- Uses one green icon source for the EXE, title bars, tray, and repository artwork.
- Builds and tests from an isolated MWB-only source tree in GitHub Actions.

## Download and run

1. Open [Releases](https://github.com/aeae1/PowerToys/releases).
2. Download `MouseWithoutBorders.exe` from the newest test release's **Assets** section.
3. Put it in a folder where you want to keep it, then run it.
4. Choose **Run portable here**, or choose **Install for me** and select an install folder.

Use the same release on every connected computer while the fork is being tested.

## Intentional limitations

This portable edition does not install a Windows service. It therefore does not control protected UAC prompts, the Windows sign-in screen, or other secure desktops. Those features conflict with the project's one-EXE, no-admin, portable design.

Current builds are unsigned and may trigger Windows SmartScreen. The automated release workflow only attaches an EXE after the exact tagged source builds and tests successfully.

## Current status

The project is approximately **85% complete**.

Completed:

- standalone compilation and PowerToys dependency removal;
- single-EXE packaging;
- portable preferences and optional self-install/startup controls;
- green multi-resolution branding;
- release automation;
- upstream audit and September 2026 transfer-safety sync;
- Windows builds, isolated-source builds, and unit tests.

Still being validated:

- clean first launch, portable mode, self-install, startup, and self-uninstall;
- Windows firewall prompting;
- mouse/keyboard, clipboard, file transfer, reconnect, and sleep/wake between real computers;
- the final cutover from this temporary PowerToys fork to a clean MWB-only repository.

## Why the repository still contains PowerToys files

The `mwb-standalone` branch temporarily retains the surrounding PowerToys tree as a comparison and recovery safety net. The standalone source already builds from `src/modules/MouseWithoutBorders` alone, and CI produces a clean source archive with known PowerToys-only files excluded.

After the corrected build passes basic two-PC testing, the MWB-only tree will become the final repository and unrelated PowerToys files and branches can be removed without losing a needed dependency.

## Documentation

- [Standalone project README](src/modules/MouseWithoutBorders/README.md)
- [Detailed extraction status](src/modules/MouseWithoutBorders/Standalone/README.md)
- [Development and compatibility guide](src/modules/MouseWithoutBorders/Standalone/DEVELOPMENT.md)
- [Upstream synchronization record](src/modules/MouseWithoutBorders/Standalone/UPSTREAM_SYNC.md)
- [AI-assistance disclosure](src/modules/MouseWithoutBorders/Standalone/AI_ASSISTANCE.md)
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)
- [Code of Conduct](CODE_OF_CONDUCT.md)

## License and attribution

This project is derived from Microsoft PowerToys Mouse Without Borders and remains under the [MIT License](LICENSE). Microsoft and the original Mouse Without Borders/PowerToys contributors retain attribution for their upstream work.

The standalone extraction and fork-specific changes are developed for repository owner `aeae1` through ChatGPT coding sessions. This repository is not affiliated with or endorsed by Microsoft.
