using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using GameWatch.Services;
using Xunit;

namespace GameWatch.Tests;

public class NativeOwnerModuleReaderTests
{
    [Fact]
    public async Task FindsThisProcessTcpConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var accepted = await listener.AcceptTcpClientAsync();

        var records = NativeOwnerModuleReader.ReadForPid(Environment.ProcessId);

        Assert.Contains(records, item => item.Connection.Protocol == TransportProtocol.Tcp &&
            item.Connection.LocalPort == port && item.Connection.IsEstablished);
        Assert.Contains(records, item => item.Connection.LocalPort == port &&
            !string.IsNullOrWhiteSpace(item.ModuleName));
    }

    [Fact]
    public void FindsThisProcessUdpEndpoint()
    {
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)client.Client.LocalEndPoint!).Port;

        var records = NativeOwnerModuleReader.ReadForPid(Environment.ProcessId);

        Assert.Contains(records, item => item.Connection.Protocol == TransportProtocol.Udp &&
            item.Connection.LocalPort == port && !string.IsNullOrWhiteSpace(item.ModuleName));
    }
}
