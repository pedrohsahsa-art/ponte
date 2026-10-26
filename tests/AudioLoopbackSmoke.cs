using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Ponte.Core;
using Ponte.Windows;

static class AudioLoopbackSmoke
{
    public static async Task RunAsync()
    {
        var directory = Path.GetFullPath(Path.Combine(".cache", "audio-smoke", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var devices = new DeviceRegistry(Path.Combine(directory, "devices"));
        var phone = devices.Register("Isolated audio test");
        using var rsa = RSA.Create(2048);
        using var generated = new CertificateRequest("CN=Ponte audio smoke", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var cert = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet);
        var remote = new RemoteAccess(() => new DesktopFrame([], 1, 1), _ => { }, () => true);
        remote.EnableFor(phone.Device.Id);
        remote.SetAudioAllowed(true);
        var session = remote.Open(phone.Device.Id).SessionId;
        var server = new TransferServer(devices, new FileLibrary(Path.Combine(directory, "files")), (_, _) => Task.FromResult(false), () => { }, remote);
        await server.StartAsync(cert);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer " + phone.Token);
            socket.Options.SetRequestHeader("X-Ponte-Session", session);
            socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString(HashAlgorithmName.SHA256) == cert.GetCertHashString(HashAlgorithmName.SHA256);
            // Play silence only, so WASAPI produces frames without changing volume.
            using var silence = new WaveOutEvent();
            silence.Init(new SignalGenerator(48000, 2) { Gain = 0 }.ToWaveProvider());
            silence.Play();
            await socket.ConnectAsync(new Uri($"wss://127.0.0.1:{TransferServer.Port}/api/remote/audio"), deadline.Token);
            var bytes = new byte[262144];
            var header = await socket.ReceiveAsync(bytes.AsMemory(), deadline.Token);
            if (header.MessageType != WebSocketMessageType.Text) throw new Exception("Audio format metadata missing");
            var format = JsonDocument.Parse(bytes.AsMemory(0, header.Count)).RootElement;
            if (format.GetProperty("channels").GetInt32() != 2 || format.GetProperty("bits").GetInt32() != 16) throw new Exception("Invalid PCM format");
            var pcm = await socket.ReceiveAsync(bytes.AsMemory(), deadline.Token);
            if (pcm.MessageType != WebSocketMessageType.Binary || pcm.Count == 0 || pcm.Count % 4 != 0) throw new Exception("Invalid PCM frame");
            Console.WriteLine("PASS: Actual WASAPI loopback -> authenticated TLS WebSocket -> PCM16 stereo; buffers kept only in memory");
            remote.SetAudioAllowed(false);
            var ended = false;
            try
            {
                for (var i = 0; i < 30; i++)
                {
                    var result = await socket.ReceiveAsync(bytes.AsMemory(), deadline.Token);
                    if (result.MessageType == WebSocketMessageType.Close) { ended = true; break; }
                }
            }
            catch (WebSocketException) { ended = true; }
            if (!ended) throw new Exception("Audio did not stop after permission withdrawal");
            Console.WriteLine("PASS: Withdrawing local audio permission interrupts the running transport");
            silence.Stop();
        }
        finally { remote.Interrupt(); await server.StopAsync(); }
    }
}
