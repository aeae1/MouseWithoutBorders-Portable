<p align="center">
  <img src="src/modules/MouseWithoutBorders/App/ClassicGreen.svg" width="180" alt="Green Mouse Without Borders portable icon">
</p>

<h1 align="center">Mouse Without Borders — Portable</h1>

<p align="center">
  <strong>Modern Mouse Without Borders, separated from PowerToys and packaged as one portable Windows EXE.</strong><br>
  Vibe-coded with ChatGPT from Microsoft's open-source implementation.
</p>

<p align="center">
  <a href="https://github.com/aeae1/MouseWithoutBorders-Portable/releases">Download a test release</a>
  ·
  <a href="https://github.com/aeae1/MouseWithoutBorders-Portable/actions/workflows/mwb-standalone-build.yml">Windows build status</a>
</p>

> [!IMPORTANT]
> This is an unofficial test build, not a Microsoft release. Automated Windows builds and unit tests pass, but the new first-launch, self-install, and two-computer behavior is still being tested on real PCs.

## What this project is

This fork keeps the maintained PowerToys-era Mouse Without Borders engine while turning it into a focused portable application. It is intended for people who want MWB without installing or running the rest of PowerToys.

The finished product is deliberately small from a user's perspective:

- one self-contained `MouseWithoutBorders.exe`;
- one adjacent `MouseWithoutBorders.prefs.json` settings file;
- no installer package and no required PowerToys installation;
- optional per-user self-install, Start with Windows, Start Menu, and desktop-shortcut support;
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
- Removes PowerToys telemetry from the portable build.
- Uses one green icon source for the EXE, title bars, tray, and repository artwork.
- Builds and tests from an isolated MWB-only source tree in GitHub Actions.
- Lets an already-configured portable copy install itself later from the **Portable** settings tab without losing its key, layout, or options.
- Does not expire security keys or periodically demand that a manually chosen key be regenerated.
- Keeps only the current and previous 5 MB local diagnostic logs instead of allowing one log to grow indefinitely.
- Reduces the tray menu to the everyday controls: Settings, About, and Exit, plus Start with Windows and Uninstall for installed copies.

## Download and run

1. Open [Releases](https://github.com/aeae1/MouseWithoutBorders-Portable/releases).
2. Download `MouseWithoutBorders.exe` from the newest test release's **Assets** section.
3. Put it in a folder where you want to keep it, then run it.
4. Choose **Run portable here**, or choose **Install for me** and select an install folder. Installation can create a desktop shortcut (on by default) and optionally enable Start with Windows.

If you start portably and decide to install later, open Settings and select the **Portable** tab. MWB moves the existing prefs after the running copy closes, then restarts from the installed folder with the same key, layout, and options.

Use the same release on every connected computer while the fork is being tested.

## Intentional limitations

This portable edition does not install a Windows service. It therefore does not control protected UAC prompts, the Windows sign-in screen, or other secure desktops. Those features conflict with the project's one-EXE, no-admin, portable design.

Current builds are unsigned and may trigger Windows SmartScreen. The automated release workflow only attaches an EXE after the exact tagged source builds and tests successfully.

## Current status

The project is approximately **85% complete**.

Completed:

- portable compilation and PowerToys dependency removal;
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
- final cleanup of the remaining PowerToys source files from the renamed MWB-only repository.

## Why the repository still contains PowerToys files

The `mwb-standalone` branch temporarily retains the surrounding PowerToys tree as a comparison and recovery safety net. The portable source already builds from `src/modules/MouseWithoutBorders` alone, and CI produces a clean source archive with known PowerToys-only files excluded.

After the corrected build passes basic two-PC testing, the MWB-only tree will become the final repository and unrelated PowerToys files and branches can be removed without losing a needed dependency.

## Documentation

- [Portable project README](src/modules/MouseWithoutBorders/README.md)
- [Detailed portable extraction status](src/modules/MouseWithoutBorders/Standalone/README.md)
- [Development and compatibility guide](src/modules/MouseWithoutBorders/Standalone/DEVELOPMENT.md)
- [Upstream synchronization record](src/modules/MouseWithoutBorders/Standalone/UPSTREAM_SYNC.md)
- [AI-assistance disclosure](src/modules/MouseWithoutBorders/Standalone/AI_ASSISTANCE.md)
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)
- [Code of Conduct](CODE_OF_CONDUCT.md)

## License and attribution

This project is derived from Microsoft PowerToys Mouse Without Borders and remains under the [MIT License](LICENSE). Microsoft and the original Mouse Without Borders/PowerToys contributors retain attribution for their upstream work.

The portable extraction and fork-specific changes are developed for repository owner `aeae1` through ChatGPT coding sessions. This repository is not affiliated with or endorsed by Microsoft.

## Technical extraction history

