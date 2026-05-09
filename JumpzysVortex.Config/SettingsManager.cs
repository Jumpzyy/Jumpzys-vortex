using System.IO;
using System.Text.Json;

namespace JumpzysVortex.Config;

public class AppSettings
{
    public bool         AutoBoostOnGameDetect    { get; set; } = true;
    public bool         StartWithWindows         { get; set; } = false;
    public bool         StartMinimized           { get; set; } = false;
    public bool         ShowFpsOverlay           { get; set; } = true;
    public bool         ShowNetworkOverlay       { get; set; } = true;
    public bool         UseMLPrediction          { get; set; } = true;
    public bool         EnableLogging            { get; set; } = true;
    public int          MonitorIntervalMs        { get; set; } = 1000;
    public int          NetworkIntervalMs        { get; set; } = 2000;
    public int          MLTrainAfterSamples      { get; set; } = 200;
    public float        CpuWarnThreshold         { get; set; } = 80f;
    public float        RamWarnThreshold         { get; set; } = 85f;
    public int          PingWarnThresholdMs      { get; set; } = 80;
    public List<string> CustomGameExes           { get; set; } = new();
    public string       CurrentProfile           { get; set; } = "Balanced";
    public bool         SafeMode                  { get; set; } = false;
    public bool         FirstRunComplete          { get; set; } = false;
    public double       OverlayScale              { get; set; } = 1.0;
    public double       OverlayOpacity            { get; set; } = 0.92;
    public string       AccentColor               { get; set; } = "#38BDF8";
    public string       ThemeDensity              { get; set; } = "Normal";
    public bool         MiniModeEnabled           { get; set; } = false;
    public string       UpdateManifestUrl         { get; set; } = "";
    public List<GameRule> GameRules               { get; set; } = new();
}

public class GameRule
{
    public string ExeName        { get; set; } = "";
    public string Profile        { get; set; } = "Balanced";
    public bool   SafeMode       { get; set; } = false;
    public bool   ShowOverlays   { get; set; } = true;

    public override string ToString() =>
        $"{ExeName}  |  {Profile}  |  {(SafeMode ? "Safe" : "Full")}  |  {(ShowOverlays ? "Overlays" : "No overlays")}";
}

public static class SettingsManager
{
    public static AppSettings Current { get; private set; } = new();

    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JumpzysVortex", "settings.json");

    public static void Load()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                Current  = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
            }
        }
        catch { Current = new(); }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(Current,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch { }
    }
}
