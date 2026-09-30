namespace GameWatch.Services;

public record TrafficHistoryPoint(DateTime Timestamp, double DownloadBytesPerSecond, double UploadBytesPerSecond);

// Fixed-size rolling buffer of recent total-traffic samples, used to draw
// the "last ~30-60 seconds" graph. Pure in-memory logic, no OS calls, so
// it's fully unit testable.
public class TrafficHistory
{
    private readonly Queue<TrafficHistoryPoint> _points = new();

    public int Capacity { get; }

    public TrafficHistory(int capacity = 30)
    {
        Capacity = capacity;
    }

    public void Add(double downloadBps, double uploadBps, DateTime? timestamp = null)
    {
        _points.Enqueue(new TrafficHistoryPoint(timestamp ?? DateTime.Now, downloadBps, uploadBps));
        while (_points.Count > Capacity) _points.Dequeue();
    }

    public IReadOnlyList<TrafficHistoryPoint> Snapshot() => _points.ToArray();

    public double MaxObservedBytesPerSecond()
    {
        double max = 0;
        foreach (var p in _points)
        {
            max = Math.Max(max, Math.Max(p.DownloadBytesPerSecond, p.UploadBytesPerSecond));
        }
        return max;
    }
}
