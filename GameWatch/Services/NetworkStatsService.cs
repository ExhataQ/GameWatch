using System.Net.NetworkInformation;

namespace GameWatch.Services;

// A single point-in-time reading of total bytes moved by all active
// adapters. We take two of these a couple of seconds apart and diff them
// to get a rate - same idea as the PowerShell version's Get-NetworkStats.
public record NetworkSample(long Received, long Sent, DateTime Timestamp);

public class NetworkStatsService
{
    public NetworkSample? GetSample()
    {
        try
        {
            long received = 0;
            long sent = 0;

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var stats = nic.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }

            return new NetworkSample(received, sent, DateTime.Now);
        }
        catch
        {
            // Matches the PS script's behavior: fail soft, caller keeps
            // using the previous sample rather than crashing the app.
            return null;
        }
    }
}
