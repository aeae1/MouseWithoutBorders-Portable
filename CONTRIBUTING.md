# Contributing to Mouse Without Borders — Standalone

Thanks for helping improve this unofficial standalone Mouse Without Borders fork.

## Before opening an issue

Please search existing issues first. For a bug, include:

- the release/tag and Windows version you tested;
- whether you chose portable or installed mode;
- whether one or multiple computers are affected;
- exact steps that reproduce the problem;
- what you expected and what happened instead;
- relevant logs with machine names, security keys, IP addresses, and other private information removed.

Mouse/keyboard, clipboard, file-transfer, reconnect, sleep/wake, startup, and first-launch bugs are especially useful when tested on two physical Windows computers.

## Proposing a change

Open an issue before a large change so its behavior and compatibility impact can be discussed. Pull requests should be focused and should preserve the existing wire protocol unless a coordinated protocol change is intentional.

The product goals are:

- one self-contained Windows EXE;
- no PowerToys runtime requirement;
- preferences beside the EXE;
- normal-desktop mouse, keyboard, clipboard, and file-transfer reliability;
- no installed service or protected-desktop support;
- a small, understandable MWB-only source tree.

## Building and testing

The active project is under `src/modules/MouseWithoutBorders` while extraction is completed. See its [development guide](src/modules/MouseWithoutBorders/Standalone/DEVELOPMENT.md) for prerequisites and commands.

Before submitting a pull request:

1. Build x64 Release.
2. Run the MWB unit tests.
3. Confirm the portable publish contains exactly one EXE.
4. Manually test behavior affected by the change.
5. For networking, input, clipboard, or file-transfer changes, test between two physical Windows computers.

## Attribution

Keep existing Microsoft and upstream copyright/license notices intact. Clearly document substantial fork-specific behavior changes. AI-assisted contributions are welcome when the contributor reviews the result, explains the change, and includes meaningful validation.
