using System.Globalization;
using System.Text;
using NexusVM.Core.Models;

namespace NexusVM.Core.Protocol;

public static class PpmImageReader
{
    public const int MaxDimension = 4096;

    public static bool TryRead(string path, out PixelFrame? frame)
    {
        frame = null;
        try
        {
            var data = File.ReadAllBytes(path);
            return TryParse(data, out frame);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out PixelFrame? frame)
    {
        frame = null;
        if (data.Length < 3 || data[0] != (byte)'P' || data[1] != (byte)'6')
            return false;

        var index = 2;
        if (!TryToken(data, ref index, out var widthToken) ||
            !TryToken(data, ref index, out var heightToken) ||
            !TryToken(data, ref index, out var maxToken))
        {
            return false;
        }

        if (!int.TryParse(widthToken, NumberStyles.None, CultureInfo.InvariantCulture, out var width) ||
            !int.TryParse(heightToken, NumberStyles.None, CultureInfo.InvariantCulture, out var height) ||
            !int.TryParse(maxToken, NumberStyles.None, CultureInfo.InvariantCulture, out var maxValue))
        {
            return false;
        }

        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension || maxValue != 255)
            return false;

        if (index >= data.Length)
            return false;

        // PPM requires exactly one whitespace byte between maxval and the raster.
        index++;
        var pixels = (long)width * height;
        if (data.Length - index < pixels * 3)
            return false;

        var bgra = new byte[pixels * 4];
        for (var pixel = 0; pixel < pixels; pixel++)
        {
            var source = index + (int)pixel * 3;
            var destination = (int)pixel * 4;
            bgra[destination] = data[source + 2];
            bgra[destination + 1] = data[source + 1];
            bgra[destination + 2] = data[source];
            bgra[destination + 3] = 255;
        }

        frame = new PixelFrame(width, height, bgra);
        return true;
    }

    private static bool TryToken(ReadOnlySpan<byte> data, ref int index, out string token)
    {
        token = string.Empty;
        while (index < data.Length)
        {
            if (data[index] == (byte)'#')
            {
                while (index < data.Length && data[index] != (byte)'\n')
                    index++;
                continue;
            }

            if (IsWhitespace(data[index]))
            {
                index++;
                continue;
            }

            break;
        }

        var start = index;
        while (index < data.Length && !IsWhitespace(data[index]))
            index++;

        if (start == index)
            return false;

        token = Encoding.ASCII.GetString(data[start..index]);
        return true;
    }

    private static bool IsWhitespace(byte value) =>
        value is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t';
}
