using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ponte.Core;

public record DesktopFrame(byte[] Jpeg, int Width, int Height, string MonitorId = "");
public record RemoteCommand(string Kind, double X = 0, double Y = 0, int Delta = 0, string? Text = null, string? Key = null);
public record RemoteSession(string SessionId, DateTimeOffset? ExpiresAt = null, int IntervalMs = 50);
public record RemoteState(bool Enabled, bool Connected, DateTimeOffset? ExpiresAt);
public record KeyboardTarget(bool Editable, bool Password = false, string FocusId = "", bool Available = true);
public enum RemoteFailure { Disabled, StaleSession, InvalidCommand, DesktopUnavailable }
public sealed class RemoteAccessException(RemoteFailure failure) : Exception(failure.ToString())
{
    public RemoteFailure Failure { get; } = failure;
}

// One phone at a time; explicit, persistent local consent until manual revocation.
// Capture/injection and revocation share a lock, so queued operations cannot run
// after Disable() has returned. Previously transmitted frames cannot be recalled.
public sealed class RemoteAccess(Func<DesktopFrame> capture, Action<RemoteCommand> execute,
    Func<bool> desktopAvailable, Action? changed = null, string? permissionPath = null,
    Func<double?, double?, KeyboardTarget>? keyboardTarget = null,
    Func<IReadOnlyList<DesktopMonitor>>? monitors = null,
    Func<StreamSettings, DesktopFrame>? configuredCapture = null,
    Action<RemoteCommand, StreamSettings>? configuredExecute = null,
    Func<StreamSettings, double?, double?, KeyboardTarget>? configuredKeyboard = null,
    Action? releaseButtons = null)
{
    private readonly object gate = new();
    private string? allowedDevice = LoadPermission(permissionPath), sessionId;
    private int consentPauses;
    private StreamSettings settings = new();
    private bool audioAllowed, dragging;
    private DateTimeOffset lastDrag;
    private Timer? dragWatchdog;
    private sealed record Permission(string? DeviceId);
    public string? AllowedDeviceId { get { lock (gate) return allowedDevice; } }
    public static IReadOnlySet<string> AllowedKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    { "enter", "backspace", "tab", "escape", "delete", "left", "right", "up", "down", "home", "end", "pageup", "pagedown", "copy", "paste", "selectall", "undo", "alttab", "volumeup", "volumedown", "mute" };

    public void EnableFor(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        lock (gate)
        {
            SavePermission(deviceId);
            ReleaseDrag();
            allowedDevice = deviceId;
            sessionId = null;
            changed?.Invoke();
        }
    }
    public void Disable()
    {
        lock (gate)
        {
            // Revoke live access even if the disk becomes unwritable.
            allowedDevice = null;
            ReleaseDrag();
            sessionId = null;
            changed?.Invoke();
            SavePermission(null);
        }
    }
    // Suspension is NOT revocation: lock, consent dialogs and shutdown must not
    // discard the user's persistent grant. Session credentials remain ephemeral.
    public void Interrupt()
    {
        lock (gate) { ReleaseDrag(); sessionId = null; changed?.Invoke(); }
    }
    public IDisposable PauseForConsent()
    {
        lock (gate) { ReleaseDrag(); consentPauses++; sessionId = null; changed?.Invoke(); }
        return new ResumeConsent(this);
    }
    private sealed class ResumeConsent(RemoteAccess owner) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            lock (owner.gate)
            {
                if (disposed) return;
                disposed = true;
                owner.consentPauses--;
                owner.NotifyChanged();
            }
        }
    }
    public RemoteState StateFor(string deviceId)
    {
        lock (gate)
        {
            var enabled = allowedDevice == deviceId;
            return new RemoteState(enabled, enabled && sessionId is not null, null);
        }
    }
    public RemoteSession Open(string deviceId)
    {
        lock (gate)
        {
            EnsureAllowed(deviceId);
            EnsureDesktop();
            ReleaseDrag();
            settings = new StreamSettings();
            sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            changed?.Invoke();
            return new RemoteSession(sessionId);
        }
    }
    public DesktopFrame Capture(string deviceId, string? session)
    {
        lock (gate)
        {
            EnsureSession(deviceId, session);
            try { return configuredCapture?.Invoke(settings) ?? capture(); }
            catch { ReleaseDrag(); throw new RemoteAccessException(RemoteFailure.DesktopUnavailable); }
        }
    }
    public void Execute(string deviceId, string? session, RemoteCommand command)
    {
        lock (gate)
        {
            EnsureSession(deviceId, session);
            if (!IsValid(command)) throw new RemoteAccessException(RemoteFailure.InvalidCommand);
            if (command.Kind == "dragkeepalive") { if (dragging) lastDrag = DateTimeOffset.UtcNow; return; }
            try
            {
                // Track intent before injection: SendInput may partially succeed.
                if (command.Kind == "mousedown") dragging = true;
                if (configuredExecute is not null) configuredExecute(command, settings); else execute(command);
                if (command.Kind == "mousedown")
                {
                    dragging = true;
                    lastDrag = DateTimeOffset.UtcNow;
                    dragWatchdog ??= new Timer(_ => { lock (gate) { try { if (dragging && (DateTimeOffset.UtcNow - lastDrag > TimeSpan.FromSeconds(2) || !desktopAvailable())) ReleaseDrag(); } catch { ReleaseDrag(); } } }, null, 250, 250);
                }
                if (command.Kind == "move" && dragging) lastDrag = DateTimeOffset.UtcNow;
                if (command.Kind == "mouseup") { dragging = false; dragWatchdog?.Dispose(); dragWatchdog = null; }
            }
            catch { ReleaseDrag(); throw new RemoteAccessException(RemoteFailure.DesktopUnavailable); }
        }
    }
    public void Close(string deviceId, string? session)
    {
        lock (gate)
        {
            EnsureSession(deviceId, session, false);
            ReleaseDrag();
            sessionId = null;
            changed?.Invoke();
        }
    }
    public KeyboardTarget ReadKeyboardTarget(string deviceId, string? session, double? x = null, double? y = null)
    {
        lock (gate)
        {
            EnsureSession(deviceId, session);
            if (x.HasValue != y.HasValue || x.HasValue && (!double.IsFinite(x.Value) || !double.IsFinite(y!.Value) || x < 0 || x > 1 || y < 0 || y > 1))
                throw new RemoteAccessException(RemoteFailure.InvalidCommand);
        }
        // UI Automation runs outside the capture/input lock and must be bounded.
        StreamSettings snapshot;
        lock (gate) snapshot = settings;
        var result = configuredKeyboard?.Invoke(snapshot, x, y) ?? keyboardTarget?.Invoke(x, y) ?? new KeyboardTarget(false, Available: false);
        lock (gate) { EnsureSession(deviceId, session); return result; }
    }
    public static bool IsValid(RemoteCommand command)
    {
        static bool Coordinate(double value) => double.IsFinite(value) && value >= 0 && value <= 1;
        return command.Kind switch
        {
            "move" or "click" or "rightclick" or "doubleclick" or "mousedown" or "mouseup" => Coordinate(command.X) && Coordinate(command.Y),
            "dragkeepalive" => true,
            "scroll" => command.Delta != 0 && command.Delta >= -1200 && command.Delta <= 1200 && command.Delta % 120 == 0,
            "text" => command.Text is { Length: > 0 and <= 500 } && !command.Text.Any(char.IsControl),
            "edit" => command.Delta is >= 0 and <= 500 && (command.Text?.Length ?? 0) <= 500 &&
                (command.Delta > 0 || !string.IsNullOrEmpty(command.Text)) &&
                !(command.Text ?? "").Any(c => char.IsControl(c) && c is not '\n' and not '\t'),
            "key" => command.Key is not null && AllowedKeys.Contains(command.Key),
            _ => false
        };
    }
    public void ValidateSession(string deviceId, string? session)
    {
        lock (gate) EnsureSession(deviceId, session);
    }
    public DesktopOptions Options(string deviceId, string? session)
    {
        lock (gate) { EnsureSession(deviceId, session); return new DesktopOptions(monitors?.Invoke() ?? [], settings, audioAllowed); }
    }
    public void Configure(string deviceId, string? session, StreamSettings value)
    {
        lock (gate)
        {
            EnsureSession(deviceId, session);
            if (!value.Valid || value.MonitorId is null || value.MonitorId.Length > 128 || value.MonitorId.Length > 0 && !(monitors?.Invoke() ?? []).Any(m => m.Id == value.MonitorId))
                throw new RemoteAccessException(RemoteFailure.InvalidCommand);
            if (value.MonitorId != settings.MonitorId) ReleaseDrag();
            settings = value;
        }
    }
    public int FrameInterval(string deviceId, string? session) { lock (gate) { EnsureSession(deviceId, session); return 1000 / settings.Limits.Fps; } }
    public void SetAudioAllowed(bool value) { lock (gate) audioAllowed = value; }
    public void ValidateAudio(string deviceId, string? session)
    { lock (gate) { EnsureSession(deviceId, session); if (!audioAllowed) throw new RemoteAccessException(RemoteFailure.Disabled); } }
    private void ReleaseDrag()
    {
        dragWatchdog?.Dispose(); dragWatchdog = null;
        if (!dragging) return;
        dragging = false;
        try { if (releaseButtons is not null) releaseButtons(); else execute(new RemoteCommand("mouseup")); } catch { /* best effort on secure desktop */ }
    }
    private void NotifyChanged() => changed?.Invoke();
    private void EnsureAllowed(string deviceId)
    {
        if (allowedDevice != deviceId) throw new RemoteAccessException(RemoteFailure.Disabled);
    }
    private void EnsureDesktop()
    {
        if (consentPauses == 0 && desktopAvailable()) return;
        ReleaseDrag();
        throw new RemoteAccessException(RemoteFailure.DesktopUnavailable);
    }
    private void EnsureSession(string deviceId, string? session, bool requireDesktop = true)
    {
        EnsureAllowed(deviceId);
        if (sessionId is null || session is null || session.Length != 64 ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(sessionId), Encoding.ASCII.GetBytes(session)))
            throw new RemoteAccessException(RemoteFailure.StaleSession);
        if (requireDesktop) EnsureDesktop();
    }
    private static string? LoadPermission(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        return JsonSerializer.Deserialize<Permission>(File.ReadAllText(path))?.DeviceId;
    }
    private void SavePermission(string? deviceId)
    {
        if (permissionPath is null) return;
        if (deviceId is null) { File.Delete(permissionPath); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(permissionPath))!);
        File.WriteAllText(permissionPath + ".tmp", JsonSerializer.Serialize(new Permission(deviceId)));
        File.Move(permissionPath + ".tmp", permissionPath, true);
    }
}
