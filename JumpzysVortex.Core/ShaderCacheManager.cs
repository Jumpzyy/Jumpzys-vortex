using System.IO;

namespace JumpzysVortex.Core;

public static class ShaderCacheManager
{
    private static readonly string Local =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string AppData =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    private static string[] NvidiaPaths => new[]
    {
        Path.Combine(Local,   @"NVIDIA\DXCache"),
        Path.Combine(Local,   @"NVIDIA\GLCache"),
        Path.Combine(AppData, @"NVIDIA\ComputeCache"),
        Path.Combine(Local,   @"NVIDIA Corporation\NV_Cache"),
    };

    private static string[] DxPaths => new[]
    {
        Path.Combine(Local, @"D3DSCache"),
        Path.Combine(Local, @"Microsoft\DirectX"),
    };

    private static string[] AmdPaths => new[]
    {
        Path.Combine(Local, @"AMD\DxCache"),
        Path.Combine(Local, @"AMD\GLCache"),
        Path.Combine(AppData, @"AMD\CLCache"),
    };

    public static (long TotalBytes, Dictionary<string, long> Breakdown) GetCacheSize()
    {
        var bd = new Dictionary<string, long>
        {
            ["NVIDIA"] = NvidiaPaths.Sum(GetFolderSize),
            ["DirectX"] = DxPaths.Sum(GetFolderSize),
            ["AMD"]    = AmdPaths.Sum(GetFolderSize),
        };
        return (bd.Values.Sum(), bd);
    }

    public static (long FreedBytes, List<string> Errors) ClearAll()
    {
        long freed  = 0;
        var  errors = new List<string>();
        var  all    = NvidiaPaths.Concat(DxPaths).Concat(AmdPaths);

        foreach (var path in all)
        {
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var fi = new FileInfo(file);
                    freed += fi.Length;
                    fi.Delete();
                }
                catch (Exception ex) { errors.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
            }
        }
        return (freed, errors);
    }

    private static long GetFolderSize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        try
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }
        catch { return 0; }
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        > 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        > 1_048_576     => $"{bytes / 1_048_576.0:F0} MB",
        > 1024          => $"{bytes / 1024.0:F0} KB",
        _               => $"{bytes} B",
    };
}
