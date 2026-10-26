using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Ponte.Core;
using Ponte.Windows;

static class AndroidStreamTests
{
    public static async Task RunAsync(AndroidScreenHub hub, string token, string device, X509Certificate2 certificate, Action<bool, string> check)
    {
        check(!hub.Send(new("tap", .5, .5)), "PC cannot send Android commands without an explicitly opened viewer");
        check(!new AndroidCommand("shell").Valid && !new AndroidCommand("tap", double.NaN).Valid && !new AndroidCommand("text", Text: new string('x', 1001)).Valid, "Android control rejects arbitrary, nonfinite and oversized input");
        check(!AndroidFrameValidation.ValidJpeg([0xff, 0xd8, 0xff, 0xd9]), "Android stream rejects JPEG without bounded dimensions");
        using var bitmap = new System.Drawing.Bitmap(64, 96);
        using var output = new MemoryStream(); bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Jpeg); var jpeg = output.ToArray();
        check(AndroidFrameValidation.ValidJpeg(jpeg), "Synthetic phone JPEG has safe validated dimensions");
        var frame = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostic = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var viewer = hub.OpenViewer(device, bytes => frame.TrySetResult(bytes), text => { if (text.Contains("Somente visualização")) diagnostic.TrySetResult(text); });
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        socket.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => cert?.GetCertHashString(HashAlgorithmName.SHA256) == certificate.GetCertHashString(HashAlgorithmName.SHA256);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await socket.ConnectAsync(new Uri($"wss://[::1]:{TransferServer.Port}/api/android/stream"), deadline.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"controlStatus\",\"code\":\"accessibility_disabled\"}").AsMemory(), WebSocketMessageType.Text, true, deadline.Token);
        check((await diagnostic.Task.WaitAsync(deadline.Token)).Contains("Acessibilidade"), "Android accessibility diagnostics reach PC without being treated as image frames");
        check(AndroidControlFeedback.Message("protected_window")!.Contains("Início/Voltar") && AndroidControlFeedback.Message("unknown") is null, "Control feedback explains protected screens and rejects unknown status codes");
        await socket.SendAsync(jpeg.AsMemory(), WebSocketMessageType.Binary, true, deadline.Token);
        check((await frame.Task.WaitAsync(deadline.Token)).SequenceEqual(jpeg), "Authenticated phone screen reaches only the selected local viewer");
        var buffer = new byte[8192];
        var ack = await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token);
        check(Encoding.UTF8.GetString(buffer, 0, ack.Count).Contains("ack"), "Phone stream receives decode acknowledgement for backpressure");
        check(hub.Send(new("tap", .5, .4)), "Local viewer can send a bounded Android tap");
        var input = await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token);
        check(Encoding.UTF8.GetString(buffer, 0, input.Count).Contains("\"tap\""), "Android input travels on the authenticated reverse TLS socket");
        hub.Interrupt();
        check(!hub.Send(new("home")), "Interrupting sharing immediately blocks further Android commands");
        var closed = false;
        try { var result = await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token); closed = result.MessageType == WebSocketMessageType.Close; }
        catch (WebSocketException) { closed = true; }
        check(closed, "Closing Android session tears down screen transport");
    }
}
