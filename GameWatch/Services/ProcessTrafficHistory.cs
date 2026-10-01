using System.Collections;
using System.Collections.Immutable;

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
    private ImmutableQueue<TrafficInterval> _recent = ImmutableQueue<TrafficInterval>.Empty;
    private ImmutableQueue<TrafficInterval> _archive = ImmutableQueue<TrafficInterval>.Empty;
    private int _recentCount;
    private int _archiveCount;
    private TrafficInterval? _latest;
    private readonly TimeSpan _retention;
    private TrafficInterval? _pendingMinute;
    private IReadOnlyList<TrafficInterval>? _snapshot;

    public ProcessTrafficHistory(TimeSpan? retention = null) => _retention = retention ?? TimeSpan.FromHours(24);

    public void Add(TrafficInterval interval)
    {
        _recent = _recent.Enqueue(interval);
        _recentCount++;
        _latest = interval;
        var cutoff = interval.End - _retention;
        while (!_recent.IsEmpty && _recent.Peek().End < cutoff)
        {
            _recent = _recent.Dequeue();
            _recentCount--;
        }
        while (!_recent.IsEmpty && _recent.Peek().End < interval.End - TimeSpan.FromHours(1))
        {
            var old = _recent.Peek();
            _recent = _recent.Dequeue();
            _recentCount--;
            var minute = new DateTime(old.Start.Ticks - old.Start.Ticks % TimeSpan.TicksPerMinute, old.Start.Kind);
            if (_pendingMinute is not null && _pendingMinute.Start != minute)
            {
                _archive = _archive.Enqueue(_pendingMinute);
                _archiveCount++;
                _pendingMinute = null;
            }
            _pendingMinute = MergeMinute(_pendingMinute, old, minute);
        }
        while (!_archive.IsEmpty && _archive.Peek().End < cutoff)
        {
            _archive = _archive.Dequeue();
            _archiveCount--;
        }
        if (_pendingMinute is not null && _pendingMinute.End < cutoff) _pendingMinute = null;
        _snapshot = null;
    }

    public IReadOnlyList<TrafficInterval> Snapshot() => _snapshot ??=
        new TrafficSnapshot(_archive, _pendingMinute, _recent, _archiveCount + _recentCount + (_pendingMinute is null ? 0 : 1), _latest);

    private sealed class TrafficSnapshot : IReadOnlyList<TrafficInterval>
    {
        private readonly ImmutableQueue<TrafficInterval> _archive;
        private readonly TrafficInterval? _pending;
        private readonly ImmutableQueue<TrafficInterval> _recent;
        private readonly TrafficInterval? _last;

        public TrafficSnapshot(ImmutableQueue<TrafficInterval> archive, TrafficInterval? pending,
            ImmutableQueue<TrafficInterval> recent, int count, TrafficInterval? last)
        {
            _archive = archive;
            _pending = pending;
            _recent = recent;
            _last = last;
            Count = count;
        }

        public int Count { get; }
        public TrafficInterval this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                if (index == Count - 1) return _last!;
                if (index == 0) return !_archive.IsEmpty ? _archive.Peek() : _pending ?? _recent.Peek();
                return this.AsEnumerable().ElementAt(index);
            }
        }

        public IEnumerator<TrafficInterval> GetEnumerator()
        {
            foreach (var interval in _archive) yield return interval;
            if (_pending is not null) yield return _pending;
            foreach (var interval in _recent) yield return interval;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private IEnumerable<TrafficInterval> AsEnumerable() => this;
    }

    private static TrafficInterval MergeMinute(TrafficInterval? existing, TrafficInterval next, DateTime minute)
    {
        var totals = new Dictionary<(int Pid, string Name, string Path), (long Down, long Up)>();
        foreach (var process in (existing?.Processes ?? Array.Empty<ProcessTransfer>()).Concat(next.Processes))
        {
            var key = (process.Pid, process.ProcessName, process.ExePath);
            totals.TryGetValue(key, out var current);
            totals[key] = (current.Down + process.DownloadBytes, current.Up + process.UploadBytes);
        }
        return new TrafficInterval(minute, next.End, totals.Select(entry =>
            new ProcessTransfer(entry.Key.Pid, entry.Key.Name, entry.Key.Path, entry.Value.Down, entry.Value.Up)).ToArray());
    }

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
