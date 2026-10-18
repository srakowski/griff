using System.Numerics;

namespace Griff.Scenes;

// Turned and flipped tiles: a cell can hold its tile turned by quarters or mirrored, so a conveyor belt, an arrow or a
// pipe is drawn once and painted pointing any way. The turns are a grid of their own (Turns), lined up with Tiles, and
// a map that turns nothing has none, so it saves and plays as it did before.
public sealed partial class Tilemap
{
    private string[] _turns = [];

    /// <summary>
    /// How the tile in cell (<paramref name="x"/>, <paramref name="y"/>) is turned (see <see cref="Turns"/>):
    /// <see cref="TileTurn.None"/> for one drawn as it is, an empty cell, or one outside the grid.
    /// </summary>
    public TileTurn GetTurn(int x, int y) => GetTile(x, y) < 0 ? TileTurn.None : TurnAt(x, y);

    // The turn saved for a cell, whether or not it holds a tile.
    private TileTurn TurnAt(int x, int y) =>
        y < _turns.Length && x < _turns[y].Length ? TurnOf(_turns[y][x]) : TileTurn.None;

    /// <summary>
    /// Sets the tile at column <paramref name="x"/>, row <paramref name="y"/> turned or flipped by
    /// <paramref name="turn"/>, e.g. <c>SetTile(4, 2, 7, TileTurn.Turn90)</c> for a belt drawn pointing right that
    /// should point down; -1 clears it. The grid grows to fit, like <see cref="SetTile(int, int, int)"/>.
    /// </summary>
    public void SetTile(int x, int y, int tile, TileTurn turn)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        if (GetTile(x, y) == tile && GetTurn(x, y) == (tile < 0 ? TileTurn.None : Normal(turn)))
            return;
        var (tiles, turns, _, _) = PaintedRect(x, y, x, y, tile, turn);
        Put(tiles, turns);
    }

    /// <summary>
    /// Turns the tile already in cell (<paramref name="x"/>, <paramref name="y"/>) to <paramref name="turn"/>, keeping
    /// the tile: <c>SetTurn(x, y, GetTurn(x, y).Turned())</c> turns it a quarter further clockwise. An empty cell stays
    /// empty.
    /// </summary>
    public void SetTurn(int x, int y, TileTurn turn)
    {
        if (GetTile(x, y) is var tile and >= 0)
            SetTile(x, y, tile, turn);
    }

    /// <summary>
    /// Sets every cell in the rectangle between two corner cells to <paramref name="tile"/> turned by
    /// <paramref name="turn"/>, as <see cref="FillRect(int, int, int, int, int)"/> does unturned.
    /// </summary>
    public void FillRect(int fromX, int fromY, int toX, int toY, int tile, TileTurn turn)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Math.Min(fromX, toX), nameof(fromX));
        ArgumentOutOfRangeException.ThrowIfNegative(Math.Min(fromY, toY), nameof(fromY));
        var (tiles, turns, _, _) = PaintedRect(fromX, fromY, toX, toY, tile, turn);
        Put(tiles, turns);
    }

    /// <summary>
    /// Fills the area around cell (<paramref name="x"/>, <paramref name="y"/>) with <paramref name="tile"/> turned by
    /// <paramref name="turn"/>, as <see cref="FloodFill(int, int, int)"/> does unturned.
    /// </summary>
    public void FloodFill(int x, int y, int tile, TileTurn turn)
    {
        var (tiles, turns) = Filled(x, y, tile, turn);
        Put(tiles, turns);
    }

    // New tiles and turns on the map; turns left as they were (a map that turns nothing) aren't set again.
    private void Put(IReadOnlyList<string> tiles, IReadOnlyList<string> turns)
    {
        Tiles = tiles;
        if (!ReferenceEquals(turns, _turns))
            Turns = turns;
    }

    /// <summary>
    /// The grid and its <see cref="Turns"/> with every cell in the rectangle between two corner cells set to
    /// <paramref name="tile"/> turned by <paramref name="turn"/> (or cleared, with -1), leaving this map alone. The grid
    /// grows left and up as <see cref="PaintedRect(int, int, int, int, int)"/> says, and the turns move with it.
    /// </summary>
    public (IReadOnlyList<string> Tiles, IReadOnlyList<string> Turns, int ShiftX, int ShiftY) PaintedRect(
        int fromX, int fromY, int toX, int toY, int tile, TileTurn turn)
    {
        var (tiles, shiftX, shiftY) = PaintedRect(fromX, fromY, toX, toY, tile);
        if (ReferenceEquals(tiles, _tiles) && tile < 0)
            return (tiles, _turns, 0, 0);
        var (left, right) = (Math.Min(fromX, toX), Math.Max(fromX, toX));
        var (top, bottom) = (Math.Min(fromY, toY), Math.Max(fromY, toY));
        if (tile < 0)
            (left, top, right, bottom) = (Math.Max(0, left), Math.Max(0, top), Math.Min(Width - 1, right), Math.Min(Height - 1, bottom));
        var cells = new List<(int X, int Y)>();
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
                cells.Add((x + shiftX, y + shiftY));
        }

        return (tiles, TurnsWith(cells, tile < 0 ? TileTurn.None : turn, shiftX, shiftY), shiftX, shiftY);
    }

    /// <summary>
    /// The grid and its <see cref="Turns"/> with the area a fill from cell (<paramref name="x"/>, <paramref name="y"/>)
    /// reaches set to <paramref name="tile"/> turned by <paramref name="turn"/>, leaving this map alone, as
    /// <see cref="Filled(int, int, int)"/> says. Unchanged when the cell already holds that tile turned that way.
    /// </summary>
    public (IReadOnlyList<string> Tiles, IReadOnlyList<string> Turns) Filled(int x, int y, int tile, TileTurn turn)
    {
        CheckTile(tile);
        turn = tile < 0 ? TileTurn.None : Normal(turn);
        if (GetTile(x, y) == tile && GetTurn(x, y) == turn)
            return (_tiles, _turns);
        var region = FillRegion(x, y);
        if (region.Count == 0)
            return (_tiles, _turns);
        var width = Width;
        var rows = _tiles.Select(r => r.PadRight(width, Empty).ToCharArray()).ToList();
        var c = CharOf(tile);
        foreach (var (cx, cy) in region)
            rows[cy][cx] = c;
        return (Trim(rows.Select(r => new string(r)).ToList()), TurnsWith(region, turn, 0, 0));
    }

    // The turns with `cells` (counted in the grid after it grew by shiftX columns and shiftY rows) set to `turn`.
    private string[] TurnsWith(IReadOnlyList<(int X, int Y)> cells, TileTurn turn, int shiftX, int shiftY)
    {
        // A map that turns nothing keeps its turns empty, so it saves as it did before turns.
        if (_turns.Length == 0 && Normal(turn) == TileTurn.None)
            return _turns;
        var rows = _turns.Select(r => shiftX > 0 && r.Length > 0 ? new string(Empty, shiftX) + r : r).ToList();
        rows.InsertRange(0, Enumerable.Repeat("", shiftY));
        var grid = rows.Select(r => r.ToCharArray()).ToList();
        var c = CharOf(turn);
        foreach (var (x, y) in cells)
        {
            while (grid.Count <= y)
                grid.Add([]);
            if (grid[y].Length <= x)
            {
                var wider = new string(grid[y]).PadRight(x + 1, Empty).ToCharArray();
                grid[y] = wider;
            }

            grid[y][x] = c;
        }

        return TrimTurns(grid.Select(r => new string(r)));
    }

    // Turns rows without the cells as drawn at their ends, or the rows after the last turned cell: none at all for a
    // map that turns nothing.
    private static string[] TrimTurns(IEnumerable<string> rows)
    {
        var trimmed = rows.Select(r => r.TrimEnd(Empty, '0', ' ')).ToList();
        while (trimmed.Count > 0 && trimmed[^1].Length == 0)
            trimmed.RemoveAt(trimmed.Count - 1);
        return trimmed.ToArray();
    }

    /// <summary>The turn a character of <see cref="Turns"/> stands for: <c>1</c> to <c>7</c>, anything else as drawn.</summary>
    public static TileTurn TurnOf(char c) => c is >= '1' and <= '7' ? (TileTurn)(c - '0') : TileTurn.None;

    /// <summary>The character for <paramref name="turn"/> in <see cref="Turns"/>: <c>.</c> for none, <c>1</c> to <c>7</c> for the rest.</summary>
    public static char CharOf(TileTurn turn) => Normal(turn) is var normal and not TileTurn.None ? (char)('0' + (int)normal) : Empty;

    private static TileTurn Normal(TileTurn turn) => (TileTurn)((int)turn & 7);

    /// <summary>
    /// The slope cell (<paramref name="x"/>, <paramref name="y"/>) makes, its <see cref="Turns"/> taken in: 1 rising to
    /// the right, -1 falling, 0 for none. A slope tile flipped left to right falls where it rose, and one turned a quarter
    /// can be the other slope; one turned so its solid corner is at the top is a solid block instead.
    /// </summary>
    public int SlopeAt(int x, int y) => GetTile(x, y) is var tile and >= 0 ? Turned(SlopeOf(tile), TurnAt(x, y)) : 0;

    // A slope turned: where its solid corner (bottom right for one rising, bottom left for one falling) ends up says what
    // it is now; at the top, it's no slope bodies can walk (a ceiling), so 0.
    private static int Turned(int slope, TileTurn turn)
    {
        if (slope == 0 || turn == TileTurn.None)
            return slope;
        var corner = turn.Apply(new Vector2(slope > 0 ? 1 : -1, -1));
        return corner.Y > 0 ? 0 : corner.X > 0 ? 1 : -1;
    }

    // 0 = solid, 1 = one-way, -1 = not a box (empty, background, a ladder, or a slope, which collides by its own shape).
    // A slope turned upside down is a solid block. One-way tiles stay open from below whichever way they're turned.
    private int CollisionAt(int x, int y, int tile)
    {
        if (tile < 0 || IsBackground(tile) || IsLadder(tile))
            return -1;
        if (SlopeOf(tile) is var slope and not 0)
            return Turned(slope, TurnAt(x, y)) == 0 ? 0 : -1;
        return IsOneWay(tile) ? 1 : 0;
    }
}

