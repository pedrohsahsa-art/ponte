using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;
using Ponte.Core;

namespace Ponte.Windows;

public sealed class DuckDnsUpdater : IAsyncDisposable
{
    private readonly string path;
    private readonly Func<string?> address;
    private readonly Action<string> report;
    private readonly HttpClient http;
    private readonly SemaphoreSlim gate = new(1), signal = new(0, 1);
    private readonly CancellationTokenSource stopping = new();
    private readonly Task worker;
    private DuckDnsSettings? settings;
    private string? confirmedHost;
    private string? confirmedAddress;
    public string? Hostname => Volatile.Read(ref confirmedAddress) == address() ? Volatile.Read(ref confirmedHost) : null;
    public string Domain => settings?.Domain ?? "";
    public bool Enabled => settings?.Enabled == true;
    public DuckDnsUpdater(string directory, Func<string?> address, Action<string> report, HttpMessageHandler? handler = null)
    {
        path = Path.Combine(directory, "ddns.dpapi"); this.address = address; this.report = report;
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            if (File.Exists(path)) settings = JsonSerializer.Deserialize<DuckDnsSettings>(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser));
            if (settings is not null) settings = DuckDnsSettings.Create(settings.Domain, settings.Token, settings.Enabled);
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or ArgumentException) { settings = null; report("DDNS: configuração protegida indisponível. Salve novamente."); }
        NetworkChange.NetworkAddressChanged += NetworkChanged;
        worker = RunAsync();
    }
    private void NetworkChanged(object? sender, EventArgs e) => RequestUpdate();
    public void RequestUpdate() { try { if (signal.CurrentCount == 0) signal.Release(); } catch (SemaphoreFullException) { } }
    public async Task ConfigureAsync(string domain, string token, bool enabled)
    {
        // An empty password preserves the stored secret; it is never shown back in UI.
        var value = DuckDnsSettings.Create(domain, string.IsNullOrWhiteSpace(token) ? settings?.Token ?? "" : token, enabled);
        await gate.WaitAsync(stopping.Token);
        try
        {
            var plain = JsonSerializer.SerializeToUtf8Bytes(value);
            try { await File.WriteAllBytesAsync(path + ".tmp", ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser), stopping.Token); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            File.Move(path + ".tmp", path, true);
            settings = value; confirmedHost = null; confirmedAddress = null;
        }
        finally { gate.Release(); }
        RequestUpdate();
    }
    private async Task RunAsync()
    {
        try
        {
            do
            {
                await gate.WaitAsync(stopping.Token);
                try
                {
                    var config = settings;
                    var ipv6 = address();
                    if (config?.Enabled != true) report("DDNS desativado. Configure sua conta DuckDNS para começar.");
                    else if (ipv6 is null) { confirmedHost = null; report("DDNS aguardando um IPv6 público não temporário."); }
                    else
                    {
                        using var response = await http.GetAsync(DuckDnsProtocol.UpdateUri(config, ipv6), stopping.Token);
                        var body = await response.Content.ReadAsStringAsync(stopping.Token);
                        if (response.IsSuccessStatusCode && body.Length < 4096 && DuckDnsProtocol.Accepted(body))
                        {
                            confirmedAddress = ipv6; confirmedHost = config.Hostname;
                            report($"{config.Hostname} · atualização aceita às {DateTime.Now:HH:mm}. DNS pode levar algum tempo para propagar.");
                        }
                        else { confirmedHost = null; report("DuckDNS recusou a atualização. Confira subdomínio e token."); }
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException)
                { confirmedHost = null; if (!stopping.IsCancellationRequested) report("Falha ao atualizar DuckDNS. Nova tentativa automática; token não foi registrado em logs."); }
                finally { gate.Release(); }
                await signal.WaitAsync(TimeSpan.FromMinutes(5), stopping.Token);
                await Task.Delay(1500, stopping.Token); // debounce interface changes
            } while (!stopping.IsCancellationRequested);
        }
        catch (OperationCanceledException) { }
    }
    public async ValueTask DisposeAsync()
    {
        NetworkChange.NetworkAddressChanged -= NetworkChanged;
        stopping.Cancel();
        await worker;
        http.Dispose(); stopping.Dispose();
    }
}
