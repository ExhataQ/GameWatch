using System.Diagnostics;
using System.Linq;

namespace GameWatch.Services;

// A single row the UI displays for one process - already unified across
// IPv4/IPv6 and TCP/UDP. Internally that split still exists (see
// ProcessConnectionSummary.Connections for the detail view), but the
// normal table only ever needs this.
public class ProcessTrafficRow
{
    public int Pid { get; init; }
    public string ProcessName { get; init; } = "";
    public string ExePath { get; init; } = "";
    public string Services { get; init; } = "";
    public string RemoteEndpoints { get; init; } = "";
    public int ConnectionCount { get; init; }
    public int TcpConnectionCount { get; init; }
    public int UdpConnectionCount { get; init; }
    public double DownloadBytesPerSecond { get; init; }
    public double UploadBytesPerSecond { get; init; }
    public bool IsTracked { get; init; }
    public IReadOnlyList<ConnectionRecord> Connections { get; init; } = Array.Empty<ConnectionRecord>();

    // Pre-formatted for display so the DataGrid can bind directly while
    // sorting still uses the underlying numeric properties above.
    public string DownloadRateDisplay => RateFormatting.FormatRate(DownloadBytesPerSecond);
    public string UploadRateDisplay => RateFormatting.FormatRate(UploadBytesPerSecond);
}

public record MonitorSnapshot(
    DateTime Timestamp,
    double TotalDownloadBps,
    double TotalUploadBps,
    IReadOnlyList<ProcessTrafficRow> Processes,
    IReadOnlyList<TrafficHistoryPoint> History,
    IReadOnlyList<TrafficInterval> Transfers,
    bool EtwRunning,
    string? EtwError);

// Owns all periodic data COLLECTION and AGGREGATION, on its own
// background timer, independent of however often the UI chooses to
// repaint. UI code should only ever read LatestSnapshot - it never
// triggers new collection work itself. This is what lets the "UI refresh
// rate" setting be a pure repaint-frequency knob (Low/Normal/Fast)
// rather than something that changes how much OS-level work happens:
// the engine always samples at a fixed, moderate cadence regardless of
// how fast or slow the UI is told to repaint.
public class NetworkMonitorEngine : IDisposable
{
    public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(2);

    private readonly NetworkStatsService _netStats = new();
    private readonly EtwNetworkMonitor _etwMonitor = new();
    private readonly Func<string, bool> _isProcessTracked;
    private System.Threading.Timer? _timer;

    private NetworkSample? _prevTotalSample;
    private Dictionary<int, (long Sent, long Received)> _prevEtwSnapshot = new();
    private DateTime _prevEtwSampleTime = DateTime.Now;
    private readonly TrafficHistory _history = new(capacity: 30); // ~60s at a 2s sample interval
    private readonly ProcessTrafficHistory _transfers = new();
    private readonly ServiceProcessReader _services = new();
    private readonly Dictionary<int, (string Name, string Path)> _processInfoCache = new();
    private Dictionary<int, string> _serviceNames = new();
    private DateTime _lastServiceRefresh = DateTime.MinValue;

    public MonitorSnapshot? LatestSnapshot { get; private set; }
    public bool EtwStarted { get; private set; }

    public AdapterFilterMode AdapterFilter
    {
        get => _netStats.AdapterFilter;
        set => _netStats.AdapterFilter = value;
    }

    // Takes a predicate (rather than a shared List) so the engine's
    // background thread never touches a collection the UI thread mutates;
    // GameModeController.IsProcessTracked does the locking.
    public NetworkMonitorEngine(Func<string, bool> isProcessTracked)
    {
        _isProcessTracked = isProcessTracked;
    }

    public bool Start()
    {
        EtwStarted = _etwMonitor.Start();
        _prevTotalSample = _netStats.GetSample();
        _prevEtwSampleTime = DateTime.Now;

        _timer = new System.Threading.Timer(_ => SafeTick(), null, SampleInterval, SampleInterval);
        return EtwStarted;
    }

    private void SafeTick()
    {
        try { Tick(); }
        catch { /* one bad sample shouldn't kill the background timer */ }
    }

