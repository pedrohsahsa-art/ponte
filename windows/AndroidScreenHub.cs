using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Ponte.Core;

namespace Ponte.Windows;

public sealed class AndroidScreenHub
{
    private readonly object gate = new();
    private Lease? viewer;
    private sealed class Lease(string device, Action<byte[]> frame, Action<string> status) : IDisposable
    {
        public readonly string Device = device;
        public readonly Action<byte[]> Frame = frame;
        public readonly Action<string> Status = status;
        public readonly CancellationTokenSource Closed = new();
        public Channel<byte[]>? Commands;
        public bool Connected;
        public void Dispose() => Closed.Cancel();
    }
    public IDisposable OpenViewer(string device, Action<byte[]> frame, Action<string> status)
    {
        lock (gate)
        {
            if (viewer is not null) throw new InvalidOperationException("Feche o visualizador anterior antes de abrir outro telefone.");
            var lease = new Lease(device, frame, status); viewer = lease;
            return new ViewerCloser(this, lease);
        }
    }
    private sealed class ViewerCloser(AndroidScreenHub hub, Lease lease) : IDisposable
    { public void Dispose() { lock (hub.gate) { if (hub.viewer != lease) return; lease.Dispose(); hub.viewer = null; } } }
    public void Interrupt(string? device = null) { lock (gate) { if (device is not null && viewer?.Device != device) return; viewer?.Commands?.Writer.TryComplete(); viewer?.Closed.Cancel(); } }
    public bool Send(AndroidCommand command)
    {
        if (!command.Valid) return false;
        lock (gate) return viewer is { Connected: true } lease && !lease.Closed.IsCancellationRequested && lease.Commands?.Writer.TryWrite(JsonSerializer.SerializeToUtf8Bytes(command, JsonSerializerOptions.Web)) == true;
    }
    public async Task RunAsync(HttpContext context, DeviceRegistry devices, string device, CancellationToken stopping)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        Lease lease;
        lock (gate)
        {
            if (viewer is null || viewer.Device != device || viewer.Closed.IsCancellationRequested) { context.Response.StatusCode = 403; return; }
            lease = viewer;
            if (lease.Connected) { context.Response.StatusCode = 409; return; }
            lease.Connected = true;
            lease.Commands = Channel.CreateBounded<byte[]>(32);
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping, lease.Closed.Token);
        WebSocket? socket = null;
        Task? sender = null, authorization = null;
        try
        {
            socket = await context.WebSockets.AcceptWebSocketAsync();
            var bearer = context.Request.Headers.Authorization.ToString()[7..];
            lease.Status("Tela conectada. Aguardando diagnóstico de controle do Android…");
            var transport = socket;
            sender = Task.Run(async () =>
            {
                try { await foreach (var command in lease.Commands.Reader.ReadAllAsync(lifetime.Token)) await transport.SendAsync(command.AsMemory(), WebSocketMessageType.Text, true, lifetime.Token); }
                finally { lifetime.Cancel(); }
            });
            authorization = Task.Run(async () =>
            {
                while (!lifetime.IsCancellationRequested)
                {
                    if (devices.AuthenticateDevice(bearer)?.Id != device) { lifetime.Cancel(); break; }
                    await Task.Delay(250, lifetime.Token);
                }
            });
            var buffer = new byte[AndroidFrameValidation.MaxBytes + 1];
            while (!lifetime.IsCancellationRequested)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(12));
                var count = 0;
                ValueWebSocketReceiveResult packet;
                do
                {
                    if (count >= buffer.Length) throw new InvalidDataException();
                    packet = await socket.ReceiveAsync(buffer.AsMemory(count), deadline.Token);
                    if (packet.MessageType == WebSocketMessageType.Close) return;
                    count += packet.Count;
                    if (packet.MessageType == WebSocketMessageType.Text && count > 1024) throw new InvalidDataException();
                } while (!packet.EndOfMessage);
                if (packet.MessageType == WebSocketMessageType.Text)
                {
                    using var json = JsonDocument.Parse(buffer.AsMemory(0, count));
                    if (json.RootElement.GetProperty("type").GetString() != "controlStatus") throw new InvalidDataException();
                    var message = AndroidControlFeedback.Message(json.RootElement.GetProperty("code").GetString());
                    if (message is null) throw new InvalidDataException();
                    lease.Status(message);
                    continue;
                }
                if (!AndroidFrameValidation.ValidJpeg(buffer.AsSpan(0, count))) throw new InvalidDataException();
                lease.Frame(buffer.AsSpan(0, count).ToArray()); // callback decodes before ACK
                if (!lease.Commands.Writer.TryWrite("{\"kind\":\"ack\"}"u8.ToArray())) throw new IOException();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException or InvalidOperationException or NotSupportedException or FormatException or JsonException or KeyNotFoundException) { }
        finally
        {
            lifetime.Cancel(); socket?.Abort(); socket?.Dispose();
            if (sender is not null) try { await sender; } catch (Exception) { }
            if (authorization is not null) try { await authorization; } catch (OperationCanceledException) { }
            lock (gate) { lease.Connected = false; lease.Commands = null; }
            lease.Status("Compartilhamento encerrado. Inicie novamente no Android para autorizar outra sessão.");
        }
    }
}
