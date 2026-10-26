using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Automation;
using Ponte.Core;

namespace Ponte.Windows;

// Reads only accessibility metadata, NEVER Value/Text/Name/password contents.
// Some applications do not expose editable controls; manual keyboard remains available.
public static class DesktopKeyboardProbe
{
    private sealed record Request(double? X, double? Y, DateTimeOffset Deadline, TaskCompletionSource<KeyboardTarget> Completion);
    private static readonly BlockingCollection<Request> Queue = new(1);
    private static readonly KeyboardTarget Unknown = new(false, Available: false);
    static DesktopKeyboardProbe()
    {
        var worker = new Thread(() =>
        {
            foreach (var request in Queue.GetConsumingEnumerable())
            {
                if (DateTimeOffset.UtcNow >= request.Deadline) { request.Completion.TrySetResult(Unknown); continue; }
                try { request.Completion.TrySetResult(Inspect(request.X, request.Y)); }
                catch (Exception) // Accessibility providers are optional and can fail independently of remote control.
                { request.Completion.TrySetResult(Unknown); }
            }
        }) { IsBackground = true, Name = "Ponte editable-control metadata" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
    }
    public static KeyboardTarget Read(double? x = null, double? y = null)
    {
        var size = NativeDesktop.GetPrimarySize();
        return ReadPhysical(x * (size.Width - 1), y * (size.Height - 1));
    }
    public static KeyboardTarget ReadConfigured(StreamSettings settings, double? x, double? y)
    {
        var monitor = NativeDesktop.Monitor(settings);
        return ReadPhysical(monitor.Left + x * (monitor.Width - 1), monitor.Top + y * (monitor.Height - 1));
    }
    private static KeyboardTarget ReadPhysical(double? x, double? y)
    {
        var completion = new TaskCompletionSource<KeyboardTarget>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Queue.TryAdd(new Request(x, y, DateTimeOffset.UtcNow.AddMilliseconds(350), completion))) return Unknown;
        return completion.Task.Wait(TimeSpan.FromMilliseconds(350)) ? completion.Task.Result : Unknown;
    }
    private static KeyboardTarget Inspect(double? x, double? y)
    {
        if (!NativeDesktop.IsAvailable()) return Unknown;
        var element = AutomationElement.FocusedElement;
        if (element is null) return new KeyboardTarget(false);
        var current = element.Current;
        if (!current.IsEnabled || current.IsOffscreen || !current.IsKeyboardFocusable) return new KeyboardTarget(false);
        if (x.HasValue && y.HasValue)
        {
            if (!current.BoundingRectangle.Contains(new System.Windows.Point(x.Value, y.Value)))
                return new KeyboardTarget(false);
        }
        var editable = false;
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value)) editable = !((ValuePattern)value).Current.IsReadOnly;
        else if (current.ControlType == ControlType.Edit) editable = true;
        else if (current.ControlType == ControlType.Document && element.TryGetCurrentPattern(TextPattern.Pattern, out var text))
            editable = ((TextPattern)text).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute) is false;
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(current.ProcessId + ":" + string.Join(",", element.GetRuntimeId()))));
        return new KeyboardTarget(editable, current.IsPassword, id);
    }
}
