using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// A grid of tiles from one sprite-sheet image, drawn and (optionally) collided with as a whole. The grid starts at
/// the entity's position and runs right and down. Paint it in the Scene view, edit <see cref="Tiles"/> as text in the
/// inspector, or change it from scripts with <see cref="SetTile"/>.
/// </summary>
/// <remarks>
/// Each row of <see cref="Tiles"/> is a line of characters, top row first: <c>.</c> is empty and
/// <c>0-9</c>, <c>a-z</c>, <c>A-Z</c> pick tiles 0–61 of the sheet. Rows are saved one per line, so levels diff
/// line by line in git.
/// </remarks>
[ComponentName("Tilemap")]
public sealed class Tilemap : Component
{
    /// <summary>The character for an empty cell.</summary>
    public const char Empty = '.';

    /// <summary>The number of distinct tiles a map can use.</summary>
    public const int MaxTiles = 62;

    private const string TileChars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private string[] _tiles = [];
    private IReadOnlyList<(int X, int Y, int Length)>? _runs;
    private IReadOnlyList<(int X, int Y, int Length, bool OneWay)>? _collisionRuns;
    private string? _backgroundTiles;
    private string? _oneWayTiles;
    private bool[] _background = new bool[MaxTiles];
    private bool[] _oneWay = new bool[MaxTiles];

    /// <summary>Tileset image relative to Assets. Without one, tiles draw as solid squares of <see cref="Tint"/>.</summary>
    public string? Image { get; set; }

    /// <summary>Tiles across the tileset image.</summary>
    public int Columns { get; set; } = 1;

    /// <summary>Tiles down the tileset image.</summary>
    public int Rows { get; set; } = 1;

    /// <summary>Width and height of one cell in world units, before the transform's scale.</summary>
    public float TileSize { get; set; } = 1f;

    public Color Tint { get; set; } = Color.White;

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/>.</summary>
    public int Order { get; set; }

    /// <summary>When set, tiles are walls and floors that bodies collide with (except <see cref="BackgroundTiles"/>).</summary>
    public bool Solid { get; set; } = true;

    /// <summary>
    /// Tiles that never collide, for decoration like grass, windows or signs: tile numbers and ranges, e.g. "10-15, 20".
    /// </summary>
    public string? BackgroundTiles
    {
        get => _backgroundTiles;
        set
        {
            _backgroundTiles = value;
            _background = ParseTileSet(value);
            _collisionRuns = null;
        }
    }

    /// <summary>
    /// Tiles that only block bodies landing on top, like <see cref="BoxCollider.OneWay"/>: tile numbers and ranges,
    /// e.g. "4, 5".
    /// </summary>
    public string? OneWayTiles
    {
        get => _oneWayTiles;
        set
        {
            _oneWayTiles = value;
            _oneWay = ParseTileSet(value);
            _collisionRuns = null;
        }
    }

    /// <summary>The collision layer of the map's tiles, like <see cref="Collider.Layer"/>. Empty is "Default".</summary>
    public string? Layer { get; set; }

    /// <summary>Layers that pass through the map's tiles, like <see cref="Collider.IgnoreLayers"/>, e.g. "Ghost".</summary>
    public string? IgnoreLayers { get; set; }

    /// <summary>The grid, top row first; see the remarks on <see cref="Tilemap"/> for the format.</summary>
    public IReadOnlyList<string> Tiles
    {
        get => _tiles;
        set
        {
            // Copied so nobody else holds the array; the map only ever replaces it, which keeps undo snapshots intact.
            _tiles = value?.ToArray() ?? [];
            _runs = null;
            _collisionRuns = null;
        }
    }

    /// <summary>Number of columns: the length of the longest row.</summary>
    public int Width => _tiles.Length == 0 ? 0 : _tiles.Max(r => r.Length);

    /// <summary>Number of rows.</summary>
    public int Height => _tiles.Length;

    /// <summary>The tile at column <paramref name="x"/>, row <paramref name="y"/> (0, 0 is top left), or -1 if empty or outside.</summary>
    public int GetTile(int x, int y) =>
        y >= 0 && y < _tiles.Length && x >= 0 && x < _tiles[y].Length ? TileOf(_tiles[y][x]) : -1;

