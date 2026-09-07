using System.ComponentModel;
using System.Runtime.CompilerServices;
using MonitorBrightness.Interop;

namespace MonitorBrightness.Models;

/// <summary>
/// A single controllable display. <see cref="Id"/> is a stable key (survives reboots/reconnects
/// where possible) used for persisting per-monitor brightness in settings.
/// </summary>
public sealed class MonitorDevice : INotifyPropertyChanged
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required MonitorControlKind Kind { get; set; }

    /// <summary>WMI InstanceName, when Kind == Wmi.</summary>
    public string? WmiInstanceName { get; init; }

    /// <summary>Open physical monitor handle, when Kind == Ddc. Must be destroyed on refresh/exit.</summary>
    public IntPtr DdcHandle { get; set; } = IntPtr.Zero;
    public uint DdcMin { get; set; }
    public uint DdcMax { get; set; } = 100;

    /// <summary>GDI device name (e.g. \\.\DISPLAY1), used for gamma fallback and to re-match on refresh.</summary>
    public string? AdapterDeviceName { get; init; }

    /// <summary>Set when Kind == SdrWhiteLevel: the CCD adapter/target identity for this display.</summary>
    public DisplayConfigInterop.LUID SdrAdapterId { get; init; }
    public uint SdrTargetId { get; init; }

    private int _brightness;
    /// <summary>UI-facing brightness, always normalized to 0-100.</summary>
    public int Brightness
    {
        get => _brightness;
        set
        {
            if (_brightness == value) return;
            _brightness = value;
            OnPropertyChanged();
        }
    }

    public bool IsSoftware => Kind == MonitorControlKind.GammaFallback;

    private bool _isUnresponsive;
    /// <summary>True after DDC/CI writes to this monitor have failed even after retries.</summary>
    public bool IsUnresponsive
    {
        get => _isUnresponsive;
        set
        {
            if (_isUnresponsive == value) return;
            _isUnresponsive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusNote));
        }
    }

    public string? StatusNote
    {
        get
        {
            if (IsUnresponsive) return "Not responding — check the monitor is still on and DDC/CI is enabled in its menu";

            return Kind switch
            {
                MonitorControlKind.GammaFallback => "Software dimming (no hardware brightness control found)",
                MonitorControlKind.SdrWhiteLevel => "HDR mode: adjusting SDR content brightness",
                _ => null
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
