using GameWatch.Services;
using Xunit;

namespace GameWatch.Tests;

public class ConnectionAggregatorTests
{
    private static ConnectionRecord Tcp(int pid, bool established = true, IpFamily family = IpFamily.IPv4) =>
        new(pid, TransportProtocol.Tcp, family, "10.0.0.5", 1111, "1.1.1.1", 443, established);

    private static ConnectionRecord Udp(int pid, IpFamily family = IpFamily.IPv4) =>
        new(pid, TransportProtocol.Udp, family, "10.0.0.5", 5353, "", 0, true);

    [Fact]
    public void CountsSingleEstablishedTcpConnection()
    {
        var result = ConnectionAggregator.Aggregate(new[] { Tcp(1234) });

        Assert.Single(result);
        Assert.Equal(1, result[1234].TcpConnectionCount);
        Assert.Equal(0, result[1234].UdpConnectionCount);
        Assert.Equal(1, result[1234].TotalConnectionCount);
    }

    [Fact]
    public void IgnoresNonEstablishedTcpConnections()
    {
        var result = ConnectionAggregator.Aggregate(new[] { Tcp(700, established: false) });

        Assert.Empty(result);
    }

    [Fact]
    public void CountsUdpEndpointsRegardlessOfState()
    {
        var result = ConnectionAggregator.Aggregate(new[] { Udp(900) });

        Assert.Single(result);
        Assert.Equal(1, result[900].UdpConnectionCount);
        Assert.Equal(0, result[900].TcpConnectionCount);
    }

    [Fact]
    public void CombinesTcpAndUdpForSamePid()
    {
        var result = ConnectionAggregator.Aggregate(new[] { Tcp(111), Tcp(111), Udp(111) });

        Assert.Equal(2, result[111].TcpConnectionCount);
        Assert.Equal(1, result[111].UdpConnectionCount);
        Assert.Equal(3, result[111].TotalConnectionCount);
    }

    [Fact]
    public void CombinesIPv4AndIPv6IntoOneUnifiedCount()
    {
        var result = ConnectionAggregator.Aggregate(new[]
        {
            Tcp(222, family: IpFamily.IPv4),
            Tcp(222, family: IpFamily.IPv6),
        });

        // The whole point of unification: callers never see a separate
        // v4/v6 count, just one combined total.
        Assert.Equal(2, result[222].TcpConnectionCount);
    }

    [Fact]
    public void TracksSeparatePidsIndependently()
    {
        var result = ConnectionAggregator.Aggregate(new[] { Tcp(111), Tcp(222), Tcp(111) });

        Assert.Equal(2, result[111].TcpConnectionCount);
        Assert.Equal(1, result[222].TcpConnectionCount);
    }

    [Fact]
    public void HandlesEmptyInput()
    {
        var result = ConnectionAggregator.Aggregate(Array.Empty<ConnectionRecord>());

        Assert.Empty(result);
    }
}
