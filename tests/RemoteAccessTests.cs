using Ponte.Core;

static class RemoteAccessTests
{
    public static void Run(Action<bool, string> check, string root)
    {
        var captured = 0;
        var injected = new List<RemoteCommand>();
        var available = true;
        var failInjection = false;
        var remote = new RemoteAccess(() => { captured++; return new DesktopFrame([0xFF, 0xD8, 0xFF, 0xD9], 10, 10); },
            command => { if (failInjection) throw new Exception("simulated native failure"); injected.Add(command); }, () => available);
        void Rejected(Action action, RemoteFailure reason, string message)
        {
            var rejected = false;
            try { action(); } catch (RemoteAccessException ex) { rejected = ex.Failure == reason; }
            check(rejected, message);
        }
        check(!remote.StateFor("phone").Enabled, "Remote access is disabled at startup");
        Rejected(() => remote.Open("phone"), RemoteFailure.Disabled, "Pairing alone cannot enable screen sharing");
        remote.EnableFor("phone");
        check(remote.StateFor("phone").Enabled && !remote.StateFor("other").Enabled, "PC consent is scoped to one selected phone");
        Rejected(() => remote.Open("other"), RemoteFailure.Disabled, "A different paired phone cannot open control");
        var session = remote.Open("phone");
        Rejected(() => remote.Capture("phone", null), RemoteFailure.StaleSession, "Frame capture requires a session credential");
        Rejected(() => remote.Execute("phone", new string('0', 64), new RemoteCommand("key", Key: "enter")), RemoteFailure.StaleSession, "Wrong remote session cannot inject input");
        remote.Capture("phone", session.SessionId);
        check(captured == 1, "Authorized session can capture a frame");
        remote.Execute("phone", session.SessionId, new RemoteCommand("click", 0.5, 0.5));
        check(injected.Count == 1, "Authorized valid input reaches the native adapter");
        foreach (var command in new[] {
            new RemoteCommand("shell", Text: "anything"), new RemoteCommand("key", Key: "arbitrary"),
            new RemoteCommand("move", double.NaN, 0), new RemoteCommand("click", -0.1, 0), new RemoteCommand("click", 1.1, 0),
            new RemoteCommand("scroll", Delta: 1201), new RemoteCommand("scroll", Delta: 1),
            new RemoteCommand("text", Text: "line\nbreak"), new RemoteCommand("text", Text: new string('x', 501)),
            new RemoteCommand("text", Text: "") })
            Rejected(() => remote.Execute("phone", session.SessionId, command), RemoteFailure.InvalidCommand, "Invalid remote command rejected: " + command.Kind);
        check(injected.Count == 1, "Rejected commands never reach native input");
        check(RemoteAccess.IsValid(new RemoteCommand("text", Text: "ação 😀")) && RemoteAccess.IsValid(new RemoteCommand("scroll", Delta: -120)), "Unicode text and bounded scroll are accepted");
        var replacement = remote.Open("phone");
        Rejected(() => remote.Capture("phone", session.SessionId), RemoteFailure.StaleSession, "Reconnecting rotates the session credential");
        remote.Close("phone", replacement.SessionId);
        check(!remote.StateFor("phone").Connected, "Closing a session stops active sharing");
        var disconnected = remote.Open("phone");
        check(disconnected.ExpiresAt is null && remote.StateFor("phone").ExpiresAt is null, "Neither grant nor session has a time expiry");
        var stopped = remote.Open("phone");
        remote.Disable();
        Rejected(() => remote.Execute("phone", stopped.SessionId, new RemoteCommand("key", Key: "enter")), RemoteFailure.Disabled, "Local emergency stop rejects queued input");
        remote.EnableFor("phone");
        var reenabled = remote.Open("phone");
        Rejected(() => remote.Capture("phone", stopped.SessionId), RemoteFailure.StaleSession, "Old session cannot revive after stop and re-enable");
        available = false;
        Rejected(() => remote.Capture("phone", reenabled.SessionId), RemoteFailure.DesktopUnavailable, "Protected or locked desktop stops screen capture");
        check(remote.StateFor("phone").Enabled, "Locking the desktop does not revoke the permanent grant");
        available = true;
        remote.EnableFor("phone");
        var failed = remote.Open("phone");
        failInjection = true;
        Rejected(() => remote.Execute("phone", failed.SessionId, new RemoteCommand("key", Key: "enter")), RemoteFailure.DesktopUnavailable, "Native injection failures fail closed");
        check(remote.StateFor("phone").Enabled, "Injection failure does not discard the user's grant");
        failInjection = false;
        remote.Execute("phone", failed.SessionId, new RemoteCommand("text", Text: "continua"));
        check(injected.Last().Text == "continua", "Input recovers without a new authorization");
        using (remote.PauseForConsent())
        {
            Rejected(() => remote.Open("phone"), RemoteFailure.DesktopUnavailable, "Consent dialog pauses remote connections");
            check(remote.StateFor("phone").Enabled, "Consent pause preserves the permanent authorization");
        }
        remote.Open("phone");
        remote.Interrupt();
        check(remote.StateFor("phone").Enabled && !remote.StateFor("phone").Connected, "Interrupt/shutdown clears only the live connection");
        var permissionPath = Path.Combine(root, "remote-permission.json");
        RemoteAccess Reload() => new(() => new DesktopFrame([], 10, 10), _ => { }, () => true, permissionPath: permissionPath);
        var persisted = Reload();
        check(!persisted.StateFor("phone").Enabled, "New installations still require explicit authorization");
        persisted.EnableFor("phone");
        var priorSession = persisted.Open("phone");
        persisted.Interrupt();
        var reloaded = Reload();
        check(reloaded.StateFor("phone").Enabled && !reloaded.StateFor("other").Enabled, "Grant survives restart for only the authorized phone");
        check(!reloaded.StateFor("phone").Connected, "Live session credentials are never persisted");
        Rejected(() => reloaded.Capture("phone", priorSession.SessionId), RemoteFailure.StaleSession, "Old session credential is invalid after restart");
        reloaded.Disable();
        check(!Reload().StateFor("phone").Enabled, "Manual revocation persists across restarts");
        var brokenPath = Path.Combine(root, "unwritable-permission.json");
        var broken = new RemoteAccess(() => new DesktopFrame([], 1, 1), _ => { }, () => true, permissionPath: brokenPath);
        broken.EnableFor("phone");
        File.Delete(brokenPath);
        Directory.CreateDirectory(brokenPath); // simulate a persistence failure without changing ACLs
        try { broken.Disable(); } catch (UnauthorizedAccessException) { }
        check(!broken.StateFor("phone").Enabled, "Manual stop still revokes live access when permission storage fails");
        check(!RemoteAccess.IsValid(new RemoteCommand("edit", Delta: -1)) && !RemoteAccess.IsValid(new RemoteCommand("edit", Text: "\0")) && !RemoteAccess.IsValid(new RemoteCommand("edit")), "Malformed or empty live edits are rejected");
        check(RemoteAccess.IsValid(new RemoteCommand("edit", Delta: 2, Text: "linha\n\tá😀")), "Live edit permits Unicode, Enter and Tab within bounded batches");
        RemoteAccess? revokedDuringProbe = null;
        revokedDuringProbe = new RemoteAccess(() => new DesktopFrame([], 1, 1), _ => { }, () => true,
            keyboardTarget: (_, _) => { revokedDuringProbe!.Disable(); return new KeyboardTarget(true); });
        revokedDuringProbe.EnableFor("phone");
        var probeSession = revokedDuringProbe.Open("phone");
        Rejected(() => revokedDuringProbe.ReadKeyboardTarget("phone", probeSession.SessionId), RemoteFailure.Disabled, "Metadata is revalidated if access is revoked during a focus probe");
        var released = 0;
        var selected = "";
        var failCapture = false;
        var configurable = new RemoteAccess(() => new DesktopFrame([], 1, 1), _ => { }, () => true,
            monitors: () => [new("left", "Left monitor", -1920, 0, 1920, 1080, false), new("main", "Main", 0, 0, 1920, 1080, true)],
            configuredCapture: settings => { if (failCapture) throw new IOException(); selected = settings.MonitorId; return new DesktopFrame([], 1, 1, selected); },
            releaseButtons: () => Interlocked.Increment(ref released));
        configurable.EnableFor("phone");
        var control = configurable.Open("phone").SessionId;
        Rejected(() => configurable.Options("other", control), RemoteFailure.Disabled, "Monitor options require authorized device and session");
        Rejected(() => configurable.Configure("phone", control, new("missing")), RemoteFailure.InvalidCommand, "Unknown monitors are rejected");
        Rejected(() => configurable.Configure("phone", control, new(Profile: "invalid")), RemoteFailure.InvalidCommand, "Unknown quality profiles are rejected");
        configurable.Configure("phone", control, new("left", "fluid"));
        configurable.Capture("phone", control);
        check(selected == "left" && configurable.FrameInterval("phone", control) == 66, "Monitor and fluid quality reach capture and pacing");
        Rejected(() => configurable.ValidateAudio("phone", control), RemoteFailure.Disabled, "PC audio requires separate local permission");
        configurable.SetAudioAllowed(true);
        configurable.ValidateAudio("phone", control);
        configurable.Execute("phone", control, new("mousedown", .5, .5));
        configurable.Configure("phone", control, new("main"));
        check(released == 1, "Changing monitor releases held mouse button");
        configurable.Execute("phone", control, new("mousedown"));
        configurable.Close("phone", control);
        check(released == 2, "Closing a session releases dragging");
        control = configurable.Open("phone").SessionId;
        configurable.Execute("phone", control, new("mousedown"));
        failCapture = true;
        Rejected(() => configurable.Capture("phone", control), RemoteFailure.DesktopUnavailable, "Capture failure pauses control");
        check(released == 3, "Capture failure releases dragging");
        configurable.Execute("phone", control, new("mousedown"));
        Thread.Sleep(2500);
        check(released == 4, "Missing drag heartbeat releases button automatically");
        configurable.Execute("phone", control, new("mousedown"));
        configurable.Disable();
        check(released == 5, "Manual revocation releases dragging");
        Rejected(() => configurable.ValidateAudio("phone", control), RemoteFailure.Disabled, "Revocation also blocks audio");
    }
}
