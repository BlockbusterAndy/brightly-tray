using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MonitorBrightness.Models;
using MonitorBrightness.Services;
using Wpf.Ui.Controls;

namespace MonitorBrightness.UI;

public partial class FlyoutWindow : FluentWindow
{
    private readonly MonitorManager _manager;
    private System.Drawing.Rectangle _workArea;
    private int _anchorCursorX;

    public FlyoutWindow(MonitorManager manager)
    {
        InitializeComponent();
        _manager = manager;
        MonitorsItemsControl.ItemsSource = manager.Monitors;
        manager.Monitors.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();

        // Content height can change while the flyout is already open (a refresh finishing, a
        // monitor reconnecting, a status note wrapping to a second line) — keep it anchored to
        // the bottom-right corner near the tray instead of drifting or leaving a gap.
        SizeChanged += (_, _) =>
        {
            if (IsVisible) RepositionVertically();
        };
    }

    private void UpdateEmptyState()
    {
        EmptyStateText.Visibility = _manager.Monitors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowNear(System.Drawing.Point cursorPos)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(cursorPos);
        _workArea = screen.WorkingArea;
        _anchorCursorX = cursorPos.X;

        // SizeToContent only knows the real height after a layout pass, so the window is shown
        // invisibly first (kept at a valid on-screen position — Mica/DWM composition needs a
        // real monitor to associate with) and revealed only once the final size is known. This
        // avoids a flash at the wrong position/size on first open or when the monitor list changes.
        Opacity = 0;
        Left = _workArea.Left;
        Top = _workArea.Top;

        Show();
        Activate();

        Dispatcher.InvokeAsync(() =>
        {
            RepositionVertically();
            Opacity = 1;
        }, DispatcherPriority.Loaded);
    }

    private void RepositionVertically()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : 0;

        Left = Math.Clamp(_anchorCursorX - width / 2, _workArea.Left + 8, _workArea.Right - width - 8);
        Top = _workArea.Bottom - height - 8;
    }

    private void Window_Deactivated(object? sender, EventArgs e) => Hide();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // The flyout lives for the whole app session; treat a close request as hide.
        e.Cancel = true;
        Hide();
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if ((int)Math.Round(e.NewValue) == (int)Math.Round(e.OldValue)) return;
        if (sender is Slider { Tag: MonitorDevice device })
        {
            _manager.RequestSetBrightness(device, (int)Math.Round(e.NewValue));
        }
    }
}
