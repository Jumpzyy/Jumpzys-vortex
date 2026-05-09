using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using JumpzysVortex.App;
using JumpzysVortex.Config;

namespace JumpzysVortex.Tray;

public class TrayApp : System.Windows.Application
{
    private NotifyIcon? _tray;
    private MainWindow? _main;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SettingsManager.Load();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _main = new MainWindow();

        bool startHidden = SettingsManager.Current.StartMinimized
                           && e.Args.Contains("--minimized");

        if (!startHidden) _main.Show();

        BuildTray();
    }

    private void BuildTray()
    {
        // Use a simple generated icon — replace Icon property with your .ico file if desired
        _tray = new NotifyIcon
        {
            Icon    = LoadTrayIcon(),
            Text    = "Jumpzys Vortex v2.2",
            Visible = true,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Dashboard",  null, (_, _) => ShowMain());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("⚡ Apply Boost",  null, (_, _) => _main?.ManualBoostPublic());
        menu.Items.Add("✔ Restore Normal",null, (_, _) => _main?.ManualRestorePublic());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit",            null, (_, _) => ExitApp());

        _tray.ContextMenuStrip  = menu;
        _tray.DoubleClick      += (_, _) => ShowMain();
        _tray.BalloonTipTitle   = "Jumpzys Vortex";
        _tray.BalloonTipText    = "Running in background. Double-click to open.";
        if (SettingsManager.Current.StartMinimized)
            _tray.ShowBalloonTip(2000);
    }

    private static Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "vortex.ico");
        return File.Exists(iconPath)
            ? new Icon(iconPath)
            : SystemIcons.Application;
    }

    private void ShowMain()
    {
        _main?.Show();
        _main?.Activate();
    }

    private void ExitApp()
    {
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
