using AutoUpdaterDotNET;

namespace JumpzysVortex.App;

public static class UpdateBootstrapper
{
    private const string UpdateXmlUrl =
        "https://raw.githubusercontent.com/Jumpzyy/Jumpzys-vortex/main/update.xml";

    public static void CheckForUpdates()
    {
        try
        {
            AutoUpdater.AppTitle = "Jumpzys Vortex";
            AutoUpdater.ReportErrors = false;
            AutoUpdater.ShowSkipButton = true;
            AutoUpdater.ShowRemindLaterButton = true;
            AutoUpdater.Mandatory = false;
            AutoUpdater.UpdateMode = Mode.ForcedDownload;
            AutoUpdater.Start(UpdateXmlUrl);
        }
        catch
        {
            // Update checks must never block app startup.
        }
    }
}
