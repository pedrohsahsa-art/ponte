using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Ponte.Core;

namespace Ponte.Windows;

// Standard-user desktop only. No elevation, service, secure-desktop bypass,
// shell execution or arbitrary virtual-key injection. Held left button is scoped
// to the authorized session and released by RemoteAccess watchdog/revocation.
public static class NativeDesktop
{
    private static readonly ImageCodecInfo JpegEncoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
    public static (int Width, int Height) GetPrimarySize()
    {
        using var dpi = new DpiScope();
        return (GetSystemMetrics(0), GetSystemMetrics(1));
    }
    public static bool IsAvailable()
    {
        var desktop = OpenInputDesktop(0, false, 0x0001);
        if (desktop == IntPtr.Zero) return false;
        try
        {
            var name = new StringBuilder(256);
            return GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out _) && name.ToString() == "Default";
        }
        finally { CloseDesktop(desktop); }
    }

    public static IReadOnlyList<DesktopMonitor> Monitors()
    {
        using var dpi = new DpiScope();
        var result = new List<DesktopMonitor>();
        MonitorCallback callback = (IntPtr handle, IntPtr dc, ref Rect rectangle, IntPtr data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (GetMonitorInfo(handle, ref info)) result.Add(new DesktopMonitor(info.Device, info.Device.Replace("\\\\.\\", "Monitor "), info.Monitor.Left, info.Monitor.Top,
                info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top, (info.Flags & 1) != 0));
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return result.OrderByDescending(m => m.Primary).ThenBy(m => m.Id).ToArray();
    }
    public static DesktopMonitor Monitor(StreamSettings settings) => Monitors().FirstOrDefault(m => settings.MonitorId.Length == 0 ? m.Primary : m.Id == settings.MonitorId)
        ?? throw new RemoteAccessException(RemoteFailure.DesktopUnavailable);
    public static DesktopFrame Capture() => Capture(new StreamSettings(Profile: "sharp"));
    public static DesktopFrame Capture(StreamSettings settings)
    {
        using var dpi = new DpiScope();
        var monitor = Monitor(settings);
        var sourceWidth = monitor.Width;
        var sourceHeight = monitor.Height;
        if (sourceWidth < 1 || sourceHeight < 1) throw new RemoteAccessException(RemoteFailure.DesktopUnavailable);
        var limits = settings.Limits;
        var scale = Math.Min(1.0, Math.Min((double)limits.Width / sourceWidth, (double)limits.Height / sourceHeight));
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var source = GetDC(IntPtr.Zero);
            if (source == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var destination = graphics.GetHdc();
            try
            {
                SetStretchBltMode(destination, 4); // HALFTONE
                SetBrushOrgEx(destination, 0, 0, IntPtr.Zero);
                if (!StretchBlt(destination, 0, 0, width, height, source, monitor.Left, monitor.Top, sourceWidth, sourceHeight, 0x40CC0020))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                DrawCursor(destination, scale, monitor);
            }
            finally { graphics.ReleaseHdc(destination); ReleaseDC(IntPtr.Zero, source); }
        }
        using var output = new MemoryStream();
        using var quality = new EncoderParameters(1);
        quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)limits.Quality);
        bitmap.Save(output, JpegEncoder, quality);
        return new DesktopFrame(output.ToArray(), width, height, monitor.Id);
    }

    public static void Execute(RemoteCommand command) => Execute(command, new StreamSettings());
    public static void ReleaseButtons() => SendInput(1, [Mouse(0x0004)], Marshal.SizeOf<Input>());
    public static void Execute(RemoteCommand command, StreamSettings settings)
    {
        if (!RemoteAccess.IsValid(command)) throw new RemoteAccessException(RemoteFailure.InvalidCommand);
        using var dpi = new DpiScope();
        var inputs = new List<Input>();
        if (command.Kind is "move" or "click" or "rightclick" or "doubleclick" or "mousedown" or "mouseup")
        {
            var monitor = Monitor(settings);
            var virtualWidth = Math.Max(2, GetSystemMetrics(78));
            var virtualHeight = Math.Max(2, GetSystemMetrics(79));
            var x = monitor.Left + command.X * (monitor.Width - 1);
            var y = monitor.Top + command.Y * (monitor.Height - 1);
            inputs.Add(Mouse(0xC001, (int)Math.Round((x - GetSystemMetrics(76)) * 65535 / (virtualWidth - 1)), (int)Math.Round((y - GetSystemMetrics(77)) * 65535 / (virtualHeight - 1))));
            if (command.Kind == "mousedown") inputs.Add(Mouse(0x0002));
            else if (command.Kind == "mouseup") inputs.Add(Mouse(0x0004));
            else if (command.Kind != "move")
            {
                var right = command.Kind == "rightclick";
                for (var index = 0; index < (command.Kind == "doubleclick" ? 2 : 1); index++)
                {
                    inputs.Add(Mouse(right ? 0x0008u : 0x0002u));
                    inputs.Add(Mouse(right ? 0x0010u : 0x0004u));
                }
            }
        }
        else if (command.Kind == "scroll") inputs.Add(Mouse(0x0800, data: unchecked((uint)command.Delta)));
        else if (command.Kind is "text" or "edit")
        {
            if (command.Kind == "edit")
                for (var i = 0; i < command.Delta; i++) { inputs.Add(Keyboard(0x08)); inputs.Add(Keyboard(0x08, flags: 2)); }
            foreach (var c in command.Text ?? "")
            {
                if (c is '\n' or '\t')
                {
                    var key = c == '\n' ? (ushort)0x0D : (ushort)0x09;
                    inputs.Add(Keyboard(key)); inputs.Add(Keyboard(key, flags: 2));
                }
                else
                {
                    inputs.Add(Keyboard(0, c, 0x0004));
                    inputs.Add(Keyboard(0, c, 0x0006));
                }
            }
        }
        else if (command.Kind == "key")
        {
            var (modifier, key) = command.Key switch
            {
                "copy" => (0x11, 0x43), "paste" => (0x11, 0x56), "selectall" => (0x11, 0x41), "undo" => (0x11, 0x5A), "alttab" => (0x12, 0x09),
                "enter" => (0, 0x0D), "backspace" => (0, 0x08), "tab" => (0, 0x09), "escape" => (0, 0x1B), "delete" => (0, 0x2E),
                "left" => (0, 0x25), "right" => (0, 0x27), "up" => (0, 0x26), "down" => (0, 0x28), "home" => (0, 0x24), "end" => (0, 0x23),
                "pageup" => (0, 0x21), "pagedown" => (0, 0x22), "volumeup" => (0, 0xAF), "volumedown" => (0, 0xAE), "mute" => (0, 0xAD),
                _ => throw new RemoteAccessException(RemoteFailure.InvalidCommand)
            };
            if (modifier != 0) inputs.Add(Keyboard((ushort)modifier));
            var extended = key is >= 0x21 and <= 0x2E || key is 0xAD or 0xAE or 0xAF;
            inputs.Add(Keyboard((ushort)key, flags: extended ? 1u : 0));
            inputs.Add(Keyboard((ushort)key, flags: extended ? 3u : 2));
            if (modifier != 0) inputs.Add(Keyboard((ushort)modifier, flags: 2));
        }
        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
        if (sent != inputs.Count)
        {
            // Best-effort release if Windows rejected part of a batch (e.g. UIPI).
            var release = inputs.Where(i => i.Type == 1).Select(i => { i.Data.Keyboard.Flags |= 2; return i; }).ToArray();
            if (release.Length > 0) SendInput((uint)release.Length, release, Marshal.SizeOf<Input>());
            throw new RemoteAccessException(RemoteFailure.DesktopUnavailable);
        }
    }

    private static Input Mouse(uint flags, int x = 0, int y = 0, uint data = 0) => new() { Type = 0, Data = new InputUnion { Mouse = new MouseInput { X = x, Y = y, Data = data, Flags = flags } } };
    private static Input Keyboard(ushort key, ushort scan = 0, uint flags = 0) => new() { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = key, Scan = scan, Flags = flags } } };
    private static void DrawCursor(IntPtr target, double scale, DesktopMonitor monitor)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info) || (info.Flags & 1) == 0 || info.Position.X < monitor.Left || info.Position.Y < monitor.Top || info.Position.X >= monitor.Left + monitor.Width || info.Position.Y >= monitor.Top + monitor.Height) return;
        if (!GetIconInfo(info.Cursor, out var icon)) return;
        try
        {
            DrawIconEx(target, (int)((info.Position.X - monitor.Left - icon.XHotspot) * scale), (int)((info.Position.Y - monitor.Top - icon.YHotspot) * scale), info.Cursor,
                Math.Max(8, (int)(GetSystemMetrics(13) * scale)), Math.Max(8, (int)(GetSystemMetrics(14) * scale)), 0, IntPtr.Zero, 3);
        }
        finally { if (icon.Mask != IntPtr.Zero) DeleteObject(icon.Mask); if (icon.Color != IntPtr.Zero) DeleteObject(icon.Color); }
    }
    private sealed class DpiScope : IDisposable
    {
        private readonly IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        public void Dispose() { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect rectangle, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size, Flags; public IntPtr Cursor; public Point Position; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { [MarshalAs(UnmanagedType.Bool)] public bool Icon; public uint XHotspot, YHotspot; public IntPtr Mask, Color; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, int sw, int sh, uint operation);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr previous);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr cursor, out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height, uint step, IntPtr brush, uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder buffer, int length, out int required);
}
