using System.Diagnostics;
using Ponte.Windows;

static class CaptureBenchmark
{
    public static void Run()
    {
        if (!NativeDesktop.IsAvailable()) throw new Exception("Desbloqueie o Windows para medir a captura.");
        var samples = new List<double>();
        long totalBytes = 0;
        for (var index = 0; index < 32; index++)
        {
            var started = Stopwatch.GetTimestamp();
            var frame = NativeDesktop.Capture();
            if (index >= 2) { samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds); totalBytes += frame.Jpeg.Length; }
        }
        samples.Sort();
        Console.WriteLine($"Native capture/JPEG, 30 measured frames: median {samples[samples.Count / 2]:F1} ms, mean {samples.Average():F1} ms, mean JPEG {totalBytes / samples.Count / 1024} KiB");
        Console.WriteLine("Capture/encoding only; no images saved or transmitted. This is not a phone/network FPS measurement.");
    }
}
