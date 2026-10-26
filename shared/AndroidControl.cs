namespace Ponte.Core;

public record AndroidCommand(string Kind, double X = 0, double Y = 0, double EndX = 0, double EndY = 0, string? Text = null)
{
    public bool Valid => Kind switch
    {
        "tap" => Coordinate(X) && Coordinate(Y),
        "swipe" => Coordinate(X) && Coordinate(Y) && Coordinate(EndX) && Coordinate(EndY),
        "back" or "home" or "recents" => true,
        "text" => Text is { Length: > 0 and <= 1000 } && !Text.Any(c => char.IsControl(c) && c != '\n'),
        _ => false
    };
    private static bool Coordinate(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
}

public static class AndroidFrameValidation
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static bool ValidJpeg(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length is < 4 or > MaxBytes || jpeg[0] != 0xff || jpeg[1] != 0xd8) return false;
        var i = 2;
        while (i + 3 < jpeg.Length)
        {
            if (jpeg[i++] != 0xff) return false;
            while (i < jpeg.Length && jpeg[i] == 0xff) i++;
            if (i >= jpeg.Length) return false;
            var marker = jpeg[i++];
            if (marker == 0xda || marker == 0xd9) return false;
            if (marker is 0xd8 or 0x01 || marker is >= 0xd0 and <= 0xd7) continue;
            if (i + 2 > jpeg.Length) return false;
            var length = (jpeg[i] << 8) | jpeg[i + 1];
            if (length < 2 || i + length > jpeg.Length) return false;
            if (marker is 0xc0 or 0xc1 or 0xc2)
            {
                if (length < 8) return false;
                var height = (jpeg[i + 3] << 8) | jpeg[i + 4];
                var width = (jpeg[i + 5] << 8) | jpeg[i + 6];
                return width is > 0 and <= 1920 && height is > 0 and <= 1920 && (long)width * height <= 2073600;
            }
            i += length;
        }
        return false;
    }
}
