using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Ponte.Core;
using Ponte.Windows;

if (args.Length == 3 && args[0] == "--probe-pc")
{
    await LivePcProbe.RunAsync(args[1], args[2]);
    return;
}
if (args.Length == 2 && args[0] == "--android-host")
{
    await AndroidProtocolHost.RunAsync(args[1]);
    return;
}
if (args.Length == 1 && args[0] == "--native-smoke")
{
    await NativeDesktopSmoke.RunAsync();
    return;
}
if (args.Length == 1 && args[0] == "--viewer-input-smoke")
{
    await AndroidViewerInputSmoke.RunAsync();
    return;
}
if (args.Length == 1 && args[0] == "--capture-benchmark")
{
    CaptureBenchmark.Run();
    return;
}
if (args.Length == 1 && args[0] == "--audio-smoke")
{
    await AudioLoopbackSmoke.RunAsync();
    return;
}

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.cache/tests", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var assertions = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    assertions++;
    Console.WriteLine("PASS: " + message);
}
RemoteAccessTests.Run(Check, root);
NetworkAddressTests.Run(Check);
NetworkBoundaryTests.Run(Check);
await FileSessionTests.RunAsync(root, Check);
await DuckDnsTests.RunAsync(root, Check);

var clock = new FakeClock();
var registryPath = Path.Combine(root, "registry");
var registry = new DeviceRegistry(registryPath, clock);
var ticket = registry.CreateTicket();
Check(!registry.ConsumeTicket("incorrect"), "Wrong pairing code rejected");
Check(registry.ConsumeTicket(ticket.Code), "Valid pairing code accepted");
Check(!registry.ConsumeTicket(ticket.Code), "Pairing code is single use");
ticket = registry.CreateTicket();
clock.Now = clock.Now.AddMinutes(6);
Check(!registry.ConsumeTicket(ticket.Code), "Expired pairing code rejected");
var oldTicket = registry.CreateTicket();
var newTicket = registry.CreateTicket();
Check(!registry.ConsumeTicket(oldTicket.Code), "Regeneration invalidates previous code");
Check(registry.ConsumeTicket(newTicket.Code), "New code remains valid");
var registration = registry.Register("Android de teste");
Check(registry.Authenticate(registration.Token), "Registered bearer token accepted");
Check(!registry.Authenticate(new string('0', 64)), "Unknown bearer token rejected");
Check(!File.ReadAllText(Path.Combine(registryPath, "devices.json")).Contains(registration.Token), "Plaintext bearer token is not persisted");
Check(new DeviceRegistry(registryPath).Authenticate(registration.Token), "Pairing survives restart");
registry.Revoke(registration.Device.Id);
Check(!registry.Authenticate(registration.Token), "Revocation takes effect immediately");
Check(!new DeviceRegistry(registryPath).Authenticate(registration.Token), "Revocation survives restart");
var concurrentTicket = registry.CreateTicket();
var successfulClaims = 0;
Parallel.For(0, 24, _ => { if (registry.ConsumeTicket(concurrentTicket.Code)) Interlocked.Increment(ref successfulClaims); });
Check(successfulClaims == 1, "Concurrent pairing claims cannot reuse a code");
using (var identity = CertificateIdentity.Load(registryPath))
using (var identityReloaded = CertificateIdentity.Load(registryPath))
{
    Check(identity.HasPrivateKey && identityReloaded.HasPrivateKey, "Windows identity retains its TLS private key");
    Check(identity.GetCertHashString(HashAlgorithmName.SHA256) == identityReloaded.GetCertHashString(HashAlgorithmName.SHA256), "DPAPI-protected TLS identity survives reload");
}

