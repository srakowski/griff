using System.Globalization;

namespace Griff;

/// <summary>An RGBA color with 0–1 float channels. Saved as hex, e.g. "#F08C2EFF".</summary>
public readonly record struct Color(float R, float G, float B, float A = 1f)
{
    public static Color White => new(1, 1, 1);
    public static Color Black => new(0, 0, 0);
    public static Color Transparent => new(0, 0, 0, 0);

    public static Color FromBytes(byte r, byte g, byte b, byte a = 255) =>
        new(r / 255f, g / 255f, b / 255f, a / 255f);

    public byte RByte => ToByte(R);
    public byte GByte => ToByte(G);
    public byte BByte => ToByte(B);
    public byte AByte => ToByte(A);

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
