using System.Text.Json;

namespace JumpzysVortex.Core;

public sealed class RestoreSafetyCenter
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JumpzysVortex",
        "restore-actions.json");

    public IReadOnlyList<RestoreAction> Load()
    {
        try
        {
            if (!File.Exists(_path)) return Array.Empty<RestoreAction>();
            return JsonSerializer.Deserialize<List<RestoreAction>>(File.ReadAllText(_path))
                   ?? new List<RestoreAction>();
        }
        catch
        {
            return Array.Empty<RestoreAction>();
        }
    }

    public void Record(string action, string restoreHint)
    {
        var list = Load().ToList();
        list.Insert(0, new RestoreAction(DateTime.Now, action, restoreHint));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}

public sealed record RestoreAction(DateTime Timestamp, string Action, string RestoreHint);
