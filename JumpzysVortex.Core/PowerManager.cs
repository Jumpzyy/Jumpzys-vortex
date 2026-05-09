using System.Diagnostics;

namespace JumpzysVortex.Core;

public static class PowerManager
{
    // High Performance GUID
    private const string HighPerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    // Balanced GUID
    private const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";

    public static void SetHighPerformance() => SetPlan(HighPerf);
    public static void SetBalanced()        => SetPlan(Balanced);

    private static void SetPlan(string guid)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = "powercfg.exe",
                Arguments       = $"/setactive {guid}",
                CreateNoWindow  = true,
                UseShellExecute = false,
            })?.WaitForExit(3000);
        }
        catch { }
    }
}
