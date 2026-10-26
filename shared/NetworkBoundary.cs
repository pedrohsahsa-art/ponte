using System.Net;
using System.Net.Sockets;

namespace Ponte.Core;

public static class NetworkBoundary
{
    public static bool MayAccess(bool local, bool pairing, bool internetEnabled) => local || !pairing && internetEnabled;
    public static bool Contains(IPAddress network, int prefixLength, IPAddress peer)
    {
        if (network.IsIPv4MappedToIPv6) network = network.MapToIPv4();
        if (peer.IsIPv4MappedToIPv6) peer = peer.MapToIPv4();
        var left = network.GetAddressBytes(); var right = peer.GetAddressBytes();
        if (left.Length != right.Length || prefixLength < 0 || prefixLength > left.Length * 8) return false;
        for (var i = 0; i < left.Length; i++)
        {
            var bits = Math.Clamp(prefixLength - i * 8, 0, 8);
            var mask = bits == 0 ? 0 : 0xff << (8 - bits);
            if ((left[i] & mask) != (right[i] & mask)) return false;
        }
        return true;
    }
    public static bool IsGlobalV6(IPAddress address) => address.AddressFamily == AddressFamily.InterNetworkV6 && (address.GetAddressBytes()[0] & 0xe0) == 0x20;
}