/// <summary>
/// How a tilemap cell's tile is turned or flipped (see <see cref="Tilemap.Turns"/>): flipped left to right first, if
/// <see cref="FlipX"/> is in it, then turned clockwise by quarters. The eight ways a square can be laid down, so one
/// picture of a belt, an arrow or a pipe points every way.
/// </summary>
[Flags]
public enum TileTurn
{
    /// <summary>As drawn in the tileset.</summary>
    None = 0,

    /// <summary>A quarter turn clockwise: what pointed right points down.</summary>
    Turn90 = 1,

    /// <summary>Half a turn: upside down and back to front.</summary>
    Turn180 = 2,

    /// <summary>Three quarters clockwise (a quarter anticlockwise): what pointed right points up.</summary>
    Turn270 = 3,

    /// <summary>Mirrored left to right. With a turn as well, mirrored first, then turned.</summary>
    FlipX = 4,

    /// <summary>Mirrored top to bottom: the same as <see cref="FlipX"/> and <see cref="Turn180"/> together.</summary>
    FlipY = 6,
}

/// <summary>Turning and flipping a <see cref="TileTurn"/> further, and what it does to points and directions.</summary>
public static class TileTurns
{
    /// <summary>
    /// <paramref name="turn"/> turned <paramref name="quarters"/> more quarter turns clockwise (negative for
    /// anticlockwise), e.g. <c>TileTurn.Turn90.Turned()</c> is <see cref="TileTurn.Turn180"/>.
    /// </summary>
    public static TileTurn Turned(this TileTurn turn, int quarters = 1) =>
        (TileTurn)(((int)turn & 4) | ((((int)turn & 3) + quarters) % 4 + 4) % 4);

