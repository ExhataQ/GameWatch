namespace GameWatch.Services;

// Pure parsing logic with no process/OS calls - fully unit testable without
// admin rights, a live network, or even Windows. This is the exact piece
// that's riskiest to get subtly wrong (text format assumptions), so it's
// split out on its own and covered by GameWatch.Tests.
public static class NetstatParser
{
    public static Dictionary<int, int> ParseEstablishedTcpPidCounts(string netstatOutput)
    {
        var pidCounts = new Dictionary<int, int>();
        if (string.IsNullOrEmpty(netstatOutput)) return pidCounts;

        foreach (var rawLine in netstatOutput.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // Expected: TCP  LocalAddr  RemoteAddr  State  PID
            if (parts.Length < 5) continue;
            if (!string.Equals(parts[3], "ESTABLISHED", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(parts[4], out int pid)) continue;

            pidCounts[pid] = pidCounts.GetValueOrDefault(pid) + 1;
        }

        return pidCounts;
    }
}
