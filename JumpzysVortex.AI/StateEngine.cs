using JumpzysVortex.Services;

namespace JumpzysVortex.AI;

/// <summary>
/// Analyses rolling snapshots and returns a SystemState + human tip.
/// Separate from PredictionEngine so the ML layer can override independently.
/// </summary>
public class StateEngine
{
    // ── Thresholds ────────────────────────────────────────
    private const float CpuRedThreshold    = 90f;
    private const float CpuYellowThreshold = 75f;
    private const float RamRedThreshold    = 92f;
    private const float RamYellowThreshold = 80f;
    private const float TempRedThreshold   = 90f;
    private const float TempYellowThreshold= 80f;
    private const float FpsLowThreshold    = 30f;
    private const float CpuTrendThreshold  = 12f;   // % rise over last 5 samples = warning
    private const float RamTrendThreshold  = 8f;

    // ── Main evaluate ──────────────────────────────────────
    public (SystemState State, string Tip) Evaluate(
        PerformanceSnapshot        snap,
        IReadOnlyList<PerformanceSnapshot> history)
    {
        var (cpuTrend, ramTrend) = ComputeTrends(history);

        // ── RED ───────────────────────────────────────────
        if (snap.Cpu >= CpuRedThreshold)
            return (SystemState.Red, $"CPU critically high ({snap.Cpu:F0}%) — close background apps immediately.");

        if (snap.Ram >= RamRedThreshold)
            return (SystemState.Red, $"RAM nearly full ({snap.Ram:F0}%) — free up memory or add more RAM.");

        if (snap.CpuTemp > 0 && snap.CpuTemp >= TempRedThreshold)
            return (SystemState.Red, $"CPU overheating ({snap.CpuTemp:F0}°C) — check cooling immediately.");

        if (snap.Fps > 0 && snap.Fps < FpsLowThreshold)
            return (SystemState.Red, $"FPS critically low ({snap.Fps:F0}) — GPU or CPU bottleneck detected.");

        // ── YELLOW ────────────────────────────────────────
        if (snap.Cpu >= CpuYellowThreshold)
            return (SystemState.Yellow, $"CPU load elevated ({snap.Cpu:F0}%) — boost applied, watching closely.");

        if (snap.Ram >= RamYellowThreshold)
            return (SystemState.Yellow, $"RAM usage high ({snap.Ram:F0}%) — consider closing background apps.");

        if (snap.CpuTemp > 0 && snap.CpuTemp >= TempYellowThreshold)
            return (SystemState.Yellow, $"CPU temperature elevated ({snap.CpuTemp:F0}°C) — monitor cooling.");

        if (cpuTrend >= CpuTrendThreshold)
            return (SystemState.Yellow, $"CPU usage rising fast (+{cpuTrend:F0}% trend) — may spike soon.");

        if (ramTrend >= RamTrendThreshold)
            return (SystemState.Yellow, $"RAM usage climbing (+{ramTrend:F0}% trend) — monitor usage.");

        // ── GREEN ─────────────────────────────────────────
        return (SystemState.Green, BuildGreenTip(snap));
    }

    // ── Trends ────────────────────────────────────────────
    private static (float Cpu, float Ram) ComputeTrends(
        IReadOnlyList<PerformanceSnapshot> history)
    {
        if (history.Count < 5) return (0f, 0f);
        var window = history.TakeLast(5).ToList();
        float cpuTrend = window.Last().Cpu - window.First().Cpu;
        float ramTrend = window.Last().Ram - window.First().Ram;
        return (cpuTrend, ramTrend);
    }

    // ── Green tips cycle ──────────────────────────────────
    private static readonly string[] GreenTips =
    {
        "System running within optimal parameters. All green.",
        "Performance nominal — boost keeping things smooth.",
        "CPU, RAM and GPU all within safe limits.",
        "Network stable, system healthy. Good to go.",
        "All metrics nominal — enjoy your session.",
    };

    private static int _tipIdx;

    private static string BuildGreenTip(PerformanceSnapshot snap)
    {
        if (snap.Fps > 0 && snap.Fps > 120)
            return $"Smooth sailing — {snap.Fps:F0} FPS, system fully optimal.";
        return GreenTips[_tipIdx++ % GreenTips.Length];
    }

    // ── Summary string (for logs) ─────────────────────────
    public static string Summarise(PerformanceSnapshot s) =>
        $"CPU {s.Cpu:F0}% | RAM {s.Ram:F0}% | GPU {s.Gpu:F0}% | " +
        $"FPS {s.Fps:F0} | Temp {s.CpuTemp:F0}°C | RAM free {s.AvailableRamMb:N0} MB";
}
