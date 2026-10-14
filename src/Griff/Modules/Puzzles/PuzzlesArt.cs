using Griff.Projects;
using static Griff.Projects.TemplateArt;

namespace Griff.Modules.Puzzles;

// The Puzzle template's pictures: a warehouse floor and walls, the crates (the Contraption's), the marks they go on, and
// the worker who pushes them.
internal static class PuzzlesArt
{
    public const string PuzzleTiles = "Textures/warehouse.png";
    public const string GoalMark = "Textures/goal.png";
    public const string Worker = "Textures/worker.png";

    /// <summary>The tiles across <see cref="PuzzleTiles"/>: floor and wall.</summary>
    public const int PuzzleColumns = 2;

    public const int WarehouseFloorTile = 0, WarehouseWallTile = 1;

    internal static Dictionary<string, byte[]> Files() => new()
    {
        [PuzzleTiles] = WarehouseSheet().Png(),
        [GoalMark] = GoalArt().Png(),
        [Worker] = WorkerArt().Png(),
        [CrateArt] = Crate().Png(),
    };

    // Two 32-pixel tiles: worn floorboards and a block wall.
    private static Canvas WarehouseSheet()
    {
        const int size = 32;
        var sheet = new Canvas(size * PuzzleColumns, size);
        sheet.Paste(Floorboards(), WarehouseFloorTile * size, 0);
        sheet.Paste(WarehouseWall(), WarehouseWallTile * size, 0);
        return sheet;
    }

    // Planks running across, each its own shade, with dark gaps between and a nail at each end.
    private static Canvas Floorboards()
    {
        var c = new Canvas(32, 32, 70);
        var board = Rgb(0x4A, 0x40, 0x38);
        var random = new Random(70);
        for (var y = 0; y < 32; y += 8)
        {
            c.Rect(0, y, 32, 8, board.Times(0.9f + random.NextSingle() * 0.2f));
            c.Rect(0, y + 7, 32, 1, board.Times(0.6f));
            c.Set(2, y + 3, board.Times(0.5f));
            c.Set(29, y + 3, board.Times(0.5f));
        }

        c.Noise(0.05f);
        return c;
    }

    private static Canvas WarehouseWall()
    {
        var c = new Canvas(32, 32, 71);
        var block = Rgb(0x6E, 0x74, 0x84);
        c.Fill(block);
        c.Bevel(0, 0, 32, 32, block.Times(1.3f), block.Times(0.5f));
        c.Rect(2, 15, 28, 2, block.Times(0.7f));
        c.Rect(15, 2, 2, 13, block.Times(0.7f));
        c.Noise(0.06f);
        return c;
    }

    // A ring painted on the floor, with a dot in the middle: where a crate goes.
    private static Canvas GoalArt()
    {
        var c = new Canvas(32, 32, 72);
        var paint = Rgb(0xE8, 0xC5, 0x4A);
        c.Ellipse(15, 15, 13, 13, paint);
        c.Ellipse(15, 15, 9, 9, Pixel.Clear);
        c.Ellipse(15, 15, 4, 4, paint);
        c.Shade(0.15f);
        return c;
    }

    // A worker in overalls and a cap, seen from above, facing right.
    private static Canvas WorkerArt()
    {
        var c = new Canvas(32, 32, 73);
        var overalls = Rgb(0x4C, 0x9A, 0xFF);
        c.Ellipse(15, 16, 11, 12, overalls);
        c.Ellipse(16, 15, 8, 8, Rgb(0xF2, 0xC2, 0x96));
        c.Ellipse(13, 15, 7, 8, Rgb(0xF0, 0x8C, 0x2E));
        c.Rect(19, 9, 6, 13, Rgb(0xF0, 0x8C, 0x2E).Times(0.8f));
        c.Shade(0.2f);
        c.Ellipse(21, 12, 1, 1, Rgb(0x1A, 0x1C, 0x22));
        c.Ellipse(21, 18, 1, 1, Rgb(0x1A, 0x1C, 0x22));
        c.Outline(Rgb(0x1A, 0x1C, 0x22));
        return c;
    }
}
