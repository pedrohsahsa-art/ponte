using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Ponte.Core;
using Ponte.Windows;

static class RemoteStreamTests
{
    public static async Task RunAsync(string token, string session, X509Certificate2 certificate, RemoteAccess remote, Action<bool, string> check)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        socket.Options.SetRequestHeader("X-Ponte-Session", session);
        socket.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => cert?.GetCertHashString(HashAlgorithmName.SHA256) == certificate.GetCertHashString(HashAlgorithmName.SHA256);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await socket.ConnectAsync(new Uri($"wss://127.0.0.1:{TransferServer.Port}/api/remote/stream"), deadline.Token);
        var buffer = new byte[8192];
        var first = await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token);
        check(first.MessageType == WebSocketMessageType.Text && System.Text.Encoding.UTF8.GetString(buffer, 0, first.Count).Contains("monitorId"), "Each screen frame carries monitor identity metadata");
        first = await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token);
        check(first.MessageType == WebSocketMessageType.Binary && first.Count == 4 && first.EndOfMessage, "Continuous TLS stream sends an authenticated binary frame");
        var next = socket.ReceiveAsync(buffer.AsMemory(), deadline.Token).AsTask();
        await Task.Delay(180, deadline.Token);
        check(!next.IsCompleted, "Backpressure prevents any next frame until decode acknowledgement");
        await socket.SendAsync(new byte[] { 1 }.AsMemory(), WebSocketMessageType.Binary, true, deadline.Token);
        check((await next).MessageType == WebSocketMessageType.Text, "Acknowledgement immediately advances the continuous stream metadata");
        check((await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token)).MessageType == WebSocketMessageType.Binary, "Metadata is followed by its JPEG");
        remote.Disable();
        await socket.SendAsync(new byte[] { 1 }.AsMemory(), WebSocketMessageType.Binary, true, deadline.Token);
        var closed = await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token);
        check(closed.MessageType == WebSocketMessageType.Close && socket.CloseStatusDescription == "access_revoked", "Manual revocation closes the live stream without sending another frame");
    }
}
