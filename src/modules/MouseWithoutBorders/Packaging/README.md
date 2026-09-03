# Installing this development build

This package uses **manual run mode**:

- Mouse Without Borders does not start automatically with Windows.
- Installing it does not launch the app.
- Open **Mouse Without Borders** from the Start menu whenever you want to use it.
- The optional Windows support service is registered as demand-start and remains stopped unless service mode is explicitly requested.

## Install

1. Extract the entire ZIP to a normal folder.
2. Double-click `Install.cmd`.
3. Approve the Windows administrator prompt.
4. When installation finishes, open **Mouse Without Borders** from the Start menu.

The installer copies the package to `C:\Program Files\Mouse Without Borders`, adds the required inbound TCP firewall rule, registers the demand-start support service, and creates Start menu shortcuts. It deliberately creates no automatic-start entry.

## Uninstall

Use **Installed apps** in Windows Settings, choose **Uninstall Mouse Without Borders** from the Start menu, or double-click `Uninstall.cmd` in the installation directory.

Uninstalling removes the program files, shortcuts, firewall rule, and support service. It preserves your saved settings so an upgrade or later reinstall does not erase your machine layout and security key.

## Development warning

This is an unsigned development build. Windows may show a security warning. Do not redistribute it as a finished release until the two-computer behavior and service-mode tests listed in the main README have passed.
