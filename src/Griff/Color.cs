using System.Globalization;

namespace Griff;

/// <summary>An RGBA color with 0–1 float channels. Saved as hex, e.g. "#F08C2EFF".</summary>
public readonly record struct Color(float R, float G, float B, float A = 1f)
{
    public static Color White => new(1, 1, 1);
    public static Color Black => new(0, 0, 0);
    public static Color Transparent => new(0, 0, 0, 0);
    public static Color Gray => new(0.5f, 0.5f, 0.5f);
    public static Color Red => new(1, 0, 0);
    public static Color Green => new(0, 1, 0);
    public static Color Blue => new(0, 0, 1);
    public static Color Yellow => new(1, 1, 0);
    public static Color Cyan => new(0, 1, 1);
    public static Color Magenta => new(1, 0, 1);
    public static Color Orange => new(1, 0.5f, 0);

    public static Color FromBytes(byte r, byte g, byte b, byte a = 255) =>
        new(r / 255f, g / 255f, b / 255f, a / 255f);

    public byte RByte => ToByte(R);
    public byte GByte => ToByte(G);
    public byte BByte => ToByte(B);
    public byte AByte => ToByte(A);

    /// <summary>The color <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/> (0 gives a, 1 gives b).</summary>
    public static Color Lerp(Color a, Color b, float t) => new(
        a.R + (b.R - a.R) * t,
        a.G + (b.G - a.G) * t,
        a.B + (b.B - a.B) * t,
        a.A + (b.A - a.A) * t);

    /// <summary>
    /// The color with hue <paramref name="hue"/> (degrees round the color wheel: 0 red, 120 green, 240 blue), saturation
    /// <paramref name="saturation"/> (0 gray to 1 full) and value <paramref name="value"/> (0 black to 1 bright).
    /// </summary>
    public static Color FromHsv(float hue, float saturation, float value, float alpha = 1f)
    {
        hue = ((hue % 360f) + 360f) % 360f / 60f;
        saturation = Math.Clamp(saturation, 0f, 1f);
        value = Math.Clamp(value, 0f, 1f);
        var chroma = value * saturation;
        var x = chroma * (1f - MathF.Abs(hue % 2f - 1f));
        var (r, g, b) = (int)hue switch
        {
            0 => (chroma, x, 0f),
            1 => (x, chroma, 0f),
            2 => (0f, chroma, x),
            3 => (0f, x, chroma),
            4 => (x, 0f, chroma),
            _ => (chroma, 0f, x),
        };
        var m = value - chroma;
        return new Color(r + m, g + m, b + m, alpha);
    }

    /// <summary>The color's hue (0 to 360 degrees, 0 for grays), saturation and value (each 0 to 1); see <see cref="FromHsv"/>.</summary>
    public (float Hue, float Saturation, float Value) ToHsv()
    {
        var (r, g, b) = (Math.Clamp(R, 0f, 1f), Math.Clamp(G, 0f, 1f), Math.Clamp(B, 0f, 1f));
        var max = MathF.Max(r, MathF.Max(g, b));
        var chroma = max - MathF.Min(r, MathF.Min(g, b));
        var hue = chroma == 0 ? 0
            : max == r ? 60f * ((g - b) / chroma % 6f)
            : max == g ? 60f * ((b - r) / chroma + 2f)
            : 60f * ((r - g) / chroma + 4f);
        return (hue < 0 ? hue + 360f : hue, max == 0 ? 0 : chroma / max, max);
    }

    public string ToHex() => $"#{RByte:X2}{GByte:X2}{BByte:X2}{AByte:X2}";

    /// <summary>Parses "#RGB", "#RRGGBB" or "#RRGGBBAA" (the # is optional).</summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        var hex = text?.Trim().TrimStart('#');
        if (hex is null)
            return false;

        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        if (hex.Length == 6)
            hex += "FF";
        if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;

        color = FromBytes((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    public override string ToString() => ToHex();

    private static byte ToByte(float channel) => (byte)MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f);
}
