# Security Policy

Mouse Without Borders handles keyboard/mouse input, clipboard contents, files, and network connections between trusted computers. Please treat potential vulnerabilities carefully.

## Supported versions

Only the newest GitHub prerelease/release of this standalone fork is supported. Older test builds may contain known defects and should not be used to evaluate whether a security issue is still present.

## Reporting a vulnerability

Do not publish an unpatched vulnerability, security key, IP address, private log, or proof-of-concept exploit in a public issue.

Use GitHub's private **Report a vulnerability** option on the repository's **Security** tab when available. If private reporting is unavailable, contact the repository owner privately through GitHub before disclosing technical details.

Please include:

- affected version/tag and commit, if known;
- affected Windows version and configuration;
- whether portable or installed mode was used;
- clear reproduction steps;
- likely impact;
- a minimal proof of concept, if safe;
- any suggested mitigation.

This is a small, unofficial community fork. No response-time guarantee or bug-bounty program is offered, but good-faith reports will be investigated as capacity allows.

## Scope notes

- This edition intentionally supports normal interactive Windows desktops only.
- It does not install a service or support protected UAC/sign-in desktops.
- The adjacent prefs file contains configuration needed by MWB. Protect the folder and do not publish the file because it includes the shared security key.
- Use MWB only between computers and networks you trust.
