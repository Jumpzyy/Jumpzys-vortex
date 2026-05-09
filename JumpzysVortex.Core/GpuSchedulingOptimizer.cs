using Microsoft.Win32;

namespace JumpzysVortex.Core;

public static class GpuSchedulingOptimizer
{
    private const string GraphicsKey =
        @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

    public static bool IsHagsEnabled()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(GraphicsKey);
            return Convert.ToInt32(k?.GetValue("HwSchMode") ?? 1) == 2;
        }
        catch { return false; }
    }

    public static (bool Ok, string Msg) SetHags(bool enable)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(GraphicsKey, writable: true);
            if (k == null) return (false, "Registry key not found.");
            k.SetValue("HwSchMode", enable ? 2 : 1, RegistryValueKind.DWord);
            return (true, $"HAGS {(enable ? "enabled" : "disabled")} — reboot required.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static bool IsWindowedOptEnabled()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(GraphicsKey);
            return Convert.ToInt32(k?.GetValue("DisableWindowedModeGpuOptimization") ?? 0) == 0;
        }
        catch { return true; }
    }

    public static (bool Ok, string Msg) SetWindowedOpt(bool enable)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(GraphicsKey, writable: true);
            if (k == null) return (false, "Registry key not found.");
            k.SetValue("DisableWindowedModeGpuOptimization", enable ? 0 : 1, RegistryValueKind.DWord);
            return (true, $"Windowed game optimisations {(enable ? "enabled" : "disabled")}.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
