using System.Windows.Forms;
using MonitorBrightness.Services;

namespace MonitorBrightness.UI;

public sealed class TrayIconService : IDisposable
{
    private static readonly int[] StepChoices = [1, 5, 10, 15];

    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startupItem;

    public TrayIconService(Action onOpen, Action onRefresh, Action onExit, int initialHotkeyStepPercent, Action<int> onHotkeyStepChanged)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Brightness", null, (_, _) => onOpen());
        menu.Items.Add(new ToolStripSeparator());

        _startupItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = StartupService.IsEnabled() };
        _startupItem.CheckedChanged += (_, _) => StartupService.SetEnabled(_startupItem.Checked);
        menu.Items.Add(_startupItem);

        var stepMenu = new ToolStripMenuItem("Hotkey step (Ctrl+Alt+↑/↓)");
        var stepItems = new List<ToolStripMenuItem>();
        foreach (var step in StepChoices)
        {
            var item = new ToolStripMenuItem($"{step}%") { CheckOnClick = false, Checked = step == initialHotkeyStepPercent };
            item.Click += (_, _) =>
            {
                foreach (var other in stepItems) other.Checked = false;
                item.Checked = true;
                onHotkeyStepChanged(step);
            };
            stepItems.Add(item);
            stepMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(stepMenu);

        menu.Items.Add("Refresh monitors", null, (_, _) => onRefresh());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => onExit());

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconFactory.CreateIcon(),
            Text = "Brightly Tray",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) onOpen();
        };
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
