using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Ponte.Core;

namespace Ponte.Windows;

internal static class RemoteScreenStream
{
    // One frame in flight, acknowledged AFTER Android decoding. No old-frame
    // backlog and no new TLS/HTTP request for each image. Inputs stay independent.
    public static async Task RunAsync(HttpContext context, RemoteAccess remote, DeviceRegistry devices, string deviceId, string session, CancellationToken stopping)
    {
        remote.ValidateSession(deviceId, session);
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping);
        var token = context.Request.Headers.Authorization.ToString()[7..];
        var reason = "stream_ended";
        try
        {
            var ack = new byte[16];
            while (!lifetime.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var started = Stopwatch.GetTimestamp();
                if (devices.AuthenticateDevice(token)?.Id != deviceId) { reason = "access_revoked"; break; }
                var frame = remote.Capture(deviceId, session);
                if (frame.Jpeg.Length > 4 * 1024 * 1024) { reason = "frame_too_large"; break; }
                var metadata = JsonSerializer.SerializeToUtf8Bytes(new { type = "frame", monitorId = frame.MonitorId, width = frame.Width, height = frame.Height });
                await socket.SendAsync(metadata.AsMemory(), WebSocketMessageType.Text, true, lifetime.Token);
                await socket.SendAsync(frame.Jpeg.AsMemory(), WebSocketMessageType.Binary, true, lifetime.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(15)); // transport health, NOT a permission/session expiry
                var received = await socket.ReceiveAsync(ack.AsMemory(), deadline.Token);
                if (received.MessageType == WebSocketMessageType.Close) break;
                if (received.MessageType != WebSocketMessageType.Binary || !received.EndOfMessage || received.Count != 1 || ack[0] != 1)
                { reason = "invalid_ack"; break; }
                var wait = TimeSpan.FromMilliseconds(remote.FrameInterval(deviceId, session)) - Stopwatch.GetElapsedTime(started);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, lifetime.Token);
            }
        }
        catch (RemoteAccessException ex)
        {
            reason = ex.Failure switch { RemoteFailure.Disabled => "access_revoked", RemoteFailure.StaleSession => "session_replaced", _ => "desktop_unavailable" };
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or System.IO.IOException) { }
        finally
        {
            try { remote.Close(deviceId, session); } catch (RemoteAccessException) { }
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var close = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, reason, close.Token); }
                catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
            }
        }
    }
}
