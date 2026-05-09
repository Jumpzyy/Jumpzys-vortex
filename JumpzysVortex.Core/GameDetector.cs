using System.Diagnostics;
using JumpzysVortex.Config;

namespace JumpzysVortex.Core;

public class GameDetector
{
    private static readonly HashSet<string> KnownGames = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── FPS / Tactical ────────────────────────────────
        "valorant", "VALORANT-Win64-Shipping",
        "csgo", "cs2",
        "RainbowSix", "RainbowSixGame",
        "r5apex",
        "cod", "ModernWarfare", "MW2", "mw3", "mw2sp", "BlackOps6", "BlackOps7",
        "warzone", "warzone2",
        "Splitgate",
        "Marathon",

        // ── Battle Royale ─────────────────────────────────
        "Fortnite", "FortniteClient-Win64-Shipping",
        "TslGame",                              // PUBG

        // ── MMO / Online ──────────────────────────────────
        "RobloxPlayerBeta", "RobloxPlayer",
        "Minecraft", "Minecraft.Windows",
        "javaw",                                // Minecraft Java

        // ── MOBA / Strategy ───────────────────────────────
        "dota2",
        "League of Legends", "LeagueofLegends",

        // ── Open World / RPG ──────────────────────────────
        "GTA5", "GTAV",
        "RDR2",
        "CyberpunkGame",                        // Cyberpunk 2077
        "eldenring",
        "sekiro",
        "DarkSoulsIII",
        "MHWilds", "MonsterHunterWilds",        // Monster Hunter Wilds
        "MonsterHunterStories3",
        "CrimsonDesert",
        "REEngineGame", "re9", "ResidentEvilRequiem",

        // ── Survival / Sandbox ────────────────────────────
        "EscapeFromTarkov",
        "RustClient",
        "BattleBit",
        "valheim",
        "PalServer", "Palworld",

        // ── Sports / Fighting ─────────────────────────────
        "WWE2K26", "WWE2K25",

        // ── Action / Shooter ──────────────────────────────
        "Battlefield1", "bf1",
        "bf2042", "Battlefield2042",
        "ArcRaiders",
        "destiny2",
        "Overwatch", "Overwatch2",

        // ── Collectible / Adventure ───────────────────────
        "PokemonPokopia",
    };

    public (string? Name, int Pid) DetectActiveGame()
    {
        var custom = SettingsManager.Current.CustomGameExes;

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var exeName = proc.ProcessName;
                bool known  = KnownGames.Contains(exeName);
                bool cust   = custom.Any(c => c.Equals(exeName + ".exe", StringComparison.OrdinalIgnoreCase)
                                           || c.Equals(exeName,          StringComparison.OrdinalIgnoreCase));

                if (known || cust)
                {
                    // Only report if it has a window / is using meaningful CPU
                    if (proc.MainWindowHandle != IntPtr.Zero || proc.WorkingSet64 > 50_000_000)
                        return (proc.ProcessName, proc.Id);
                }
            }
            catch { }
        }
        return (null, 0);
    }
}
