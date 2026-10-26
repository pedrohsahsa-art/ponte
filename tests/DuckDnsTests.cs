using System.Net;
using System.Net.Http;
using System.Text;
using Ponte.Core;
using Ponte.Windows;

static class DuckDnsTests
{
    public static async Task RunAsync(string root, Action<bool, string> check)
    {
        var token = Guid.NewGuid().ToString("D");
        var config = DuckDnsSettings.Create("Ponte-Test.duckdns.org", token);
        check(config.Domain == "ponte-test" && config.Hostname == "ponte-test.duckdns.org", "DuckDNS accepts only a normalized single subdomain");
        check(!config.ToString().Contains(token), "DuckDNS settings do not disclose token in diagnostic strings");
        foreach (var bad in new[] { "https://example.org", "x,y", "a/b", "-bad", "a.example.org" })
        {
            var rejected = false; try { DuckDnsSettings.Create(bad, token); } catch (ArgumentException) { rejected = true; }
            check(rejected, "DuckDNS rejects arbitrary endpoints/domain injection");
        }
        var uri = DuckDnsProtocol.UpdateUri(config, "2001:db8::5");
        check(uri.Scheme == "https" && uri.Host == "www.duckdns.org" && uri.Query.Contains("&ipv6=") && !uri.Query.Contains("&ip="), "DDNS uses only official HTTPS endpoint and explicit IPv6, without IPv4 autodetection");
        check(DuckDnsProtocol.Accepted("OK\n\n2001:db8::5\nUPDATED") && !DuckDnsProtocol.Accepted("KO"), "DuckDNS rejection is not reported as success");
        var directory = Path.Combine(root, "ddns"); Directory.CreateDirectory(directory);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var handler = new FakeHandler();
        await using (var updater = new DuckDnsUpdater(directory, () => "2001:db8::5", text => { messages.Enqueue(text); if (text.Contains("atualização aceita")) accepted.TrySetResult(); }, handler))
        {
            await updater.ConfigureAsync(config.Domain, token, true);
            await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(updater.Hostname == config.Hostname, "Confirmed DDNS hostname is available for authenticated discovery");
            check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(directory, "ddns.dpapi"))).Contains(token), "DDNS token is encrypted with Windows DPAPI at rest");
            await updater.ConfigureAsync(config.Domain, "", false);
            check(!updater.Enabled && updater.Hostname is null, "Disabling DDNS withdraws hostname and preserves protected token without displaying it");
        }
        check(messages.All(m => !m.Contains(token)), "DDNS status messages never contain account token");
        await using var restored = new DuckDnsUpdater(directory, () => "2001:db8::5", _ => { }, new FakeHandler());
        check(restored.Domain == config.Domain && !restored.Enabled, "Protected DDNS configuration survives restart without re-enabling itself");
    }
    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("OK\n\n2001:db8::5\nUPDATED") });
    }
}
