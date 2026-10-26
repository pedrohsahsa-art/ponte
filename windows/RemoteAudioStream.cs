using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using NAudio.Wave;
using Ponte.Core;

namespace Ponte.Windows;

internal static class RemoteAudioStream
{
    private static int active;
    public static async Task RunAsync(HttpContext context, RemoteAccess remote, DeviceRegistry devices, string device, string session, CancellationToken stopping)
    {
        remote.ValidateAudio(device, session);
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0) { context.Response.StatusCode = 409; return; }
        try
        {
            using var capture = new WasapiLoopbackCapture(); // output only, never microphone
            var format = capture.WaveFormat;
            var provider = new BufferedWaveProvider(format) { ReadFully = false, BufferDuration = TimeSpan.FromSeconds(1) };
            var samples = provider.ToSampleProvider();
            var queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(6) { FullMode = BoundedChannelFullMode.Wait });
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping);
            void CancelCapture() { try { lifetime.Cancel(); } catch (ObjectDisposedException) { } }
            capture.DataAvailable += (_, e) =>
            {
                try
                {
                    provider.AddSamples(e.Buffer, 0, e.BytesRecorded);
                    var values = new float[e.BytesRecorded / format.BlockAlign * format.Channels];
                    var count = samples.Read(values, 0, values.Length);
                    var frames = count / format.Channels;
                    var pcm = new byte[frames * 4];
                    for (var f = 0; f < frames; f++)
                        for (var c = 0; c < 2; c++)
                        {
                            var value = values[f * format.Channels + Math.Min(c, format.Channels - 1)];
                            var sample = (short)(Math.Clamp(float.IsFinite(value) ? value : 0, -1f, 1f) * 32767);
                            pcm[f * 4 + c * 2] = (byte)sample;
                            pcm[f * 4 + c * 2 + 1] = (byte)(sample >> 8);
                        }
                    if (pcm.Length > 0 && !queue.Writer.TryWrite(pcm)) CancelCapture();
                }
                catch { CancelCapture(); }
            };
            capture.RecordingStopped += (_, _) => CancelCapture();
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var receiver = WatchCloseAsync(socket, lifetime);
            var bearer = context.Request.Headers.Authorization.ToString()[7..];
            try
            {
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { sampleRate = format.SampleRate, channels = 2, bits = 16 }).AsMemory(), WebSocketMessageType.Text, true, lifetime.Token);
                capture.StartRecording();
                while (!lifetime.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    remote.ValidateAudio(device, session);
                    if (devices.AuthenticateDevice(bearer)?.Id != device) break;
                    if (queue.Reader.TryRead(out var pcm))
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        deadline.CancelAfter(TimeSpan.FromSeconds(1));
                        await socket.SendAsync(pcm.AsMemory(), WebSocketMessageType.Binary, true, deadline.Token);
                    }
                    else await Task.Delay(25, lifetime.Token);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or RemoteAccessException) { }
            finally { lifetime.Cancel(); capture.StopRecording(); socket.Abort(); await receiver; }
        }
        finally { Interlocked.Exchange(ref active, 0); }
    }
    private static async Task WatchCloseAsync(WebSocket socket, CancellationTokenSource lifetime)
    {
        try { await socket.ReceiveAsync(new byte[8].AsMemory(), lifetime.Token); }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
        finally { lifetime.Cancel(); }
    }
}
