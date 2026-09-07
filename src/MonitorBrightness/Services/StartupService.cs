using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace MonitorBrightness.Services;

/// <summary>
/// Controls "run at login." A plain portable/self-contained exe (the default distribution in
/// this repo) uses the classic HKCU Run key. An MSIX-packaged build (e.g. from the Microsoft
/// Store) has that registry write virtualized into a private per-package location that the real
/// startup mechanism never sees — packaged apps must instead declare a windows.startupTask
/// extension in the manifest and drive it via the Windows.ApplicationModel.StartupTask API.
/// Both paths are kept so the same binary works correctly either way it's distributed.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MonitorBrightness";

    /// <summary>Must match the desktop:StartupTask TaskId declared in Package.appxmanifest.</summary>
    private const string PackagedStartupTaskId = "MonitorBrightnessStartup";

    public static bool IsEnabled() => IsPackaged() ? IsPackagedStartupEnabled() : IsUnpackagedStartupEnabled();

    public static void SetEnabled(bool enabled)
    {
        if (IsPackaged()) SetPackagedStartupEnabled(enabled);
        else SetUnpackagedStartupEnabled(enabled);
    }

    /// <summary>
    /// True when running with MSIX package identity (Store install or a locally sideloaded
    /// package) — false for the plain portable exe. Uses the kernel32 identity query directly
    /// rather than touching Package.Current, so it works safely even before any WinRT context
    /// is otherwise needed.
    /// </summary>
    public static bool IsPackaged()
    {
        var length = 0;
        _ = GetCurrentPackageFullName(ref length, null);
        return length != 0;
    }

    private static bool IsUnpackagedStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    private static void SetUnpackagedStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                         ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static bool IsPackagedStartupEnabled()
    {
        try
        {
            var task = StartupTask.GetAsync(PackagedStartupTaskId).AsTask().GetAwaiter().GetResult();
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch
        {
            return false;
        }
    }

    private static void SetPackagedStartupEnabled(bool enabled)
    {
        try
        {
            var task = StartupTask.GetAsync(PackagedStartupTaskId).AsTask().GetAwaiter().GetResult();

            if (enabled && task.State == StartupTaskState.Disabled)
            {
                // Pops a one-time Windows permission dialog on first enable — by design, not
                // something the app can suppress.
                task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
            }
            else if (!enabled && task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
            {
                task.Disable();
            }
        }
        catch
        {
            // Best-effort: if the startup task extension is missing from the manifest for some
            // reason, fail quietly rather than crash the tray menu.
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);
}