    /// <summary>
    /// Sets the tile at column <paramref name="x"/>, row <paramref name="y"/>; -1 clears it. The grid grows to fit,
    /// and empty space at the right and bottom is trimmed.
    /// </summary>
    public void SetTile(int x, int y, int tile)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        if (GetTile(x, y) != tile)
            Tiles = Painted(x, y, tile).Tiles;
    }

    /// <summary>
    /// The grid with one cell changed, leaving this map alone. Cells left of or above the grid are allowed: the grid
    /// grows that way and <c>ShiftX</c>/<c>ShiftY</c> say how many columns and rows were added before the old ones,
    /// so the caller can move the entity back by that many cells to keep existing tiles in place.
    /// </summary>
    public (IReadOnlyList<string> Tiles, int ShiftX, int ShiftY) Painted(int x, int y, int tile)
    {
        if (tile < -1 || tile >= MaxTiles)
            throw new ArgumentOutOfRangeException(nameof(tile), $"Tiles are numbered 0 to {MaxTiles - 1}, or -1 for empty.");

        // Erasing outside the grid changes nothing.
        if (tile < 0 && GetTile(x, y) < 0)
            return (_tiles, 0, 0);

        var shiftX = Math.Max(0, -x);
        var shiftY = Math.Max(0, -y);
        var rows = _tiles.Select(r => shiftX > 0 && r.Length > 0 ? new string(Empty, shiftX) + r : r).ToList();
        rows.InsertRange(0, Enumerable.Repeat("", shiftY));
        x += shiftX;
        y += shiftY;

        while (rows.Count <= y)
            rows.Add("");
        var row = rows[y].PadRight(x + 1, Empty).ToCharArray();
        row[x] = CharOf(tile);
        rows[y] = new string(row);
        return (Trim(rows), shiftX, shiftY);
    }

    /// <summary>The cell containing <paramref name="local"/>, a point in the entity's own space.</summary>
    public (int X, int Y) CellAt(Vector2 local)
    {
        var size = TileSize > 0 ? TileSize : 1;
        return ((int)MathF.Floor(local.X / size), (int)MathF.Floor(-local.Y / size));
    }

    /// <summary>The center of cell (<paramref name="x"/>, <paramref name="y"/>) in the entity's own space.</summary>
    public Vector2 CellCenter(int x, int y) => new((x + 0.5f) * TileSize, -(y + 0.5f) * TileSize);

    /// <summary>The part of the tileset image showing <paramref name="tile"/>: (u, v, width, height) in 0–1 image space.</summary>
    public Vector4 TileRect(int tile)
    {
        var columns = Math.Max(1, Columns);
        var rows = Math.Max(1, Rows);
        tile = ((tile % (columns * rows)) + columns * rows) % (columns * rows);
        return new Vector4((float)(tile % columns) / columns, (float)(tile / columns) / rows, 1f / columns, 1f / rows);
    }

    /// <summary>Every non-empty cell and its tile, row by row.</summary>
    public IEnumerable<(int X, int Y, int Tile)> Cells()
    {
        for (var y = 0; y < _tiles.Length; y++)
        {
            var row = _tiles[y];
            for (var x = 0; x < row.Length; x++)
            {
                if (TileOf(row[x]) is var tile and >= 0)
                    yield return (x, y, tile);
            }
        }
    }

    /// <summary>Horizontal runs of non-empty cells, for collision. Cached until the tiles change.</summary>
    public IReadOnlyList<(int X, int Y, int Length)> Runs() =>
        _runs ??= FindRuns(_tiles, c => TileOf(c) >= 0 ? 0 : -1).Select(r => (r.X, r.Y, r.Length)).ToList();

    /// <summary>
    /// Horizontal runs of tiles that collide, split where solid tiles meet one-way tiles. Background tiles are left out.
    /// Cached until the tiles or tile settings change.
    /// </summary>
    public IReadOnlyList<(int X, int Y, int Length, bool OneWay)> CollisionRuns() =>
        _collisionRuns ??= FindRuns(_tiles, c => CollisionOf(TileOf(c)))
            .Select(r => (r.X, r.Y, r.Length, r.Kind == 1)).ToList();

    /// <summary>True if <paramref name="tile"/> is listed in <see cref="BackgroundTiles"/>.</summary>
    public bool IsBackground(int tile) => tile is >= 0 and < MaxTiles && _background[tile];

    /// <summary>True if <paramref name="tile"/> is listed in <see cref="OneWayTiles"/> (and not as a background tile).</summary>
    public bool IsOneWay(int tile) => tile is >= 0 and < MaxTiles && _oneWay[tile] && !_background[tile];

    /// <summary>
    /// Reads a list of tile numbers and ranges like "3, 7-9". Anything that isn't a number or range is ignored,
    /// as are tiles outside 0–61.
    /// </summary>
    public static bool[] ParseTileSet(string? text)
    {
        var set = new bool[MaxTiles];
        foreach (var part in (text ?? "").Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var dash = part.IndexOf('-', 1);
            var (first, last) = dash < 0 ? (part, part) : (part[..dash], part[(dash + 1)..]);
            if (!int.TryParse(first, out var from) || !int.TryParse(last, out var to))
                continue;
            if (from > to)
                (from, to) = (to, from);
            for (var tile = Math.Max(0, from); tile <= Math.Min(MaxTiles - 1, to); tile++)
                set[tile] = true;
        }

        return set;
    }

    /// <summary>The tile a character stands for, or -1 for empty (and anything unrecognized).</summary>
    public static int TileOf(char c) => TileChars.IndexOf(c);

    /// <summary>The character for <paramref name="tile"/>; -1 gives <see cref="Empty"/>.</summary>
    public static char CharOf(int tile) => tile >= 0 && tile < MaxTiles ? TileChars[tile] : Empty;

    // 0 = solid, 1 = one-way, -1 = doesn't collide (empty or background).
    private int CollisionOf(int tile) => tile < 0 || IsBackground(tile) ? -1 : IsOneWay(tile) ? 1 : 0;

    // Runs of cells with the same non-negative kind; a change of kind ends a run.
    private static List<(int X, int Y, int Length, int Kind)> FindRuns(string[] tiles, Func<char, int> kindOf)
    {
        var runs = new List<(int, int, int, int)>();
        for (var y = 0; y < tiles.Length; y++)
        {
            var row = tiles[y];
            var start = -1;
            var runKind = -1;
            for (var x = 0; x <= row.Length; x++)
            {
                var kind = x < row.Length ? kindOf(row[x]) : -1;
                if (kind == runKind)
                    continue;
                if (start >= 0)
                    runs.Add((start, y, x - start, runKind));
                (start, runKind) = kind >= 0 ? (x, kind) : (-1, -1);
            }
        }

        return runs;
    }

    private static string[] Trim(List<string> rows)
    {
        for (var i = 0; i < rows.Count; i++)
            rows[i] = rows[i].TrimEnd(Empty, ' ');
        while (rows.Count > 0 && rows[^1].Length == 0)
            rows.RemoveAt(rows.Count - 1);
        return rows.ToArray();
    }
}
