# Mouse Without Borders standalone extraction

This branch is producing a standalone Windows build of Mouse Without Borders from the actively maintained PowerToys-era source while preserving the useful MWB behavior and minimizing unrelated PowerToys baggage.

## Current status

- Direct `PowerToys.Interop` dependency: **removed**.
- Native C++/WinRT `PowerToys.GPOWrapper` project dependency: **removed from the MWB app, helper, and service**; MWB has a small local managed compatibility implementation instead.
- `Settings.UI.Library` project dependency: **removed**; the subset MWB needs now lives with MWB as local compatibility code.
- `ManagedCommon` / PowerToys telemetry runtime project dependencies: **removed/replaced locally**. PowerToys telemetry is intentionally a no-op in this standalone fork.
- PowerToys-runner lifetime coupling: **neutralized for standalone operation**.
- Classic standalone tray/settings UI: **forced on** without requiring the separate PowerToys Settings process.
- Focused Windows x64 build/test workflow: **added** at `.github/workflows/mwb-standalone-build.yml`.
- Human-friendly shared-key policy: **added**. User-chosen keys of 4+ characters are accepted; generated keys are 10 cryptographically random easy-to-type characters.
- Classic visual branding: **added**. The old orange MWB tray/title-bar design is green in this fork.
- PowerToys-free app/helper/service comparison projects: **added**. The distributed portable build now combines the app and clipboard helper into one executable.
- Shared PowerToys MSBuild/package infrastructure: **removed from the standalone build path**.
- MWB-only build proof: **passing**. CI copies only this directory to an isolated location, builds all three programs there, and runs the unit tests there.
- Downloadable single-file Windows x64 build: **added** to successful workflow runs.
- Clean standalone source archive: **added**. PowerToys-only project files and the native module interface are excluded automatically.
- Portable first launch and per-user self-install: **added**. Preferences stay beside the EXE; installation defaults to the current user's local Programs folder and Start with Windows is optional.
- Upstream MWB audit through September 3, 2026: **completed**. Microsoft's September 2 receive-safety improvements are incorporated and documented in `UPSTREAM_SYNC.md`.
- Real-Windows portable/self-install and two-machine regression tests: **not finished yet**.

## Goals

- Preserve keyboard and mouse sharing behavior.
- Preserve clipboard sharing and **file transfer**.
- Preserve normal-desktop behavior without requiring an installed Windows service.
- Keep same-fork peers compatible with each other.
- Retain useful modern MWB fixes from upstream PowerToys.
- Remove runtime and build-time dependence on unrelated PowerToys modules.
- Finish with an MWB-only repository that is small enough to understand and modify without cloning/building all of PowerToys.

## Deliberate fork behavior

### Friendlier shared keys

Upstream PowerToys MWB requires at least 16 characters and its generator produces characters in a repeating lowercase / uppercase / digit / symbol class sequence. This fork intentionally changes that UX:

- users may type their own key;
- minimum accepted length is 4 characters;
- short custom keys are allowed even though they are easier to guess;
- generated keys are 10 characters long;
- generated characters come from `abcdefghjkmnpqrstuvwxyz23456789` to avoid ambiguous characters and keyboard-layout-hostile punctuation;
- every generated position is selected independently with `RandomNumberGenerator` instead of following a character-class formula.

The underlying encryption remains the modern PowerToys-era implementation: AES-256 keys are derived using PBKDF2-SHA512 and encrypted connections use fresh random salt/IV material. A short human-chosen secret is still less resistant to guessing; the fork simply lets the user make that tradeoff.

### Green classic branding

This fork deliberately keeps the recognizable **classic MWB tray/title-bar icon shape** but changes its orange accent to green. The purpose is practical: a machine running this fork should be visually distinguishable from an old standalone or Microsoft build at a glance.

The standalone build has one replaceable artwork source: `App/ClassicGreen.ico`. That file is embedded into the EXE, and the title-bar and tray icons are derived from the embedded EXE icon at runtime. The old `App/Icon/notify_default.bmp` and `App/Logo.ico` remain only for upstream/legacy project compatibility and do not control the standalone branding.

## Extraction strategy

Remove PowerToys-only dependencies incrementally, compile/test after each meaningful change, and keep useful MWB behavior intact. The portable product deliberately omits installed-service UAC/sign-in-screen support, but it must retain normal-desktop clipboard and file-transfer behavior.

Once the single-file app and tests build without the surrounding PowerToys tree, move only the required files into the clean final repository `aeae1/MouseWithoutBorders` and leave unrelated PowerToys files/history behind.

