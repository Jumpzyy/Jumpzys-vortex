using System.IO;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace JumpzysVortex.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashReport(args.ExceptionObject as Exception);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var splash = new SplashWindow();
        MainWindow = splash;
        splash.Show();

        splash.Completion.ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                var main = new MainWindow();
                MainWindow = main;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                main.Show();
                Dispatcher.BeginInvoke(UpdateBootstrapper.CheckForUpdates);
            });
        });
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        var crashPath = WriteCrashReport(e.Exception);
        MessageBox.Show(
            $"Jumpzys Vortex hit an unexpected error.\n\nCrash report:\n{crashPath}",
            "Jumpzys Vortex",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static string WriteCrashReport(Exception? ex)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JumpzysVortex",
            "CrashReports");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, $"crash_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
        File.WriteAllText(path,
            "Jumpzys Vortex crash report\n" +
            $"Time: {DateTime.Now:G}\n" +
            $"Version: v2.2\n\n" +
            (ex?.ToString() ?? "Unknown exception"));
        return path;
    }
}
