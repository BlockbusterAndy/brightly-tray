namespace MonitorBrightness.Models;

public enum MonitorControlKind
{
    /// <summary>Laptop/built-in panel controlled via WMI (WmiMonitorBrightnessMethods).</summary>
    Wmi,

    /// <summary>External monitor controlled via DDC/CI (Dxva2 Monitor Configuration API).</summary>
    Ddc,

    /// <summary>DDC/CI unavailable or unsupported; brightness simulated via a gamma-ramp overlay.</summary>
    GammaFallback,

    /// <summary>
    /// Display has HDR/Advanced Color active, where DDC/CI brightness is commonly ignored by
    /// monitor firmware. Controlled instead via Windows' "SDR content brightness" (SDR white level).
    /// </summary>
    SdrWhiteLevel
}
