using MonitorBrightness.Interop;

namespace MonitorBrightness.Services;

public sealed record DdcHandleInfo(IntPtr Handle, uint Min, uint Current, uint Max);

/// <summary>
/// Controls external monitors via DDC/CI (VESA Monitor Control Command Set) through the
/// Windows Monitor Configuration API (Dxva2.dll). Not all monitors support this reliably —
/// callers should fall back to <see cref="GammaBrightnessService"/> when it fails.
///
/// DDC/CI is a slow, decades-old protocol riding on the same wire as the video signal, and cheap
/// monitor controllers are known to drop or ignore commands sent back-to-back with no gap, or to
/// occasionally NAK a request for no discoverable reason. Both a settle delay between consecutive
/// VCP transactions and retrying a failed write are standard, necessary mitigations — without
/// them, DDC/CI-controlled monitors intermittently "just don't respond," which is one of the more
/// common complaints about brightness tools.
/// </summary>
public sealed class DdcBrightnessService
{
    private const int SettleDelayMs = 40;
    private const int WriteRetryCount = 3;
    private const int WriteRetryDelayMs = 60;

    /// <summary>
    /// Opens physical monitor handle(s) for an HMONITOR and returns the first one that reports
    /// brightness support, along with its current min/cur/max. Returns null if unsupported.
    /// The caller owns the returned handle and must call <see cref="Destroy"/> on it eventually.
    /// </summary>
    public DdcHandleInfo? TryOpen(IntPtr hMonitor)
    {
        uint count = 0;
        if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref count) || count == 0)
            return null;

        var physicalMonitors = new NativeMethods.PHYSICAL_MONITOR[count];
        if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physicalMonitors))
            return null;

        // Extended-desktop monitors normally yield exactly one physical monitor per HMONITOR;
        // if a hub/KVM reports more, just use the first one that actually supports brightness.
        for (int i = 0; i < physicalMonitors.Length; i++)
        {
            var handle = physicalMonitors[i].hPhysicalMonitor;

            uint caps = 0, colorTemps = 0;
            bool capsOk = NativeMethods.GetMonitorCapabilities(handle, ref caps, ref colorTemps);

            Thread.Sleep(SettleDelayMs);

            uint min = 0, cur = 0, max = 100;
            bool brightnessOk = NativeMethods.GetMonitorBrightness(handle, ref min, ref cur, ref max);
            if (!brightnessOk)
            {
                // A single dropped probe shouldn't permanently demote a working monitor to
                // gamma fallback — give it one more chance after a short settle.
                Thread.Sleep(SettleDelayMs);
                brightnessOk = NativeMethods.GetMonitorBrightness(handle, ref min, ref cur, ref max);
            }

            if (brightnessOk && (!capsOk || (caps & NativeMethods.MC_CAPS_BRIGHTNESS) != 0) && max > min)
            {
                // Destroy the handles we're not using.
                for (int j = 0; j < physicalMonitors.Length; j++)
                {
                    if (j != i) NativeMethods.DestroyPhysicalMonitor(physicalMonitors[j].hPhysicalMonitor);
                }
                return new DdcHandleInfo(handle, min, cur, max);
            }
        }

        // None supported brightness — release everything.
        foreach (var pm in physicalMonitors)
            NativeMethods.DestroyPhysicalMonitor(pm.hPhysicalMonitor);

        return null;
    }

    /// <summary>Retries a few times on failure — a dropped DDC/CI write is common and usually transient.</summary>
    public bool SetBrightness(IntPtr handle, uint value)
    {
        for (int attempt = 1; attempt <= WriteRetryCount; attempt++)
        {
            if (NativeMethods.SetMonitorBrightness(handle, value)) return true;
            if (attempt < WriteRetryCount) Thread.Sleep(WriteRetryDelayMs);
        }
        return false;
    }

    public void Destroy(IntPtr handle)
    {
        if (handle != IntPtr.Zero) NativeMethods.DestroyPhysicalMonitor(handle);
    }
}
