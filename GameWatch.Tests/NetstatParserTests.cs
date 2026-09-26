using GameWatch.Services;
using Xunit;

namespace GameWatch.Tests;

public class NetstatParserTests
{
    [Fact]
    public void ParsesSingleEstablishedConnection()
    {
        string sample =
            "Active Connections\r\n\r\n" +
            "  Proto  Local Address          Foreign Address        State           PID\r\n" +
            "  TCP    192.168.1.10:54321     93.184.216.34:443      ESTABLISHED     1234\r\n";

        var result = NetstatParser.ParseEstablishedTcpPidCounts(sample);

        Assert.Single(result);
        Assert.Equal(1, result[1234]);
    }

    [Fact]
    public void CountsMultipleConnectionsForSamePid()
    {
        string sample =
            "  TCP    10.0.0.5:1111   1.1.1.1:443   ESTABLISHED   500\r\n" +
            "  TCP    10.0.0.5:1112   1.1.1.1:443   ESTABLISHED   500\r\n" +
            "  TCP    10.0.0.5:1113   1.1.1.1:443   ESTABLISHED   500\r\n";

        var result = NetstatParser.ParseEstablishedTcpPidCounts(sample);

        Assert.Equal(3, result[500]);
    }

    [Fact]
    public void IgnoresNonEstablishedStates()
    {
        string sample =
            "  TCP    10.0.0.5:1111   0.0.0.0:0     LISTENING     700\r\n" +
            "  TCP    10.0.0.5:1112   1.1.1.1:443   TIME_WAIT     700\r\n";

        var result = NetstatParser.ParseEstablishedTcpPidCounts(sample);

        Assert.Empty(result);
    }

    [Fact]
    public void IgnoresUdpAndHeaderLines()
    {
        string sample =
            "Active Connections\r\n\r\n" +
            "  Proto  Local Address          Foreign Address        State           PID\r\n" +
            "  UDP    0.0.0.0:5353           *:*                                    900\r\n";

        var result = NetstatParser.ParseEstablishedTcpPidCounts(sample);

        Assert.Empty(result);
    }

    [Fact]
    public void HandlesEmptyOrNullInput()
    {
        Assert.Empty(NetstatParser.ParseEstablishedTcpPidCounts(""));
        Assert.Empty(NetstatParser.ParseEstablishedTcpPidCounts(null!));
    }

    [Fact]
    public void TracksSeparatePidsIndependently()
    {
        string sample =
            "  TCP    10.0.0.5:1111   1.1.1.1:443   ESTABLISHED   111\r\n" +
            "  TCP    10.0.0.5:1112   1.1.1.1:443   ESTABLISHED   222\r\n" +
            "  TCP    10.0.0.5:1113   1.1.1.1:443   ESTABLISHED   111\r\n";

        var result = NetstatParser.ParseEstablishedTcpPidCounts(sample);

        Assert.Equal(2, result[111]);
        Assert.Equal(1, result[222]);
    }
}
