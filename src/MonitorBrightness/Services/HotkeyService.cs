using System.Windows.Interop;
using MonitorBrightness.Interop;

namespace MonitorBrightness.Services;

/// <summary>
/// Registers Ctrl+Alt+Up / Ctrl+Alt+Down as global hotkeys that adjust every known monitor's
/// brightness by a fixed step. Uses a message-only window so no visible UI is required.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyIdUp = 1;
    private const int HotkeyIdDown = 2;
    private const uint VkUp = 0x26;
    private const uint VkDown = 0x28;

    private int _stepPercent;
    private readonly Action<int> _onAdjust;
    private HwndSource? _source;

    public HotkeyService(int stepPercent, Action<int> onAdjust)
    {
        _stepPercent = stepPercent;
        _onAdjust = onAdjust;
    }

    public void SetStep(int stepPercent) => _stepPercent = stepPercent;

    public bool Register()
    {
        var parameters = new HwndSourceParameters("MonitorBrightnessHotkeys")
        {
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE: message-only window, no UI needed.
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        var hwnd = _source.Handle;
        var mods = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;

        var okUp = NativeMethods.RegisterHotKey(hwnd, HotkeyIdUp, mods, VkUp);
        var okDown = NativeMethods.RegisterHotKey(hwnd, HotkeyIdDown, mods, VkDown);
        return okUp && okDown;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (id == HotkeyIdUp) { _onAdjust(_stepPercent); handled = true; }
            else if (id == HotkeyIdDown) { _onAdjust(-_stepPercent); handled = true; }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source is null) return;

        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyIdUp);
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyIdDown);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }
}
