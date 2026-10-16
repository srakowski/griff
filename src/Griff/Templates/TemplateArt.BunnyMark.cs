namespace Griff.Templates;

// The BunnyMark template's one picture: the bunny it adds by the thousand.
internal static partial class TemplateArt
{
    public const string Bunny = "Textures/bunny.png";

    internal static Dictionary<string, byte[]> BunnyMarkFiles() => new()
    {
        [Bunny] = BunnyArt().Png(),
    };

    // A white bunny sitting up, facing out: tall ears pink inside, dark eyes and a pink nose, drawn from rows of letters.
    private static Canvas BunnyArt()
    {
        string[] rows =
        [
            "...oo...oo....",
            "..owwo.owwo...",
            "..owpo.owpo...",
            "..owpo.owpo...",
            "..owpo.owpo...",
            "..owwo.owwo...",
            "..owwoooowwo..",
            ".owwwwwwwwwwo.",
            ".owwkwwwwkwwo.",
            ".owwwwppwwwwo.",
            ".owwwwwwwwwwo.",
            "..owwwwwwwwo..",
            ".owwwwwwwwwwo.",
            "owwwwwwwwwwwwo",
            "owwwwwwwwwwwwo",
            "owwwwwwwwwwwwo",
            ".owwwwwwwwwwo.",
            "..oooo..oooo..",
        ];
        var colors = new Dictionary<char, Pixel>
        {
            ['o'] = Rgb(0x8A, 0x8F, 0xA3),
            ['w'] = Rgb(0xF4, 0xF4, 0xF8),
            ['p'] = Rgb(0xF2, 0x9C, 0xB4),
            ['k'] = Rgb(0x22, 0x24, 0x2E),
        };
        var c = new Canvas(rows[0].Length, rows.Length, 80);
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                if (colors.TryGetValue(rows[y][x], out var color))
                    c.Set(x, y, color);
            }
        }

        return c;
    }
}
