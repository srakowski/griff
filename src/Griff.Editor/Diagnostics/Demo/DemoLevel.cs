#if DEBUG
using System.Numerics;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// The demo game's one level, "Ember Hollow": a 64 × 14 tile map placed at (0, 7), so cell (column c, row r) covers
/// x c..c+1 and y 6-r..7-r, with the ground's surface at y = -4.
/// </summary>
internal static class DemoLevel
{
    public const int Width = 64, Height = 14;
    public const float Top = 7;

    // Tile characters: '0' ground (autotiles 0-15), 'g' plank (16, one way), 'i' grass (18), 'j' flower (19),
    // 'k' water (20, ripples through 20-23).
    public static string[] Rows()
    {
        var map = new char[Height, Width];
        for (var r = 0; r < Height; r++)
        for (var c = 0; c < Width; c++)
            map[r, c] = '.';

        void Fill(int c0, int c1, int r0, int r1, char t)
        {
            for (var r = r0; r <= r1; r++)
            for (var c = c0; c <= c1; c++)
                map[r, c] = t;
        }

        Fill(0, 0, 0, 13, '0');          // left wall
        Fill(63, 63, 0, 13, '0');        // right wall
        Fill(1, 62, 11, 13, '0');        // ground
        Fill(15, 18, 11, 13, '.');       // first pond
        Fill(15, 18, 12, 13, 'k');
        Fill(23, 25, 9, 10, '0');        // a step up
        Fill(30, 33, 7, 7, 'g');         // planks to jump up through
        Fill(36, 41, 4, 5, '0');         // the ledge with the key
        Fill(36, 36, 10, 10, '0');       // a bump under it
        Fill(45, 45, 10, 10, '0');       // a bump that keeps the second slime in
        Fill(54, 57, 11, 13, '.');       // second pond
        Fill(54, 57, 12, 13, 'k');

        foreach (var c in new[] { 3, 8, 12, 20, 27, 47, 50, 59 })
            map[10, c] = 'i';
        foreach (var c in new[] { 5, 11, 21, 49, 60 })
            map[10, c] = 'j';
        map[3, 38] = 'i';
        map[3, 40] = 'j';

        return Enumerable.Range(0, Height).Select(r => new string(Enumerable.Range(0, Width).Select(c => map[r, c]).ToArray())).ToArray();
    }

    /// <summary>The floating island painted by hand in the demo: cells (column, row).</summary>
    public static readonly (int Column, int Row)[][] IslandStrokes =
    [
        [(6, 3), (7, 3), (8, 3), (9, 3), (10, 3)],
        [(7, 4), (8, 4), (9, 4)],
    ];

    public static Vector2 CellCenter(int column, int row) => new(column + 0.5f, Top - row - 0.5f);

    public static readonly Vector2 PlayerStart = new(3, -3.5f);

    public static readonly Vector2[] Coins =
    [
        new(6, -3.4f), new(8, -3.4f), new(10, -3.4f),
        new(15.6f, -0.5f), new(17.4f, -0.5f),
        new(23.6f, -0.6f),
        new(31.3f, 0.6f), new(32.2f, 0.6f),
        new(40, 3.6f),
        new(47, -3.4f), new(49, -3.4f),
        new(54.2f, -0.6f),
    ];

    public static readonly Vector2 Slime1 = new(30, -3.45f);
    public static readonly Vector2 Slime2 = new(49, -3.45f);
    public static readonly Vector2 Key = new(38.5f, 3.7f);
    public static readonly Vector2 Checkpoint = new(43.5f, -3);
    public static readonly Vector2 Door = new(60.5f, -3);
    public static readonly Vector2[] Torches = [new(13, -2.6f), new(42.5f, -2.6f), new(59, -2.6f), new(37, 4.6f)];
    public static readonly (Vector2 Position, Vector2 Size)[] Ponds = [(new(17, -6.5f), new(4, 1)), (new(56, -6.5f), new(4, 1))];
}
#endif
