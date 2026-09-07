using System.Collections.ObjectModel;
using MonitorBrightness.Models;
using Application = System.Windows.Application;

namespace MonitorBrightness.Services;

/// <summary>
/// Discovers every controllable display and routes brightness changes to the right backend
/// (WMI for the internal panel, DDC/CI for external monitors, gamma-ramp as a last resort),
/// debouncing hardware writes so dragging a slider doesn't flood a monitor with commands.
/// </summary>
public sealed class MonitorManager
{
    public ObservableCollection<MonitorDevice> Monitors { get; } = new();

    private readonly WmiBrightnessService _wmi = new();
    private readonly DdcBrightnessService _ddc = new();
    private readonly GammaBrightnessService _gamma = new();
    private readonly SettingsService _settingsService;

    private readonly Dictionary<string, CancellationTokenSource> _pendingWrites = new();
    private readonly object _writeLock = new();

    // Tracks which monitor ids we've already restored a saved brightness for this session, so a
    // manual change made outside the app (monitor buttons, Windows Settings) between refreshes
    // isn't silently overwritten — we only force a restore the first time we see an id (covers
    // app startup and monitor reconnect), not on every subsequent refresh.
    private readonly HashSet<string> _restoredThisSession = new();

    public event Action? Refreshed;

    public MonitorManager(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void RefreshAndPublish()
    {
        var dispatcher = Application.Current.Dispatcher;

        var oldSnapshot = dispatcher.Invoke(() => Monitors.ToList());

        // A slider drag or hotkey press right before a refresh could otherwise fire its debounced
        // write after this refresh has already destroyed the DDC handle it captured.
        lock (_writeLock)
        {
            foreach (var cts in _pendingWrites.Values) cts.Cancel();
            _pendingWrites.Clear();
        }

        Task.Run(() =>
        {
            foreach (var old in oldSnapshot)
            {
                if (old.Kind == MonitorControlKind.Ddc && old.DdcHandle != IntPtr.Zero)
                    _ddc.Destroy(old.DdcHandle);
                if (old.Kind == MonitorControlKind.GammaFallback && old.AdapterDeviceName is not null)
                    _gamma.Reset(old.AdapterDeviceName);
            }

            var newList = BuildMonitorList();

            dispatcher.Invoke(() =>
            {
                Monitors.Clear();
                foreach (var m in newList) Monitors.Add(m);
                Refreshed?.Invoke();
            });
        });
    }

    private List<MonitorDevice> BuildMonitorList()
    {
        var result = new List<MonitorDevice>();

        var wmiPanels = _wmi.QueryPanels();
        var wmiByNormalizedId = new Dictionary<string, WmiPanelInfo>();
        foreach (var panel in wmiPanels)
        {
            var normalized = DisplayEnumerationService.NormalizeHardwareId(panel.InstanceName);
            if (normalized is not null) wmiByNormalizedId[normalized] = panel;
        }

        var dcTargets = DisplayConfigService.QueryTargets();
        var dcByNormalizedId = new Dictionary<string, DisplayConfigTargetInfo>();
        var dcByAdapterName = new Dictionary<string, DisplayConfigTargetInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in dcTargets)
        {
            if (t.NormalizedHardwareId is not null) dcByNormalizedId[t.NormalizedHardwareId] = t;
            if (t.SourceGdiDeviceName is not null) dcByAdapterName[t.SourceGdiDeviceName] = t;
        }

        var consumedWmiInstances = new HashSet<string>();
        var activeDisplays = DisplayEnumerationService.EnumerateActiveDisplays();

        foreach (var display in activeDisplays)
        {
            var normalized = DisplayEnumerationService.NormalizeHardwareId(display.MonitorDeviceId);

            DisplayConfigTargetInfo? dcInfo = null;
            if (normalized is not null) dcByNormalizedId.TryGetValue(normalized, out dcInfo);
            dcInfo ??= dcByAdapterName.GetValueOrDefault(display.AdapterDeviceName);

            // The legacy EnumDisplayDevices name is often a generic "Generic PnP Monitor"; the CCD
            // API usually has the real EDID-derived model name (what Windows' own Settings shows).
            var displayName = !string.IsNullOrWhiteSpace(dcInfo?.FriendlyName) ? dcInfo.FriendlyName! : display.FriendlyName;

            // HDR/Advanced Color displays commonly ignore or clamp DDC/CI brightness — Windows'
            // own fix is the "SDR content brightness" level, so prefer that whenever HDR is active,
            // ahead of WMI/DDC/gamma.
            if (dcInfo is { HdrActive: true })
            {
                if (normalized is not null && wmiByNormalizedId.TryGetValue(normalized, out var hdrPanel))
                    consumedWmiInstances.Add(hdrPanel.InstanceName);

                var sdrId = "sdr:" + (normalized ?? display.AdapterDeviceName);
                var sdrDevice = new MonitorDevice
                {
                    Id = sdrId,
                    DisplayName = displayName,
                    Kind = MonitorControlKind.SdrWhiteLevel,
                    AdapterDeviceName = display.AdapterDeviceName,
                    SdrAdapterId = dcInfo.AdapterId,
                    SdrTargetId = dcInfo.TargetId
                };
                sdrDevice.Brightness = DisplayConfigService.GetSdrWhiteLevelPercent(dcInfo.AdapterId, dcInfo.TargetId) ?? 50;
                ApplySavedOverride(sdrDevice);
                result.Add(sdrDevice);
                continue;
            }

            if (normalized is not null && wmiByNormalizedId.TryGetValue(normalized, out var panel))
            {
                consumedWmiInstances.Add(panel.InstanceName);
                result.Add(MakeWmiDevice(panel));
                continue;
            }

            var ddcInfo = _ddc.TryOpen(display.HMonitor);
            if (ddcInfo is not null)
            {
                var id = "ddc:" + (normalized ?? display.AdapterDeviceName);
                var device = new MonitorDevice
                {
                    Id = id,
                    DisplayName = displayName,
                    Kind = MonitorControlKind.Ddc,
                    DdcHandle = ddcInfo.Handle,
                    DdcMin = ddcInfo.Min,
                    DdcMax = ddcInfo.Max,
                    AdapterDeviceName = display.AdapterDeviceName
                };
                device.Brightness = PercentFromRaw(ddcInfo.Current, ddcInfo.Min, ddcInfo.Max);
                ApplySavedOverride(device);
                result.Add(device);
                continue;
            }

            // Neither WMI nor DDC/CI worked for this display — fall back to gamma-ramp dimming.
            var gammaId = "gamma:" + (normalized ?? display.AdapterDeviceName);
            var gammaDevice = new MonitorDevice
            {
                Id = gammaId,
                DisplayName = displayName,
                Kind = MonitorControlKind.GammaFallback,
                AdapterDeviceName = display.AdapterDeviceName
            };
            gammaDevice.Brightness = 100;
            ApplySavedOverride(gammaDevice);
            result.Add(gammaDevice);
        }

        // WMI panels not currently matched to an active HMONITOR (rare, e.g. transient state) still
        // deserve a control entry since WMI doesn't need an HMONITOR to work.
        foreach (var panel in wmiPanels)
        {
            if (consumedWmiInstances.Contains(panel.InstanceName)) continue;
            result.Add(MakeWmiDevice(panel));
        }

        return result;
    }

