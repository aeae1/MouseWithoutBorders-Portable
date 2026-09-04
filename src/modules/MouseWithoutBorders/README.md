<p align="center">
  <img src="App/ClassicGreen.svg" width="180" alt="Green Mouse Without Borders portable icon">
</p>

<h1 align="center">Mouse Without Borders — Portable</h1>

<p align="center">
  <strong>One portable EXE. Preferences beside it. No PowerToys installation.</strong><br>
  An unofficial, ChatGPT-assisted portable fork of Microsoft's open-source Mouse Without Borders.
</p>

> [!IMPORTANT]
> This project is in prerelease testing. Automated Windows builds and tests pass, but first-launch, installation, and two-computer behavior still need continued real-hardware validation.

## Highlights

- One self-contained x64 `MouseWithoutBorders.exe`.
- `MouseWithoutBorders.prefs.json` stays beside the EXE.
- First launch offers **Install for me** or **Run portable here**.
- Per-user installation defaults to `%LOCALAPPDATA%\Programs\Mouse Without Borders` and does not require administrator access.
- Optional Start with Windows support, off by default.
- Optional desktop shortcut, on by default when installing.
- Clipboard-helper behavior is folded into a hidden mode of the same EXE.
- Mouse, keyboard, clipboard, file-transfer, drag/drop, layout, reconnect, and modern encryption remain in scope for normal desktops.
- Microsoft's September 2, 2026 incoming-file safety update is included.
- PowerToys runtime dependencies and telemetry are removed from the portable build.
- Local diagnostic logging rolls at 5 MB and keeps one previous file rather than growing forever.
- The original classic MWB pixel symbol is mechanically recolored green and shared by the EXE, title bars, tray, and repository page.

## Downloading

During development, builds are published on the project's [Releases page](https://github.com/aeae1/MouseWithoutBorders-Portable/releases). Download `MouseWithoutBorders.exe` from the newest test release's **Assets** section and use that same release on every connected computer.

The adjacent `.sha256` file is an optional checksum. A release asset is attached only after the tagged source passes the Windows build, isolated-source build, unit tests, and one-file packaging checks.

## First launch

If no prefs file exists beside the EXE, MWB offers:

- **Install for me** — copies the EXE to a chosen per-user folder, creates its adjacent prefs file and Start Menu shortcut, offers a desktop shortcut by default, and optionally enables Start with Windows.
- **Run portable here** — creates the prefs file in the current folder and continues without installation.
- **Cancel** — exits without installing or starting MWB.

After either run choice, the portable app opens the machine matrix directly instead of launching the old blue setup wizard. The generated key is visible on that first screen: use the same case-sensitive key on each computer, check an empty tile, and enter the other computer's Windows name. Applying the layout rejects blank or duplicate checked names, immediately saves an edited key to the adjacent prefs file before reconnecting, and reports whether each tile is waiting, connecting, connected, mismatched, timed out, or disconnected.

Choosing portable mode is not permanent. The **Portable** tab in Settings can install the currently configured copy later. MWB saves and moves the existing prefs after the running process closes, then restarts from the installed folder with the same key, machine layout, and options.

The app does not install a Windows service. Protected UAC prompts and the Windows sign-in screen are therefore intentionally unsupported; normal desktop operation remains the target.

New preference files default **Wrap mouse** to off. Users can still enable it under **Other Options**, and upgrades preserve whatever value is already saved.

## Fork-specific behavior

### Security keys

- Users may enter their own key.
- The minimum accepted custom key length is four characters.
- Generated keys are twelve random characters from `abcdefghjkmnpqrstuvwxyz23456789`.
- Easily confused characters and keyboard-layout-sensitive punctuation are excluded.
- Longer random secrets remain safer than short human-chosen keys.
- Keys do not expire automatically, and the portable build does not periodically demand a generated replacement.

### Mini Log

The **Mini Log** link opens a resizable/maximizable **Diagnostic Log** in a separate modeless window, so the main Settings window remains clickable while it is open. It includes the version, run mode, EXE/preferences paths, Windows and runtime information, process details, machine/configuration/connection snapshot, key length/checksum, and the most recent bounded portion of the on-disk program log. Its text is scrollable and selectable, and **Copy all** copies it only when requested; merely opening the window does not replace the current clipboard contents. Reopening Mini Log refreshes and focuses the existing viewer. The actual shared key is redacted. Review before sharing because computer names, IP addresses, and local paths can appear.

### Portable settings

The only product settings file is:

`MouseWithoutBorders.prefs.json`

It lives beside the currently running EXE. Do not publish it: it contains the shared security key used by your connected computers.

### Green portable branding

`App/ClassicGreen.svg` reproduces the original 32×32 classic artwork on its exact pixel grid; only the orange pixels were mechanically recolored green. `App/ClassicGreen.ico` contains nearest-neighbor 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixel versions. The portable EXE, title bars, and tray all derive from that embedded ICO.

The old `App/Icon/notify_default.bmp` is retained as the canonical legacy-shape reference, and `App/Logo.ico` remains for upstream/legacy comparison builds. Neither is loaded as the portable runtime icon.

## Building from source

Prerequisites:

- Windows 10 version 2004 or newer;
- Visual Studio Build Tools with MSBuild and the .NET desktop workload;
- .NET 10 SDK.

From a Visual Studio Developer PowerShell window in this directory:

```powershell
.\build.ps1 -Configuration Release -Platform x64 -RunTests
```

To publish the one-file application:

```powershell
.\publish-portable.ps1 -Configuration Release -Platform x64 -Destination .\portable
```

## Project status

The extraction is approximately **85% complete**. The MWB folder builds independently, and CI already packages it as a clean source archive. Remaining work is primarily real-Windows validation and the final move into the dedicated `aeae1/MouseWithoutBorders` repository.

See:

- [Detailed portable extraction status](Standalone/README.md)
- [Development and compatibility guide](Standalone/DEVELOPMENT.md)
- [Upstream synchronization record](Standalone/UPSTREAM_SYNC.md)
- [AI-assistance disclosure](Standalone/AI_ASSISTANCE.md)
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)
- [Code of Conduct](CODE_OF_CONDUCT.md)

## License and attribution

This work is derived from Microsoft PowerToys Mouse Without Borders and remains under the [MIT License](LICENSE). Microsoft and the original contributors retain attribution for upstream work. This fork is not affiliated with or endorsed by Microsoft.
