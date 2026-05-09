using System.Diagnostics;

namespace JumpzysVortex.Core;

public static class ProcessPriority
{
    private static readonly string[] ThrottleTargets =
    {
        "OneDrive","SearchIndexer","MsMpEng","SgrmBroker","SearchProtocolHost",
        "SearchFilterHost","WmiPrvSE","TiWorker","UsoClient","WaasMedic",
        "AgentService","MpCmdRun","NisSrv",
    };

    private static readonly Dictionary<int, ProcessPriorityClass> _saved = new();

    public static void ThrottleBackgroundProcesses()
    {
        _saved.Clear();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (ThrottleTargets.Any(t =>
                        proc.ProcessName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    _saved[proc.Id] = proc.PriorityClass;
                    proc.PriorityClass = ProcessPriorityClass.Idle;
                }
            }
            catch { }
        }
    }

    public static void RestoreBackgroundProcesses()
    {
        foreach (var (pid, prio) in _saved)
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                proc.PriorityClass = prio;
            }
            catch { }
        }
        _saved.Clear();
    }
}
