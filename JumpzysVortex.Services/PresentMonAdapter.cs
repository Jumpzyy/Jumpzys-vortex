using System.Diagnostics;

namespace JumpzysVortex.Services;

public sealed class PresentMonAdapter
{
    public bool IsAvailable => ResolvePresentMonPath() != null;

    public string Status => IsAvailable
        ? $"PresentMon found: {ResolvePresentMonPath()}"
        : "PresentMon not found. Drop PresentMon.exe next to JumpzysVortex.exe to enable capture.";

    public string? ResolvePresentMonPath()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "PresentMon.exe");
        if (File.Exists(local)) return local;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, "PresentMon.exe");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    public ProcessStartInfo? CreateCaptureStartInfo(string outputCsv)
    {
        var exe = ResolvePresentMonPath();
        if (exe == null) return null;

        return new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"-output_file \"{outputCsv}\" -timed 30",
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }
}
