<p align="center">
  <img src="App/ClassicGreen.svg" width="180" alt="Green Mouse Without Borders portable icon">
</p>

<h1 align="center">Mouse Without Borders — Portable</h1>

<p align="center">
  <strong>Modern Mouse Without Borders, separated from PowerToys and packaged as one portable Windows EXE.</strong><br>
  Vibe-coded with ChatGPT from Microsoft's open-source implementation.
</p>

<p align="center">
  <a href="https://github.com/aeae1/MouseWithoutBorders-Portable/releases/latest">Download the latest stable release</a>
  ·
  <a href="https://github.com/aeae1/MouseWithoutBorders-Portable/actions/workflows/build.yml">Windows build status</a>
</p>

> [!IMPORTANT]
> This is an unofficial fork, not a Microsoft release. Version 1.0.0 is the first stable portable release after the Test and Release Candidate series. It packages the maintained MWB engine as one standalone EXE and includes the completed portable setup, settings, diagnostics, shortcuts, branding, and responsive machine-matrix work.

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
- Allows custom security keys of four or more characters and generates easy-to-type random twelve-character keys.
- Removes PowerToys telemetry from the portable build.
- Uses one green icon source for the EXE, title bars, tray, and repository artwork.
- Builds and tests directly from a cleaned MWB-only repository in GitHub Actions.
- Lets an already-configured portable copy install itself later from the **Portable** settings tab without losing its key, layout, or options.
- Does not expire security keys or periodically demand that a manually chosen key be regenerated.
- Keeps only the current and previous 5 MB local diagnostic logs instead of allowing one log to grow indefinitely.
- Reduces the tray menu to the everyday controls: Settings, About, and Exit, plus Start with Windows and Uninstall for installed copies.
- Makes the About window fully opaque instead of retaining the original 90% transparency.
- Makes an applied security-key edit persist immediately before reconnecting, and treats letter case as significant.
- Opens Mini Log as a resizable, modeless Diagnostic Log instead of disabling Settings or overwriting the clipboard automatically.
- Defaults **Wrap mouse** to off for newly created preferences while preserving existing users' saved choice.
- Keeps the modal install-from-Settings window centered above the always-on-top Settings window so its controls remain reachable.
- Manages supported keyboard shortcuts directly in the portable Settings window, stores them in the adjacent preferences file, and defaults all shortcuts to disabled for newly created preferences while preserving existing saved choices.
- Hides obsolete Show Settings, Exit, and screen-capture shortcut rows whose backing commands are not part of the current portable engine.
- Keeps the four supported shortcut rows centered, evenly spaced, and aligned as the Settings window or Windows display scaling changes.
- Keeps only the two newest `1.0.0-rc.*` download pages after publishing a release candidate, while retaining older source tags and commit history.
- Hides the deprecated, disconnected **Use Key Mappings** checkbox and gives disabled sign-in/clipboard-dependent options self-explanatory labels.
- Presents mouse-edge switching as an ordinary Other Options checkbox with Always, Hold Ctrl, and Hold Shift activation choices instead of mixing that behavior into the shortcut panel.
- Provides one **Enable keyboard shortcuts** master switch, defaulting off, that suppresses every configured hotkey while preserving its individual assignment.
- Replaces the original stretched 43×27 computer-tile bitmaps with crisp transparent artwork: a colorful configured state and a matching grayscale empty state, rendered without distortion in responsive one-row and two-row layouts and without changing the classic green app/tray icon.

## Download and run

