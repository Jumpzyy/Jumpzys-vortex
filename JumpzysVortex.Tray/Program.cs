using JumpzysVortex.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new TrayApp();
        app.Run();
    }
}