## Upstream synchronization

The extraction began from PowerToys commit `becc96f59cf18f3128fedbd6856a5248104216dd` (August 14, 2026). An audit of Microsoft PowerToys `main` on September 3, 2026 found one newer commit affecting MWB: [`103d376`](https://github.com/microsoft/PowerToys/commit/103d376c0a987cf350d4594bb3f8d71282fddfd6).

That update is now incorporated. It makes received-file writes transactional, rejects overlapping receives safely, validates the final byte count, cleans up incomplete partial files, preserves an existing destination if a replacement transfer fails, and makes elevated-user impersonation cleanup exception-safe. The portable build benefits from the general transfer protections; its service-only branch remains unused because this edition does not install a service.

See `UPSTREAM_SYNC.md` for the durable audit marker, intentional divergences, and the process for reviewing future PowerToys changes.

## Dependency status

### `PowerToys.Interop` — removed

MWB used it only for named event constants. The exact event-name literals are retained locally so existing internal behavior is preserved.

### `PowerToys.GPOWrapper` — external project removed

`Core/GpoCompatibility.cs` supplies the policy API MWB still expects without carrying the native PowerToys GPO project into the standalone package.

### `Settings.UI.Library` — external project removed

MWB-specific settings models/storage/helper behavior now compile from MWB-local compatibility files. The upstream namespace/API shape is temporarily retained in places to keep the fork diff manageable while extraction is underway.

### `ManagedCommon` / PowerToys telemetry — external runtime dependency removed

`Core/PowerToysRuntimeCompatibility.cs` provides the tiny pieces MWB still calls. Logging is local to MWB and Microsoft PowerToys telemetry calls resolve to a no-op implementation in this fork.

### PowerToys build infrastructure — removed from standalone projects

The standalone app, helper, service, and test projects now own their target framework, package versions, build properties, and output layout inside the MWB directory. The focused CI workflow proves this by archiving only `src/modules/MouseWithoutBorders`, extracting it outside the PowerToys checkout, building the projects, running the tests, and publishing the one-file product in that isolated copy.

The older PowerToys-shaped project files remain temporarily as an upstream comparison and compatibility check. They are not needed in the final MWB-only repository.

### Single-file helper integration — added

The app starts a hidden second copy of `MouseWithoutBorders.exe` in clipboard-helper mode. This preserves the existing helper IPC and clipboard design without shipping a second executable. The self-install mode copies only the EXE and adjacent prefs file, creates a Start menu shortcut, and optionally adds a per-user startup entry.

The service source remains available for upstream comparison, but the portable product does not install or launch a Windows service. Protected UAC prompts and the Windows sign-in screen are intentionally outside the portable edition's supported behavior.

## Work order

1. ~~Remove narrow PowerToys runtime dependencies.~~
2. ~~Isolate/replace enterprise-policy access.~~
3. ~~Bring required MWB settings contracts/persistence into the MWB project.~~
4. ~~Neutralize PowerToys runner/telemetry runtime coupling.~~
5. ~~Remove shared PowerToys MSBuild/package/build-tree dependencies from the standalone projects.~~
6. ~~Make app + helper + service + tests build from an MWB-only directory tree.~~
7. ~~Rename executable/service identities used by standalone builds and update matching references together.~~
8. ~~Publish the app as one self-contained EXE with adjacent portable preferences and optional per-user self-install.~~
9. Create the clean `aeae1/MouseWithoutBorders` repository with only required source/assets/tests/build files.
10. **Finish real-Windows validation of first launch, self-install, startup, self-uninstall, and Windows firewall prompting.**
11. Real-machine regression testing: input switching, clipboard, file transfer, reconnect, and sleep/wake behavior.

## AI-assisted development

The custom extraction/modification work is performed through ChatGPT coding sessions using the repository owner's authenticated GitHub connection. GitHub therefore displays the owner's account on repository writes even when the owner did not manually author the edit.

AI-assisted commits use:

`AI-Assisted-By: aeae1's vibe coding assistant — ChatGPT GPT-5.6 Sol`

## Compatibility rule

Unless intentionally changed, protocol constants, named IPC objects, settings migration behavior, and network/file-transfer semantics should remain aligned with the modern PowerToys MWB implementation.

Do **not** assume compatibility with the old Garage standalone `2.2.1.0327`; Microsoft has changed the MWB implementation/protocol since that generation. During testing, use the same fork/current-generation build on all connected machines unless mixed-version compatibility has been explicitly verified.
