using MonitorBrightness.Interop;

namespace MonitorBrightness.Services;

/// <summary>
/// Simulates brightness on displays that don't support DDC/CI by scaling the per-monitor gamma
/// ramp (the same trick Night Light / f.lux use). This can't produce true black — it's clamped
/// to a 10% floor so the screen never looks "off", which is a common source of "this app is
/// broken" complaints with naive gamma-based dimmers.
/// </summary>
public sealed class GammaBrightnessService
{
    private const double MinFactor = 0.10;

    public bool Apply(string adapterDeviceName, int brightnessPercent)
    {
        var factor = Math.Clamp(brightnessPercent / 100.0, MinFactor, 1.0);
        return ApplyRaw(adapterDeviceName, factor);
    }

    public bool Reset(string adapterDeviceName) => ApplyRaw(adapterDeviceName, 1.0);

    private static bool ApplyRaw(string adapterDeviceName, double factor)
    {
        var hdc = NativeMethods.CreateDC(adapterDeviceName, adapterDeviceName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;

        try
        {
            var ramp = BuildRamp(factor);
            return NativeMethods.SetDeviceGammaRamp(hdc, ref ramp);
        }
        finally
        {
            NativeMethods.DeleteDC(hdc);
        }
    }

    private static NativeMethods.RAMP BuildRamp(double factor)
    {
        var ramp = new NativeMethods.RAMP
        {
            Red = new ushort[256],
            Green = new ushort[256],
            Blue = new ushort[256]
        };

        for (int i = 0; i < 256; i++)
        {
            var v = (ushort)Math.Clamp((int)Math.Round(i * 257 * factor), 0, 65535);
            ramp.Red[i] = v;
            ramp.Green[i] = v;
            ramp.Blue[i] = v;
        }

        return ramp;
    }
}