    /// <summary><paramref name="turn"/> then mirrored left to right, as it looks: a belt pointing right points left after.</summary>
    public static TileTurn FlippedX(this TileTurn turn) => (TileTurn)(((int)turn & 4 ^ 4) | (4 - ((int)turn & 3)) % 4);

    /// <summary><paramref name="turn"/> then mirrored top to bottom, as it looks: a belt pointing down points up after.</summary>
    public static TileTurn FlippedY(this TileTurn turn) => turn.FlippedX().Turned(2);

    /// <summary>How many quarter turns clockwise it makes, 0 to 3, after any flip.</summary>
    public static int Quarters(this TileTurn turn) => (int)turn & 3;

    /// <summary>True if it mirrors the tile (left to right, before turning it).</summary>
    public static bool IsFlipped(this TileTurn turn) => ((int)turn & 4) != 0;

    /// <summary>
    /// Where <paramref name="point"/> (in a tile's own space: its middle at 0, 0, x right and y up) ends up when the tile
    /// is turned by <paramref name="turn"/>. A direction too: a belt drawn moving right, (1, 0), turned by
    /// <see cref="TileTurn.Turn90"/> moves down, (0, -1), the world's way (one row further down the grid).
    /// </summary>
    public static Vector2 Apply(this TileTurn turn, Vector2 point)
    {
        if (turn.IsFlipped())
            point.X = -point.X;
        for (var i = 0; i < turn.Quarters(); i++)
            point = new Vector2(point.Y, -point.X);
        return point;
    }

    /// <summary>The transform of a tile's picture (round its middle, y up) that turns it by <paramref name="turn"/>.</summary>
    public static Matrix4x4 Matrix(this TileTurn turn)
    {
        var (x, y) = (turn.Apply(Vector2.UnitX), turn.Apply(Vector2.UnitY));
        return new Matrix4x4(x.X, x.Y, 0, 0, y.X, y.Y, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
    }
}
