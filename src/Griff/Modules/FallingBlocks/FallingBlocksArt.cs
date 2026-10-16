using static Griff.Templates.TemplateArt;

namespace Griff.Modules.FallingBlocks;

// The Falling Blocks template's pictures: seven bevelled blocks, one for each falling shape.
internal static class FallingBlocksArt
{
    public const string PieceTiles = "Textures/pieces.png";

    /// <summary>The tiles across <see cref="PieceTiles"/>: one for each shape, in the order of FallingBoard.AllShapes.</summary>
    public const int PieceColumns = 7;

    internal static Dictionary<string, byte[]> Files() => new() { [PieceTiles] = PieceSheet().Png() };

    private static Canvas PieceSheet()
    {
        const int size = 32;
        var sheet = new Canvas(size * PieceColumns, size);
        // I J L O S T Z: cyan, blue, orange, yellow, green, purple, red.
        Pixel[] colors =
        [
            Rgb(0x4C, 0xD4, 0xE8), Rgb(0x4A, 0x6C, 0xE8), Rgb(0xF0, 0x9A, 0x3A), Rgb(0xF2, 0xD3, 0x4B),
            Rgb(0x5C, 0xC0, 0x5A), Rgb(0xA0, 0x5C, 0xD8), Rgb(0xE0, 0x4A, 0x4A),
        ];
        for (var i = 0; i < colors.Length; i++)
        {
            var c = new Canvas(size, size, 90 + i);
            var color = colors[i];
            c.Fill(color.Times(0.85f));
            c.Rect(5, 5, 22, 22, color);
            c.Rect(8, 7, 10, 3, color.Times(1.3f));
            c.Rect(0, 0, 32, 3, color.Times(1.35f));
            c.Rect(0, 0, 3, 32, color.Times(1.2f));
            c.Rect(0, 29, 32, 3, color.Times(0.55f));
            c.Rect(29, 0, 3, 32, color.Times(0.65f));
            c.Bevel(0, 0, 32, 32, color.Times(1.5f), color.Times(0.4f));
            sheet.Paste(c, i * size, 0);
        }

        return sheet;
    }
}
