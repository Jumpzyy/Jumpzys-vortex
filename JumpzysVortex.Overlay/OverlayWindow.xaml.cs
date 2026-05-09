using System.Windows;
using System.Windows.Media;
using JumpzysVortex.Services;

namespace JumpzysVortex.Overlay;

public partial class OverlayWindow : Window
{
    public OverlayWindow() => InitializeComponent();

    public void UpdateStats(PerformanceSnapshot snap, string state, System.Windows.Media.Color col)
    {
        var brush = new SolidColorBrush(col);
        FpsText.Text       = snap.Fps > 0 ? $"{snap.Fps:F0}" : "—";
        CpuText.Text       = $"{snap.Cpu:F0}%";
        StateText.Text     = "●";
        FpsText.Foreground = brush;
        CpuText.Foreground = brush;
        StateText.Foreground = brush;
    }
}
