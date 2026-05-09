using System.IO;
using JumpzysVortex.Config;

namespace JumpzysVortex.Services;

public static class LoggingService
{
    private static string? _logFile;

    public static void StartSession(string? gameName)
    {
        var name = gameName ?? "session";
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));

        foreach (var dir in GetLogDirectoryCandidates())
        {
            try
            {
                Directory.CreateDirectory(dir);
                _logFile = Path.Combine(dir,
                    $"{safeName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
                File.WriteAllText(_logFile,
                    $"=== Jumpzys Vortex v2.2 - {name} - {DateTime.Now:G} ===\n");
                return;
            }
            catch
            {
                _logFile = null;
            }
        }
    }

    public static void LogSnapshot(PerformanceSnapshot s, string? game)
    {
        if (_logFile == null || !SettingsManager.Current.EnableLogging) return;
        try
        {
            var line = $"[{s.Timestamp:HH:mm:ss}] CPU {s.Cpu:F0}% | RAM {s.Ram:F0}% | " +
                       $"GPU {s.Gpu:F0}% | FPS {s.Fps:F0} | Temp {s.CpuTemp:F0}C\n";
            File.AppendAllText(_logFile, line);
        }
        catch { }
    }

    public static string GetLogDirectory() => GetLogDirectoryCandidates().First();

    private static IEnumerable<string> GetLogDirectoryCandidates()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "JumpzysVortex", "Logs");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JumpzysVortex", "Logs");
    }
}
