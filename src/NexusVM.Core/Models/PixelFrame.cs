namespace NexusVM.Core.Models;

/// <summary>
/// Tightly packed BGRA8888 frame. The Avalonia surface copies it into a WriteableBitmap.
/// </summary>
public sealed class PixelFrame
{
    public PixelFrame(int width, int height, byte[] bgra32)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Frame dimensions must be positive.");

        var expected = checked(width * height * 4);
        if (bgra32 is null || bgra32.Length != expected)
            throw new ArgumentException($"BGRA buffer must be {expected} bytes.", nameof(bgra32));

        Width = width;
        Height = height;
        Bgra32 = bgra32;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Bgra32 { get; }
}
