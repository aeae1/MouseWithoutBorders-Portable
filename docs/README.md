# Mouse Without Borders portable extraction record

The `main` branch contains a portable Windows build of Mouse Without Borders derived from the actively maintained PowerToys-era source. The `STANDALONE` build symbol remains an internal compatibility identifier; the user-facing product name is **Mouse Without Borders — Portable**.

## Current status

- Direct `PowerToys.Interop` dependency: **removed**.
- Native C++/WinRT `PowerToys.GPOWrapper` project dependency: **removed**; MWB has a small local managed compatibility implementation instead.
- `Settings.UI.Library` project dependency: **removed**; the subset MWB needs now lives with MWB as local compatibility code.
- `ManagedCommon` / PowerToys telemetry runtime project dependencies: **removed/replaced locally**. PowerToys telemetry is intentionally a no-op in this portable fork.
- PowerToys-runner lifetime coupling: **neutralized for portable operation**.
- Classic MWB tray/settings UI: **forced on** without requiring the separate PowerToys Settings process.
- Focused Windows x64 build/test workflow: **added** at `.github/workflows/build.yml`.
- Human-friendly shared-key policy: **added**. User-chosen keys of 4+ characters are accepted; generated keys are 12 cryptographically random easy-to-type characters.
- Classic visual branding: **added**. The original orange 32×32 MWB pixel design is mechanically recolored green, with a matching multi-resolution ICO and exact pixel-grid SVG.
- Single portable application project: **completed**. The app and clipboard helper compile into one executable; the retired helper/service comparison projects have been removed.
- Shared PowerToys MSBuild/package infrastructure: **removed from the portable build path**.
- MWB-only build proof: **passing**. CI builds the portable app and unit tests directly from this cleaned repository.
- Downloadable single-file Windows x64 build: **added** to successful workflow runs.
- Clean portable repository: **completed**. The PowerToys source tree, old solution, native module interface, service project, installers, unrelated tooling, and comparison project files are removed from the product branch.
- Portable first launch and per-user self-install: **added**. Preferences stay beside the EXE; installation defaults to the current user's local Programs folder, a desktop shortcut is offered by default, and Start with Windows is optional.
- Later installation from Settings: **added**. The Portable tab can install an already-configured copy, preserving and moving its key, matrix, and options before restarting from the installed directory.
- Install-dialog ordering: **fixed in RC2**. The configured-copy installer is centered and kept above the always-on-top Settings owner, preventing the disabled Settings window from covering the modal installer.
- Portable shortcut ownership and layout: **fixed in RC3/RC4**. Supported keyboard shortcuts are editable in the classic Settings window and saved to the adjacent portable JSON; the stale PowerToys Settings lockout is removed, unsupported command rows are hidden, new preference files start with every shortcut disabled, and the remaining four rows now reflow evenly with the Settings window and Windows display scaling.
- Disabled-option clarity: **fixed in RC5**. The deprecated and disconnected key-mapping checkbox is hidden; service-only sign-in options explain their requirement inline; and disabled file transfer names its Share Clipboard dependency instead of relying on a tooltip that cannot open.
- Mouse-edge/shortcut separation: **completed in RC6**. Screen-edge switching is an Other Options checkbox with Always/Ctrl/Shift activation, while one master keyboard-shortcut switch defaults off and gates all saved assignments without deleting them. Per-feature absence is labeled None rather than the ambiguous Disable.
- High-resolution machine tiles: **corrected in RC8**. The 43×27 monitor bitmaps formerly stretched across each tile are replaced by geometrically aligned 360×288 true-alpha PNGs. The graphite/silver configured state uses restrained emerald, blue, cyan, and violet, while the empty state is grayscale. Aspect-preserving zoom and responsive one-row/two-row bounds prevent flattening, white boxes, and lower-row clipping at scaled display settings.
- Quiet long-term key behavior: **added**. Portable builds never expire a key and do not run the legacy timed check that demanded an auto-generated replacement.
- Reliable key application: **added**. Apply writes an edited, case-sensitive key to both the live encryption state and adjacent preferences before reconnecting.
- Inspectable Diagnostic Log: **added**. Mini Log opens a resizable/maximizable, modeless viewer containing the configuration/connection snapshot, environment details, and a bounded recent-event tail; it changes the clipboard only through an explicit Copy all action and redacts the security key.
- Conservative edge behavior: **added**. New preference files default Wrap mouse to off; existing saved values remain unchanged.
- First-launch startup ordering fix: **added and verified on real Windows PCs**. A brand-new folder no longer creates default preferences before the portable/install choice or leaves the initial process hidden.
- Streamlined machine setup: **added**. The portable first run opens the classic machine matrix directly, validates checked computer names, and shows plain-language connection state on every configured tile; the legacy blue wizard and its reconfigure link are no longer part of this product flow.
- Essential tray menu: **added**. Portable copies show Settings, About, and Exit; installed copies also show Start with Windows and Uninstall. Rare legacy commands are no longer presented in the portable product's tray menu.
- Small-icon rollback: **completed in Test 7**. The rejected Test 5 tiny-frame redesign lost some of the classic black pixel structure, so the multi-resolution green ICO is restored byte-for-byte to its pre-Test-5 artwork.
- Opaque About window: **added**. The portable About screen overrides the legacy 90% opacity so windows behind it do not show through.
- Short-lived CI artifacts: **added**. Temporary workflow copies of the EXE expire after one day; published GitHub Release downloads remain available independently.
- Release-candidate retention: **added**. Publishing an RC keeps its two newest RC download pages and prunes older RC release entries; Git tags and commit history remain intact.
- GitHub Releases publishing: **added and proven**. Tagged and requested releases are built, tested, and populated with the EXE and checksum automatically.
- Upstream MWB audit through September 3, 2026: **completed**. Microsoft's September 2 receive-safety improvements are incorporated and documented in `UPSTREAM_SYNC.md`.
- Real-Windows connection testing: **validated through the Test and Release Candidate series**. Version 1.0.0 keeps that behavior and completes the portable setup, shortcut, diagnostics, branding, responsive machine-tile, and flicker-free row-transition work; continued firewall, sleep/wake, display-scaling, and two-machine regression checks remain worthwhile as the stable release receives broader use.
- Machine-tile artwork validation: **completed for 1.0.0**. The replacement assets and responsive layout do not touch network, input, preferences, or connection-state logic; the stable build also makes row changes repaint atomically instead of exposing intermediate control bounds.

