using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Ponte.Core;
using Ponte.Windows;

static class AndroidProtocolHost
{
    public static async Task RunAsync(string configPath)
    {
        // This test-only host uses isolated data and automatic approval; never
        // enabled by the production executable or its configuration.
        var directory = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        Directory.CreateDirectory(directory);
        var devices = new DeviceRegistry(directory);
        var files = new FileLibrary(Path.Combine(directory, "files"));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Ponte Android integration test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet);
        // Synthetic frame only: integration tests never transmit the real desktop.
        using var bitmap = new System.Drawing.Bitmap(1920, 1080);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            for (var y = 0; y < bitmap.Height; y += 24)
            for (var x = 0; x < bitmap.Width; x += 24)
            {
                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb((x * 7 + y) % 256, (y * 11 + x) % 256, (x + y * 3) % 256));
                graphics.FillRectangle(brush, x, y, 24, 24);
            }
        }
        using var image = new MemoryStream();
        bitmap.Save(image, System.Drawing.Imaging.ImageFormat.Jpeg);
        var jpeg = image.ToArray();
        var remote = new RemoteAccess(() => new DesktopFrame(jpeg, 1920, 1080), _ => { }, () => true,
            keyboardTarget: (_, _) => new KeyboardTarget(true, false, "synthetic-edit"),
            monitors: () => [new("main", "Main", 0, 0, 1920, 1080, true), new("left", "Left", -1920, 0, 1920, 1080, false)],
            configuredCapture: settings => new DesktopFrame(jpeg, 1920, 1080, settings.MonitorId.Length == 0 ? "main" : settings.MonitorId));
        var server = new TransferServer(devices, files, (_, _) => Task.FromResult(true), () =>
        {
            var first = devices.List().FirstOrDefault();
            if (first is not null && !remote.StateFor(first.Id).Enabled) remote.EnableFor(first.Id);
        }, remote);
        await server.StartAsync(certificate);
        try
        {
            var ticket = devices.CreateTicket();
            var json = JsonSerializer.Serialize(new
            {
                host = "127.0.0.1", port = TransferServer.Port,
                fingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256), code = ticket.Code, frameBase64 = Convert.ToBase64String(jpeg)
            });
            await File.WriteAllTextAsync(configPath + ".tmp", json);
            File.Move(configPath + ".tmp", configPath);
            await Task.WhenAny(Console.In.ReadLineAsync(), Task.Delay(TimeSpan.FromMinutes(5)));
        }
        finally { await server.StopAsync(); FileLibrary.BeginNewSession(Path.Combine(directory, "files")); }
    }
}
