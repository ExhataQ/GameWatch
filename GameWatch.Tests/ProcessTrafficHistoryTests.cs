using GameWatch.Services;
using Xunit;

namespace GameWatch.Tests;

public class ProcessTrafficHistoryTests
{
    [Fact]
    public void SumsProcessesAcrossSamplesAndFractionsAtTheBoundary()
    {
        var start = new DateTime(2026, 9, 30, 10, 0, 0);
        var history = new ProcessTrafficHistory();
        history.Add(new TrafficInterval(start, start.AddSeconds(10),
            new[] { new ProcessTransfer(1, "chrome", "chrome.exe", 1000, 200) }));
        history.Add(new TrafficInterval(start.AddSeconds(10), start.AddSeconds(20),
            new[] { new ProcessTransfer(2, "chrome", "chrome.exe", 500, 100),
                    new ProcessTransfer(3, "other", "other.exe", 300, 0) }));

        var totals = ProcessTrafficHistory.Summarize(history.Snapshot(), start.AddSeconds(5), start.AddSeconds(15));

        Assert.Equal(2, totals.Count);
        Assert.Equal(750, totals[0].DownloadBytes);
        Assert.Equal(150, totals[0].UploadBytes);
        Assert.Equal(150, totals[1].DownloadBytes);
    }

    [Fact]
    public void RemovesSamplesOlderThanRetention()
    {
        var start = new DateTime(2026, 9, 30, 10, 0, 0);
        var history = new ProcessTrafficHistory(TimeSpan.FromMinutes(5));
        history.Add(new TrafficInterval(start, start.AddSeconds(2), Array.Empty<ProcessTransfer>()));
        history.Add(new TrafficInterval(start.AddMinutes(6), start.AddMinutes(6).AddSeconds(2), Array.Empty<ProcessTransfer>()));

        Assert.Single(history.Snapshot());
    }
}
