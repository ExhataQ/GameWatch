using GameWatch.Services;
using Xunit;

namespace GameWatch.Tests;

public class TrafficHistoryTests
{
    [Fact]
    public void StartsEmpty()
    {
        var history = new TrafficHistory(capacity: 5);

        Assert.Empty(history.Snapshot());
    }

    [Fact]
    public void KeepsSamplesUpToCapacity()
    {
        var history = new TrafficHistory(capacity: 3);
        history.Add(1, 1);
        history.Add(2, 2);
        history.Add(3, 3);

        Assert.Equal(3, history.Snapshot().Count);
    }

    [Fact]
    public void DropsOldestSampleBeyondCapacity()
    {
        var history = new TrafficHistory(capacity: 2);
        history.Add(1, 1);
        history.Add(2, 2);
        history.Add(3, 3);

        var snapshot = history.Snapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(2, snapshot[0].DownloadBytesPerSecond);
        Assert.Equal(3, snapshot[1].DownloadBytesPerSecond);
    }

    [Fact]
    public void ReportsMaxObservedRateAcrossDownloadAndUpload()
    {
        var history = new TrafficHistory(capacity: 5);
        history.Add(downloadBps: 100, uploadBps: 500);
        history.Add(downloadBps: 900, uploadBps: 200);

        Assert.Equal(900, history.MaxObservedBytesPerSecond());
    }

    [Fact]
    public void MaxObservedIsZeroWhenEmpty()
    {
        var history = new TrafficHistory(capacity: 5);

        Assert.Equal(0, history.MaxObservedBytesPerSecond());
    }
}
