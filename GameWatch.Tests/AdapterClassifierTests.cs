using System.Net.NetworkInformation;
using GameWatch.Services;
using Xunit;

namespace GameWatch.Tests;

public class AdapterClassifierTests
{
    [Fact]
    public void ClassifiesLoopbackRegardlessOfName()
    {
        var category = AdapterClassifier.Classify("Loopback Pseudo-Interface", NetworkInterfaceType.Loopback, "");

        Assert.Equal(AdapterClassifier.Category.Loopback, category);
    }

    [Theory]
    [InlineData("VPN Client Adapter")]
    [InlineData("Hyper-V Virtual Ethernet Adapter")]
    [InlineData("vEthernet (WSL)")]
    [InlineData("TAP-Windows Adapter V9")]
    [InlineData("WireGuard Tunnel")]
    public void ClassifiesVpnAndVirtualAdaptersByName(string name)
    {
        var category = AdapterClassifier.Classify(name, NetworkInterfaceType.Ethernet, "");

        Assert.Equal(AdapterClassifier.Category.VpnOrVirtual, category);
    }

    [Fact]
    public void ClassifiesWireless80211AsWiFi()
    {
        var category = AdapterClassifier.Classify("Wi-Fi", NetworkInterfaceType.Wireless80211, "Intel Wireless");

        Assert.Equal(AdapterClassifier.Category.WiFi, category);
    }

    [Fact]
    public void ClassifiesEthernetTypesAsEthernet()
    {
        var category = AdapterClassifier.Classify("Ethernet", NetworkInterfaceType.GigabitEthernet, "Realtek PCIe");

        Assert.Equal(AdapterClassifier.Category.Ethernet, category);
    }

    [Theory]
    [InlineData(AdapterClassifier.Category.Ethernet, AdapterFilterMode.Automatic, true)]
    [InlineData(AdapterClassifier.Category.WiFi, AdapterFilterMode.Automatic, true)]
    [InlineData(AdapterClassifier.Category.VpnOrVirtual, AdapterFilterMode.Automatic, false)]
    [InlineData(AdapterClassifier.Category.Ethernet, AdapterFilterMode.WiFi, false)]
    [InlineData(AdapterClassifier.Category.WiFi, AdapterFilterMode.WiFi, true)]
    [InlineData(AdapterClassifier.Category.VpnOrVirtual, AdapterFilterMode.VpnOrVirtual, true)]
    public void MatchesFilterByMode(AdapterClassifier.Category category, AdapterFilterMode filter, bool expected)
    {
        Assert.Equal(expected, AdapterClassifier.MatchesFilter(category, filter));
    }
}
