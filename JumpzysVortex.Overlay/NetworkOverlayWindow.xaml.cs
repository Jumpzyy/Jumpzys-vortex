using System.Windows;
using JumpzysVortex.Network;

namespace JumpzysVortex.Overlay;

public partial class NetworkOverlayWindow : Window
{
    public NetworkOverlayWindow() => InitializeComponent();

    public void UpdateStats(NetworkSnapshot net)
    {
        PingText.Text   = net.PingMs > 0 ? $"{net.PingMs:F0} ms" : "— ms";
        JitterText.Text = net.PingMs > 0 ? $"{net.Jitter:F0} ms" : "— ms";
    }
}