This section records how the PowerToys module became this portable product. It is intentionally more detailed than the user guide so future maintainers can tell which pieces are essential and which are temporary scaffolding.

1. **Established a recoverable upstream baseline.** The work began from Microsoft PowerToys commit `becc96f59cf18f3128fedbd6856a5248104216dd`. The fork keeps a clean `main` for reviewing upstream changes and develops the product on `mwb-standalone`; that branch name and the internal `STANDALONE` build symbol remain technical identifiers even though the product is presented as **Portable**.
2. **Mapped the behavior that had to survive.** Input capture/injection, the machine matrix, networking, encryption, clipboard sharing, file transfer, drag/drop, reconnect, and the clipboard-helper IPC path were treated as compatibility-sensitive. Service-only control of protected UAC and sign-in desktops was deliberately excluded because it conflicts with a one-EXE, no-admin product.
3. **Removed PowerToys project dependencies.** Direct dependencies on `PowerToys.Interop`, the native `PowerToys.GPOWrapper`, the full `Settings.UI.Library`, `ManagedCommon`, and PowerToys telemetry were replaced with small MWB-local compatibility implementations. The API shapes needed by imported MWB code were retained where that reduced risky churn; telemetry calls compile to a local no-op.
4. **Made the MWB directory build on its own.** Portable app, helper/service comparison projects, package versions, target framework settings, and output paths were moved under `src/modules/MouseWithoutBorders`. CI copies only that directory to a separate location, builds it there, and runs its unit tests there. That is the proof that the product no longer needs the surrounding PowerToys source tree to compile.
5. **Reduced the shipped product to one program.** The clipboard helper was folded into a hidden command-line mode of `MouseWithoutBorders.exe`, preserving the existing IPC design without distributing a companion helper. The release publish is self-contained and is checked to contain exactly one executable.
6. **Implemented adjacent portable preferences.** MWB settings are stored in `MouseWithoutBorders.prefs.json` beside the running EXE. Writes use a temporary file followed by replacement. Startup was reordered so the preferences singleton cannot silently create defaults before the user sees the first-launch choice—the cause of the original “process exists but no window or tray icon” failure.
7. **Built optional self-installation without an installer package.** First launch can run in place or copy the EXE into a per-user folder. Installation writes the prefs beside that EXE, creates a Start Menu shortcut, offers a desktop shortcut checked by default, and can add a current-user Start with Windows entry. No service, MSI, machine-wide registry registration, or administrator permission is added.
8. **Made portable-to-installed migration lossless.** A **Portable** tab in Settings can install an already-running configured copy. MWB first forces a synchronous preferences save, validates and copies the JSON with `appMode` changed to `Installed`, creates the selected shortcuts/startup entry, waits for the old process to exit, removes the old prefs, and launches the installed EXE. Invalid JSON aborts the migration without overwriting the destination or deleting the source.
9. **Simplified first connection setup.** The legacy blue setup wizard and its dead reconfigure link were removed from the portable flow. First run opens the classic matrix, shows the generated key, validates that checked computer names are nonblank and unique, and reports connection state on each configured tile in plain language.
10. **Adjusted key policy deliberately.** The fork accepts manually chosen keys of four or more characters and generates ten-character keys from an easy-to-type alphabet using `RandomNumberGenerator`. The modern PowerToys-era AES/PBKDF2 transport remains. Legacy timed enforcement that demanded an auto-generated key or warned that a key had expired is excluded from the portable build; keys change only when the user changes them.
11. **Restored a recognizable, exact icon.** `ClassicGreen.svg` reproduces the old 32×32 pixel grid exactly while mechanically mapping only the orange pixels to green. `ClassicGreen.ico` contains nearest-neighbor sizes from 16 through 256 pixels. The embedded icon is used by Explorer, title bars, and the tray, and the same SVG is displayed at the top of this page. A Test 5 experiment that simplified the smallest ICO frames was rejected because it lost part of the black pixel structure; Test 6 restores the complete pre-Test-5 artwork byte-for-byte.
12. **Added long-running and release safety rails.** The local diagnostic log rolls at 5 MB and retains only one previous file; there is no updater, survey, or telemetry sender. The fork audited PowerToys MWB through September 3, 2026 and ported Microsoft's September 2 transactional incoming-file protections. GitHub Actions builds, tests, verifies the isolated source, checks the one-file package, computes a SHA-256 checksum, and creates test releases only after validation succeeds. Physical two-PC testing remains the final authority for input, clipboard, file transfer, sleep/wake, firewall, install, and uninstall behavior before the temporary PowerToys comparison tree is removed.
13. **Simplified the everyday interface.** The portable build's tray menu intentionally exposes only Settings, About, and Exit; installed copies additionally expose Start with Windows and Uninstall. Legacy screen-capture, broadcast-control, machine-switching, diagnostic, and dead help entries were removed from the visible menu without removing the underlying connection engine.