## Goals

- Preserve keyboard and mouse sharing behavior.
- Preserve clipboard sharing and **file transfer**.
- Preserve normal-desktop behavior without requiring an installed Windows service.
- Keep same-fork peers compatible with each other.
- Retain useful modern MWB fixes from upstream PowerToys.
- Remove runtime and build-time dependence on unrelated PowerToys modules.
- Keep the MWB-only repository small enough to understand and modify without cloning/building all of PowerToys.

## Deliberate fork behavior

### Friendlier shared keys

Upstream PowerToys MWB requires at least 16 characters and its generator produces characters in a repeating lowercase / uppercase / digit / symbol class sequence. This fork intentionally changes that UX:

- users may type their own key;
- minimum accepted length is 4 characters;
- short custom keys are allowed even though they are easier to guess;
- generated keys are 12 characters long, providing about 59.5 bits of entropy from this 31-character alphabet;
- generated characters come from `abcdefghjkmnpqrstuvwxyz23456789` to avoid ambiguous characters and keyboard-layout-hostile punctuation;
- every generated position is selected independently with `RandomNumberGenerator` instead of following a character-class formula.

The underlying encryption remains the modern PowerToys-era implementation: AES-256 keys are derived using PBKDF2-SHA512 and encrypted connections use fresh random salt/IV material. A short human-chosen secret is still less resistant to guessing; the fork simply lets the user make that tradeoff.

### Green classic branding

This fork deliberately keeps the recognizable **classic MWB tray/title-bar icon shape** but changes its orange accent to green. The purpose is practical: a machine running this fork should be visually distinguishable from an old Microsoft build at a glance.

`App/ClassicGreen.svg` reproduces the original 32×32 artwork pixel-for-pixel; the transparent grid, black edging, and pale highlights are unchanged, while only the orange pixels are mechanically shifted to green. `App/ClassicGreen.ico` contains nine nearest-neighbor sizes from 16 through 256 pixels. The ICO is embedded into the EXE, and the title-bar and tray icons derive from it at runtime. The old `App/Icon/notify_default.bmp` remains only as the canonical shape reference.

## Extraction strategy

PowerToys-only dependencies were removed incrementally, with compile/test checkpoints after meaningful changes so useful MWB behavior stayed intact. The portable product deliberately omits installed-service UAC/sign-in-screen support, but retains normal-desktop clipboard and file-transfer behavior.

The portable source is promoted to both the repository root and its `main` branch. Stable, Release Candidate, and Test tags preserve recovery points, while upstream comparisons use the recorded Microsoft PowerToys commit identifiers directly. Unrelated PowerToys files no longer appear in the product branch.