var files = new FileLibrary(Path.Combine(root, "files"));
var data = RandomNumberGenerator.GetBytes(200_000);
var added = await files.AddAsync(new MemoryStream(data), "../foto:2026.png", "test");
Check(added.Size == data.Length, "Streamed upload retains byte count");
Check(!added.Name.Contains('/') && !added.Name.Contains(':'), "Unsafe display names sanitized");
Check(files.Find("../../devices") is null, "Path traversal rejected");
Check(files.Find(new string('z', 32)) is null, "Invalid identifiers rejected");
Check(File.ReadAllBytes(files.Find(added.Id)!.Value.Path).SequenceEqual(data), "File bytes round-trip unchanged");
Check(new FileLibrary(Path.Combine(root, "files")).List().Count == 1, "File metadata survives restart");
var before = files.List().Count;
try { await files.AddAsync(new BrokenStream(), "interrupted.txt", "test"); throw new Exception("Expected stream failure"); }
catch (IOException) { }
Check(files.List().Count == before, "Interrupted uploads not published");
Check(!Directory.EnumerateFiles(Path.Combine(root, "files"), "*.partial").Any(), "Interrupted temporary file cleaned up");
using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    try { await files.AddAsync(new MemoryStream(data), "cancelled", "test", cancellation.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { }
}
Check(files.List().Count == before, "Cancelled uploads not published");
var boundedPath = Path.Combine(root, "bounded-files");
var bounded = new FileLibrary(boundedPath, 64);
try { await bounded.AddAsync(new MemoryStream(new byte[65]), "oversized", "test"); throw new Exception("Expected size limit failure"); }
catch (InvalidDataException) { }
Check(bounded.List().Count == 0 && !Directory.EnumerateFiles(boundedPath).Any(), "Size limit stops publication and cleans temporary data");
var boundaryFile = await bounded.AddAsync(new MemoryStream(new byte[64]), "exact-limit", "test");
Check(boundaryFile.Size == 64, "File at exact size limit is accepted");

using var rsa = RSA.Create(2048);
var request = new CertificateRequest("CN=Ponte integration test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
using var generatedCertificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
using var certificate = X509CertificateLoader.LoadPkcs12(generatedCertificate.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet);
var apiRegistry = new DeviceRegistry(Path.Combine(root, "api-registry"));
var apiFiles = new FileLibrary(Path.Combine(root, "api-files"));
var approvalAllowed = true;
var approvalCount = 0;
var remoteInputs = new List<RemoteCommand>();
var remote = new RemoteAccess(() => new DesktopFrame([0xFF, 0xD8, 0xFF, 0xD9], 10, 10), remoteInputs.Add, () => true,
    keyboardTarget: (_, _) => new KeyboardTarget(true, false, "test-focus"));
var observations = new System.Collections.Concurrent.ConcurrentQueue<string>();
var androidHub = new AndroidScreenHub();
var server = new TransferServer(apiRegistry, apiFiles, (_, _) => { approvalCount++; return Task.FromResult(approvalAllowed); }, () => { }, remote, observations.Enqueue, androidScreen: androidHub);
await server.StartAsync(certificate);
try
{
    using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.GetCertHashString(HashAlgorithmName.SHA256) == certificate.GetCertHashString(HashAlgorithmName.SHA256) };
    using var client = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{TransferServer.Port}") };
    Check((await client.GetAsync("/api/files")).StatusCode == HttpStatusCode.Unauthorized, "API rejects unauthenticated file listing");
    Check((await client.GetAsync("/api/network/wake")).StatusCode == HttpStatusCode.Unauthorized, "Wake network information requires paired authentication");
    Check((await client.GetAsync("/api/network/internet")).StatusCode == HttpStatusCode.Unauthorized, "Public IPv6 information requires paired authentication");
    using (var discovery = new System.Net.Sockets.UdpClient())
    {
        var nonce = Guid.NewGuid().ToString("N");
        await discovery.SendAsync(System.Text.Encoding.ASCII.GetBytes("PONTE_DISCOVER " + nonce), new IPEndPoint(IPAddress.Loopback, 47832));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var packet = await discovery.ReceiveAsync(deadline.Token);
        var reply = System.Text.Json.JsonDocument.Parse(packet.Buffer).RootElement;
        Check(reply.GetProperty("nonce").GetString() == nonce && reply.GetProperty("fingerprint").GetString() == certificate.GetCertHashString(HashAlgorithmName.SHA256), "LAN discovery returns matching nonce and certificate identity without credentials");
    }
    Check((await client.PostAsync("/api/remote/session", null)).StatusCode == HttpStatusCode.Unauthorized, "Remote API also requires paired authentication");
    using var wrongPinHandler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => false };
    using var wrongPinClient = new HttpClient(wrongPinHandler);
    var pinRejected = false;
    try { await wrongPinClient.GetAsync(client.BaseAddress); }
    catch (HttpRequestException) { pinRejected = true; }
    Check(pinRejected, "TLS rejects a certificate that does not match the pin");
    var badPair = await client.PostAsJsonAsync("/pair", new { code = "bad", name = "test" });
    Check(badPair.StatusCode == HttpStatusCode.Unauthorized && approvalCount == 0, "Bad pairing code never triggers approval");
    approvalAllowed = false;
    var deniedTicket = apiRegistry.CreateTicket();
    var denied = await client.PostAsJsonAsync("/pair", new { code = deniedTicket.Code, name = "test" });
    Check(denied.StatusCode == HttpStatusCode.Forbidden && apiRegistry.List().Count == 0, "PC denial prevents token issuance");
    approvalAllowed = true;
    var apiTicket = apiRegistry.CreateTicket();
    var pairResponse = await client.PostAsJsonAsync("/pair", new { code = apiTicket.Code, name = "test" });
    pairResponse.EnsureSuccessStatusCode();
    var pair = await pairResponse.Content.ReadFromJsonAsync<PairResult>();
    Check(pair?.Token.Length == 64 && approvalCount == 2, "Approved pairing issues a bearer token");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pair!.Token);
    Check((await client.GetAsync("/api/status")).IsSuccessStatusCode, "Paired client can access status");
    await AndroidStreamTests.RunAsync(androidHub, pair.Token, pair.DeviceId, certificate, Check);
    using (var ipv6Client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri($"https://[::1]:{TransferServer.Port}") })
    {
        Check((await ipv6Client.GetAsync("/api/status")).StatusCode == HttpStatusCode.Unauthorized, "Actual IPv6 TLS listener rejects unauthenticated access");
        ipv6Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pair.Token);
        Check((await ipv6Client.GetAsync("/api/status")).IsSuccessStatusCode, "Actual IPv6 TLS listener accepts the same pinned identity and bearer");
        using var internetJson = System.Text.Json.JsonDocument.Parse(await ipv6Client.GetStringAsync("/api/network/internet"));
        Check(!internetJson.RootElement.GetProperty("enabled").GetBoolean() && internetJson.RootElement.GetProperty("ipv6").GetArrayLength() == 0, "Public Internet access is opt-in and off by default");
    }
    Check((await client.PostAsync("/api/remote/session", null)).StatusCode == HttpStatusCode.Forbidden, "Remote HTTP access denied without separate PC consent");
    remote.EnableFor(pair.DeviceId);
    var remoteResponse = await client.PostAsync("/api/remote/session", null);
    remoteResponse.EnsureSuccessStatusCode();
    var remoteSession = (await remoteResponse.Content.ReadFromJsonAsync<RemoteSession>())!;
    Check((await client.GetAsync("/api/remote/frame")).StatusCode == HttpStatusCode.Conflict, "Remote HTTP frame requires session header");
    client.DefaultRequestHeaders.Add("X-Ponte-Session", remoteSession.SessionId);
    Check((await client.GetAsync("/api/remote/audio")).StatusCode == HttpStatusCode.Forbidden, "Audio endpoint refuses capture without separate local opt-in");
    var desktopOptions = await client.GetFromJsonAsync<DesktopOptions>("/api/remote/options");
    Check(desktopOptions is { AudioAllowed: false, Settings.Profile: "balanced" }, "Authenticated desktop options expose safe defaults");
    Check((await client.PostAsJsonAsync("/api/remote/options", new StreamSettings(Profile: "fluid"))).IsSuccessStatusCode, "Quality can be configured through the authenticated session");
    var frameResponse = await client.GetAsync("/api/remote/frame");
    Check(frameResponse.IsSuccessStatusCode && frameResponse.Content.Headers.ContentType?.MediaType == "image/jpeg", "Remote HTTP frame returns an authenticated JPEG");
    var commandResponse = await client.PostAsJsonAsync("/api/remote/input", new RemoteCommand("click", 0.5, 0.5));
    Check(commandResponse.StatusCode == HttpStatusCode.NoContent && remoteInputs.Count == 1, "Remote HTTP input reaches only the authorized adapter");
    Check((await client.PostAsJsonAsync("/api/remote/input", new RemoteCommand("shell"))).StatusCode == HttpStatusCode.BadRequest, "Remote HTTP rejects unknown commands");
    var keyboard = await client.GetFromJsonAsync<KeyboardTarget>("/api/remote/keyboard?x=0.5&y=0.5");
    Check(keyboard is { Editable: true, FocusId: "test-focus" }, "Authorized session can query editable-focus metadata without field contents");
    var edit = await client.PostAsJsonAsync("/api/remote/input", new RemoteCommand("edit", Delta: 1, Text: "á"));
    Check(edit.StatusCode == HttpStatusCode.NoContent && remoteInputs.Last().Kind == "edit", "Live edit sends deletion and replacement atomically to the adapter");
    Check((await client.PostAsJsonAsync("/api/remote/input", new RemoteCommand("edit", Delta: 501))).StatusCode == HttpStatusCode.BadRequest, "Live edit batches are bounded independently of file size");
    await RemoteStreamTests.RunAsync(pair.Token, remoteSession.SessionId, certificate, remote, Check);
    remote.Disable();
    Check((await client.GetAsync("/api/remote/frame")).StatusCode == HttpStatusCode.Forbidden, "Local stop immediately blocks HTTP frames");
    Check((await client.PostAsJsonAsync("/api/remote/input", new RemoteCommand("key", Key: "enter"))).StatusCode == HttpStatusCode.Forbidden && remoteInputs.Count == 2, "Local stop blocks HTTP input without injecting keys");
    using var content = new ByteArrayContent(data);
    var upload = await client.PostAsync("/api/files?name=foto%20a%C3%A7%C3%A3o.png", content);
    upload.EnsureSuccessStatusCode();
    var uploaded = await upload.Content.ReadFromJsonAsync<SharedFile>();
    Check(uploaded!.Name == "foto ação.png" && uploaded.Size == data.Length, "API upload retains Unicode filename and byte count");
    var listed = await client.GetFromJsonAsync<SharedFile[]>("/api/files");
    Check(listed?.Length == 1 && listed[0].Id == uploaded.Id, "API lists uploaded file");
    var downloaded = await client.GetByteArrayAsync("/api/files/" + uploaded.Id);
    Check(downloaded.SequenceEqual(data), "HTTPS file transfer preserves exact bytes");
    using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "api/files/" + uploaded.Id);
    rangeRequest.Headers.Range = new RangeHeaderValue(0, 31);
    using var rangeResponse = await client.SendAsync(rangeRequest);
    Check(rangeResponse.StatusCode == HttpStatusCode.PartialContent && (await rangeResponse.Content.ReadAsByteArrayAsync()).SequenceEqual(data[..32]), "Downloads support byte ranges");
    Check((await client.GetAsync("/api/files/not-a-guid")).StatusCode == HttpStatusCode.NotFound, "API rejects invalid file identifiers");
    Check(observations.Any(line => line.Contains("/api/remote/session") && line.Contains("HTTP 403")), "PC diagnostics distinguish missing consent from missing network traffic");
    Check(!observations.Any(line => line.Contains(pair.Token) || line.Contains(apiTicket.Code)), "Connection diagnostics never expose bearer tokens or pairing codes");
    apiRegistry.Revoke(pair.DeviceId);
    Check((await client.GetAsync("/api/files")).StatusCode == HttpStatusCode.Unauthorized, "Revoked client loses API access");
    await client.PostAsJsonAsync("/pair", new { code = "bad", name = "test" });
    await client.PostAsJsonAsync("/pair", new { code = "bad", name = "test" });
    var limited = await client.PostAsJsonAsync("/pair", new { code = "bad", name = "test" });
    Check((int)limited.StatusCode is 429 or 503, "Pairing endpoint rate-limits repeated attempts");
}
finally { await server.StopAsync(); }
Console.WriteLine($"\n{assertions} checks passed. Artifacts: {root}");

record PairResult(string Token, string DeviceId, string PcName);
sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
sealed class BrokenStream : MemoryStream
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("simulated failure");
}
