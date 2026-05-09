using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace JumpzysVortex.App;

/// <summary>
/// View model for a single row in the background-process list.
/// All properties are read by the XAML DataTemplate bindings.
/// </summary>
public class ProcessItem
{
    public string Name     { get; }
    public float  CpuPct   { get; }
    public bool   Throttled { get; }

    // ── Computed display ──────────────────────────────────
    public string CpuStr => $"{CpuPct:F1}%";
    public string Status  => Throttled ? "THR" : "OK";

    public Brush BarColor => Throttled
        ? new SolidColorBrush(Color.FromArgb(100, 255, 45,  85))
        : new SolidColorBrush(Color.FromRgb(42,  52,  72));

    public Brush BadgeBg => Throttled
        ? new SolidColorBrush(Color.FromArgb(30,  255, 45,  85))
        : new SolidColorBrush(Color.FromArgb(20,  0,   255, 136));

    public Brush BadgeBorder => Throttled
        ? new SolidColorBrush(Color.FromArgb(80,  255, 45,  85))
        : new SolidColorBrush(Color.FromArgb(60,  0,   255, 136));

    public Brush BadgeFg => Throttled
        ? new SolidColorBrush(Color.FromRgb(255, 45,  85))
        : new SolidColorBrush(Color.FromRgb(0,   255, 136));

    public ProcessItem(string name, float cpuPct, bool throttled)
    {
        Name      = name;
        CpuPct    = Math.Max(0.05f, cpuPct);
        Throttled = throttled;
    }
}
