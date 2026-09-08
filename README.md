# Brightly Tray - Monitor Brightness Control

A lightweight Windows tray app for controlling brightness on both the laptop's built-in
display and external monitors — including monitors that don't support real hardware
brightness control.

## Why this exists

Most third-party brightness tools only handle one of two mechanisms:

- **WMI** (`WmiMonitorBrightnessMethods`) — controls the laptop's internal panel. This is
  the same mechanism Windows' own brightness slider uses.
- **DDC/CI** (VESA Monitor Control Command Set, via the Windows Monitor Configuration API)
  — controls external monitors over HDMI/DisplayPort/USB-C. Support varies a lot by
  monitor, cable, and dock, which is the most common reason "brightness apps don't work."

This app detects which mechanism applies to each connected display automatically, and if a
monitor advertises DDC/CI but the calls fail or brightness isn't actually supported, it
falls back to a gamma-ramp software dimmer (clamped to a 10% floor) instead of silently
doing nothing.

**HDR monitors are a third case.** Once a display has HDR/Advanced Color turned on, monitor
firmware commonly ignores or clamps DDC/CI brightness commands entirely — this is why
brightness control "stops working" specifically in HDR mode with most tools. Windows itself
works around this with a separate "SDR content brightness" control (the extra slider that
appears in Settings > Display when HDR is on), which changes SDR tone-mapping instead of
talking to the monitor. This app detects per-display HDR state via the CCD API and
automatically switches to that same mechanism whenever HDR is active, then switches back to
normal DDC/CI automatically once HDR is turned off. (Note: the get side of this is officially
documented by Microsoft; the set side is not, and was reverse-engineered by the community —
see `Interop/DisplayConfigInterop.cs` for details. It's verified working end-to-end here, but
it's the one part of this app resting on an undocumented API.)

## Features

- Native Windows 11 look: Mica window backdrop, Fluent 2 slider/typography, and automatic
  light/dark theme matching (via [WPF-UI](https://github.com/lepoco/wpfui)) — not a flat
  fixed-color popup skinned to look dark
- System tray icon with a popup slider panel (one slider per detected display), using each
  monitor's real EDID model name rather than a generic "Generic PnP Monitor" label
- Automatic WMI / DDC-CI / software-gamma / HDR-SDR-white-level detection per monitor
- Global hotkeys: `Ctrl+Alt+Up` / `Ctrl+Alt+Down` adjust all displays by a configurable step
  (1/5/10/15%, set from the tray menu)
- Per-monitor brightness is remembered across restarts and monitor reconnects — but a
  refresh of an already-tracked monitor trusts its live value instead of fighting a
  brightness change you made another way (monitor buttons, Windows Settings)
- "Start with Windows" toggle in the tray menu
- Debounced hardware writes so dragging a slider doesn't flood a monitor with DDC/CI
  commands (a common cause of laggy/unresponsive sliders in other tools)
- DDC/CI reads and writes retry automatically with a settle delay — cheap monitor
  controllers commonly drop a command sent right after another; a monitor that still won't
  respond after retries is flagged "Not responding" in the flyout instead of silently doing
  nothing
- Automatically re-detects displays on hotplug/resolution changes

## Build & run

Requires the .NET 8 SDK.

```
dotnet build
dotnet run --project src/MonitorBrightness/MonitorBrightness.csproj
```

## Publish a standalone .exe

No .NET install required on the target machine:

```
dotnet publish src/MonitorBrightness/MonitorBrightness.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish
```

The result is `publish/MonitorBrightness.exe` — copy it anywhere and run it.

## Notes / limitations

- Software (gamma) dimming only affects rendered pixel brightness, not the actual
  backlight, so it can't reduce power draw and won't go fully dark.
- Matching a WMI panel to its DDC/CI identity (to avoid listing the same laptop screen
  twice) is done via EDID-derived hardware IDs; in rare cases a screen could still show up
  twice if that correlation fails — use "Refresh monitors" in the tray menu if so.
- Settings are stored at `%AppData%\MonitorBrightness\settings.json`.
- `StartupService` supports both a plain portable exe (classic `HKCU...\Run` registry key)
  and an MSIX-packaged build (`Windows.ApplicationModel.StartupTask`, since packaged apps
  have that registry write virtualized and never actually seen by the real startup
  mechanism) — it detects which one it's running as and uses the right mechanism
  automatically. This is why the project targets `net8.0-windows10.0.19041.0` rather than
  plain `net8.0-windows` — the versioned Windows SDK TFM is what makes the `StartupTask`
  WinRT API resolve at all.
