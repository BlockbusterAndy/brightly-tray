# Contributing to Brightly Tray

Thanks for considering a contribution. This is a small utility app, so the process is
intentionally lightweight.

## Before you start

For anything beyond a small fix (typos, obvious bugs), please open an issue first to
discuss the change. This avoids wasted work on a PR that doesn't fit the project's
direction, especially for UI changes or new brightness-control mechanisms.

## Development setup

Requires the .NET 8 SDK on Windows (this app uses WMI, DDC/CI, and the CCD API, all of
which are Windows-only, so it can't be built or run on Linux/macOS).

```bash
dotnet build
dotnet run --project src/MonitorBrightness/MonitorBrightness.csproj
```

Run from a checkout with a real (or virtual) display attached — there's no test harness
that mocks monitor hardware, so manual verification against your own displays is how
changes get checked. If you have both a laptop panel and an external monitor available,
please test against both; if you have an HDR-capable display, exercising the HDR/SDR
switch in `DisplayConfigService` is especially valuable since that path is hardest to
cover otherwise.

## Code style

- Match the existing style in the file you're editing (nullable reference types are
  enabled project-wide, file-scoped namespaces, expression-bodied members where they
  read cleanly).
- Keep hardware interop (`Interop/`) and business logic (`Services/`) separated the way
  they are now — don't call Win32/WMI/COM directly from UI code.
- Prefer small, focused PRs over broad ones. If a change touches multiple unrelated
  things, please split it up.

## Submitting a PR

1. Fork the repo and create a branch off `master`.
2. Make your change, and confirm `dotnet build` succeeds.
3. Test the affected behavior manually on real hardware (see above) and describe what
   you tested in the PR description — screen/monitor model, HDR on/off, etc. is helpful
   context for reviewers who can't reproduce your exact setup.
4. Open the PR against `master` with a clear description of what changed and why.

## Reporting bugs

Please include:

- Windows version
- Laptop/monitor model(s) and connection type (HDMI/DisplayPort/USB-C/dock)
- Whether HDR is enabled on the affected display
- Steps to reproduce, and what you expected vs. what happened

## Reporting security issues

Please do not open a public issue for a security vulnerability. See
[SECURITY.md](SECURITY.md) for how to report it privately.
