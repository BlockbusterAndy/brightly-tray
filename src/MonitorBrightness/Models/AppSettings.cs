namespace MonitorBrightness.Models;

public sealed class AppSettings
{
    public Dictionary<string, int> MonitorBrightness { get; set; } = new();
    public bool StartWithWindows { get; set; }
    public bool HotkeysEnabled { get; set; } = true;
    public int HotkeyStepPercent { get; set; } = 5;
}