1. Open [Releases](https://github.com/aeae1/MouseWithoutBorders-Portable/releases).
2. Download `MouseWithoutBorders.exe` from the newest release's **Assets** section.
3. Put it in a folder where you want to keep it, then run it.
4. Choose **Run portable here**, or choose **Install for me** and select an install folder. Installation can create a desktop shortcut (on by default) and optionally enable Start with Windows.

If you start portably and decide to install later, open Settings and select the **Portable** tab. MWB moves the existing prefs after the running copy closes, then restarts from the installed folder with the same key, layout, and options.

Use the same release on every connected computer.

## Intentional limitations

This portable edition does not install a Windows service. It therefore does not control protected UAC prompts, the Windows sign-in screen, or other secure desktops. Those features conflict with the project's one-EXE, no-admin, portable design.

Current builds are unsigned and may trigger Windows SmartScreen. The automated release workflow only attaches an EXE after the exact tagged source builds and tests successfully.

## Current status

The project is now at **1.0.0 stable**. The intended portable 1.0 scope is complete and the automated Windows build, unit-test, packaging, and release pipeline validates every downloadable EXE.

Completed:

- portable compilation and PowerToys dependency removal;
- single-EXE packaging;
- portable preferences and optional self-install/startup controls;
- green multi-resolution branding;
- release automation;
- upstream audit and September 2026 transfer-safety sync;
- Windows builds, unit tests, and a cleaned MWB-only source repository;
- removal of the legacy PowerToys solution, unrelated modules, native module interface, service project, comparison projects, packaging machinery, and thousands of unrelated assets.

Ongoing real-hardware regression coverage:

- clean first launch, portable mode, self-install, startup, and self-uninstall;
- Windows firewall prompting;
- broader mouse/keyboard, clipboard, file transfer, reconnect, and sleep/wake regression testing between real computers;
- cautious follow-up pruning of dormant code inside MWB itself, only when builds and real-PC tests can prove it is safe.

## Clean repository layout

The default `mwb-standalone` branch now contains only this product: `App`, unit tests, focused build/release automation, documentation, and required legal notices. The portable application project is simply `App/MouseWithoutBorders.csproj`; the old parallel `.Standalone.csproj`, helper/service comparison projects, native PowerToys module interface, and surrounding PowerToys source tree are gone from this branch.

The historical `main` branch and the Test 12 tag remain available as recovery and upstream-comparison points. Release Candidate 1 is the first versioned build produced directly from the cleaned repository. Future Microsoft MWB updates should be reviewed from upstream and ported deliberately rather than merging the full PowerToys tree back into the product branch.

## Documentation

- [Detailed extraction and product status](docs/README.md)
- [Development and compatibility guide](docs/DEVELOPMENT.md)
- [Upstream synchronization record](docs/UPSTREAM_SYNC.md)
- [AI-assistance disclosure](docs/AI_ASSISTANCE.md)
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)
- [Code of Conduct](CODE_OF_CONDUCT.md)

## License and attribution

This project is derived from Microsoft PowerToys Mouse Without Borders and remains under the [MIT License](LICENSE). Microsoft and the original Mouse Without Borders/PowerToys contributors retain attribution for their upstream work. The upstream [third-party notices](NOTICE.md) are intentionally retained as a conservative attribution record even though this branch now contains only MWB.

The portable extraction and fork-specific changes are developed for repository owner `aeae1` through ChatGPT coding sessions. This repository is not affiliated with or endorsed by Microsoft.

## Technical extraction history

This section records how the PowerToys module became this portable product. It is intentionally more detailed than the user guide so future maintainers can tell which pieces are essential and which are temporary scaffolding.

