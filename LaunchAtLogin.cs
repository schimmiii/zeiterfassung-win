using Microsoft.Win32;

namespace Zeiterfassung;

/// <summary>
/// Auto-Start bei Login ueber den HKCU-Run-Key (kein Admin, kein Entitlement noetig).
/// Windows-Pendant zu SMAppService (macOS).
/// </summary>
public static class LaunchAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Zeiterfassung";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue(ValueName) is not null;
            }
            catch { return false; }
        }
    }

    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                          ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (k is null) return;
            if (on)
            {
                var path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path)) k.SetValue(ValueName, $"\"{path}\"");
            }
            else
            {
                k.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch { /* nicht fatal */ }
    }
}
