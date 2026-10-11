using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// A grid of tiles from one sprite-sheet image, drawn and (optionally) collided with as a whole. The grid starts at
/// the entity's position and runs right and down. Paint it in the Scene view, edit <see cref="Tiles"/> as text in the
/// inspector, or change it from scripts with <see cref="SetTile"/>, <see cref="FillRect"/> and <see cref="FloodFill"/>.
/// </summary>
/// <remarks>
/// Each row of <see cref="Tiles"/> is a line of characters, top row first: <c>.</c> is empty and
/// <c>0-9</c>, <c>a-z</c>, <c>A-Z</c> pick tiles 0–61 of the sheet. Rows are saved one per line, so levels diff
/// line by line in git.
/// </remarks>
[Category("Rendering")]
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
    private string? _animatedTiles;
    private IReadOnlyList<TileAnimation> _animations = [];
    private string? _ladderTiles;
    private bool[] _ladder = new bool[MaxTiles];
    private string? _autoTiles;
    private string? _slopeUpTiles;
    private bool[] _slopeUp = new bool[MaxTiles];
    private string? _slopeDownTiles;
    private bool[] _slopeDown = new bool[MaxTiles];
    private IReadOnlyList<(int X, int Y, bool Rising)>? _slopes;
    private IReadOnlyList<int> _autoGroups = [];

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

    /// <summary>
    /// Tiles that animate while the game runs, as runs of frames side by side in the tileset with an optional speed in
    /// frames a second: "16-19 @6" turns tiles 16, 17, 18 and 19 into water that ripples through all four. Separate runs
    /// with commas: "16-19 @6, 30-31 @2". A cell painted with a later frame starts the run from there, so neighbors
    /// needn't move in step.
    /// </summary>
    public string? AnimatedTiles
    {
        get => _animatedTiles;
        set
        {
            _animatedTiles = value;
            _animations = ParseAnimations(value);
        }
    }

    /// <summary>
    /// Tiles that are ladders: they never collide, and a <see cref="PlatformerMovement"/> climbs them with up and down.
    /// Tile numbers and ranges, e.g. "12, 13".
    /// </summary>
    public string? LadderTiles
    {
        get => _ladderTiles;
        set
        {
            _ladderTiles = value;
            _ladder = ParseTileSet(value);
            _collisionRuns = null;
        }
    }

    /// <summary>
    /// Tiles that are slopes rising to the right, like "/": solid below the line from their bottom-left corner to their
    /// top-right one, so bodies walk up and down them. Tile numbers and ranges, e.g. "20".
    /// </summary>
    public string? SlopeUpTiles
    {
        get => _slopeUpTiles;
        set
        {
            _slopeUpTiles = value;
            _slopeUp = ParseTileSet(value);
            _collisionRuns = null;
            _slopes = null;
        }
    }

    /// <summary>Tiles that are slopes falling to the right, like "\": solid below the line from their top-left corner to their bottom-right one.</summary>
    public string? SlopeDownTiles
    {
        get => _slopeDownTiles;
        set
        {
            _slopeDownTiles = value;
            _slopeDown = ParseTileSet(value);
            _collisionRuns = null;
            _slopes = null;
        }
    }

    /// <summary>1 for a tile that's a slope rising to the right, -1 for one falling to the right, 0 for anything else.</summary>
    public int SlopeOf(int tile) =>
        tile is < 0 or >= MaxTiles || _background[tile] ? 0 : _slopeUp[tile] ? 1 : _slopeDown[tile] ? -1 : 0;

    /// <summary>The cells painted with slope tiles and which way each rises. Cached until the tiles or tile settings change.</summary>
    public IReadOnlyList<(int X, int Y, bool Rising)> SlopeCells() =>
        _slopes ??= Cells().Where(c => SlopeOf(c.Tile) != 0).Select(c => (c.X, c.Y, SlopeOf(c.Tile) > 0)).ToList();

    /// <summary>
    /// Runs of 16 tiles in the tileset that fit themselves to their neighbors: paint with any tile of a run and each cell
    /// shows the piece that joins up with the cells around it painted from the same run. "16-31" (or just "16"), several
    /// separated by commas. A run's tiles go in order of which sides join, counting up 1, right 2, down 4 and left 8: the
    /// first is a lone block, the second joins only above, the last joins on every side.
    /// </summary>
    public string? AutoTiles
    {
        get => _autoTiles;
        set
        {
            _autoTiles = value;
            _autoGroups = ParseAutoTiles(value);
        }
    }

    /// <summary>Seconds its <see cref="AnimatedTiles"/> have been playing. Advanced by the game.</summary>
    [DoNotSerialize]
    public float AnimationTime { get; set; }

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
            _slopes = null;
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
    public (IReadOnlyList<string> Tiles, int ShiftX, int ShiftY) Painted(int x, int y, int tile) => PaintedRect(x, y, x, y, tile);

    /// <summary>
    /// Sets every cell in the rectangle between cells (<paramref name="fromX"/>, <paramref name="fromY"/>) and
    /// (<paramref name="toX"/>, <paramref name="toY"/>), corners included and either way round; -1 clears them. The grid
    /// grows to fit, like <see cref="SetTile"/>.
    /// </summary>
    public void FillRect(int fromX, int fromY, int toX, int toY, int tile)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Math.Min(fromX, toX), nameof(fromX));
        ArgumentOutOfRangeException.ThrowIfNegative(Math.Min(fromY, toY), nameof(fromY));
        Tiles = PaintedRect(fromX, fromY, toX, toY, tile).Tiles;
    }

    /// <summary>
    /// Fills the area around cell (<paramref name="x"/>, <paramref name="y"/>) with <paramref name="tile"/>, like a paint
    /// bucket (see <see cref="FillRegion"/> for what it reaches); -1 clears it.
    /// </summary>
    public void FloodFill(int x, int y, int tile) => Tiles = Filled(x, y, tile);

    /// <summary>
    /// The grid with every cell in the rectangle between two corner cells (either way round) set to
    /// <paramref name="tile"/>, leaving this map alone. Like <see cref="Painted"/>, the grid grows left and up to take
    /// in a rectangle reaching past those edges; clearing (-1) only reaches the cells already in the grid.
    /// </summary>
    public (IReadOnlyList<string> Tiles, int ShiftX, int ShiftY) PaintedRect(int fromX, int fromY, int toX, int toY, int tile)
    {
        CheckTile(tile);
        var (left, right) = (Math.Min(fromX, toX), Math.Max(fromX, toX));
        var (top, bottom) = (Math.Min(fromY, toY), Math.Max(fromY, toY));
        if (tile < 0)
        {
            // Erasing outside the grid changes nothing.
            (left, top, right, bottom) = (Math.Max(0, left), Math.Max(0, top), Math.Min(Width - 1, right), Math.Min(Height - 1, bottom));
            if (left > right || top > bottom)
                return (_tiles, 0, 0);
        }

        var shiftX = Math.Max(0, -left);
        var shiftY = Math.Max(0, -top);
        var rows = _tiles.Select(r => shiftX > 0 && r.Length > 0 ? new string(Empty, shiftX) + r : r).ToList();
        rows.InsertRange(0, Enumerable.Repeat("", shiftY));
        while (rows.Count <= bottom + shiftY)
            rows.Add("");

        var c = CharOf(tile);
        for (var y = top + shiftY; y <= bottom + shiftY; y++)
        {
            var row = rows[y].PadRight(right + shiftX + 1, Empty).ToCharArray();
            Array.Fill(row, c, left + shiftX, right - left + 1);
            rows[y] = new string(row);
        }

        var tiles = Trim(rows);
        return tiles.SequenceEqual(_tiles) ? (_tiles, 0, 0) : (tiles, shiftX, shiftY);
    }

    /// <summary>
    /// The grid with the area a fill from cell (<paramref name="x"/>, <paramref name="y"/>) reaches (see
    /// <see cref="FillRegion"/>) set to <paramref name="tile"/>, leaving this map alone. Unchanged when the cell is
    /// outside the grid or already holds that tile.
    /// </summary>
    public IReadOnlyList<string> Filled(int x, int y, int tile)
    {
        CheckTile(tile);
        if (GetTile(x, y) == tile)
            return _tiles;
        var region = FillRegion(x, y);
        if (region.Count == 0)
            return _tiles;

        var width = Width;
        var rows = _tiles.Select(r => r.PadRight(width, Empty).ToCharArray()).ToList();
        var c = CharOf(tile);
        foreach (var (cx, cy) in region)
            rows[cy][cx] = c;
        return Trim(rows.Select(r => new string(r)).ToList());
    }

    /// <summary>
    /// The cells a paint-bucket fill from cell (<paramref name="x"/>, <paramref name="y"/>) reaches: that cell and every
    /// cell joined to it side by side through cells holding the same tile (or all empty), within the grid's
    /// <see cref="Width"/> and <see cref="Height"/>. Empty when the cell is outside the grid.
    /// </summary>
    public IReadOnlyList<(int X, int Y)> FillRegion(int x, int y)
    {
        var (width, height) = (Width, Height);
        if (x < 0 || y < 0 || x >= width || y >= height)
            return [];

        var tile = GetTile(x, y);
        var seen = new bool[width * height];
        var region = new List<(int X, int Y)>();
        var open = new Stack<(int X, int Y)>();
        open.Push((x, y));
        seen[y * width + x] = true;
        while (open.TryPop(out var cell))
        {
            region.Add(cell);
            Visit(cell.X + 1, cell.Y);
            Visit(cell.X - 1, cell.Y);
            Visit(cell.X, cell.Y + 1);
            Visit(cell.X, cell.Y - 1);
        }

        return region;

        void Visit(int cx, int cy)
        {
            if (cx < 0 || cy < 0 || cx >= width || cy >= height || seen[cy * width + cx] || GetTile(cx, cy) != tile)
                return;
            seen[cy * width + cx] = true;
            open.Push((cx, cy));
        }
    }

    private static void CheckTile(int tile)
    {
        if (tile < -1 || tile >= MaxTiles)
            throw new ArgumentOutOfRangeException(nameof(tile), $"Tiles are numbered 0 to {MaxTiles - 1}, or -1 for empty.");
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

    /// <summary>
    /// The tile to draw for a cell painted with <paramref name="tile"/> at <see cref="AnimationTime"/>: itself, or the
    /// frame its <see cref="AnimatedTiles"/> run has reached.
    /// </summary>
    public int ShownTile(int tile)
    {
        foreach (var animation in _animations)
        {
            if (tile < animation.First || tile > animation.Last)
                continue;
            var length = animation.Last - animation.First + 1;
            var step = (long)MathF.Floor(Math.Max(0, AnimationTime) * animation.FramesPerSecond);
            return animation.First + (int)((tile - animation.First + step) % length);
        }

        return tile;
    }

    /// <summary>
    /// The tile to draw for cell (<paramref name="x"/>, <paramref name="y"/>) painted with <paramref name="tile"/>: the
    /// piece of its <see cref="AutoTiles"/> run that joins its neighbors, then animated by <see cref="ShownTile(int)"/>.
    /// </summary>
    public int ShownTile(int x, int y, int tile)
    {
        var group = AutoGroupOf(tile);
        if (group >= 0)
        {
            var mask = (Joins(x, y - 1) ? 1 : 0) | (Joins(x + 1, y) ? 2 : 0) | (Joins(x, y + 1) ? 4 : 0) | (Joins(x - 1, y) ? 8 : 0);
            tile = _autoGroups[group] + mask;
        }

        return ShownTile(tile);

        bool Joins(int cx, int cy) => AutoGroupOf(GetTile(cx, cy)) == group;
    }

    // Which AutoTiles run a tile belongs to, or -1.
    private int AutoGroupOf(int tile)
    {
        if (tile < 0)
            return -1;
        for (var i = 0; i < _autoGroups.Count; i++)
        {
            if (tile >= _autoGroups[i] && tile < _autoGroups[i] + 16)
                return i;
        }

        return -1;
    }

    /// <summary>True if <paramref name="tile"/> is listed in <see cref="LadderTiles"/>.</summary>
    public bool IsLadder(int tile) => tile is >= 0 and < MaxTiles && _ladder[tile];

    /// <summary>
    /// The ladder cell containing <paramref name="world"/>, a world position, as the world X of its middle; null if there's
    /// no ladder tile there.
    /// </summary>
    public float? LadderAt(Vector2 world)
    {
        var matrix = Entity?.Transform.WorldMatrix ?? Matrix4x4.Identity;
        if (!Matrix4x4.Invert(matrix, out var toLocal))
            return null;
        var (x, y) = CellAt(Vector2.Transform(world, toLocal));
        return IsLadder(GetTile(x, y)) ? Vector2.Transform(CellCenter(x, y), matrix).X : null;
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

    /// <summary>
    /// True if cell (<paramref name="x"/>, <paramref name="y"/>) is a wall for <see cref="FindPath"/>: a solid tile on a
    /// <see cref="Solid"/> map. Empty, background, ladder and one-way tiles can be walked through.
    /// </summary>
    public bool IsBlocked(int x, int y) => Solid && GetTile(x, y) is var tile and >= 0 && CollisionOf(tile) == 0;

    /// <summary>
    /// The shortest way from <paramref name="from"/> to <paramref name="to"/> (world positions) that doesn't go through
    /// walls, as world positions to head for one after another, ending at the center of <paramref name="to"/>'s cell.
    /// Straight stretches are one waypoint. Empty if there's no way there, or either end is inside a wall. Moves
    /// diagonally unless <paramref name="diagonal"/> is false, but never cuts a wall's corner. The search stays within
    /// the map, plus one cell all round so paths can go around its outside.
    /// </summary>
    /// <example>
    /// <code>
    /// var path = level.FindPath(Transform.WorldPosition, player.Transform.WorldPosition);
    /// if (path.Count > 0)
    ///     body.Velocity = Vector2.Normalize(path[0] - here) * Speed;
    /// </code>
    /// </example>
    public IReadOnlyList<Vector2> FindPath(Vector2 from, Vector2 to, bool diagonal = true)
    {
        var world = Entity?.Transform.WorldMatrix ?? Matrix4x4.Identity;
        if (!Matrix4x4.Invert(world, out var toLocal))
            return [];

        var start = CellAt(Vector2.Transform(from, toLocal));
        var goal = CellAt(Vector2.Transform(to, toLocal));
        var cells = FindCellPath(start, goal, diagonal);
        return cells.Select(c => Vector2.Transform(CellCenter(c.X, c.Y), world)).ToList();
    }

    /// <summary>A world position, given as a <see cref="Vector3"/>; its Z is ignored.</summary>
    public IReadOnlyList<Vector2> FindPath(Vector3 from, Vector3 to, bool diagonal = true) =>
        FindPath(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y), diagonal);

    /// <summary>
    /// A* over cells: the cells to step through after <paramref name="start"/>, up to <paramref name="goal"/>, keeping only
    /// the cells where the direction changes (and the goal). Empty if unreachable; just the goal if already there.
    /// </summary>
    public IReadOnlyList<(int X, int Y)> FindCellPath((int X, int Y) start, (int X, int Y) goal, bool diagonal = true)
    {
        if (IsBlocked(start.X, start.Y) || IsBlocked(goal.X, goal.Y))
            return [];
        if (start == goal)
            return [goal];

        // The searched area: the grid plus a margin of one cell, widened to take in both ends.
        var minX = Math.Min(-1, Math.Min(start.X, goal.X));
        var minY = Math.Min(-1, Math.Min(start.Y, goal.Y));
        var maxX = Math.Max(Width, Math.Max(start.X, goal.X));
        var maxY = Math.Max(Height, Math.Max(start.Y, goal.Y));
        var width = maxX - minX + 1;
        var cellCount = width * (maxY - minY + 1);
        if (cellCount > 250_000)
            return [];

        // A state is a cell plus the way the last step went into it (9 ways, the middle one meaning "just started"), so
        // that of equally short ways the search can prefer the one that turns least: fewer waypoints, no zig-zags.
        const int startHeading = 4;
        int StateOf((int X, int Y) c, int heading) => ((c.Y - minY) * width + (c.X - minX)) * 9 + heading;
        (int X, int Y) CellOf(int state) => (state / 9 % width + minX, state / 9 / width + minY);

        const float straight = 1f;
        const float turnCost = 1e-3f;
        var diagonalCost = MathF.Sqrt(2);
        float Estimate((int X, int Y) c)
        {
            var dx = Math.Abs(c.X - goal.X);
            var dy = Math.Abs(c.Y - goal.Y);
            return diagonal ? straight * Math.Abs(dx - dy) + diagonalCost * Math.Min(dx, dy) : straight * (dx + dy);
        }

        var cost = new float[cellCount * 9];
        Array.Fill(cost, float.PositiveInfinity);
        var cameFrom = new int[cellCount * 9];
        var open = new PriorityQueue<int, float>();
        var startState = StateOf(start, startHeading);
        cost[startState] = 0;
        open.Enqueue(startState, Estimate(start));

        while (open.TryDequeue(out var current, out var priority))
        {
            var cell = CellOf(current);
            if (cell == goal)
                return Waypoints(cameFrom, startState, current, CellOf);
            if (priority > cost[current] + Estimate(cell) + 1e-5f)
                continue; // A stale entry: this state was reached more cheaply since.

            var heading = current % 9;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dy == 0) || (!diagonal && dx != 0 && dy != 0))
                        continue;
                    var next = (X: cell.X + dx, Y: cell.Y + dy);
                    if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY || IsBlocked(next.X, next.Y))
                        continue;
                    if (dx != 0 && dy != 0 && (IsBlocked(cell.X + dx, cell.Y) || IsBlocked(cell.X, cell.Y + dy)))
                        continue;

                    var nextHeading = (dy + 1) * 3 + dx + 1;
                    var nextState = StateOf(next, nextHeading);
                    var turned = heading != startHeading && heading != nextHeading;
                    var nextCost = cost[current] + (dx != 0 && dy != 0 ? diagonalCost : straight) + (turned ? turnCost : 0);
                    if (nextCost >= cost[nextState])
                        continue;
                    cost[nextState] = nextCost;
                    cameFrom[nextState] = current;
                    open.Enqueue(nextState, nextCost + Estimate(next));
                }
            }
        }

        return [];
    }

    // Walks back from the goal, keeping the corners: a cell where the next step goes a different way from the last.
    private static List<(int X, int Y)> Waypoints(int[] cameFrom, int start, int goal, Func<int, (int X, int Y)> cellOf)
    {
        var cells = new List<(int X, int Y)>();
        for (var state = goal; state != start; state = cameFrom[state])
            cells.Add(cellOf(state));
        cells.Reverse();

        var waypoints = new List<(int X, int Y)>();
        var previous = cellOf(start);
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var isLast = i == cells.Count - 1;
            if (isLast || (cells[i + 1].X - cell.X, cells[i + 1].Y - cell.Y) != (cell.X - previous.X, cell.Y - previous.Y))
                waypoints.Add(cell);
            previous = cell;
        }

        return waypoints;
    }

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

    /// <summary>Reads <see cref="AnimatedTiles"/>: "first-last @fps" runs separated by commas. Anything else is skipped.</summary>
    public static IReadOnlyList<TileAnimation> ParseAnimations(string? text)
    {
        var runs = new List<TileAnimation>();
        foreach (var part in (text ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var at = part.IndexOf('@');
            var range = (at >= 0 ? part[..at] : part).Trim();
            var fps = 4f;
            if (at >= 0 && !(float.TryParse(part[(at + 1)..].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out fps) && fps > 0))
                continue;
            var dash = range.IndexOf('-');
            if (dash <= 0 || !int.TryParse(range[..dash].Trim(), out var first) || !int.TryParse(range[(dash + 1)..].Trim(), out var last))
                continue;
            if (first > last)
                (first, last) = (last, first);
            if (first >= 0 && last < MaxTiles && last > first)
                runs.Add(new TileAnimation(first, last, fps));
        }

        return runs;
    }

    /// <summary>
    /// Reads <see cref="AutoTiles"/>: the first tile of each run of 16, as "16-31" or "16", separated by commas. Runs that
    /// aren't 16 long or don't fit in the tileset's 62 tiles are skipped.
    /// </summary>
    public static IReadOnlyList<int> ParseAutoTiles(string? text)
    {
        var firsts = new List<int>();
        foreach (var part in (text ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = part.IndexOf('-', 1);
            if (!int.TryParse(dash < 0 ? part : part[..dash].Trim(), out var first))
                continue;
            if (dash >= 0 && !(int.TryParse(part[(dash + 1)..].Trim(), out var last) && last == first + 15))
                continue;
            if (first >= 0 && first + 15 < MaxTiles && !firsts.Contains(first))
                firsts.Add(first);
        }

        return firsts;
    }

    /// <summary>The tile a character stands for, or -1 for empty (and anything unrecognized).</summary>
    public static int TileOf(char c) => TileChars.IndexOf(c);

    /// <summary>The character for <paramref name="tile"/>; -1 gives <see cref="Empty"/>.</summary>
    public static char CharOf(int tile) => tile >= 0 && tile < MaxTiles ? TileChars[tile] : Empty;

    // 0 = solid, 1 = one-way, -1 = not a box (empty, background, a ladder, or a slope, which collides by its own shape).
    private int CollisionOf(int tile) => tile < 0 || IsBackground(tile) || IsLadder(tile) || SlopeOf(tile) != 0 ? -1 : IsOneWay(tile) ? 1 : 0;

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

/// <summary>A run of tiles that animate one after another; see <see cref="Tilemap.AnimatedTiles"/>.</summary>
public readonly record struct TileAnimation(int First, int Last, float FramesPerSecond);
