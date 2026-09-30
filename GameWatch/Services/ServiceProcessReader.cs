using System.Runtime.InteropServices;

namespace GameWatch.Services;

public class ServiceProcessReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode;
        public uint CheckPoint, WaitHint, ProcessId, ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceEntry
    {
        public IntPtr Name;
        public IntPtr DisplayName;
        public ServiceStatusProcess Status;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumServicesStatusEx(IntPtr manager, int infoLevel, uint serviceType, uint serviceState,
        IntPtr services, uint bufferSize, out uint bytesNeeded, out uint returned, ref uint resumeHandle, string? group);

    [DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr handle);

    public Dictionary<int, string> Read()
    {
        var result = new Dictionary<int, List<string>>();
        var manager = OpenSCManager(null, null, 0x0004);
        if (manager == IntPtr.Zero) return new();
        try
        {
            uint size = 256 * 1024;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    uint resume = 0;
                    if (!EnumServicesStatusEx(manager, 0, 0x30, 0x03, buffer, size, out var needed, out var count, ref resume, null))
                    {
                        if (Marshal.GetLastWin32Error() == 234 && needed > size) { size = needed; continue; }
                        break;
                    }
                    var entrySize = Marshal.SizeOf<ServiceEntry>();
                    for (var index = 0; index < count; index++)
                    {
                        var entry = Marshal.PtrToStructure<ServiceEntry>(IntPtr.Add(buffer, index * entrySize));
                        if (entry.Status.ProcessId == 0) continue;
                        var name = Marshal.PtrToStringUni(entry.DisplayName) ?? Marshal.PtrToStringUni(entry.Name) ?? "Service";
                        var pid = (int)entry.Status.ProcessId;
                        if (!result.TryGetValue(pid, out var names)) result[pid] = names = new();
                        names.Add(name);
                    }
                    break;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { CloseServiceHandle(manager); }
        return result.ToDictionary(item => item.Key, item => string.Join(", ", item.Value.OrderBy(name => name)));
    }
}