1. **Established a recoverable upstream baseline.** The work began from Microsoft PowerToys commit `becc96f59cf18f3128fedbd6856a5248104216dd`. The fork keeps a clean `main` for reviewing upstream changes and develops the product on `mwb-standalone`; that branch name and the internal `STANDALONE` build symbol remain technical identifiers even though the product is presented as **Portable**.
2. **Mapped the behavior that had to survive.** Input capture/injection, the machine matrix, networking, encryption, clipboard sharing, file transfer, drag/drop, reconnect, and the clipboard-helper IPC path were treated as compatibility-sensitive. Service-only control of protected UAC and sign-in desktops was deliberately excluded because it conflicts with a one-EXE, no-admin product.
3. **Removed PowerToys project dependencies.** Direct dependencies on `PowerToys.Interop`, the native `PowerToys.GPOWrapper`, the full `Settings.UI.Library`, `ManagedCommon`, and PowerToys telemetry were replaced with small MWB-local compatibility implementations. The API shapes needed by imported MWB code were retained where that reduced risky churn; telemetry calls compile to a local no-op.
4. **Made the MWB directory build on its own.** During extraction, the portable app, tests, package versions, target framework settings, and output paths were first made self-contained under the MWB module directory. CI built an archive of only that directory, proving the product no longer needed the surrounding PowerToys source tree before the final cleanup occurred.
5. **Reduced the shipped product to one program.** The clipboard helper was folded into a hidden command-line mode of `MouseWithoutBorders.exe`, preserving the existing IPC design without distributing a companion helper. The release publish is self-contained and is checked to contain exactly one executable.
6. **Implemented adjacent portable preferences.** MWB settings are stored in `MouseWithoutBorders.prefs.json` beside the running EXE. Writes use a temporary file followed by replacement. Startup was reordered so the preferences singleton cannot silently create defaults before the user sees the first-launch choice—the cause of the original “process exists but no window or tray icon” failure.
7. **Built optional self-installation without an installer package.** First launch can run in place or copy the EXE into a per-user folder. Installation writes the prefs beside that EXE, creates a Start Menu shortcut, offers a desktop shortcut checked by default, and can add a current-user Start with Windows entry. No service, MSI, machine-wide registry registration, or administrator permission is added.
8. **Made portable-to-installed migration lossless.** A **Portable** tab in Settings can install an already-running configured copy. MWB first forces a synchronous preferences save, validates and copies the JSON with `appMode` changed to `Installed`, creates the selected shortcuts/startup entry, waits for the old process to exit, removes the old prefs, and launches the installed EXE. Invalid JSON aborts the migration without overwriting the destination or deleting the source.
9. **Simplified first connection setup.** The legacy blue setup wizard and its dead reconfigure link were removed from the portable flow. First run opens the classic matrix, shows the generated key, validates that checked computer names are nonblank and unique, and reports connection state on each configured tile in plain language.
10. **Adjusted key policy deliberately.** The fork accepts manually chosen keys of four or more characters and generates twelve-character keys from an easy-to-type alphabet using `RandomNumberGenerator`. The 31-character alphabet provides about 59.5 bits of entropy at that length. The modern PowerToys-era AES/PBKDF2 transport remains. Legacy timed enforcement that demanded an auto-generated key or warned that a key had expired is excluded from the portable build; keys change only when the user changes them.
11. **Restored a recognizable, exact icon.** `ClassicGreen.svg` reproduces the old 32×32 pixel grid exactly while mechanically mapping only the orange pixels to green. `ClassicGreen.ico` contains nearest-neighbor sizes from 16 through 256 pixels. The embedded icon is used by Explorer, title bars, and the tray, and the same SVG is displayed at the top of this page. A Test 5 experiment that simplified the smallest ICO frames was rejected because it lost part of the black pixel structure; Test 7 restores the complete pre-Test-5 artwork byte-for-byte.
12. **Added long-running and release safety rails.** The local diagnostic log rolls at 5 MB and retains only one previous file; there is no updater, survey, or telemetry sender. The fork audited PowerToys MWB through September 3, 2026 and ported Microsoft's September 2 transactional incoming-file protections. GitHub Actions builds, tests, checks the one-file package, computes a SHA-256 checksum, and creates test releases only after validation succeeds. Release-candidate publishing retains the two newest RC download pages and removes older RC release entries without deleting their source tags. Physical two-PC testing remains the final authority for input, clipboard, file transfer, sleep/wake, firewall, install, and uninstall behavior.
13. **Simplified the everyday interface.** The portable build's tray menu intentionally exposes only Settings, About, and Exit; installed copies additionally expose Start with Windows and Uninstall. Legacy screen-capture, broadcast-control, machine-switching, diagnostic, and dead help entries were removed from the visible menu without removing the underlying connection engine.
14. **Polished the portable presentation and build retention.** The portable About window overrides the legacy form's 90% opacity and renders fully opaque. Temporary CI executables are retained for one day—long enough for release publication and diagnosis—while durable downloadable builds remain attached to GitHub Releases.
15. **Fixed key application and made diagnostics inspectable.** Applying a typed security key now updates both the live encryption state and the adjacent preferences JSON, forces that save to finish before sockets reconnect, and compares keys with case-sensitive semantics. The **Mini Log** link opens a resizable/maximizable, modeless **Diagnostic Log** with selectable text and an explicit **Copy all** button. It combines the configuration/connection snapshot with version, mode, paths, environment, process, key-checksum, and a bounded recent-event tail; the actual key is redacted and the viewer warns that names, IPs, and paths may appear. It does not disable Settings, repeated clicks refresh the existing viewer, and the redundant modeless Close button was removed in favor of the normal window X. New preference files start with **Wrap mouse** off so an outer matrix edge does not unexpectedly jump to the opposite side; existing saved choices are not migrated or overwritten.
16. **Completed the repository cutover.** After Test 12 worked on real PCs, the portable source was promoted to the repository root. More than 8,500 unrelated PowerToys files were removed, leaving roughly 260 tracked files and about 3 MB of project content. Legacy PowerToys projects, the native module interface, service executable source, comparison-only project files, installers, build tooling, unrelated documentation, and unused legacy icon/manifest files were removed. The app project was renamed to `App/MouseWithoutBorders.csproj`, documentation moved to `docs`, and CI gained a repository-layout check that rejects the retired PowerToys paths if they return.
17. **Restored local shortcut ownership.** The inherited form still disabled its complete shortcut group with a stale tooltip claiming PowerToys Settings controlled it, while several letter-shortcut handlers were compiled out. RC3 removes that dependency assumption, reconnects the supported controls to the adjacent JSON settings, persists changes, hides three obsolete command rows, and makes every shortcut opt-in for newly created preferences.
18. **Repaired the Settings-to-install window ordering.** RC1 exposed a Windows z-order conflict: the modal installer disabled its Settings owner but could appear behind that owner's always-on-top window. The configured-copy installer now centers on its parent, stays above that topmost owner, and omits a redundant taskbar button. The true first-launch window retains its normal centered, taskbar-visible behavior.
19. **Made the reduced shortcut panel responsive.** After RC3 hid three obsolete PowerToys-era shortcut rows, their remaining controls were initially pinned near the panel's top edge while the panel itself continued to expand with Settings. RC4 lays out the four supported rows from the panel's current scaled dimensions, distributes them evenly, and caps their separation so both normal and maximized windows stay readable.
20. **Removed inaccessible grey-control explanations.** WinForms does not normally deliver hover events to disabled controls, so their tooltips could not explain why they were unavailable. RC5 hides the fully deprecated and disconnected **Use Key Mappings** switch, labels the two service-only sign-in options inline, and makes the disabled file-transfer label state its Share Clipboard dependency.
21. **Separated mouse-edge behavior from optional hotkeys.** RC6 moves Easy Mouse into Other Options as a plain screen-edge-switching checkbox while retaining Always, Hold Ctrl, and Hold Shift activation. Keyboard Shortcuts gains a persisted master switch that defaults off, gates both local and remotely processed assigned hotkeys, and preserves individual choices while inactive. The ambiguous per-row Disable wording becomes None, and the responsive panel is reorganized as a master row plus three assignment rows.
22. **Rebuilt the machine-tile artwork for modern displays.** RC7 replaces the 43×27 enabled/disabled monitor bitmaps that WinForms had to enlarge with 424×216 PNGs designed for the tile's native aspect ratio. The configured state combines a graphite-and-silver monitor with a restrained emerald, blue, cyan, and violet screen; the matching inactive state is grayscale. Machine naming, layout dragging, checkboxes, and status reporting are unchanged, and the accepted classic green EXE/tray icon remains untouched.
23. **Corrected machine-tile transparency and responsive layout.** RC8 replaces RC7's accidentally flattened white image backgrounds with true-alpha 360×288 PNGs, changes the image control from distortion-prone stretching to aspect-preserving zoom, and sizes each tile from the matrix's actual scaled bounds. One-row mode uses the otherwise available height, while two-row mode calculates both row heights and spacing so the lower monitors remain fully visible. The portable tile condenses its two rare split status phrases onto one line to leave more room for the art; machine order, state reporting, naming, connection behavior, and the classic green product icon are otherwise unchanged.
