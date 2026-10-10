namespace Griff.Art;

/// <summary>A named set of colors to paint with (0xAARRGGBB).</summary>
public sealed record Palette(string Name, IReadOnlyList<uint> Colors)
{
    /// <summary>Sixteen bright colors that sit well together, the classic fantasy-console set. The default.</summary>
    public static readonly Palette Jam = new("Jam", Rgb(
        0x000000, 0x1D2B53, 0x7E2553, 0x008751, 0xAB5236, 0x5F574F, 0xC2C3C7, 0xFFF1E8,
        0xFF004D, 0xFFA300, 0xFFEC27, 0x00E436, 0x29ADFF, 0x83769C, 0xFF77A8, 0xFFCCAA));

    /// <summary>Four greens, like an old handheld's screen.</summary>
    public static readonly Palette Handheld = new("Handheld", Rgb(0x0F380F, 0x306230, 0x8BAC0F, 0x9BBC0F));

    /// <summary>Earthy colors for dungeons, forests and caves.</summary>
    public static readonly Palette Earth = new("Earth", Rgb(
        0x1A1C2C, 0x5D275D, 0xB13E53, 0xEF7D57, 0xFFCD75, 0xA7F070, 0x38B764, 0x257179,
        0x29366F, 0x3B5DC9, 0x41A6F6, 0x73EFF7, 0xF4F4F4, 0x94B0C2, 0x566C86, 0x333C57));

    public static readonly Palette Grays = new("Grays", Rgb(0x000000, 0x222222, 0x444444, 0x666666, 0x888888, 0xAAAAAA, 0xCCCCCC, 0xFFFFFF));

    public static IReadOnlyList<Palette> All { get; } = [Jam, Earth, Handheld, Grays];

    private static uint[] Rgb(params uint[] colors) => colors.Select(c => 0xFF000000u | c).ToArray();
}
