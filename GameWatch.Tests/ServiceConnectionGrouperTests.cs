using GameWatch.Services;
using Xunit;

namespace GameWatch.Tests;

public class ServiceConnectionGrouperTests
{
    [Fact]
    public void GroupsByOwnerModuleAndDoesNotInventServiceNames()
    {
        var endpoint = new ConnectionRecord(10, TransportProtocol.Tcp, IpFamily.IPv4, "127.0.0.1", 1000, "1.2.3.4", 443, true);
        var records = new[] { new ModuleConnection(endpoint, "BITS"),
            new ModuleConnection(endpoint with { RemoteAddress = "5.6.7.8" }, "bits"),
            new ModuleConnection(endpoint, "timer.dll"), new ModuleConnection(endpoint, "") };

        var groups = ServiceConnectionGrouper.Group(records, new[] {
            new HostedService("BITS", "Background Intelligent Transfer Service", "C:\\Windows\\System32\\qmgr.dll"),
            new HostedService("DoSvc", "Delivery Optimization", "C:\\Windows\\System32\\dosvc.dll") });

        Assert.Equal(3, groups.Count);
        Assert.Contains(groups, group => group.Label == "Background Intelligent Transfer Service" && group.Endpoints.Count == 2);
        Assert.Contains(groups, group => group.Label == "Module: timer.dll");
        Assert.Contains(groups, group => group.Label == "Unidentified owner");
    }
}
