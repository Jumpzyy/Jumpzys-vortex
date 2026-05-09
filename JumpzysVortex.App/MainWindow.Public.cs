namespace JumpzysVortex.App;

// Exposes boost/restore for the system tray right-click menu
public partial class MainWindow
{
    public void ManualBoostPublic()   => Dispatcher.Invoke(ManualBoost);
    public void ManualRestorePublic() => Dispatcher.Invoke(ManualRestore);
}
