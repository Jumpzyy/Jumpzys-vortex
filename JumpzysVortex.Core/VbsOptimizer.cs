using System.Diagnostics;
using System.Management;
using Microsoft.Win32;

namespace JumpzysVortex.Core;

public static class VbsOptimizer
{
    private const string DeviceGuardKey =
        @"SYSTEM\CurrentControlSet\Control\DeviceGuard";
    private const string HvciKey =
        @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";

    public static VbsStatus GetStatus()
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                @"root\cimv2", "SELECT * FROM Win32_DeviceGuard");
            foreach (ManagementObject o in s.Get())
            {
                int vbs  = Convert.ToInt32(o["VirtualizationBasedSecurityStatus"]  ?? 0);
                int hvci = Convert.ToInt32(o["CodeIntegrityPolicyEnforcementStatus"] ?? 0);
                return new VbsStatus { VbsRunning = vbs == 2, HvciRunning = hvci == 2,
                                       VbsEnabled = vbs >= 1, HvciEnabled = hvci >= 1 };
            }
        }
        catch { }
        return new VbsStatus();
    }

    public static bool CreateRestorePoint(string description)
    {
        try
        {
            var scope = new ManagementScope(@"\\localhost\root\default");
            scope.Connect();
            using var cls = new ManagementClass(scope,
                new ManagementPath("SystemRestore"), null);
            var args = cls.GetMethodParameters("CreateRestorePoint");
            args["Description"]      = description;
            args["RestorePointType"] = 12;
            args["EventType"]        = 100;
            var result = cls.InvokeMethod("CreateRestorePoint", args, null);
            return Convert.ToInt32(result["ReturnValue"]) == 0;
        }
        catch { return false; }
    }

    public static (bool Success, string Message) Disable()
    {
        if (!IsAdmin()) return (false, "Administrator privileges required.");
        var errors = new List<string>();

        Try(() =>
        {
            using var k = Registry.LocalMachine.CreateSubKey(DeviceGuardKey);
            k?.SetValue("EnableVirtualizationBasedSecurity", 0, RegistryValueKind.DWord);
            k?.SetValue("RequirePlatformSecurityFeatures",   0, RegistryValueKind.DWord);
        }, "Registry DeviceGuard", errors);

        Try(() =>
        {
            using var k = Registry.LocalMachine.CreateSubKey(HvciKey);
            k?.SetValue("Enabled", 0, RegistryValueKind.DWord);
        }, "Registry HVCI", errors);

        Try(() =>
        {
            using var k = Registry.LocalMachine.CreateSubKey(
                @"SYSTEM\CurrentControlSet\Control\Lsa");
            k?.SetValue("LsaCfgFlags", 0, RegistryValueKind.DWord);
        }, "Registry LSA", errors);

        RunCmd("bcdedit", "/set hypervisorlaunchtype off", errors);

        return errors.Count == 0
            ? (true, "VBS/HVCI disabled. Reboot required.")
            : (false, string.Join("\n", errors));
    }

    public static (bool Success, string Message) Enable()
    {
        if (!IsAdmin()) return (false, "Administrator privileges required.");
        var errors = new List<string>();

        Try(() =>
        {
            using var k = Registry.LocalMachine.CreateSubKey(DeviceGuardKey);
            k?.SetValue("EnableVirtualizationBasedSecurity", 1, RegistryValueKind.DWord);
            k?.SetValue("RequirePlatformSecurityFeatures",   1, RegistryValueKind.DWord);
        }, "Registry DeviceGuard", errors);

        Try(() =>
        {
            using var k = Registry.LocalMachine.CreateSubKey(HvciKey);
            k?.SetValue("Enabled", 1, RegistryValueKind.DWord);
        }, "Registry HVCI", errors);

        RunCmd("bcdedit", "/set hypervisorlaunchtype auto", errors);

        return errors.Count == 0
            ? (true, "VBS/HVCI re-enabled. Reboot required.")
            : (false, string.Join("\n", errors));
    }

    private static void Try(Action a, string label, List<string> errors)
    {
        try { a(); }
        catch (Exception ex) { errors.Add($"{label}: {ex.Message}"); }
    }

    private static void RunCmd(string exe, string args, List<string> errors)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = args,
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
            });
            p?.WaitForExit(5000);
            if (p?.ExitCode != 0) errors.Add($"{exe}: exit code {p?.ExitCode}");
        }
        catch (Exception ex) { errors.Add($"{exe}: {ex.Message}"); }
    }

    public static bool IsAdmin()
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}

public class VbsStatus
{
    public bool VbsRunning  { get; init; }
    public bool HvciRunning { get; init; }
    public bool VbsEnabled  { get; init; }
    public bool HvciEnabled { get; init; }
    public string Summary   => VbsRunning  ? "VBS ACTIVE — impacting performance"
                             : VbsEnabled  ? "VBS enabled but not running"
                                           : "VBS disabled — optimal";
    public string Color     => VbsRunning  ? "#FF2D55"
                             : VbsEnabled  ? "#FFD600"
                                           : "#00FF88";
}