## Upstream synchronization

The extraction began from PowerToys commit `becc96f59cf18f3128fedbd6856a5248104216dd` (August 14, 2026). An audit of Microsoft PowerToys `main` on September 3, 2026 found one newer commit affecting MWB: [`103d376`](https://github.com/microsoft/PowerToys/commit/103d376c0a987cf350d4594bb3f8d71282fddfd6).

That update is now incorporated. It makes received-file writes transactional, rejects overlapping receives safely, validates the final byte count, cleans up incomplete partial files, preserves an existing destination if a replacement transfer fails, and makes elevated-user impersonation cleanup exception-safe. The portable build benefits from the general transfer protections; its service-only branch remains unused because this edition does not install a service.

See `UPSTREAM_SYNC.md` for the durable audit marker, intentional divergences, and the process for reviewing future PowerToys changes.

## Dependency status

### `PowerToys.Interop` — removed

MWB used it only for named event constants. The exact event-name literals are retained locally so existing internal behavior is preserved.

### `PowerToys.GPOWrapper` — external project removed

`Core/GpoCompatibility.cs` supplies the policy API MWB still expects without carrying the native PowerToys GPO project into the portable package.

### `Settings.UI.Library` — external project removed

MWB-specific settings models/storage/helper behavior now compile from MWB-local compatibility files. The upstream namespace/API shape is temporarily retained in places to keep the fork diff manageable while extraction is underway.

### `ManagedCommon` / PowerToys telemetry — external runtime dependency removed

`Core/PowerToysRuntimeCompatibility.cs` provides the tiny pieces MWB still calls. Logging is local to MWB and Microsoft PowerToys telemetry calls resolve to a no-op implementation in this fork.

### PowerToys build infrastructure — removed from portable projects

The portable app and test projects own their target framework, package versions, build properties, and output layout at the repository root. The focused CI workflow verifies the clean repository shape, builds the app and tests, runs the unit tests, and publishes the one-file product.

The older PowerToys-shaped project files, native module interface, helper/service comparison projects, and service executable source are no longer present on the product branch.

### Single-file helper integration — added

The app starts a hidden second copy of `MouseWithoutBorders.exe` in clipboard-helper mode. This preserves the existing helper IPC and clipboard design without shipping a second executable. The self-install mode copies only the EXE and adjacent prefs file, creates a Start Menu shortcut, offers a desktop shortcut by default, and optionally adds a per-user startup entry. A portable copy can invoke the same install flow later from Settings; current preferences are synchronously saved and copied first, the source prefs are removed only after the old process exits, and the installed EXE is then launched.

The x64 EXE is roughly 88 MB (about 84 MiB) because it is published as a self-contained .NET 10 application. It includes the .NET runtime, Windows Forms desktop assemblies, and required native runtime components inside the single-file bundle, avoiding a separate .NET installation on the destination computer. The majority of the download size is this bundled platform rather than MWB application code.

The portable product does not contain, install, or launch a Windows service. Protected UAC prompts and the Windows sign-in screen are intentionally outside the portable edition's supported behavior.

## Work order

1. ~~Remove narrow PowerToys runtime dependencies.~~
2. ~~Isolate/replace enterprise-policy access.~~
3. ~~Bring required MWB settings contracts/persistence into the MWB project.~~
4. ~~Neutralize PowerToys runner/telemetry runtime coupling.~~
5. ~~Remove shared PowerToys MSBuild/package/build-tree dependencies from the standalone projects.~~
6. ~~Make the app and tests build from an MWB-only directory tree.~~
7. ~~Rename executable identities used by portable builds and update matching references together.~~
8. ~~Publish the app as one self-contained EXE with adjacent portable preferences and optional per-user self-install.~~
9. **Finish real-Windows validation of first launch, self-install, startup, self-uninstall, and Windows firewall prompting.**
10. Complete a basic two-machine regression pass: input switching, clipboard, file transfer, reconnect, and sleep/wake behavior.
11. ~~Promote the portable source to the root and remove the temporary PowerToys comparison tree.~~

## AI-assisted development

The custom extraction/modification work is performed through ChatGPT coding sessions using the repository owner's authenticated GitHub connection. GitHub therefore displays the owner's account on repository writes even when the owner did not manually author the edit.

AI-assisted commits use:

`Assisted-by: ChatGPT (GPT-5.6 Sol)`

## Compatibility rule

Unless intentionally changed, protocol constants, named IPC objects, settings migration behavior, and network/file-transfer semantics should remain aligned with the modern PowerToys MWB implementation.

Do **not** assume compatibility with the old Garage standalone `2.2.1.0327`; Microsoft has changed the MWB implementation/protocol since that generation. During testing, use the same fork/current-generation build on all connected machines unless mixed-version compatibility has been explicitly verified.

## RC5 transfer candidate

Independent, resumable drag/drop transfers with per-file controls, restart recovery, and Explorer folder targeting. Install RC5 on BOTH PCs. This is a release candidate: automated Windows tests do not replace the two-PC checks below.

- One transfer window per PC, with a progress bar and Pause/Resume, Move to end, Cancel, and Retry for each file. Up to four outgoing files run concurrently; additional files wait. Each PC also accepts up to four incoming files.
- Move to end defers that file until the other runnable work finishes; explicitly deferred files then run in order. Paused files stay paused. New drags join the same list.
- Keep checked partial data when paused or interrupted. Verify each chunk, the resume prefix, and the complete file with SHA-256. Require a saved acknowledgement and retain completion receipts to avoid duplicate copies after a lost acknowledgement.
- Retry transient connection interruptions twice automatically, then leave an actionable error. Pending peer commands no longer block the transfer scheduler.
- Save transfer recovery beside the EXE. After app or PC restart, unfinished transfers are paused and a popup offers to open the list; nothing resumes automatically. Reopen the list from the File transfers tray item.
- Drop over an Explorer folder window to target that folder. Otherwise create/open Desktop\MouseWithoutBorders immediately when dropped. Preserve existing files by choosing an unused destination name.
- Replace the small drag indicator with a larger Windows file-type image and filename/file-count preview on the receiving screen. This is a custom preview using Windows icons, not Explorer thumbnail rendering.
- Use full transfer speed without speed choices. Bring the transfer list forward for newly added files, avoid focus changes on progress ticks, and close it after all visible entries complete successfully.

### Recovery and limits

- RC5 drag/drop uses a fork-specific protocol; use RC5 on both PCs. Mouse/keyboard packets and the existing underlying encryption are unchanged.
- Full-speed transfers can still cause Wi-Fi mouse lag. Initial source hashing, resume-prefix checks, and final verification can take time on large files.
- No folder transfers, Explorer virtual-folder support, or native cross-PC OLE drag session. Explorer window targeting, Windows 11 tabs, focus, and cursor behavior require real-PC validation.
- Legacy clipboard file copy/paste retains its existing single-file transport; the new list, resume, and checksums apply to drag/drop.
- Recovery uses MouseWithoutBorders.transfers.json and its backup beside the EXE, plus hidden partial files in the destination. Do not delete these while you want to resume. Inactive partials older than 30 days are discarded during recovery; those jobs restart from zero if resumed.
- Limits: 256 files per drag, 4096 retained job/receipt entries, and a 16 MiB recovery journal. Clear finished hides completed rows but retains receipts for duplicate protection.
- Closing the transfer window lets copying continue; exiting MWB pauses unfinished jobs. A file already being committed may complete while cancellation is requested. No installed service or protected UAC/sign-in support.

### Two-PC acceptance checks

1. Exit MWB on both PCs. Back up your EXE/preferences, replace only the EXE, and confirm About shows 1.0.1-rc.5 on both. Keep the EXE in its existing folder so recovery files stay beside it.
2. Drag six disposable files, including large files, an empty file, and a duplicate filename. Confirm one list on each PC, four active outgoing files at most, individual progress bars, preserved duplicates, and intact received contents.
3. Pause one large file from each PC in separate attempts. Other files should continue. Resume should reuse its partial data. Move one to end and confirm it waits for the other runnable work. Cancel one and confirm the others complete.
4. Disconnect Wi-Fi during a large copy, then reconnect. Check the automatic retries and manual Retry if needed. Confirm completion does not create a second copy after reconnect.
5. Exit and restart MWB during a transfer. Expect the recovery popup and paused entries; no copying should start until you explicitly resume. Repeat after restarting both PCs.
6. Drop over an Explorer folder window and verify the destination. Drop elsewhere and confirm Desktop\MouseWithoutBorders opens immediately. Check drag feedback when crossing screens quickly and slowly.
7. Add another drag while copying and while the transfer list is minimized. Check both lists restore for new files, typing does not cause cursor flashing or repeated focus stealing, and successful lists close automatically. Check that paused/error entries remain visible.
8. Recheck mouse/keyboard switching, text/image clipboard, single-file clipboard copy/paste, reconnect, lock/unlock, and sleep/wake.
