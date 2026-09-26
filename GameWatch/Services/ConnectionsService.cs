using System.Diagnostics;

namespace GameWatch.Services;

public class ProcessConnectionInfo
{
    public int Pid { get; init; }
    public string ProcessName { get; init; } = "";
    public int ConnectionCount { get; init; }
    public string ExePath { get; init; } = "";
    public bool IsTracked { get; init; }

    // Filled in separately from ConnectionsService, by merging in an
    // EtwNetworkMonitor snapshot delta - kept mutable (not init) since
    // it's set after construction rather than from netstat data.
    public string DownloadRate { get; set; } = "-";
    public string UploadRate { get; set; } = "-";
}

// Uses netstat.exe rather than hand-rolled P/Invoke against
// GetExtendedTcpTable. It's a small native tool (not a new PowerShell host
// process like the old Start-Job bug), its output format is extremely
// stable, and text parsing is far easier to get right on the first try
// than manually marshaling the MIB_TCPROW_OWNER_PID struct - worth
// revisiting later if we ever need lower overhead.
public class ConnectionsService
{
    public List<ProcessConnectionInfo> GetTopProcessesByConnectionCount(List<string> trackedProcessNames, int top = 15)
    {
        Dictionary<int, int> pidCounts;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat.exe",
                Arguments = "-ano -p TCP",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null) return new List<ProcessConnectionInfo>();

            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);

            pidCounts = NetstatParser.ParseEstablishedTcpPidCounts(output);
        }
        catch
        {
            return new List<ProcessConnectionInfo>();
        }

        var results = new List<ProcessConnectionInfo>();
        foreach (var (pid, count) in pidCounts)
        {
            string name = "Unknown";
            string path = "";

            try
            {
                using var proc = Process.GetProcessById(pid);
                name = proc.ProcessName;
                try { path = proc.MainModule?.FileName ?? "(access denied)"; }
                catch { path = "(access denied - try running as Administrator)"; }
            }
            catch
            {
                name = $"Unknown (PID {pid})";
            }

            bool tracked = trackedProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase);

            results.Add(new ProcessConnectionInfo
            {
                Pid = pid,
                ProcessName = name,
                ConnectionCount = count,
                ExePath = path,
                IsTracked = tracked
            });
        }

        return results.OrderByDescending(r => r.ConnectionCount).Take(top).ToList();
    }
}
