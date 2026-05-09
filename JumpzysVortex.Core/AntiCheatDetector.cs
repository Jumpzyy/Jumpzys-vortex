using System.Diagnostics;

namespace JumpzysVortex.Core;

public static class AntiCheatDetector
{
    private static readonly Dictionary<string, string[]> AntiCheatProcesses = new()
    {
        ["EasyAntiCheat"] = ["EasyAntiCheat", "EasyAntiCheat_EOS"],
        ["BattlEye"]      = ["BEService", "BEClient", "BEDaisy"],
        ["Vanguard"]      = ["vgtray", "vgc"],
        ["FACEIT"]        = ["faceit", "FACEITClient", "FACEIT Anti-cheat"],
        ["Ricochet"]      = ["ricochet"],
        ["nProtect"]      = ["GameGuard", "npggNT"],
        ["EAC (Epic)"]    = ["EACLaunch"],
    };

    // Features that could theoretically conflict with Vanguard
    private static readonly HashSet<string> VanguardSensitive =
        ["RealTimePriority", "MemoryFlush", "KernelBoost"];

    public static AntiCheatStatus GetStatus()
    {
        var runningProcs = Process.GetProcesses()
            .Select(p => { try { return p.ProcessName; } catch { return ""; } })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var found = new List<string>();
        foreach (var (name, exes) in AntiCheatProcesses)
            if (exes.Any(runningProcs.Contains))
                found.Add(name);

        return new AntiCheatStatus { ActiveAntiCheats = found };
    }

    public static string? GetWarning(string featureName, AntiCheatStatus status)
    {
        if (status.VanguardActive && VanguardSensitive.Contains(featureName))
            return $"Vanguard (Valorant) is active. '{featureName}' may trigger a flag. Proceed with caution.";
        return null;
    }
}

public class AntiCheatStatus
{
    public List<string> ActiveAntiCheats { get; init; } = [];
    public bool VanguardActive    => ActiveAntiCheats.Contains("Vanguard");
    public bool HasAntiCheat      => ActiveAntiCheats.Count > 0;
    public string Summary         => HasAntiCheat
        ? $"Detected: {string.Join(", ", ActiveAntiCheats)}"
        : "No anti-cheat detected";
    public string StatusColour    => HasAntiCheat ? "#FFD600" : "#00FF88";
}
