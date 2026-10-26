using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Ponte.Core;
using Ponte.Windows;

static class AndroidViewerInputSmoke
{
    public static async Task RunAsync()
    {
        var root = Path.GetFullPath(Path.Combine(".cache", "viewer-input", Guid.NewGuid().ToString("N")));
        var devices = new DeviceRegistry(root); var phone = devices.Register("Telefone sintético");
        using var key = RSA.Create(2048);
        using var generated = new CertificateRequest("CN=Ponte viewer input test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var cert = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet);
        var hub = new AndroidScreenHub();
        var server = new TransferServer(devices, new FileLibrary(Path.Combine(root, "files")), (_, _) => Task.FromResult(false), () => { }, androidScreen: hub);
        await server.StartAsync(cert);
        AndroidViewer? window = null;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previousWindow = GetForegroundWindow(); GetCursorPos(out var previousCursor);
        var ui = new Thread(() =>
        {
            try
            {
                window = new AndroidViewer(hub, phone.Device) { Title = "Ponte · TESTE ISOLADO DO VISUALIZADOR", Left = 80, Top = 80, Width = 480, Height = 780, WindowStartupLocation = WindowStartupLocation.Manual };
                window.Closed += (_, _) => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                window.Loaded += (_, _) => ready.TrySetResult(); window.Show(); Dispatcher.Run(); exited.TrySetResult();
            }
            catch (Exception e) { ready.TrySetException(e); exited.TrySetException(e); }
        }) { IsBackground = true };
        ui.SetApartmentState(ApartmentState.STA); ui.Start();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(18));
            await ready.Task.WaitAsync(deadline.Token);
            using var socket = new ClientWebSocket(); socket.Options.SetRequestHeader("Authorization", "Bearer " + phone.Token);
            socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString(HashAlgorithmName.SHA256) == cert.GetCertHashString(HashAlgorithmName.SHA256);
            await socket.ConnectAsync(new Uri($"wss://[::1]:{TransferServer.Port}/api/android/stream"), deadline.Token);
            using var bitmap = new System.Drawing.Bitmap(360, 640);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.Clear(System.Drawing.Color.Teal);
            using var bytes = new MemoryStream(); bitmap.Save(bytes, System.Drawing.Imaging.ImageFormat.Jpeg);
            var jpeg = bytes.ToArray(); var buffer = new byte[8192];
            async Task ReceiveKind(string kind)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token);
                if (result.MessageType != WebSocketMessageType.Text || JsonDocument.Parse(buffer.AsMemory(0, result.Count)).RootElement.GetProperty("kind").GetString() != kind) throw new Exception("Expected viewer command: " + kind);
            }
            async Task Frame() { await socket.SendAsync(jpeg.AsMemory(), WebSocketMessageType.Binary, true, deadline.Token); await ReceiveKind("ack"); }
            await Frame();
            await window!.Dispatcher.InvokeAsync(() =>
            {
                var handle = new WindowInteropHelper(window).Handle;
                var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _); var own = GetCurrentThreadId();
                var attached = foregroundThread != 0 && foregroundThread != own && AttachThreadInput(own, foregroundThread, true);
                try { window.Topmost = true; window.Activate(); SetForegroundWindow(handle); window.Topmost = false; }
                finally { if (attached) AttachThreadInput(own, foregroundThread, false); }
            });
            await Task.Delay(150, deadline.Token);
            Image FindImage(DependencyObject parent)
            {
                if (parent is Image image) return image;
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var found = FindImageOrNull(VisualTreeHelper.GetChild(parent, i)); if (found is not null) return found; }
                throw new Exception("Viewer image missing");
            }
            Image? FindImageOrNull(DependencyObject parent) { try { return FindImage(parent); } catch (Exception) { return null; } }
            async Task Mouse(string kind, double offset = 0)
            {
                await window.Dispatcher.InvokeAsync(() =>
                {
                    if (GetForegroundWindow() != new WindowInteropHelper(window).Handle) throw new Exception("Viewer lost focus: no native input sent to another window");
                    if (new[] { 0x10, 0x11, 0x12, 0x5b, 0x5c }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0)) throw new Exception("Release keyboard modifiers before isolated input test");
                    var image = FindImage(window); var point = image.PointToScreen(new Point(image.ActualWidth / 2 + offset, image.ActualHeight / 2));
                    var monitor = NativeDesktop.Monitors().Single(m => point.X >= m.Left && point.X < m.Left + m.Width && point.Y >= m.Top && point.Y < m.Top + m.Height);
                    NativeDesktop.Execute(new RemoteCommand(kind, (point.X - monitor.Left) / (monitor.Width - 1), (point.Y - monitor.Top) / (monitor.Height - 1)), new StreamSettings(monitor.Id));
                });
                await Task.Delay(80, deadline.Token);
            }
            await Mouse("move"); await Mouse("mousedown"); await Mouse("mouseup"); await ReceiveKind("tap");
            Console.WriteLine("PASS: Actual Windows mouse click -> WPF viewer -> authenticated Android command socket");
            await Mouse("mousedown"); await Frame(); await Mouse("move", 55); await Mouse("mouseup", 55); await ReceiveKind("swipe");
            Console.WriteLine("PASS: New screen frame during held mouse does not lose viewer drag; swipe reaches phone transport");
        }
        finally
        {
            NativeDesktop.ReleaseButtons();
            if (window is not null && !window.Dispatcher.HasShutdownStarted) await window.Dispatcher.InvokeAsync(() => { if (GetForegroundWindow() == new WindowInteropHelper(window).Handle) { SetCursorPos(previousCursor.X, previousCursor.Y); if (previousWindow != IntPtr.Zero) SetForegroundWindow(previousWindow); } window.Close(); });
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(5)); await server.StopAsync();
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint thread, uint otherThread, bool attach);
}
