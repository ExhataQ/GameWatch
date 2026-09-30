using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace GameWatch.Services;

// Same core design as before:
// - Only stops services that were actually running, and only restarts the
//   ones *this* call stopped. Services GameWatch didn't touch are never
//   modified, so existing user/OS configuration is respected.
// - Suspends (not kills) listed processes via NtSuspendProcess, fully
//   resumable via NtResumeProcess.
// New: every change is persisted to disk (GameModeStateStore) the moment
// it happens, so a crash or forced-kill while Game Mode is on doesn't
// leave services stopped or processes suspended forever - the next
// launch calls TryRecoverFromCrash() and undoes exactly what was changed.
public class GameModeController
{
    public List<string> ServiceNames { get; set; } = new() { "BITS", "DoSvc", "wuauserv" };
    public List<string> ProcessNames { get; set; } = new(); // e.g. "OneDrive", "Spotify"

    // ProcessNames is touched from the UI thread (Track checkboxes), the
    // engine's background sampling thread (IsTracked flag), and Enable()
    // running off the UI thread. A plain List<string> isn't safe for that,
    // so all access after startup goes through these lock-guarded helpers.
    private readonly object _namesLock = new();

    public void AddProcessName(string name)
    {
        lock (_namesLock)
        {
            if (!ProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                ProcessNames.Add(name);
        }
    }

    public void RemoveProcessName(string name)
    {
        lock (_namesLock)
        {
            ProcessNames.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool IsProcessTracked(string name)
    {
        lock (_namesLock)
        {
            return ProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase);
        }
    }

    public List<string> GetProcessNamesSnapshot()
    {
        lock (_namesLock)
        {
            return new List<string>(ProcessNames);
        }
    }

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
        // Snapshot under the lock, then do the slow suspend work outside it.
        foreach (var name in GetProcessNamesSnapshot())
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

        // Persist immediately - not just held in memory - so a crash
        // right after this line still leaves a record of exactly what
        // needs undoing.
        GameModeStateStore.Save(new GameModeState(
            new List<string>(StoppedServices), new List<int>(PausedProcessIds), DateTime.Now));
    }

    public void Disable()
    {
        if (!IsOn) return;

        RestoreServicesAndProcesses(StoppedServices, PausedProcessIds);

        PausedProcessIds.Clear();
        StoppedServices.Clear();
        IsOn = false;
        GameModeStateStore.Clear();
    }

    // Shared restore logic used both by the normal Disable() path and by
    // TryRecoverFromCrash() at startup - a leftover state file describes
    // exactly the same StoppedServices/PausedProcessIds shape.
    private static void RestoreServicesAndProcesses(IEnumerable<string> stoppedServices, IEnumerable<int> pausedProcessIds)
    {
        foreach (var pid in pausedProcessIds)
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                NtResumeProcess(proc.Handle);
            }
            catch
            {
                // Process may have exited on its own while suspended (or
                // since the crash) - fine, nothing to resume.
            }
        }

        foreach (var name in stoppedServices)
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
    }

    // Call once at startup, before relying on a fresh GameModeController
    // for normal use. If GameWatch crashed or was killed while Game Mode
    // was on, this restores whatever it had changed and clears the stale
    // state file, so a paused Spotify or a stopped BITS service doesn't
    // stay that way forever just because the app didn't shut down
    // cleanly. Returns true if a crash-recovery restore actually ran.
    public static bool TryRecoverFromCrash()
    {
        var state = GameModeStateStore.Load();
        if (state is null) return false;

        RestoreServicesAndProcesses(state.StoppedServices, state.PausedProcessIds);
        GameModeStateStore.Clear();
        return true;
    }
}