    private MonitorDevice MakeWmiDevice(WmiPanelInfo panel)
    {
        var device = new MonitorDevice
        {
            Id = "wmi:" + panel.InstanceName,
            DisplayName = panel.FriendlyName,
            Kind = MonitorControlKind.Wmi,
            WmiInstanceName = panel.InstanceName
        };
        device.Brightness = panel.CurrentBrightness;
        ApplySavedOverride(device);
        return device;
    }

    private void ApplySavedOverride(MonitorDevice device)
    {
        var hasSaved = _settingsService.Settings.MonitorBrightness.TryGetValue(device.Id, out var saved);

        // Gamma-ramp state never survives a refresh (the ramp is reset to identity before rebuilding
        // and there's no hardware to read a "current" value back from), so it must always be reapplied.
        // Everything else has real persistent state (hardware or OS-level) — only force it back to the
        // saved value the first time we see this id this session; a later refresh just trusts what's
        // actually there now, so it won't fight a brightness change made outside the app.
        var shouldForce = hasSaved && (device.Kind == MonitorControlKind.GammaFallback || !_restoredThisSession.Contains(device.Id));

        if (shouldForce && saved != device.Brightness)
        {
            device.Brightness = saved;
            WriteHardware(device, saved);
        }
        else
        {
            _settingsService.Settings.MonitorBrightness[device.Id] = device.Brightness;
        }

        _restoredThisSession.Add(device.Id);
    }

