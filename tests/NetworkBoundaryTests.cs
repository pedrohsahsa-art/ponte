using System.Net;
using Ponte.Core;

static class NetworkBoundaryTests
{
    public static void Run(Action<bool, string> check)
    {
        check(!NetworkBoundary.MayAccess(false, true, false) && !NetworkBoundary.MayAccess(false, true, true), "Internet pairing remains blocked even when Internet access is enabled");
        check(!NetworkBoundary.MayAccess(false, false, false) && NetworkBoundary.MayAccess(false, false, true) && NetworkBoundary.MayAccess(true, true, false), "Internet opt-in never blocks LAN pairing or implicitly enables public access");
        bool Has(string network, int prefix, string peer) => NetworkBoundary.Contains(IPAddress.Parse(network), prefix, IPAddress.Parse(peer));
        check(Has("192.168.2.153", 24, "192.168.2.25") && !Has("192.168.2.153", 24, "192.168.3.25"), "LAN pairing boundary respects actual IPv4 subnet");
        check(Has("2001:db8:abcd:1::5", 64, "2001:db8:abcd:1::8") && !Has("2001:db8:abcd:1::5", 64, "2001:db8:abcd:2::8"), "Global IPv6 on a different subnet is not local pairing");
        check(Has("192.168.2.153", 24, "::ffff:192.168.2.4"), "Mapped IPv4 clients retain correct LAN boundary");
        check(!Has("192.168.2.153", 24, "2001:db8::1") && !Has("2001:db8::1", 129, "2001:db8::1"), "Address families and invalid masks cannot bypass boundary");
        check(NetworkBoundary.IsGlobalV6(IPAddress.Parse("2001:db8::1")) && !NetworkBoundary.IsGlobalV6(IPAddress.Parse("fd00::1")) && !NetworkBoundary.IsGlobalV6(IPAddress.Parse("fe80::1")), "Internet address learning excludes private and link-local IPv6");
    }
}
