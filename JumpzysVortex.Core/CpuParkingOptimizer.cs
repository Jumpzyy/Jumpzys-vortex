using System.Diagnostics;

namespace JumpzysVortex.Core;

public static class CpuParkingOptimizer
{
    // Disable core parking — keep all cores available
    public static void DisableParking()
    {
        // Set min/max parked cores to 0% (no parking)
        RunPowerCfg("/SETACVALUEINDEX SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 0");
        RunPowerCfg("/SETACTIVE SCHEME_CURRENT");
    }

    public static void RestoreParking()
    {
        // Restore default (100% = let Windows decide)
        RunPowerCfg("/SETACVALUEINDEX SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 100");
        RunPowerCfg("/SETACTIVE SCHEME_CURRENT");
    }

    // Keep CPU at 100% min frequency — eliminates frequency-scaling stutter
    public static void DisableCStates()
    {
        RunPowerCfg("/SETACVALUEINDEX SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 100");
        RunPowerCfg("/SETACTIVE SCHEME_CURRENT");
    }

    public static void RestoreCStates()
    {
        RunPowerCfg("/SETACVALUEINDEX SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 5");
        RunPowerCfg("/SETACTIVE SCHEME_CURRENT");
    }

    private static void RunPowerCfg(string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = "powercfg.exe",
                Arguments       = args,
                CreateNoWindow  = true,
                UseShellExecute = false,
            })?.WaitForExit(3000);
        }
        catch { }
    }
}
