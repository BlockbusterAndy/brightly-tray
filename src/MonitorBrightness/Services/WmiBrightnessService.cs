using System.Management;

namespace MonitorBrightness.Services;

public sealed record WmiPanelInfo(string InstanceName, byte CurrentBrightness, string FriendlyName);

/// <summary>
/// Controls the internal laptop panel via the root\wmi WmiMonitorBrightness* classes.
/// This is the same mechanism Windows' own brightness slider uses, so it is far more reliable
/// than DDC/CI for built-in displays.
/// </summary>
public sealed class WmiBrightnessService
{
    public List<WmiPanelInfo> QueryPanels()
    {
        var results = new List<WmiPanelInfo>();
        try
        {
            var scope = new ManagementScope(@"root\wmi");
            scope.Connect();

            var friendlyNames = new Dictionary<string, string>();
            using (var idSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM WmiMonitorID")))
            {
                foreach (ManagementBaseObject mo in idSearcher.Get())
                {
                    var instanceName = (string)mo["InstanceName"];
                    var name = DecodeUshortArray(mo["UserFriendlyName"] as ushort[]);
                    friendlyNames[instanceName] = string.IsNullOrWhiteSpace(name) ? "Built-in Display" : name;
                }
            }

            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM WmiMonitorBrightness"));
            foreach (ManagementBaseObject mo in searcher.Get())
            {
                var instanceName = (string)mo["InstanceName"];
                var current = (byte)mo["CurrentBrightness"];
                friendlyNames.TryGetValue(instanceName, out var friendly);
                results.Add(new WmiPanelInfo(instanceName, current, friendly ?? "Built-in Display"));
            }
        }
        catch
        {
            // No WMI brightness support on this machine (e.g. desktop PC with no ACPI backlight) — normal, not an error.
        }

        return results;
    }

    public void SetBrightness(string instanceName, byte brightness)
    {
        try
        {
            var scope = new ManagementScope(@"root\wmi");
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods"));
            foreach (ManagementObject mo in searcher.Get().Cast<ManagementObject>())
            {
                if ((string)mo["InstanceName"] != instanceName) continue;

                var inParams = mo.GetMethodParameters("WmiSetBrightness");
                inParams["Timeout"] = 0;
                inParams["Brightness"] = brightness;
                mo.InvokeMethod("WmiSetBrightness", inParams, null);
                return;
            }
        }
        catch
        {
            // Best-effort; if the panel disappeared (e.g. docked/undocked) just skip this write.
        }
    }

    private static string DecodeUshortArray(ushort[]? raw)
    {
        if (raw is null || raw.Length == 0) return string.Empty;
        var chars = raw.TakeWhile(c => c != 0).Select(c => (char)c).ToArray();
        return new string(chars);
    }
}
