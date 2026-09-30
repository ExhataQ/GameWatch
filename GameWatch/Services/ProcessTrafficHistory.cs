namespace GameWatch.Services;

public record ProcessTransfer(int Pid, string ProcessName, string ExePath, long DownloadBytes, long UploadBytes);
public record TrafficInterval(DateTime Start, DateTime End, IReadOnlyList<ProcessTransfer> Processes);

public record TransferTotal(string ProcessName, string ExePath, long DownloadBytes, long UploadBytes)
{
    public long TotalBytes => DownloadBytes + UploadBytes;
    public string DownloadDisplay => ByteFormatting.FormatBytes(DownloadBytes);
    public string UploadDisplay => ByteFormatting.FormatBytes(UploadBytes);
}

public static class ByteFormatting
{
    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024 * 1024):N2} GB";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024d * 1024):N2} MB";
        if (bytes >= 1024) return $"{bytes / 1024d:N1} KB";
        return $"{bytes:N0} B";
    }
}

public class ProcessTrafficHistory
{
    private readonly Queue<TrafficInterval> _intervals = new();
    private readonly TimeSpan _retention;

    public ProcessTrafficHistory(TimeSpan? retention = null) => _retention = retention ?? TimeSpan.FromHours(24);

    public void Add(TrafficInterval interval)
    {
        _intervals.Enqueue(interval);
        while (_intervals.Count > 0 && _intervals.Peek().End < interval.End - _retention)
            _intervals.Dequeue();
    }

    public IReadOnlyList<TrafficInterval> Snapshot() => _intervals.ToArray();

    public static IReadOnlyList<TransferTotal> Summarize(IEnumerable<TrafficInterval> intervals, DateTime start, DateTime end)
    {
        var totals = new Dictionary<(string Name, string Path), (double Down, double Up)>();
        foreach (var interval in intervals)
        {
            var duration = (interval.End - interval.Start).TotalMilliseconds;
            if (duration <= 0) continue;
            var overlap = (Math.Min(end.Ticks, interval.End.Ticks) - Math.Max(start.Ticks, interval.Start.Ticks)) / (double)TimeSpan.TicksPerMillisecond;
            if (overlap <= 0) continue;
            var fraction = Math.Min(1, overlap / duration);
            foreach (var process in interval.Processes)
            {
                var key = (process.ProcessName, process.ExePath);
                totals.TryGetValue(key, out var current);
                totals[key] = (current.Down + process.DownloadBytes * fraction, current.Up + process.UploadBytes * fraction);
            }
        }
        return totals.Select(entry => new TransferTotal(entry.Key.Name, entry.Key.Path,
                (long)Math.Round(entry.Value.Down), (long)Math.Round(entry.Value.Up)))
            .OrderByDescending(row => row.TotalBytes).ToArray();
    }
}
