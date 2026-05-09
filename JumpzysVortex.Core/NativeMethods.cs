using System.Runtime.InteropServices;

namespace JumpzysVortex.Core;

public static class NativeMethods
{
    [DllImport("psapi.dll", SetLastError = true)]
    internal static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("ntdll.dll")]
    internal static extern uint NtSetSystemInformation(
        int    systemInformationClass,
        IntPtr systemInformation,
        int    systemInformationLength);

    // Flush Windows standby RAM list — frees RAM held speculatively by OS
    // SystemMemoryListInformation = 80, MemoryPurgeStandbyList = 4
    public static void FlushStandbyList()
    {
        var ptr = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(ptr, 4); // MemoryPurgeStandbyList
            NtSetSystemInformation(80, ptr, sizeof(int));
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
