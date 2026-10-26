using Ponte.Core;

static class NetworkAddressTests
{
    public static void Run(Action<bool, string> check)
    {
        var vpn = new NetworkAddress("26.96.90.226", "Radmin VPN", false, true);
        var lan = new NetworkAddress("192.168.2.153", "Ethernet", true, false);
        var tail = new NetworkAddress("100.70.80.90", "Tailscale", false, true);
        var virtualLan = new NetworkAddress("172.18.0.1", "vEthernet", false, true);
        var ordered = NetworkAddressSelection.Order([vpn, tail, virtualLan, lan]);
        check(ordered[0] == lan, "Physical local LAN with gateway is preferred over Radmin and Tailscale");
        check(NetworkAddressSelection.Order([vpn, lan])[0] == lan, "Adapter enumeration order cannot select Radmin before the Wi-Fi LAN");
        check(ordered.Contains(tail) && ordered.Contains(vpn), "VPN addresses remain available for explicit manual selection");
        check(NetworkAddressSelection.IsVirtual("Radmin VPN", "adapter") && NetworkAddressSelection.IsVirtual("Ethernet 2", "TAP-Windows adapter"), "Virtual network interfaces are recognized");
        check(!NetworkAddressSelection.IsVirtual("Wi-Fi", "Intel Wi-Fi"), "Physical Wi-Fi is not classified as VPN");
        check(NetworkAddressSelection.Order([new("127.0.0.1", "loopback", false, false), new("169.254.1.2", "no DHCP", false, false), new("::1", "IPv6", false, false), new("invalid", "invalid", false, false), lan]).Count == 1,
            "Unreachable/link-local/invalid addresses are excluded from automatic selection");
        check(NetworkAddressSelection.Order([lan, lan]).Count == 1, "Duplicate addresses are removed");
    }
}
