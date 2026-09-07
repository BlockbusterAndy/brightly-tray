using System.Runtime.InteropServices;
using MonitorBrightness.Interop;
using static MonitorBrightness.Interop.DisplayConfigInterop;

namespace MonitorBrightness.Services;

public sealed record DisplayConfigTargetInfo(
    LUID AdapterId,
    uint TargetId,
    string? NormalizedHardwareId,
    string? SourceGdiDeviceName,
    bool HdrActive,
    string? FriendlyName
);

/// <summary>
/// Reads per-target HDR/Advanced Color state and controls the "SDR content brightness" level —
/// the mechanism Windows itself uses for brightness once a display is in HDR mode, since DDC/CI
/// is commonly ignored or clamped by monitor firmware while HDR is active.
/// </summary>
public static class DisplayConfigService
{
    // Windows' own SDR content brightness slider operates over roughly this nits range.
    private const double MinNits = 80.0;
    private const double MaxNits = 480.0;

    public static List<DisplayConfigTargetInfo> QueryTargets()
    {
        var results = new List<DisplayConfigTargetInfo>();

        DISPLAYCONFIG_PATH_INFO[] paths;
        DISPLAYCONFIG_MODE_INFO[] modes;
        int err;

        do
        {
            err = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount);
            if (err != 0) return results;

            paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            err = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        } while (err == ERROR_INSUFFICIENT_BUFFER);

        if (err != 0) return results;

        foreach (var path in paths)
        {
            var targetName = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    size = Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id
                }
            };
            if (DisplayConfigGetDeviceInfo(ref targetName) != 0) continue;

            var sourceName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                    size = Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                    adapterId = path.sourceInfo.adapterId,
                    id = path.sourceInfo.id
                }
            };
            DisplayConfigGetDeviceInfo(ref sourceName); // best-effort; leave name empty on failure

            var colorInfo = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                    size = Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id
                }
            };
            var hdrActive = DisplayConfigGetDeviceInfo(ref colorInfo) == 0 && colorInfo.AdvancedColorEnabled;

            var normalized = DisplayEnumerationService.NormalizeHardwareId(targetName.monitorDevicePath);
            var friendlyName = string.IsNullOrWhiteSpace(targetName.monitorFriendlyDeviceName)
                ? null
                : targetName.monitorFriendlyDeviceName.Trim();

            results.Add(new DisplayConfigTargetInfo(
                path.targetInfo.adapterId, path.targetInfo.id, normalized, sourceName.viewGdiDeviceName, hdrActive, friendlyName));
        }

        return results;
    }

    public static int? GetSdrWhiteLevelPercent(LUID adapterId, uint targetId)
    {
        var info = new DISPLAYCONFIG_SDR_WHITE_LEVEL
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL,
                size = Marshal.SizeOf<DISPLAYCONFIG_SDR_WHITE_LEVEL>(),
                adapterId = adapterId,
                id = targetId
            }
        };

        if (DisplayConfigGetDeviceInfo(ref info) != 0) return null;

        var nits = info.SDRWhiteLevel * 80.0 / 1000.0;
        return NitsToPercent(nits);
    }

    public static bool SetSdrWhiteLevelPercent(LUID adapterId, uint targetId, int percent)
    {
        var nits = PercentToNits(percent);
        var sdrValue = (uint)Math.Round(nits * 1000.0 / 80.0);

        var request = new DISPLAYCONFIG_SET_SDR_WHITE_LEVEL
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_SET_SDR_WHITE_LEVEL,
                size = Marshal.SizeOf<DISPLAYCONFIG_SET_SDR_WHITE_LEVEL>(),
                adapterId = adapterId,
                id = targetId
            },
            SDRWhiteLevel = sdrValue,
            finalValue = 1
        };

        return DisplayConfigSetDeviceInfo(ref request) == 0;
    }

    private static double PercentToNits(int percent) => MinNits + Math.Clamp(percent, 0, 100) / 100.0 * (MaxNits - MinNits);

    private static int NitsToPercent(double nits) => (int)Math.Round(Math.Clamp((nits - MinNits) / (MaxNits - MinNits), 0, 1) * 100);
}