    private void Tick()
    {
        var now = DateTime.Now;
        if (now - _lastServiceRefresh > TimeSpan.FromSeconds(10))
        {
            _serviceNames = _services.Read();
            _lastServiceRefresh = now;
        }

        // --- total adapter throughput ---
        double totalDown = 0, totalUp = 0;
        var current = _netStats.GetSample();
        if (current is not null && _prevTotalSample is not null)
        {
            var seconds = (current.Timestamp - _prevTotalSample.Timestamp).TotalSeconds;
            if (seconds <= 0) seconds = SampleInterval.TotalSeconds;
            totalDown = Math.Max(0, (current.Received - _prevTotalSample.Received) / seconds);
            totalUp = Math.Max(0, (current.Sent - _prevTotalSample.Sent) / seconds);
        }
        if (current is not null) _prevTotalSample = current;

        _history.Add(totalDown, totalUp, now);

        // --- per-process connections (native table - no netstat.exe) ---
        var connections = NativeConnectionReader.GetAllConnections();
        var byPid = ConnectionAggregator.Aggregate(connections);

        // Evict cache entries for PIDs that no longer exist, so a PID
        // that gets reused by an unrelated later process doesn't keep
        // showing the old process's name/path.
        var currentPids = new HashSet<int>(byPid.Keys);
        foreach (var stale in _processInfoCache.Keys.Where(k => !currentPids.Contains(k)).ToList())
        {
            _processInfoCache.Remove(stale);
        }

        // --- per-process rates (ETW byte counters) ---
        Dictionary<int, (long Sent, long Received)> etwNow = new();
        double elapsed = SampleInterval.TotalSeconds;
        if (_etwMonitor.IsRunning)
        {
            etwNow = _etwMonitor.Snapshot();
            elapsed = (now - _prevEtwSampleTime).TotalSeconds;
            if (elapsed <= 0) elapsed = SampleInterval.TotalSeconds;
        }

        var rows = new List<ProcessTrafficRow>();
        var transfers = new List<ProcessTransfer>();
        foreach (var pid in byPid.Keys.Union(etwNow.Where(entry =>
            !_prevEtwSnapshot.TryGetValue(entry.Key, out var previous) ||
            entry.Value.Sent > previous.Sent || entry.Value.Received > previous.Received).Select(entry => entry.Key)))
        {
            byPid.TryGetValue(pid, out var summary);
            var (name, path) = ResolveProcessInfo(pid);

            double down = 0, up = 0;
            long downBytes = 0, upBytes = 0;
            if (etwNow.TryGetValue(pid, out var cur))
            {
                _prevEtwSnapshot.TryGetValue(pid, out var prev);
                downBytes = Math.Max(0, cur.Received - prev.Received);
                upBytes = Math.Max(0, cur.Sent - prev.Sent);
                down = downBytes / elapsed;
                up = upBytes / elapsed;
            }

            if (downBytes > 0 || upBytes > 0)
                transfers.Add(new ProcessTransfer(pid, name, path, downBytes, upBytes));

            rows.Add(new ProcessTrafficRow
            {
                Pid = pid,
                ProcessName = name,
                ExePath = path,
                ConnectionCount = summary?.TotalConnectionCount ?? 0,
                TcpConnectionCount = summary?.TcpConnectionCount ?? 0,
                UdpConnectionCount = summary?.UdpConnectionCount ?? 0,
                Services = _serviceNames.GetValueOrDefault(pid, ""),
                RemoteEndpoints = summary is null ? "" : string.Join(", ", summary.Connections
                    .Where(connection => connection.IsEstablished && connection.RemotePort > 0)
                    .Select(connection => $"{connection.RemoteAddress}:{connection.RemotePort}").Distinct().Take(8)),
                DownloadBytesPerSecond = down,
                UploadBytesPerSecond = up,
                IsTracked = _isProcessTracked(name),
                Connections = summary?.Connections ?? Array.Empty<ConnectionRecord>()
            });
        }

        if (_etwMonitor.IsRunning)
        {
            _transfers.Add(new TrafficInterval(_prevEtwSampleTime, now, transfers));
            _prevEtwSnapshot = etwNow;
            _prevEtwSampleTime = now;
        }

        LatestSnapshot = new MonitorSnapshot(
            now, totalDown, totalUp, rows, _history.Snapshot(), _transfers.Snapshot(), _etwMonitor.IsRunning, _etwMonitor.LastError);
    }

    private (string Name, string Path) ResolveProcessInfo(int pid)
    {
        if (_processInfoCache.TryGetValue(pid, out var cached)) return cached;

        string name = $"Unknown (PID {pid})";
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
            // Process may have exited between the connection snapshot and
            // this lookup - keep the "Unknown (PID x)" fallback.
        }

        var info = (name, path);
        _processInfoCache[pid] = info;
        return info;
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _etwMonitor.Dispose();
    }

    public void Dispose() => Stop();
}
