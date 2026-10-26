namespace Ponte.Core;

public record DesktopMonitor(string Id, string Name, int Left, int Top, int Width, int Height, bool Primary);
public record StreamSettings(string MonitorId = "", string Profile = "balanced")
{
    public bool Valid => Profile is "fluid" or "balanced" or "sharp";
    public (int Width, int Height, int Quality, int Fps) Limits => Profile switch
    {
        "fluid" => (960, 540, 42, 15),
        "sharp" => (1920, 1080, 72, 20),
        _ => (1280, 720, 56, 20)
    };
}
public record DesktopOptions(IReadOnlyList<DesktopMonitor> Monitors, StreamSettings Settings, bool AudioAllowed);
