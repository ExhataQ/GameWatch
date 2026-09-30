using System.Net.NetworkInformation;

namespace GameWatch.Services;

// Pure classification logic - no OS calls - so it's fully unit testable.
// Decides which broad category a NIC falls into, and whether that
// category should count toward the "total traffic" figure for a given
// AdapterFilterMode, so NetworkStatsService doesn't need to duplicate
// this reasoning and callers don't need to know adapter internals.
public static class AdapterClassifier
{
    public enum Category { Ethernet, WiFi, VpnOrVirtual, Loopback, Other }

    public static Category Classify(string adapterName, NetworkInterfaceType type, string description)
    {
        if (type == NetworkInterfaceType.Loopback) return Category.Loopback;

        var haystack = $"{adapterName} {description}".ToLowerInvariant();

        if (haystack.Contains("vpn") || haystack.Contains("virtual") || haystack.Contains("hyper-v") ||
            haystack.Contains("vethernet") || haystack.Contains("tap-") || haystack.Contains("tunnel") ||
            haystack.Contains("wireguard") || haystack.Contains("wsl"))
        {
            return Category.VpnOrVirtual;
        }

        if (type == NetworkInterfaceType.Wireless80211) return Category.WiFi;

        if (type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx)
        {
            return Category.Ethernet;
        }

        return Category.Other;
    }

    public static bool MatchesFilter(Category category, AdapterFilterMode filter)
    {
        return filter switch
        {
            // Automatic: everything except loopback and VPN/virtual
            // adapters, which otherwise inflate totals with duplicated
            // or tunnel-internal traffic.
            AdapterFilterMode.Automatic => category is Category.Ethernet or Category.WiFi or Category.Other,
            AdapterFilterMode.Ethernet => category == Category.Ethernet,
            AdapterFilterMode.WiFi => category == Category.WiFi,
            AdapterFilterMode.VpnOrVirtual => category == Category.VpnOrVirtual,
            _ => true
        };
    }
}
