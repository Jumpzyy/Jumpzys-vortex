using System.Text.Json;

namespace JumpzysVortex.Services;

public sealed class PluginManager
{
    public string PluginDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JumpzysVortex",
        "Plugins");

    public IReadOnlyList<VortexPluginInfo> Discover()
    {
        Directory.CreateDirectory(PluginDirectory);
        var plugins = new List<VortexPluginInfo>();

        foreach (var file in Directory.EnumerateFiles(PluginDirectory, "plugin.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(file);
                var info = JsonSerializer.Deserialize<VortexPluginInfo>(json);
                if (info != null)
                {
                    info.Path = Path.GetDirectoryName(file) ?? "";
                    plugins.Add(info);
                }
            }
            catch { }
        }

        return plugins;
    }
}

public sealed class VortexPluginInfo
{
    public string Name { get; set; } = "Unnamed Plugin";
    public string Version { get; set; } = "0.0.0";
    public string Kind { get; set; } = "Optimizer";
    public string Description { get; set; } = "";
    public string Path { get; set; } = "";
}
