using System.Net;
using System.Security.Cryptography;
using Ponte.Windows;

static class LivePcProbe
{
    public static async Task RunAsync(string address, string dataDirectory)
    {
        if (!IPAddress.TryParse(address, out _)) throw new Exception("Informe o IP local do computador.");
        if (!File.Exists(Path.Combine(dataDirectory, "identity.dpapi"))) throw new Exception("A identidade existente do Ponte nao foi encontrada; nenhum certificado foi criado.");
        using var identity = CertificateIdentity.Load(dataDirectory);
        var pin = identity.GetCertHashString(HashAlgorithmName.SHA256);
        using var handler = new HttpClientHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.GetCertHashString(HashAlgorithmName.SHA256) == pin
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.GetAsync(new UriBuilder("https", address, TransferServer.Port, "/api/status").Uri);
        if (response.StatusCode != HttpStatusCode.Unauthorized) throw new Exception("Resposta inesperada ao teste sem credenciais: " + response.StatusCode);
        Console.WriteLine($"PASS: Ponte responds on local LAN {address}:{TransferServer.Port}, exact TLS identity verified, unauthenticated access rejected (expected HTTP 401).");
        Console.WriteLine("This verifies the local server, not Wi-Fi isolation or connectivity from the physical phone. No device was paired or granted control by this probe.");
    }
}
