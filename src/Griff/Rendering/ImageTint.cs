namespace Griff.Rendering;

/// <summary>
/// Multiplies image pixels by a tint, the way the game's renderer tints sprites, for views that can't tint on the GPU
/// (the editor's Scene view).
/// </summary>
public static class ImageTint
{
    /// <summary>Levels per color channel that <see cref="Quantize"/> keeps.</summary>
    public const int Levels = 32;

    /// <summary>True if multiplying by <paramref name="tint"/> leaves colors unchanged (alpha is applied separately).</summary>
    public static bool IsIdentity(Color tint) => tint.RByte == 255 && tint.GByte == 255 && tint.BByte == 255;

    /// <summary>
    /// The tint's color rounded to <see cref="Levels"/> steps per channel, packed as 0xRRGGBB, with alpha left out. Close
    /// tints share a key, so a cache of tinted images stays small even while colors animate.
    /// </summary>
    public static int Quantize(Color tint)
    {
        static int Step(byte channel) => (int)Math.Round(channel * (Levels - 1) / 255.0) * 255 / (Levels - 1);
        return (Step(tint.RByte) << 16) | (Step(tint.GByte) << 8) | Step(tint.BByte);
    }

    /// <summary>
    /// Multiplies the color channels of 32-bit BGRA pixels (unpremultiplied) by a color packed as 0xRRGGBB, in place.
    /// Alpha is untouched.
    /// </summary>
    public static void ApplyBgra(Span<byte> pixels, int rgb)
    {
        var (r, g, b) = ((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            pixels[i] = (byte)((pixels[i] * b + 127) / 255);
            pixels[i + 1] = (byte)((pixels[i + 1] * g + 127) / 255);
            pixels[i + 2] = (byte)((pixels[i + 2] * r + 127) / 255);
        }
    }
}
