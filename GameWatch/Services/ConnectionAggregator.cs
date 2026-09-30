using System.Linq;

namespace GameWatch.Services;

public record ProcessConnectionSummary(
    int Pid,
    int TcpConnectionCount,
    int UdpConnectionCount,
    IReadOnlyList<ConnectionRecord> Connections)
{
    public int TotalConnectionCount => TcpConnectionCount + UdpConnectionCount;
}

// Pure grouping logic with no OS calls - takes whatever list of
// ConnectionRecord NativeConnectionReader produced (or a hand-built list
// in tests) and groups it per PID. Kept separate from the native reader
// so this logic is unit-testable without admin rights, a live network,
// or even Windows - the same philosophy the old NetstatParser used.
public static class ConnectionAggregator
{
    public static Dictionary<int, ProcessConnectionSummary> Aggregate(IEnumerable<ConnectionRecord> connections)
    {
        var byPid = new Dictionary<int, List<ConnectionRecord>>();

        foreach (var c in connections)
        {
            // Only count TCP rows that represent an active conversation -
            // LISTENING/TIME_WAIT/etc. aren't "this app is talking to the
            // network right now" in the way a user cares about. UDP rows
            // have no such state, so they always count.
            if (c.Protocol == TransportProtocol.Tcp && !c.IsEstablished) continue;

            if (!byPid.TryGetValue(c.Pid, out var list))
            {
                list = new List<ConnectionRecord>();
                byPid[c.Pid] = list;
            }
            list.Add(c);
        }

        var result = new Dictionary<int, ProcessConnectionSummary>();
        foreach (var (pid, list) in byPid)
        {
            result[pid] = new ProcessConnectionSummary(
                Pid: pid,
                TcpConnectionCount: list.Count(c => c.Protocol == TransportProtocol.Tcp),
                UdpConnectionCount: list.Count(c => c.Protocol == TransportProtocol.Udp),
                Connections: list);
        }
        return result;
    }
}
