using System.Net.NetworkInformation;

namespace GameWatch.Services;

// A single point-in-time reading of total bytes moved by adapters that
// pass the current AdapterFilter. We take two of these a couple of
// seconds apart and diff them to get a rate.
public record NetworkSample(long Received, long Sent, DateTime Timestamp);

public class NetworkStatsService
{
    public AdapterFilterMode AdapterFilter { get; set; } = AdapterFilterMode.Automatic;

    public NetworkSample? GetSample()
    {
        try
        {
            long received = 0;
            long sent = 0;

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                var category = AdapterClassifier.Classify(nic.Name, nic.NetworkInterfaceType, nic.Description);
                if (category == AdapterClassifier.Category.Loopback) continue;
                if (!AdapterClassifier.MatchesFilter(category, AdapterFilter)) continue;

                // GetIPStatistics() (rather than GetIPv4Statistics()) gives
                // combined IPv4+IPv6 byte counts on Windows, so the total
                // traffic figure doesn't silently miss IPv6-only traffic.
                var stats = nic.GetIPStatistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }

            return new NetworkSample(received, sent, DateTime.Now);
        }
        catch
        {
            // Fail soft - caller keeps using the previous sample rather
            // than crashing the app.
            return null;
        }
    }
}
