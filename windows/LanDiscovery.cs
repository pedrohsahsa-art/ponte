using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Ponte.Windows;

internal sealed class LanDiscovery : IDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private UdpClient? socket;
    public void Start(X509Certificate2 certificate)
    {
        try { socket = new UdpClient(new IPEndPoint(IPAddress.Any, 47832)); }
        catch (SocketException) { return; } // manual address remains available
        var fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        _ = RunAsync(fingerprint);
    }
    private async Task RunAsync(string fingerprint)
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var packet = await socket!.ReceiveAsync(stopping.Token);
                var text = Encoding.ASCII.GetString(packet.Buffer);
                const string prefix = "PONTE_DISCOVER ";
                if (text.Length != prefix.Length + 32 || !text.StartsWith(prefix) || !text[prefix.Length..].All(Uri.IsHexDigit)) continue;
                var reply = JsonSerializer.SerializeToUtf8Bytes(new { nonce = text[prefix.Length..], fingerprint, port = TransferServer.Port });
                await socket.SendAsync(reply, packet.RemoteEndPoint, stopping.Token);
                await Task.Delay(100, stopping.Token); // bounded response rate; no secrets in discovery
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    public static object WakeInfo(IPAddress? localAddress)
    {
        var entries = new List<object>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var mac = adapter.GetPhysicalAddress().GetAddressBytes();
            if (mac.Length != 6) continue;
            foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
            {
                if (!unicast.Address.Equals(localAddress) || unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = unicast.Address.GetAddressBytes();
                var mask = unicast.IPv4Mask.GetAddressBytes();
                var broadcast = new IPAddress(ip.Zip(mask, (a, m) => (byte)(a | ~m)).ToArray()).ToString();
                entries.Add(new { mac = Convert.ToHexString(mac), broadcast });
            }
        }
        return entries;
    }
    public void Dispose() { stopping.Cancel(); socket?.Dispose(); stopping.Dispose(); }
}
