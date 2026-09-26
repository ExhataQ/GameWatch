using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace GameWatch.Services;

// Same design as the PowerShell version's Enable/Disable-GameMode:
// - Only stops services that were actually running, and only restarts the
//   ones *this* call stopped.
// - Suspends (not kills) listed processes via NtSuspendProcess, fully
//   resumable via NtResumeProcess.
public class GameModeController
{
    // Edit these to suit your setup.
    public List<string> ServiceNames { get; set; } = new() { "BITS", "DoSvc", "wuauserv" };
    public List<string> ProcessNames { get; set; } = new(); // e.g. "OneDrive", "Spotify"

    public bool IsOn { get; private set; }
    public List<string> StoppedServices { get; } = new();
    public List<int> PausedProcessIds { get; } = new();

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    public void Enable()
    {
        if (IsOn) return;
        StoppedServices.Clear();

        foreach (var name in ServiceNames)
        {
            try
            {
                using var sc = new ServiceController(name);
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                    StoppedServices.Add(name);
                }
            }
            catch
            {
                // Service missing, access denied (try running as admin),
                // or didn't stop in time - skip it rather than crash.
            }
        }

        PausedProcessIds.Clear();
        foreach (var name in ProcessNames)
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    NtSuspendProcess(proc.Handle);
                    PausedProcessIds.Add(proc.Id);
                }
                catch
                {
                }
            }
        }

        IsOn = true;
    }

    public void Disable()
    {
        if (!IsOn) return;

        foreach (var pid in PausedProcessIds)
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                NtResumeProcess(proc.Handle);
            }
            catch
            {
                // Process may have exited on its own while suspended - fine.
            }
        }
        PausedProcessIds.Clear();

        foreach (var name in StoppedServices)
        {
            try
            {
                using var sc = new ServiceController(name);
                sc.Start();
            }
            catch
            {
            }
        }
        StoppedServices.Clear();

        IsOn = false;
    }
}
