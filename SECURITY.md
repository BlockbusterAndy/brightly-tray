# Security Policy

Brightly Tray is a local desktop utility — it doesn't talk to the network, doesn't
collect telemetry, and doesn't handle user credentials or remote data. Most of its
attack surface is Win32/WMI/COM interop and local settings file parsing.

## Reporting a vulnerability

If you find a security issue (e.g. something exploitable via a malicious settings file,
or unsafe handling of data from monitor EDID/DDC responses), please report it privately
rather than opening a public issue:

- Use GitHub's [private vulnerability reporting](../../security/advisories/new) for this
  repository, or
- Email the maintainer directly (see the GitHub profile for contact info).

Please include reproduction steps and the affected version/commit. We'll aim to
acknowledge reports within a few days.
