using Microsoft.Win32;

namespace JumpzysVortex.Core;

public static class NvidiaOptimizer
{
    private const string DriverKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";

    public static bool IsNvidiaPresent()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(DriverKey);
            var desc = k?.GetValue("DriverDesc")?.ToString() ?? "";
            return desc.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static bool IsAmdPresent()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(DriverKey);
            var desc = k?.GetValue("DriverDesc")?.ToString() ?? "";
            return desc.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                || desc.Contains("Radeon", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static string GetGpuName()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(DriverKey);
            return k?.GetValue("DriverDesc")?.ToString() ?? "Unknown GPU";
        }
        catch { return "Unknown GPU"; }
    }

    // Low Latency Mode (NVIDIA Reflex pre-cursor via driver)
    // Requires NVIDIA Control Panel override — applied via NvCP profile key
    public static (bool Ok, string Msg) SetLowLatencyMode(NvidiaLatencyMode mode)
    {
        try
        {
            // NVIDIA stores global driver settings in this key
            using var k = Registry.LocalMachine.OpenSubKey(DriverKey, writable: true);
            if (k == null) return (false, "NVIDIA driver key not found.");
            // Value 0x00F00F0A = Low Latency Mode setting ID
            k.SetValue("0x00F00F0A", (int)mode, RegistryValueKind.DWord);
            return (true, $"Low Latency Mode set to {mode}. Restart game to apply.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static bool IsLowLatencyEnabled()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(DriverKey);
            var val = k?.GetValue("0x00F00F0A");
            if (val is int i) return i != 0;
            return false;
        }
        catch { return false; }
    }

    // Power Management Mode
    public static (bool Ok, string Msg) SetMaxPerformance(bool enable)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(DriverKey, writable: true);
            if (k == null) return (false, "NVIDIA driver key not found.");
            // 8 = Prefer Maximum Performance, 1 = Adaptive
            k.SetValue("PowerMizerEnable",      1,          RegistryValueKind.DWord);
            k.SetValue("PowerMizerLevel",       enable?1:0, RegistryValueKind.DWord);
            k.SetValue("PowerMizerLevelAC",     enable?1:0, RegistryValueKind.DWord);
            return (true, enable
                ? "NVIDIA Maximum Performance enabled."
                : "NVIDIA Adaptive power restored.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static bool IsMaxPerformanceEnabled()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(DriverKey);
            var val = k?.GetValue("PowerMizerLevel");
            if (val is int i) return i == 1;
            return false;
        }
        catch { return false; }
    }

    // Shader Cache disk size limit
    public static (bool Ok, string Msg) SetShaderCacheSize(int sizeGb)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"NVIDIA\NvBackend\ApplicationOntology\data");

            // Write NvCache config — driver reads on next launch
            using var k = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\NVIDIA Corporation\Global\NVTweak", writable: true)
                ?? Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\NVIDIA Corporation\Global\NVTweak");
            k?.SetValue("DiskCacheMaxSize", sizeGb * 1024, RegistryValueKind.DWord);
            return (true, $"Shader cache limit set to {sizeGb} GB.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}

public enum NvidiaLatencyMode : int
{
    Off   = 0,
    On    = 1,
    Ultra = unchecked((int)0xFFFFFFFF),
}
