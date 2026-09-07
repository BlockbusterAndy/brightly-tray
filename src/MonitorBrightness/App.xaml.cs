using System.Windows;
using Microsoft.Win32;
using MonitorBrightness.Services;
using MonitorBrightness.UI;
using Wpf.Ui.Appearance;
using Application = System.Windows.Application;

namespace MonitorBrightness;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private SettingsService _settingsService = null!;
    private MonitorManager _monitorManager = null!;
    private HotkeyService? _hotkeyService;
    private TrayIconService _trayIconService = null!;
    private FlyoutWindow _flyoutWindow = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "MonitorBrightness-SingleInstance-8F2E5C1A", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settingsService = new SettingsService();
        _settingsService.Load();

        ApplicationThemeManager.Apply(ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light);

        _monitorManager = new MonitorManager(_settingsService);
        _flyoutWindow = new FlyoutWindow(_monitorManager);
        SystemThemeWatcher.Watch(_flyoutWindow);

        if (_settingsService.Settings.HotkeysEnabled)
        {
            _hotkeyService = new HotkeyService(_settingsService.Settings.HotkeyStepPercent, delta =>
            {
                _monitorManager.AdjustAll(delta);
            });
            _hotkeyService.Register();
        }

        _trayIconService = new TrayIconService(
            onOpen: ToggleFlyout,
            onRefresh: () => _monitorManager.RefreshAndPublish(),
            onExit: Shutdown,
            initialHotkeyStepPercent: _settingsService.Settings.HotkeyStepPercent,
            onHotkeyStepChanged: step =>
            {
                _settingsService.Settings.HotkeyStepPercent = step;
                _settingsService.Save();
                _hotkeyService?.SetStep(step);
            });

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _monitorManager.RefreshAndPublish();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() => _monitorManager.RefreshAndPublish());
    }

    private void ToggleFlyout()
    {
        if (_flyoutWindow.IsVisible)
            _flyoutWindow.Hide();
        else
            _flyoutWindow.ShowNear(System.Windows.Forms.Cursor.Position);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _hotkeyService?.Dispose();
        _monitorManager?.Shutdown();
        _trayIconService?.Dispose();

        if (_singleInstanceMutex is not null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned, e.g. second instance */ }
        }

        base.OnExit(e);
    }
}
