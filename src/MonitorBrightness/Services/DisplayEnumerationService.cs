using System.Text.RegularExpressions;
using MonitorBrightness.Interop;

namespace MonitorBrightness.Services;

public sealed record RawDisplay(
    string AdapterDeviceName,   // e.g. \\.\DISPLAY1
    IntPtr HMonitor,
    string? MonitorDeviceId,    // EDID-based interface path, when available
    string FriendlyName
);

/// <summary>
/// Enumerates active display adapters/monitors via GDI + SetupAPI-style DISPLAY_DEVICE calls.
/// This is the bridge needed to correlate an HMONITOR (used for DDC/CI) with a WMI InstanceName
/// (used for the internal laptop panel) so the same physical screen isn't offered twice.
/// </summary>
public static class DisplayEnumerationService
{
    public static List<RawDisplay> EnumerateActiveDisplays()
    {
        var monitorsByDevice = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
        {
            var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal_SizeOf() };
            if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                monitorsByDevice[info.szDevice] = hMonitor;
            }
            return true;
        }, IntPtr.Zero);

        var results = new List<RawDisplay>();

        var adapter = new NativeMethods.DISPLAY_DEVICE { cb = Marshal_SizeOfDD() };
        uint adapterIndex = 0;
        while (NativeMethods.EnumDisplayDevices(null, adapterIndex, ref adapter, 0))
        {
            adapterIndex++;

            bool attached = adapter.StateFlags.HasFlag(NativeMethods.DisplayDeviceStateFlags.AttachedToDesktop);
            if (!attached)
            {
                adapter = new NativeMethods.DISPLAY_DEVICE { cb = Marshal_SizeOfDD() };
                continue;
            }

            if (!monitorsByDevice.TryGetValue(adapter.DeviceName, out var hMonitor))
            {
                adapter = new NativeMethods.DISPLAY_DEVICE { cb = Marshal_SizeOfDD() };
                continue;
            }

            var monitor = new NativeMethods.DISPLAY_DEVICE { cb = Marshal_SizeOfDD() };
            string? deviceId = null;
            string friendlyName = adapter.DeviceString;
            if (NativeMethods.EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME))
            {
                deviceId = monitor.DeviceID;
                if (!string.IsNullOrWhiteSpace(monitor.DeviceString))
                    friendlyName = monitor.DeviceString;
            }

            results.Add(new RawDisplay(adapter.DeviceName, hMonitor, deviceId, friendlyName));

            adapter = new NativeMethods.DISPLAY_DEVICE { cb = Marshal_SizeOfDD() };
        }

        return results;
    }

    /// <summary>
    /// Normalizes a WMI InstanceName ("DISPLAY\LGD0000\4&amp;23e6afa3&amp;0&amp;UID0_0") and a
    /// DISPLAY_DEVICE.DeviceID interface path ("\\?\DISPLAY#LGD0000#4&amp;23e6afa3&amp;0&amp;UID0#{guid}")
    /// down to a comparable "DISPLAY\VENDORMODEL\INSTANCE" key so the two APIs' identifiers for the
    /// same physical panel can be matched.
    /// </summary>
    public static string? NormalizeHardwareId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var s = raw.Replace(@"\\?\", string.Empty).Replace('#', '\\');

        var guidIdx = s.IndexOf("\\{", StringComparison.Ordinal);
        if (guidIdx >= 0) s = s[..guidIdx];

        s = Regex.Replace(s, @"_\d+$", string.Empty);

        return s.ToUpperInvariant();
    }

    private static int Marshal_SizeOf() => System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>();
    private static int Marshal_SizeOfDD() => System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>();
}
