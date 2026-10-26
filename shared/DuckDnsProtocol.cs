using System.Net;
using System.Text.RegularExpressions;

namespace Ponte.Core;

public sealed record DuckDnsSettings(string Domain, string Token, bool Enabled = true)
{
    public string Hostname => Domain + ".duckdns.org";
    public static DuckDnsSettings Create(string domain, string token, bool enabled = true)
    {
        domain = domain.Trim().ToLowerInvariant();
        if (domain.EndsWith(".duckdns.org")) domain = domain[..^12];
        if (!Regex.IsMatch(domain, "^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")) throw new ArgumentException("Informe somente seu subdomínio DuckDNS.");
        if (!Guid.TryParseExact(token.Trim(), "D", out var parsed)) throw new ArgumentException("Token DuckDNS inválido. Cole o token da sua conta no campo protegido.");
        return new(domain, parsed.ToString("D"), enabled);
    }
    // Do not include the token in diagnostic strings, logs or exceptions.
    public override string ToString() => $"DuckDNS: {Hostname}, enabled={Enabled}";
}

public static class DuckDnsProtocol
{
    public static Uri UpdateUri(DuckDnsSettings settings, string ipv6)
    {
        if (!IPAddress.TryParse(ipv6, out var ip) || !NetworkBoundary.IsGlobalV6(ip)) throw new ArgumentException("Nenhum IPv6 público disponível para DDNS.");
        return new Uri($"https://www.duckdns.org/update?domains={Uri.EscapeDataString(settings.Domain)}&token={Uri.EscapeDataString(settings.Token)}&ipv6={Uri.EscapeDataString(ip.ToString())}&verbose=true");
    }
    public static bool Accepted(string response) => response.Trim().Split('\n')[0].Trim() == "OK";
}
