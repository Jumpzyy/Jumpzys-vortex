using Microsoft.Win32;

namespace JumpzysVortex.Config;

public static class StartupManager
{
    private const string RegKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "JumpzysVortex";

    public static void Sync(bool enable, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true);
            if (key == null) return;

            if (enable)
                key.SetValue(AppName, $"\"{exePath}\" --minimized");
            else
                key.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch { }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }
}
