# Contributing to Mouse Without Borders — Portable

Bug reports, real-PC testing, documentation improvements, and focused code contributions are welcome.

For bugs, include the tested release/tag, Windows version, portable or installed mode, number of affected computers, reproduction steps, expected result, and actual result. Remove security keys, machine names, IP addresses, clipboard contents, and other private information from logs or screenshots.

Please discuss large changes before implementation. Preserve the existing wire protocol unless a coordinated protocol change is intentional, and keep the one-EXE/no-PowerToys/no-service product goals intact.

Before submitting code:

1. Run `.\build.ps1 -Configuration Release -Platform x64 -RunTests`.
2. Confirm the portable publish contains exactly one EXE.
3. Manually test affected behavior.
4. Test networking, input, clipboard, or file-transfer changes between two physical Windows computers.

See [Standalone/DEVELOPMENT.md](Standalone/DEVELOPMENT.md) for the detailed development and compatibility rules. Keep existing Microsoft/upstream copyright and license notices intact, and clearly document substantial fork-specific behavior.
