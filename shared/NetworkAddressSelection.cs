using System.Net;
using System.Net.Sockets;

namespace Ponte.Core;

public record NetworkAddress(string Address, string InterfaceName, bool HasGateway, bool Virtual);

public static class NetworkAddressSelection
{
    public static IReadOnlyList<NetworkAddress> Order(IEnumerable<NetworkAddress> addresses) => addresses
        .Where(a => IPAddress.TryParse(a.Address, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ip) && !a.Address.StartsWith("169.254.") && a.Address != "0.0.0.0")
        .OrderByDescending(Score).ThenBy(a => a.Address, StringComparer.Ordinal)
        .DistinctBy(a => a.Address).ToArray();

    public static bool IsVirtual(string name, string description) =>
        new[] { "radmin", "vpn", "tailscale", "zerotier", "wireguard", "virtual", "hyper-v", "vmware", "tap-", "tunnel" }
            .Any(word => (name + " " + description).Contains(word, StringComparison.OrdinalIgnoreCase));

    private static int Score(NetworkAddress address)
    {
        var bytes = IPAddress.Parse(address.Address).GetAddressBytes();
        var lan = bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
        if (lan && !address.Virtual) return address.HasGateway ? 300 : 250;
        if (lan) return 150;
        if (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) return 100;
        return !address.Virtual && address.HasGateway ? 80 : address.Virtual ? 10 : 30;
    }
}