    private static int PercentFromRaw(uint value, uint min, uint max)
    {
        if (max <= min) return 100;
        return (int)Math.Round((value - min) * 100.0 / (max - min));
    }

    /// <summary>Called from UI-thread event handlers (slider drag, hotkey). Debounces the actual hardware write.</summary>
    public void RequestSetBrightness(MonitorDevice device, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        device.Brightness = percent;

        lock (_writeLock)
        {
            if (_pendingWrites.TryGetValue(device.Id, out var existing))
                existing.Cancel();

            var cts = new CancellationTokenSource();
            _pendingWrites[device.Id] = cts;
            var token = cts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(120, token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested) return;

                WriteHardware(device, percent);
                _settingsService.Settings.MonitorBrightness[device.Id] = percent;
                _settingsService.Save();
            }, token);
        }
    }

    public void AdjustAll(int deltaPercent)
    {
        foreach (var device in Monitors)
        {
            RequestSetBrightness(device, device.Brightness + deltaPercent);
        }
    }

    private void WriteHardware(MonitorDevice device, int percent)
    {
        try
        {
            switch (device.Kind)
            {
                case MonitorControlKind.Wmi:
                    if (device.WmiInstanceName is not null)
                        _wmi.SetBrightness(device.WmiInstanceName, (byte)percent);
                    break;

                case MonitorControlKind.Ddc:
                    var raw = device.DdcMin + (uint)Math.Round(percent / 100.0 * (device.DdcMax - device.DdcMin));
                    var ok = _ddc.SetBrightness(device.DdcHandle, raw);
                    SetUnresponsive(device, !ok);
                    break;

                case MonitorControlKind.GammaFallback:
                    if (device.AdapterDeviceName is not null)
                        _gamma.Apply(device.AdapterDeviceName, percent);
                    break;

                case MonitorControlKind.SdrWhiteLevel:
                    DisplayConfigService.SetSdrWhiteLevelPercent(device.SdrAdapterId, device.SdrTargetId, percent);
                    break;
            }
        }
        catch
        {
            // A monitor going to sleep/disconnecting mid-write shouldn't crash the app.
        }
    }

    /// <summary>Marshals the flag flip to the UI thread without blocking the caller (background write thread).</summary>
    private static void SetUnresponsive(MonitorDevice device, bool value)
    {
        if (device.IsUnresponsive == value) return;
        Application.Current.Dispatcher.BeginInvoke(() => device.IsUnresponsive = value);
    }

    public void Shutdown()
    {
        foreach (var device in Monitors)
        {
            if (device.Kind == MonitorControlKind.Ddc && device.DdcHandle != IntPtr.Zero)
                _ddc.Destroy(device.DdcHandle);
            if (device.Kind == MonitorControlKind.GammaFallback && device.AdapterDeviceName is not null)
                _gamma.Reset(device.AdapterDeviceName);
        }

        _settingsService.Save();
    }
}
