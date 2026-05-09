using System.Diagnostics;
using JumpzysVortex.Config;

namespace JumpzysVortex.Core;

public class SafeOptimizer
{
    private bool     _boosted;
    private int      _currentGamePid;
    private DateTime _lastMicroAdjust = DateTime.MinValue;

    public void SetGamePid(int pid) => _currentGamePid = pid;

    public void ApplySafeGameMode(string gameName)
    {
        if (_boosted) return;

        try { PowerManager.SetHighPerformance();          } catch { }
        try { CpuParkingOptimizer.DisableParking();       } catch { }
        try { CpuParkingOptimizer.DisableCStates();       } catch { }

        // Flush standby RAM before boosting
        try { NativeMethods.FlushStandbyList();           } catch { }

        // Elevate game process priority
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.ProcessName.Contains(gameName, StringComparison.OrdinalIgnoreCase))
                    proc.PriorityClass = ProcessPriorityClass.High;
            }
            catch { }
        }

        _boosted = true;
    }

    public void RestoreNormalState()
    {
        if (!_boosted) return;
        try { PowerManager.SetBalanced();                 } catch { }
        try { CpuParkingOptimizer.RestoreParking();       } catch { }
        try { CpuParkingOptimizer.RestoreCStates();       } catch { }
        _boosted = false;
    }

    // Called every monitoring tick — applies micro-adjustments if stutter detected
    public void EvaluateMidSession(bool stutterDetected)
    {
        if (!_boosted || !stutterDetected) return;
        if (DateTime.Now - _lastMicroAdjust < TimeSpan.FromSeconds(30)) return;

        _lastMicroAdjust = DateTime.Now;
        Task.Run(ApplyAntiStutter);
    }

    private void ApplyAntiStutter()
    {
        // 1. Flush standby list — most effective for memory-pressure stutter
        try { NativeMethods.FlushStandbyList(); } catch { }

        // 2. Empty working sets of non-essential processes
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (!IsEssential(proc.ProcessName))
                    NativeMethods.EmptyWorkingSet(proc.Handle);
            }
            catch { }
        }

        // 3. Temporarily boost game process to RealTime for 5s, then drop back
        if (_currentGamePid > 0)
        {
            try
            {
                var proc = Process.GetProcessById(_currentGamePid);
                proc.PriorityClass = ProcessPriorityClass.RealTime;
                Task.Delay(5000).ContinueWith(_ =>
                {
                    try { proc.PriorityClass = ProcessPriorityClass.High; } catch { }
                });
            }
            catch { }
        }
    }

    private static bool IsEssential(string name) =>
        name is "System" or "svchost" or "csrss" or "lsass" or "winlogon"
             or "explorer" or "dwm" or "audiodg" or "services" or "smss";
}
