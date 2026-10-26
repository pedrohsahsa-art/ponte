using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ponte.Core;

namespace Ponte.Windows;

public record PairRequest(string Code, string Name);

public sealed class TransferServer(DeviceRegistry devices, FileLibrary files, Func<string, string, Task<bool>> approve, Action changed, RemoteAccess? remote = null, Action<string>? connectionObserved = null, Func<bool>? internetAllowed = null, Func<string?>? internetHostname = null, AndroidScreenHub? androidScreen = null)
{
    private WebApplication? app;
    private readonly LanDiscovery discovery = new();
    private readonly string fileSessionId = Guid.NewGuid().ToString("N");
    public const int Port = 47831;

    public async Task StartAsync(X509Certificate2 certificate)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        // Pairing codes and bearer tokens must never enter request logs.
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = null; // files stream to disk; no application size ceiling
            options.Limits.MaxConcurrentConnections = 128;
            options.Limits.MaxConcurrentUpgradedConnections = 8;
            options.Limits.MaxRequestHeadersTotalSize = 16384;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            options.ListenAnyIP(Port, listen => listen.UseHttps(certificate));
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetConcurrencyLimiter("server", _ => new ConcurrencyLimiterOptions { PermitLimit = 32, QueueLimit = 0 }));
            options.AddPolicy("pair", context =>
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("frames", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 12, Window = TimeSpan.FromSeconds(1), QueueLimit = 0 }));
            options.AddPolicy("commands", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = 80, Window = TimeSpan.FromSeconds(1), QueueLimit = 0 }));
        });
        app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(10) });
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            try { await next(context); }
            finally
            {
                var path = context.Request.Path.Value;
                if (connectionObserved is not null && !app.Lifetime.ApplicationStopping.IsCancellationRequested &&
                    path is "/api/status" or "/api/remote/session" or "/pair")
                    connectionObserved($"{DateTimeOffset.Now:HH:mm:ss} · cliente {context.Connection.RemoteIpAddress} · {path} · HTTP {context.Response.StatusCode}");
            }
        });
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            // Never trust Host/X-Forwarded-For for LAN pairing or Internet opt-in.
            var local = InternetConnectivity.IsLocal(context.Connection.RemoteIpAddress);
            if (!NetworkBoundary.MayAccess(local, context.Request.Path == "/pair", internetAllowed?.Invoke() == true))
            { context.Response.StatusCode = 403; return; }
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                var auth = context.Request.Headers.Authorization.ToString();
                var device = auth.Length == 71 && auth.StartsWith("Bearer ", StringComparison.Ordinal) ? devices.AuthenticateDevice(auth[7..]) : null;
                if (device is null)
                { context.Response.StatusCode = 401; return; }
                context.Items["device"] = device;
            }
            if (context.Request.Path.StartsWithSegments("/api/remote") || context.Request.Path == "/pair")
            {
                var size = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (size is { IsReadOnly: false }) size.MaxRequestBodySize = 8192;
            }
            try { await next(context); }
            catch (RemoteAccessException ex)
            {
                if (!context.Response.HasStarted) context.Response.StatusCode = ex.Failure switch
                { RemoteFailure.Disabled => 403, RemoteFailure.StaleSession => 409, RemoteFailure.InvalidCommand => 400, _ => 423 };
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (InvalidDataException) { if (!context.Response.HasStarted) context.Response.StatusCode = 413; }
            catch (IOException) { if (!context.Response.HasStarted) context.Response.StatusCode = 507; }
        });
        app.MapPost("/pair", async (PairRequest request, HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 80 || request.Name.Any(char.IsControl)) return Results.BadRequest();
            if (!devices.ConsumeTicket(request.Code)) return Results.Unauthorized();
            if (!await approve(request.Name, context.Connection.RemoteIpAddress?.ToString() ?? "?")) return Results.StatusCode(403);
            if (context.RequestAborted.IsCancellationRequested) return Results.StatusCode(408);
            var registration = devices.Register(request.Name);
            changed();
            return Results.Ok(new { token = registration.Token, deviceId = registration.Device.Id, pcName = Environment.MachineName });
        }).RequireRateLimiting("pair");
        app.MapGet("/api/status", () => Results.Ok(new { pcName = Environment.MachineName, version = "0.7.1", maxFileBytes = (long?)null, fileSession = "pc-restart", fileSessionId, liveKeyboard = true }));
        app.MapGet("/api/network/wake", (HttpContext context) => Results.Ok(LanDiscovery.WakeInfo(context.Connection.LocalIpAddress?.MapToIPv4())));
        app.MapGet("/api/network/internet", () => Results.Ok(new { enabled = internetAllowed?.Invoke() == true, hostname = internetAllowed?.Invoke() == true ? internetHostname?.Invoke() : null, ipv6 = internetAllowed?.Invoke() == true ? InternetConnectivity.StableAddresses() : [], port = Port }));
        app.MapGet("/api/android/stream", async (HttpContext context) =>
        {
            if (androidScreen is null) { context.Response.StatusCode = 503; return; }
            await androidScreen.RunAsync(context, devices, ((Device)context.Items["device"]!).Id, app.Lifetime.ApplicationStopping);
        });
        static string DeviceId(HttpContext context) => ((Device)context.Items["device"]!).Id;
        static string SessionId(HttpContext context) => context.Request.Headers["X-Ponte-Session"].ToString();
        app.MapGet("/api/remote/stream", async (HttpContext context) =>
        {
            if (remote is null) { context.Response.StatusCode = 503; return; }
            await RemoteScreenStream.RunAsync(context, remote, devices, DeviceId(context), SessionId(context), app.Lifetime.ApplicationStopping);
        });
        app.MapGet("/api/remote/audio", async (HttpContext context) =>
        {
            if (remote is null) { context.Response.StatusCode = 503; return; }
            await RemoteAudioStream.RunAsync(context, remote, devices, DeviceId(context), SessionId(context), app.Lifetime.ApplicationStopping);
        });
        app.MapGet("/api/remote/status", (HttpContext context) => remote is null ? Results.StatusCode(503) : Results.Ok(remote.StateFor(DeviceId(context))));
        app.MapGet("/api/remote/options", (HttpContext context) => remote is null ? Results.StatusCode(503) : Results.Ok(remote.Options(DeviceId(context), SessionId(context))));
        app.MapPost("/api/remote/options", (StreamSettings settings, HttpContext context) =>
        {
            if (remote is null) return Results.StatusCode(503);
            remote.Configure(DeviceId(context), SessionId(context), settings);
            return Results.NoContent();
        }).RequireRateLimiting("commands");
        app.MapGet("/api/remote/keyboard", (double? x, double? y, HttpContext context) => remote is null ? Results.StatusCode(503) :
            Results.Ok(remote.ReadKeyboardTarget(DeviceId(context), SessionId(context), x, y))).RequireRateLimiting("commands");
        app.MapPost("/api/remote/session", (HttpContext context) => remote is null ? Results.StatusCode(503) : Results.Ok(remote.Open(DeviceId(context))));
        app.MapDelete("/api/remote/session", (HttpContext context) =>
        {
            if (remote is null) return Results.StatusCode(503);
            remote.Close(DeviceId(context), SessionId(context));
            return Results.NoContent();
        });
        app.MapGet("/api/remote/frame", (HttpContext context) =>
        {
            if (remote is null) return Results.StatusCode(503);
            var frame = remote.Capture(DeviceId(context), SessionId(context));
            return Results.Bytes(frame.Jpeg, "image/jpeg");
        }).RequireRateLimiting("frames");
        app.MapPost("/api/remote/input", (RemoteCommand command, HttpContext context) =>
        {
            if (remote is null) return Results.StatusCode(503);
            remote.Execute(DeviceId(context), SessionId(context), command);
            return Results.NoContent();
        }).RequireRateLimiting("commands");
        app.MapGet("/api/files", () => Results.Ok(files.List()));
        app.MapGet("/api/files/{id}", (string id) =>
        {
            var file = files.Find(id);
            return file is null ? Results.NotFound() : Results.File(file.Value.Path, "application/octet-stream", file.Value.Info.Name, enableRangeProcessing: true);
        });
        app.MapPost("/api/files", async (HttpContext context) =>
        {
            var name = context.Request.Query["name"].ToString();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 512) return Results.BadRequest();
            var file = await files.AddAsync(context.Request.Body, name, "Android", context.RequestAborted);
            changed();
            return Results.Ok(file);
        });
        await app.StartAsync();
        discovery.Start(certificate);
    }
    public async Task StopAsync()
    {
        discovery.Dispose();
        if (app is null) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await app.StopAsync(timeout.Token);
        await app.DisposeAsync();
    }
}
