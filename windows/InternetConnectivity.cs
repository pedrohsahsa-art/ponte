using System.Net;
using System.Net.NetworkInformation;
using Ponte.Core;

namespace Ponte.Windows;

internal static class InternetConnectivity
{
    private static IEnumerable<UnicastIPAddressInformation> Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not NetworkInterfaceType.Tunnel and not NetworkInterfaceType.Loopback && !NetworkAddressSelection.IsVirtual(n.Name, n.Description))
        .SelectMany(n => n.GetIPProperties().UnicastAddresses);
    public static string[] StableAddresses() => Addresses().Where(a => NetworkBoundary.IsGlobalV6(a.Address) && a.PrefixLength < 128 && a.SuffixOrigin != SuffixOrigin.Random && a.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Preferred)
        .Select(a => a.Address.ToString()).Distinct().ToArray();
    public static bool IsLocal(IPAddress? peer)
    {
        if (peer is null) return false;
        if (peer.IsIPv4MappedToIPv6) peer = peer.MapToIPv4();
        if (IPAddress.IsLoopback(peer)) return true;
        return Addresses().Any(a => a.PrefixLength > 0 && NetworkBoundary.Contains(a.Address, a.PrefixLength, peer));
    }
}
