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
- PowerToys-free app/helper/service projects: **added**. Each standalone executable has a clean `MouseWithoutBorders*` name and identity.
- Shared PowerToys MSBuild/package infrastructure: **removed from the standalone build path**.
- MWB-only build proof: **passing**. CI copies only this directory to an isolated location, builds all three programs there, and runs the unit tests there.
- Downloadable Windows x64 development bundle: **added** to successful workflow runs.
- Final installer and real-machine regression tests: **not finished yet**.

## Goals

- Preserve keyboard and mouse sharing behavior.
- Preserve clipboard sharing and **file transfer**.
- Preserve service/UAC/logon-desktop support.
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

## Extraction strategy

Remove PowerToys-only dependencies incrementally, compile/test after each meaningful change, and keep useful MWB behavior intact. Do not simplify the project by deleting functionality such as file transfer or service mode merely because it is complicated.

Once the MWB app/helper/service/tests can build without the surrounding PowerToys tree, move only the required files into the clean final repository `aeae1/MouseWithoutBorders` and leave unrelated PowerToys files/history behind.

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

The standalone app, helper, service, and test projects now own their target framework, package versions, build properties, and output layout inside the MWB directory. The focused CI workflow proves this by archiving only `src/modules/MouseWithoutBorders`, extracting it outside the PowerToys checkout, building all four projects, and running the tests in that isolated copy.

The older PowerToys-shaped project files remain temporarily as an upstream comparison and compatibility check. They are not needed in the final MWB-only repository.

### Service/helper integration — standalone builds added, installer pending

The helper and service now have standalone project files and clean executable identities. The existing session/UAC/logon mechanics remain in place. Final Windows service registration, permissions, firewall rules, and uninstall behavior belong in the installer and still need end-to-end testing.

## Work order

1. ~~Remove narrow PowerToys runtime dependencies.~~
2. ~~Isolate/replace enterprise-policy access.~~
3. ~~Bring required MWB settings contracts/persistence into the MWB project.~~
4. ~~Neutralize PowerToys runner/telemetry runtime coupling.~~
5. ~~Remove shared PowerToys MSBuild/package/build-tree dependencies from the standalone projects.~~
6. ~~Make app + helper + service + tests build from an MWB-only directory tree.~~
7. ~~Rename executable/service identities used by standalone builds and update matching references together.~~
8. Create the clean `aeae1/MouseWithoutBorders` repository with only required source/assets/tests/build files.
9. **Package and validate a standalone installer; the portable CI bundle is already available for development testing.**
10. Real-machine regression testing: input switching, clipboard, file transfer, reconnect, UAC/logon/service behavior.

## AI-assisted development

The custom extraction/modification work is performed through ChatGPT coding sessions using the repository owner's authenticated GitHub connection. GitHub therefore displays the owner's account on repository writes even when the owner did not manually author the edit.

AI-assisted commits use:

`AI-Assisted-By: aeae1's vibe coding assistant — ChatGPT GPT-5.6 Sol`

## Compatibility rule

Unless intentionally changed, protocol constants, named IPC objects, settings migration behavior, and network/file-transfer semantics should remain aligned with the modern PowerToys MWB implementation.

Do **not** assume compatibility with the old Garage standalone `2.2.1.0327`; Microsoft has changed the MWB implementation/protocol since that generation. During testing, use the same fork/current-generation build on all connected machines unless mixed-version compatibility has been explicitly verified.
