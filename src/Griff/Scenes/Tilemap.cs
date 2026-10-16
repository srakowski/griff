using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// A grid of tiles from one or more tileset images, drawn and (optionally) collided with as a whole. The grid starts
/// at the entity's position and runs right and down, on an isometric map (see <see cref="Shape"/>) as diamonds
/// running down to the right and down to the left, and on a hex map as rows of hexagons. Paint it in the Scene view, edit <see cref="Tiles"/> as text in the
/// inspector, or change it from scripts with <see cref="SetTile"/>, <see cref="FillRect"/> and <see cref="FloodFill"/>.
/// A tilemap with no <see cref="Image"/> on an entity inside another tilemap's is one of its layers (see
/// <see cref="LayerOf"/>), painted on its own: it uses that map's tilesets, tile size and tile settings, while its
/// <see cref="Tiles"/>, <see cref="Tint"/>, <see cref="Order"/> and collision (<see cref="Solid"/>, <see cref="Layer"/>,
/// <see cref="IgnoreLayers"/>) are its own.
/// </summary>
/// <remarks>
/// Each row of <see cref="Tiles"/> is a line of characters, one per cell, top row first: <c>.</c> is empty and
/// <c>0-9</c>, <c>a-z</c>, <c>A-Z</c> pick tiles 0–61; tiles from 62 on are accented letters, then CJK characters
/// (see <see cref="CharOf"/>). Rows are saved one per line, so levels diff line by line in git.
/// </remarks>
[Category("Rendering")]
[ComponentName("Tilemap")]
public sealed partial class Tilemap : Component, ISortsInParts
{
    /// <summary>The character for an empty cell.</summary>
    public const char Empty = '.';

    /// <summary>One more than the highest tile number a cell can hold.</summary>
    public const int MaxTiles = 62 + 398 + 20992;

    /// <summary>Each tileset after the first starts at the next multiple of this after the tiles before it.</summary>
    public const int TilesetStep = 100;

    private string[] _tiles = [];
    private IReadOnlyList<(int X, int Y, int Length)>? _runs;
    private IReadOnlyList<(int X, int Y, int Length, bool OneWay)>? _collisionRuns;
    private string? _backgroundTiles;
    private string? _oneWayTiles;
    private bool[] _background = [];
    private bool[] _oneWay = [];
    private string? _animatedTiles;
    private IReadOnlyList<TileAnimation> _animations = [];
    private string? _ladderTiles;
    private bool[] _ladder = [];
    private string? _autoTiles;
    private string? _slopeUpTiles;
    private bool[] _slopeUp = [];
    private string? _slopeDownTiles;
    private bool[] _slopeDown = [];
    private IReadOnlyList<(int X, int Y, bool Rising)>? _slopes;
    private IReadOnlyList<int> _autoGroups = [];
    private string[] _moreTilesets = [];
    private TilesetCache? _tilesets;
    private float _tileSize = 1f;
    private TileShape _shape;
    private float _tileHeight = 0.5f;

    // Bumped when a tile setting the collision caches depend on changes, so a layer's caches see its base map's changes.
    private int _settingsVersion;
    private (Tilemap Base, int Version)? _cachedFor;

    /// <summary>
    /// The tilemap this one is a layer of, or null when it isn't one. A tilemap with no <see cref="Image"/> of its own
    /// on an entity inside another tilemap's entity is a layer of that map: it's drawn with that map's tilesets,
    /// <see cref="TileSize"/> and tile settings (Background, One Way, Ladder, Slope, Animated and Auto Tiles), while
    /// its <see cref="Tiles"/>, <see cref="Tint"/>, <see cref="Order"/> and collision (<see cref="Solid"/>,
    /// <see cref="Layer"/>, <see cref="IgnoreLayers"/>) are its own. Giving it an Image makes it a map of its own again.
    /// Art > Add Layer to Tilemap adds one.
    /// </summary>
    public Tilemap? LayerOf => string.IsNullOrWhiteSpace(Image) ? Entity?.Parent?.GetComponent<Tilemap>() : null;

    /// <summary>
    /// The map whose tilesets and tile settings this one uses: itself, or for a layer the map it's a layer of (the
    /// bottom one, for a layer of a layer).
    /// </summary>
    public Tilemap BaseMap
    {
        get
        {
            var map = this;
            while (map.LayerOf is { } owner)
                map = owner;
            return map;
        }
    }

    /// <summary>
    /// The layers this map belongs to: its <see cref="BaseMap"/> first, then every layer of it (and layers of those), in
    /// the order of the Hierarchy. Just this map when it has no layers.
    /// </summary>
    public IReadOnlyList<Tilemap> Layers()
    {
        var layers = new List<Tilemap>();
        Add(BaseMap);
        return layers;

        void Add(Tilemap map)
        {
            layers.Add(map);
            foreach (var child in map.Entity?.Children ?? [])
            {
                if (child.GetComponent<Tilemap>() is { } layer && layer.LayerOf == map)
                    Add(layer);
            }
        }
    }

    /// <summary>
    /// On a layer, the tileset settings it takes from its map aren't its own, so the Inspector hides them; and
    /// <see cref="TileHeight"/> only means something on an isometric map.
    /// </summary>
    public override bool Uses(string property) =>
        (LayerOf is null || !TilesetSettings.Contains(property)) && (property != nameof(TileHeight) || Shape == TileShape.Isometric);

    /// <summary>On a layer, which map it's a layer of; and a map sorting by Y in a scene whose camera doesn't.</summary>
    public override string? SetupNote()
    {
        if (LayerOf is { } owner)
            return $"A layer of {owner.Entity?.Name ?? "its tilemap"}: drawn with its tilesets, tile size and tile settings.";
        if (SortByY && Entity?.Scene is { } scene && !scene.Entities.Any(e => e.GetComponent<Camera>() is { SortByY: true }))
            return "Turn on the camera's Sort By Y too, so its rows sort among the sprites.";
        return null;
    }

    // What a layer takes from its base map.
    private static readonly HashSet<string> TilesetSettings =
    [
        nameof(Columns), nameof(Rows), nameof(MoreTilesets), nameof(Shape), nameof(TileSize), nameof(TileHeight), nameof(BackgroundTiles), nameof(OneWayTiles),
        nameof(AnimatedTiles), nameof(LadderTiles), nameof(SlopeUpTiles), nameof(SlopeDownTiles), nameof(AutoTiles),
    ];

    /// <summary>
    /// The first tileset's image, relative to Assets: tiles 0 on. Without one, its tiles draw as solid squares of
    /// <see cref="Tint"/>, or inside another tilemap's entity, it's a layer of that map (see <see cref="LayerOf"/>).
    /// <see cref="MoreTilesets"/> adds others.
    /// </summary>
    public string? Image { get; set; }

    /// <summary>Tiles across the first tileset's image.</summary>
    public int Columns { get; set; } = 1;

    /// <summary>Tiles down the first tileset's image.</summary>
    public int Rows { get; set; } = 1;

    /// <summary>
    /// Tilesets after the first (<see cref="Image"/>), one per line: an image and its tiles across and down, like
    /// "Tiles/props.png 4x4". Each one's tiles are numbered on from the next hundred after the tiles before it: with a
    /// first tileset of 16 tiles, the second's are 100 on and a third's 200 on, so a tileset that grows (up to the next
    /// hundred) doesn't renumber the ones after it. Tile number fields (Background Tiles, Auto Tiles and the rest) and
    /// <see cref="GetTile"/> use these numbers. Art > Add Tileset to Tilemap adds one.
    /// </summary>
    [Tilesets]
    public IReadOnlyList<string> MoreTilesets
    {
        get => _moreTilesets;
        set => _moreTilesets = value?.ToArray() ?? [];
    }

    /// <summary>
    /// How the cells are laid out: <see cref="TileShape.Square"/> in rows and columns, or
    /// <see cref="TileShape.Isometric"/> as diamonds twice as wide as they're tall, the grid's columns running down to the
    /// right from the entity's position (the top corner of cell 0, 0) and its rows down to the left. Tiles, painting,
    /// <see cref="CellAt"/> and <see cref="CellCenter"/> all follow it, and so do collisions: on an isometric map a body
    /// meets a solid tile's diamond with the diamond under its collider (see <see cref="Body"/>), sliding along the
    /// walls, and grid movers step from diamond to diamond. <see cref="TileShape.Hex"/> makes the cells hexagons
    /// pointing up and down, a tile wide and a tile tall, in rows three quarters of a tile apart with every odd row (the
    /// second, fourth...) shifted half a tile to the right, the entity's position at the top left of cell 0, 0's picture;
    /// grid movers step from hexagon to hexagon on them (and Tactics units walk and fight over them), and a body's
    /// collider meets a solid hexagon's six sides and slides along them. A layer's is its map's (see <see cref="LayerOf"/>).
    /// </summary>
    public TileShape Shape
    {
        get => LayerOf is { } owner ? owner.Shape : _shape;
        set => _shape = value;
    }

    /// <summary>
    /// Width and height of one cell in world units, before the transform's scale; on an isometric map, a diamond's width
    /// (it's half as tall), and on a hex map a hexagon's width and height (its rows are three quarters of it apart). A
    /// layer's is its map's (see <see cref="LayerOf"/>).
    /// </summary>
    public float TileSize
    {
        get => LayerOf is { } owner ? owner.TileSize : _tileSize;
        set => _tileSize = value;
    }

    /// <summary>
    /// On an isometric map, how tall each tile's picture is drawn, in tile widths: 0.5 (the default) is a flat diamond,
    /// and 1 a square picture standing on its diamond, a block or wall as tall again (a tileset of 32 by 32 pixel tiles
    /// with a 32 by 16 diamond at the bottom). A picture stands on its cell's bottom corner. A layer's is its map's.
    /// </summary>
    public float TileHeight
    {
        get => LayerOf is { } owner ? owner.TileHeight : _tileHeight;
        set => _tileHeight = value;
    }

    /// <summary>
    /// Draws each row of tiles (each diagonal row, on an isometric map) in depth among the sprites, as the camera's
    /// <see cref="Camera.SortByY"/> does for things that move: whatever stands lower down draws in front, so a character
    /// walks behind a wall and in front of it. Needs the camera's Sort By Y on too. Off, the map draws as one, behind
    /// what's lower in the Hierarchy at its <see cref="Order"/>: right for floors, so put walls and props on a layer of
    /// their own (see <see cref="LayerOf"/>) and turn this on there. The tiles always draw back to front among
    /// themselves.
    /// </summary>
    public bool SortByY { get; set; }

    /// <summary>Multiplies the tiles' colors: white leaves them as they are, and a lower alpha fades them.</summary>
    public Color Tint
    {
        get => _tint.Value;
        set => _tint.Set(this, value);
    }

    // Kept as it was when the step began too, so a fade is drawn between steps.
    private Smoothed<Color> _tint = new(Color.White);

    /// <summary>Its <see cref="Tint"/> in a frame drawn as <paramref name="blend"/> says: part way through a fade between steps (for a component drawing its tiles itself).</summary>
    public Color TintDrawn(StepBlend blend) => blend.Of(_tint);

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/>.</summary>
    public int Order { get; set; }

    /// <summary>When set, tiles are walls and floors that bodies collide with (except <see cref="BackgroundTiles"/>).</summary>
    public bool Solid { get; set; } = true;

    /// <summary>
    /// Tiles that never collide, for decoration like grass, windows or signs: tile numbers and ranges, e.g. "10-15, 20".
    /// </summary>
    [TileNumbers]
    public string? BackgroundTiles
    {
        get => _backgroundTiles;
        set
        {
            _backgroundTiles = value;
            _background = ParseTileSet(value);
            _settingsVersion++;
        }
    }

    /// <summary>
    /// Tiles that only block bodies landing on top, like <see cref="BoxCollider.OneWay"/>: tile numbers and ranges,
    /// e.g. "4, 5".
    /// </summary>
    [TileNumbers]
    public string? OneWayTiles
    {
        get => _oneWayTiles;
        set
        {
            _oneWayTiles = value;
            _oneWay = ParseTileSet(value);
            _settingsVersion++;
        }
    }

    /// <summary>
    /// Tiles that animate while the game runs, as runs of frames side by side in the tileset with an optional speed in
    /// frames a second: "16-19 @6" turns tiles 16, 17, 18 and 19 into water that ripples through all four. Separate runs
    /// with commas: "16-19 @6, 30-31 @2". A cell painted with a later frame starts the run from there, so neighbors
    /// needn't move in step.
    /// </summary>
    [TileAnimations]
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
    [TileNumbers]
    public string? LadderTiles
    {
        get => _ladderTiles;
        set
        {
            _ladderTiles = value;
            _ladder = ParseTileSet(value);
            _settingsVersion++;
        }
    }

    /// <summary>
    /// Tiles that are slopes rising to the right, like "/": solid below the line from their bottom-left corner to their
    /// top-right one, so bodies walk up and down them. Tile numbers and ranges, e.g. "20".
    /// </summary>
    [TileNumbers]
    public string? SlopeUpTiles
    {
        get => _slopeUpTiles;
        set
        {
            _slopeUpTiles = value;
            _slopeUp = ParseTileSet(value);
            _settingsVersion++;
        }
    }

    /// <summary>Tiles that are slopes falling to the right, like "\": solid below the line from their top-left corner to their bottom-right one.</summary>
    [TileNumbers]
    public string? SlopeDownTiles
    {
        get => _slopeDownTiles;
        set
        {
            _slopeDownTiles = value;
            _slopeDown = ParseTileSet(value);
            _settingsVersion++;
        }
    }

    /// <summary>1 for a tile that's a slope rising to the right, -1 for one falling to the right, 0 for anything else.</summary>
    public int SlopeOf(int tile)
    {
        var map = BaseMap;
        return InSet(map._background, tile) ? 0 : InSet(map._slopeUp, tile) ? 1 : InSet(map._slopeDown, tile) ? -1 : 0;
    }

    /// <summary>The cells painted with slope tiles and which way each rises. Cached until the tiles or tile settings change.</summary>
    public IReadOnlyList<(int X, int Y, bool Rising)> SlopeCells()
    {
        CheckCaches();
        return _slopes ??= Cells().Where(c => SlopeOf(c.Tile) != 0).Select(c => (c.X, c.Y, SlopeOf(c.Tile) > 0)).ToList();
    }

    // Drops the caches that depend on the tile settings when they (or the map they come from) have changed.
    private void CheckCaches()
    {
        var map = BaseMap;
        if (_cachedFor is var (cachedBase, version) && cachedBase == map && version == map._settingsVersion)
            return;
        _collisionRuns = null;
        _slopes = null;
        _cachedFor = (map, map._settingsVersion);
    }

    /// <summary>
    /// Runs of 16 tiles in the tileset that fit themselves to their neighbors: paint with any tile of a run and each cell
    /// shows the piece that joins up with the cells around it painted from the same run. "16-31" (or just "16"), several
    /// separated by commas. A run's tiles go in order of which sides join, counting up 1, right 2, down 4 and left 8: the
    /// first is a lone block, the second joins only above, the last joins on every side.
    /// </summary>
    [AutoTiles]
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
    [Names(NameKind.Layer, Defines = true)]
    public string? Layer { get; set; }

    /// <summary>Layers that pass through the map's tiles, like <see cref="Collider.IgnoreLayers"/>, e.g. "Ghost".</summary>
    [Names(NameKind.Layer, Many = true)]
    public string? IgnoreLayers { get; set; }

    /// <summary>The grid, top row first; see the remarks on <see cref="Tilemap"/> for the format.</summary>
    [PlainText("the grid, a line a row: painted in the Scene view with the tile palette")]
    public IReadOnlyList<string> Tiles
    {
        get => _tiles;
        set
        {
            // Copied so nobody else holds the array; the map only ever replaces it, which keeps undo snapshots intact.
            var tiles = value?.ToArray() ?? [];
            if (Solid && Entity is { Scene.Step: > 0, IsActive: true } entity)
                VacateChanged(entity.Scene!, tiles);
            _tiles = tiles;
            _runs = null;
            _slopes = null;
            _collisionRuns = null;
        }
    }

    // While the game runs, tiles changing might take away what holds up a body asleep on them: where the rows that changed
    // are, to wake what's there.
    private void VacateChanged(Scene scene, string[] tiles)
    {
        var (first, last) = (-1, -1);
        for (var y = 0; y < Math.Max(tiles.Length, _tiles.Length); y++)
        {
            if (y < tiles.Length && y < _tiles.Length && string.Equals(tiles[y], _tiles[y], StringComparison.Ordinal))
                continue;
            first = first < 0 ? y : first;
            last = y;
        }

        if (first >= 0)
            scene.Vacate(RowsBounds(first, last));
    }

    /// <summary>The box in the world round the map's cells, from its first row and column to its last.</summary>
    [Area]
    public Box Area
    {
        get
        {
            if (Entity is null)
                return default;
            var world = Entity.Transform.WorldMatrix;
            var (width, height) = (Math.Max(Width, 1), Math.Max(Height, 1));
            var (min, max) = (new Vector2(float.PositiveInfinity), new Vector2(float.NegativeInfinity));
            foreach (var (x, y) in (ReadOnlySpan<(float, float)>)[(0, 0), (width, 0), (0, height), (width, height)])
            {
                var point = Vector2.Transform(PointAt(x, y), world);
                (min, max) = (Vector2.Min(min, point), Vector2.Max(max, point));
            }

            return new Box((min + max) / 2, (max - min) / 2);
        }
    }

    // The box in the world round rows `first` to `last` of the map, a cell more all round.
    internal Box RowsBounds(int first, int last)
    {
        var world = Entity!.Transform.WorldMatrix;
        var width = Math.Max(Width, 1);
        var (min, max) = (new Vector2(float.PositiveInfinity), new Vector2(float.NegativeInfinity));
        foreach (var (x, y) in (ReadOnlySpan<(float, float)>)[(-1, first - 1), (width + 1, first - 1), (-1, last + 2), (width + 1, last + 2)])
        {
            var point = Vector2.Transform(PointAt(x, y), world);
            (min, max) = (Vector2.Min(min, point), Vector2.Max(max, point));
        }

        return new Box((min + max) / 2, (max - min) / 2);
    }

    /// <summary>Number of columns: the length of the longest row.</summary>
    public int Width
    {
        get
        {
            var width = 0;
            foreach (var row in _tiles)
                width = Math.Max(width, row.Length);
            return width;
        }
    }

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
    /// in a rectangle reaching past those edges (a hex map up an even number of rows, so its odd rows stay the shifted
    /// ones); clearing (-1) only reaches the cells already in the grid.
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
        // A hex map's odd rows are the shifted ones, so it grows up two rows at a time to keep each row's shift.
        if (Shape == TileShape.Hex && shiftY % 2 == 1)
            shiftY++;
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
    /// cell joined to it side by side (see <see cref="Neighbors"/>) through cells holding the same tile (or all empty), within the grid's
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
            foreach (var (nx, ny) in Neighbors(cell.X, cell.Y))
                Visit(nx, ny);
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
        if (Shape == TileShape.Hex)
            return HexAt(local, size);
        if (Shape != TileShape.Isometric)
            return ((int)MathF.Floor(local.X / size), (int)MathF.Floor(-local.Y / size));
        var grid = GridAt(local);
        return ((int)MathF.Floor(grid.X), (int)MathF.Floor(grid.Y));
    }

    // The hexagon a point is in: the one whose middle is nearest, measured with the hexagons stretched up and down until
    // they're regular (a tile tall, they're a little squat), where each one holds the points nearer its middle than any
    // other's.
    private static (int X, int Y) HexAt(Vector2 local, float size)
    {
        var stretch = 2 / MathF.Sqrt(3);
        var row = (int)MathF.Floor(-local.Y / (size * 0.75f));
        var (best, nearest) = ((0, 0), float.PositiveInfinity);
        for (var y = row - 1; y <= row + 1; y++)
        {
            var column = (int)MathF.Floor(local.X / size - (y & 1) * 0.5f);
            for (var x = column - 1; x <= column + 1; x++)
            {
                var away = local - HexCenter(x, y, size);
                var distance = away.X * away.X + away.Y * away.Y * stretch * stretch;
                if (distance < nearest)
                    (best, nearest) = ((x, y), distance);
            }
        }

        return best;
    }

    // The middle of hexagon x, y on a hex map: odd rows are shifted half a tile right, and rows are 3/4 of a tile apart.
    private static Vector2 HexCenter(int x, int y, float size) => new((x + 0.5f + (y & 1) * 0.5f) * size, -(y * 0.75f + 0.5f) * size);

    /// <summary>
    /// How many columns (X) and rows (Y) along the grid <paramref name="local"/>, a point in the entity's own space, is
    /// from its start: the other way from <see cref="PointAt"/>, so on a square or isometric map cell x, y holds the
    /// points from (x, y) to (x + 1, y + 1). On an isometric map that turns the diamonds into squares, where walls run
    /// straight along the grid; on a hex map rows are three quarters of a tile apart, and odd rows' shift isn't counted.
    /// </summary>
    public Vector2 GridAt(Vector2 local)
    {
        var size = TileSize > 0 ? TileSize : 1;
        if (Shape == TileShape.Hex)
            return new Vector2(local.X / size, -local.Y / (size * 0.75f));
        if (Shape != TileShape.Isometric)
            return new Vector2(local.X / size, -local.Y / size);

        // In half widths across and quarter widths (half a diamond's height) down, those are the column less the row and
        // the column plus the row.
        var across = local.X / (size / 2);
        var down = -local.Y / (size / 4);
        return new Vector2((down + across) / 2, (down - across) / 2);
    }

    /// <summary>
    /// The center of cell (<paramref name="x"/>, <paramref name="y"/>) in the entity's own space (on a hex map, odd rows'
    /// are half a tile further right).
    /// </summary>
    public Vector2 CellCenter(int x, int y) => Shape == TileShape.Hex ? HexCenter(x, y, TileSize) : PointAt(x + 0.5f, y + 0.5f);

    /// <summary>
    /// The point <paramref name="x"/> columns and <paramref name="y"/> rows along the grid from its start (the entity's
    /// position), in the entity's own space: whole numbers are the corners between cells, so (x + 0.5, y + 0.5) is cell
    /// x, y's center. On a square map that's right and down; on an isometric one, down to the right and down to the left.
    /// On a hex map it's right and down by rows three quarters of a tile apart, not counting odd rows' shift (use
    /// <see cref="CellCenter"/> for a hexagon's middle).
    /// </summary>
    public Vector2 PointAt(float x, float y)
    {
        var size = TileSize;
        return Shape switch
        {
            TileShape.Isometric => new Vector2((x - y) * size / 2, -(x + y) * size / 4),
            TileShape.Hex => new Vector2(x * size, -y * size * 0.75f),
            _ => new Vector2(x * size, -y * size),
        };
    }

    /// <summary>
    /// The outline of the cells from (<paramref name="left"/>, <paramref name="top"/>) to (<paramref name="right"/>,
    /// <paramref name="bottom"/>), corners included, in the entity's own space, from the grid's start (top left, or top
    /// on an isometric map) round the way the columns run. A square on a square map, a diamond on an isometric one, and
    /// on a hex map the hexagons' edges all the way round: a single cell's six corners from its top left one.
    /// </summary>
    public Vector2[] OutlineOf(int left, int top, int right, int bottom) => Shape == TileShape.Hex
        ? HexOutline(left, top, right, bottom)
        : [PointAt(left, top), PointAt(right + 1, top), PointAt(right + 1, bottom + 1), PointAt(left, bottom + 1)];

    // Round the edge of a block of hexagons, clockwise from the top left corner of its top left cell: along the top row's
    // peaks, down the right column's sides, back along the bottom row and up the left column. Worked out in half tiles
    // across and quarter tiles down, where every corner is a whole number, so corners two cells share come out the same
    // and are kept once.
    private Vector2[] HexOutline(int left, int top, int right, int bottom)
    {
        var corners = new List<(int X, int Y)>();
        for (var x = left; x <= right; x++)
            Add(x, top, -1, -1, 0, -2);
        for (var y = top; y <= bottom; y++)
        {
            Add(right, y, 1, -1, 1, 1);
            // An odd row sticks out on the right: round its bottom corner to the row below.
            if (y < bottom && (y & 1) == 1)
                Add(right, y, 0, 2);
        }

        for (var x = right; x >= left; x--)
            Add(x, bottom, 0, 2, -1, 1);
        for (var y = bottom; y >= top; y--)
        {
            Add(left, y, -1, 1, -1, -1);
            // An even row sticks out on the left: round its top corner to the row above.
            if (y > top && (y & 1) == 0)
                Add(left, y, 0, -2);
        }

        if (corners.Count > 1 && corners[^1] == corners[0])
            corners.RemoveAt(corners.Count - 1);
        var size = TileSize;
        return corners.Select(c => new Vector2(c.X * size / 2, -c.Y * size / 4)).ToArray();

        // Corners of cell x, y, each as half tiles across and quarter tiles down from its middle.
        void Add(int x, int y, params int[] offsets)
        {
            var (middleX, middleY) = (2 * x + 1 + (y & 1), 3 * y + 2);
            for (var i = 0; i < offsets.Length; i += 2)
            {
                var corner = (middleX + offsets[i], middleY + offsets[i + 1]);
                if (corners.Count == 0 || corners[^1] != corner)
                    corners.Add(corner);
            }
        }
    }

    /// <summary>
    /// The cells that share a side with cell (<paramref name="x"/>, <paramref name="y"/>), inside the grid or not: the
    /// four above, right, below and left of it, or on a hex map the six round it, clockwise from the top left (the two
    /// in the row above, which for an odd row are its own column and the one to its right and for an even row the one to
    /// its left and its own; the one to its right; the two below, the same way; the one to its left).
    /// </summary>
    public IEnumerable<(int X, int Y)> Neighbors(int x, int y)
    {
        if (Shape != TileShape.Hex)
            return [(x, y - 1), (x + 1, y), (x, y + 1), (x - 1, y)];
        var shift = y & 1;
        return [(x - 1 + shift, y - 1), (x + shift, y - 1), (x + 1, y), (x + shift, y + 1), (x - 1 + shift, y + 1), (x - 1, y)];
    }

    // Neighbor `side` (0 to 5) of a cell on a hex map, as Neighbors counts them.
    internal static (int X, int Y) NeighborOf(int x, int y, int side)
    {
        var shift = y & 1;
        return side switch
        {
            0 => (x - 1 + shift, y - 1),
            1 => (x + shift, y - 1),
            2 => (x + 1, y),
            3 => (x + shift, y + 1),
            4 => (x - 1 + shift, y + 1),
            _ => (x - 1, y),
        };
    }

    // Corner `corner` (0 to 5) of a cell on a hex map in the entity's own space, clockwise from its top left as
    // OutlineOf gives a single cell's: the side from corner i to corner i + 1 is the one it shares with neighbor i.
    internal Vector2 HexCorner(int x, int y, int corner)
    {
        // In half tiles across and quarter tiles down from the cell's middle, as HexOutline works them out.
        var (dx, dy) = corner switch
        {
            0 => (-1, -1),
            1 => (0, -2),
            2 => (1, -1),
            3 => (1, 1),
            4 => (0, 2),
            _ => (-1, 1),
        };
        var size = TileSize;
        return new Vector2((2 * x + 1 + (y & 1) + dx) * size / 2, -(3 * y + 2 + dy) * size / 4);
    }

    /// <summary>
    /// The box the grid's tiles cover in the entity's own space, (min, max): all its cells, and on an isometric map the
    /// pictures standing up from them (see <see cref="TileHeight"/>). One cell for an empty map.
    /// </summary>
    public (Vector2 Min, Vector2 Max) Bounds()
    {
        var (min, max) = (new Vector2(float.PositiveInfinity), new Vector2(float.NegativeInfinity));
        foreach (var corner in OutlineOf(0, 0, Math.Max(1, Width) - 1, Math.Max(1, Height) - 1))
            (min, max) = (Vector2.Min(min, corner), Vector2.Max(max, corner));
        if (Shape == TileShape.Isometric)
            max.Y += Math.Max(0, TileHeight - 0.5f) * TileSize;
        return (min, max);
    }

    /// <summary>
    /// Where cell (<paramref name="x"/>, <paramref name="y"/>)'s picture is drawn in the entity's own space: its middle and
    /// size. On a square map that's the cell, and on a hex map a tile-sized square round the hexagon; on an isometric one
    /// it's a tile wide and <see cref="TileHeight"/> tall, standing on the cell's bottom corner.
    /// </summary>
    public (Vector2 Center, Vector2 Size) PictureOf(int x, int y)
    {
        if (Shape != TileShape.Isometric)
            return (CellCenter(x, y), new Vector2(TileSize));
        var size = new Vector2(TileSize, TileSize * Math.Max(0, TileHeight));
        var bottom = PointAt(x + 1, y + 1);
        return (bottom with { Y = bottom.Y + size.Y / 2 }, size);
    }

    /// <summary>
    /// Which row cell (<paramref name="x"/>, <paramref name="y"/>) is drawn in, back (0) to front: its row on a square
    /// or hex map, its diagonal (x + y) on an isometric one. A map that sorts by Y sorts each of these among the sprites.
    /// </summary>
    public int DepthOf(int x, int y) => Shape == TileShape.Isometric ? x + y : y;

    /// <summary>How many rows <see cref="DepthOf"/> gives, back to front.</summary>
    public int Depths => Shape == TileShape.Isometric ? Math.Max(0, Width + Height - 1) : Height;

    /// <summary>
    /// The part of its tileset's image showing <paramref name="tile"/> (see <see cref="TilesetOf"/>): (u, v, width,
    /// height) in 0–1 image space.
    /// </summary>
    public Vector4 TileRect(int tile) => TilesetOf(tile).Rect(tile);

    /// <summary>The image <paramref name="tile"/> is cut from (see <see cref="TilesetOf"/>); null draws it as a square of <see cref="Tint"/>.</summary>
    public string? ImageOf(int tile) => TilesetOf(tile).Image;

    /// <summary>
    /// The map's tilesets in order: the first (<see cref="Image"/>, <see cref="Columns"/> and <see cref="Rows"/>) from
    /// tile 0, then each line of <see cref="MoreTilesets"/> from the next hundred on. Blank lines are skipped. Cached
    /// until those change.
    /// </summary>
    public IReadOnlyList<Tileset> Tilesets()
    {
        if (LayerOf is { } owner)
            return owner.Tilesets();
        if (_tilesets is { } cached && cached.Image == Image && cached.Columns == Columns && cached.Rows == Rows && cached.More == _moreTilesets)
            return cached.Tilesets;

        var list = new List<Tileset> { new(string.IsNullOrWhiteSpace(Image) ? null : Image, Math.Max(1, Columns), Math.Max(1, Rows), 0) };
        foreach (var line in _moreTilesets)
        {
            var last = list[^1];
            var first = ((long)last.End + TilesetStep - 1) / TilesetStep * TilesetStep;
            if (first >= MaxTiles)
                break;
            if (Tileset.Parse(line, (int)first) is { } tileset)
                list.Add(tileset);
        }

        _tilesets = new TilesetCache(Image, Columns, Rows, _moreTilesets, list);
        return list;
    }

    /// <summary>
    /// The tileset <paramref name="tile"/> comes from: the last one starting at or before it. A number past a tileset's
    /// own tiles (and before the next one's) wraps round them, as numbers past the end of a single tileset always have.
    /// </summary>
    public Tileset TilesetOf(int tile)
    {
        var tilesets = Tilesets();
        for (var i = tilesets.Count - 1; i > 0; i--)
        {
            if (tile >= tilesets[i].First)
                return tilesets[i];
        }

        return tilesets[0];
    }

    /// <summary>
    /// The tile to draw for a cell painted with <paramref name="tile"/> at <see cref="AnimationTime"/>: itself, or the
    /// frame its <see cref="AnimatedTiles"/> run has reached.
    /// </summary>
    public int ShownTile(int tile)
    {
        foreach (var animation in BaseMap._animations)
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
        var groups = BaseMap._autoGroups;
        var group = AutoGroupOf(groups, tile);
        if (group >= 0)
        {
            var mask = (Joins(x, y - 1) ? 1 : 0) | (Joins(x + 1, y) ? 2 : 0) | (Joins(x, y + 1) ? 4 : 0) | (Joins(x - 1, y) ? 8 : 0);
            tile = groups[group] + mask;
        }

        return ShownTile(tile);

        bool Joins(int cx, int cy) => AutoGroupOf(groups, GetTile(cx, cy)) == group;
    }

    // Which AutoTiles run a tile belongs to, or -1.
    private static int AutoGroupOf(IReadOnlyList<int> groups, int tile)
    {
        if (tile < 0)
            return -1;
        for (var i = 0; i < groups.Count; i++)
        {
            if (tile >= groups[i] && tile < groups[i] + 16)
                return i;
        }

        return -1;
    }

    /// <summary>True if <paramref name="tile"/> is listed in <see cref="LadderTiles"/>.</summary>
    public bool IsLadder(int tile) => InSet(BaseMap._ladder, tile);

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
    public IReadOnlyList<(int X, int Y, int Length, bool OneWay)> CollisionRuns()
    {
        CheckCaches();
        return _collisionRuns ??= FindRuns(_tiles, c => CollisionOf(TileOf(c)))
            .Select(r => (r.X, r.Y, r.Length, r.Kind == 1)).ToList();
    }

    /// <summary>
    /// True if cell (<paramref name="x"/>, <paramref name="y"/>) is a wall for <see cref="FindPath"/>: a solid tile on a
    /// <see cref="Solid"/> map. Empty, background, ladder and one-way tiles can be walked through.
    /// </summary>
    public bool IsBlocked(int x, int y) => Solid && GetTile(x, y) is var tile and >= 0 && CollisionOf(tile) == 0;

    /// <summary>
    /// Whether bodies meet its cells as boxes, as they meet colliders (with one-way and slope tiles): a square map's. Other
    /// shapes' cells are walls of their own (see <see cref="Walls"/>).
    /// </summary>
    internal bool TilesAreBoxes => Shape == TileShape.Square;

    /// <summary>
    /// The walls its solid cells make for bodies when they aren't boxes, from its <see cref="Shape"/>, as it's placed now:
    /// an isometric map's diamonds, a hex map's hexagons. Null for a square map, or one that can't be placed.
    /// </summary>
    internal TileWalls? Walls() => Shape switch
    {
        TileShape.Isometric => IsometricWalls.Of(this),
        TileShape.Hex => HexWalls.Of(this),
        _ => null,
    };

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
    public bool IsBackground(int tile) => InSet(BaseMap._background, tile);

    /// <summary>True if <paramref name="tile"/> is listed in <see cref="OneWayTiles"/> (and not as a background tile).</summary>
    public bool IsOneWay(int tile) => InSet(BaseMap._oneWay, tile) && !IsBackground(tile);

    /// <summary>
    /// Reads a list of tile numbers and ranges like "3, 7-9", as flags by tile number up to the highest one listed (look
    /// tiles up with <see cref="InSet"/>). Anything that isn't a number or range is ignored, as are tiles outside 0 to
    /// <see cref="MaxTiles"/> - 1.
    /// </summary>
    public static bool[] ParseTileSet(string? text)
    {
        var ranges = new List<(int From, int To)>();
        foreach (var part in (text ?? "").Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var dash = part.IndexOf('-', 1);
            var (first, last) = dash < 0 ? (part, part) : (part[..dash], part[(dash + 1)..]);
            if (!int.TryParse(first, out var from) || !int.TryParse(last, out var to))
                continue;
            if (from > to)
                (from, to) = (to, from);
            if (to >= 0 && from < MaxTiles)
                ranges.Add((Math.Max(0, from), Math.Min(MaxTiles - 1, to)));
        }

        var set = new bool[ranges.Count == 0 ? 0 : ranges.Max(r => r.To) + 1];
        foreach (var (from, to) in ranges)
            Array.Fill(set, true, from, to - from + 1);
        return set;
    }

    /// <summary>True if <paramref name="tile"/> is in a set read by <see cref="ParseTileSet"/>.</summary>
    public static bool InSet(bool[] set, int tile) => (uint)tile < (uint)set.Length && set[tile];

    /// <summary>Reads <see cref="AnimatedTiles"/>: "first-last @fps" runs separated by commas. Anything else is skipped.</summary>
    public static IReadOnlyList<TileAnimation> ParseAnimations(string? text)
    {
        var runs = new List<TileAnimation>();
        foreach (var part in Formats.Names(text))
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
    /// aren't 16 long, or run past the highest tile number, are skipped.
    /// </summary>
    public static IReadOnlyList<int> ParseAutoTiles(string? text)
    {
        var firsts = new List<int>();
        foreach (var part in Formats.Names(text))
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

    /// <summary>The tile a character stands for, or -1 for empty (and anything unrecognized); see <see cref="CharOf"/>.</summary>
    public static int TileOf(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'z' => c - 'a' + 10,
        >= 'A' and <= 'Z' => c - 'A' + 36,
        >= 'À' and <= 'Ö' => c - 0xC0 + 62,
        >= 'Ø' and <= 'ö' => c - 0xD8 + 85,
        >= 'ø' and <= 'ɏ' => c - 0xF8 + 116,
        >= '一' and <= '鿿' => c - 0x4E00 + 460,
        _ => -1,
    };

    /// <summary>
    /// The character for <paramref name="tile"/>; -1 (or anything from <see cref="MaxTiles"/> on) gives
    /// <see cref="Empty"/>. Tiles 0–61 are <c>0-9</c>, <c>a-z</c> and <c>A-Z</c>, 62–459 the accented letters from
    /// <c>À</c> to <c>ɏ</c> (skipping <c>×</c> and <c>÷</c>), and 460 on the CJK characters from <c>一</c>, so a cell
    /// is always one character.
    /// </summary>
    public static char CharOf(int tile) => tile switch
    {
        < 0 or >= MaxTiles => Empty,
        < 10 => (char)('0' + tile),
        < 36 => (char)('a' + tile - 10),
        < 62 => (char)('A' + tile - 36),
        < 85 => (char)(0xC0 + tile - 62),
        < 116 => (char)(0xD8 + tile - 85),
        < 460 => (char)(0xF8 + tile - 116),
        _ => (char)(0x4E00 + tile - 460),
    };

    // 0 = solid, 1 = one-way, -1 = not a box (empty, background, a ladder, or a slope, which collides by its own shape).
    private int CollisionOf(int tile) => tile < 0 || IsBackground(tile) || IsLadder(tile) || SlopeOf(tile) != 0 ? -1 : IsOneWay(tile) ? 1 : 0;

    // What Tilesets() was worked out from, and the answer.
    private sealed record TilesetCache(string? Image, int Columns, int Rows, string[] More, IReadOnlyList<Tileset> Tilesets);

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

/// <summary>
/// One of a tilemap's tilesets (see <see cref="Tilemap.Tilesets"/>): <see cref="Image"/> cut into <see cref="Columns"/>
/// by <see cref="Rows"/> tiles, numbered from <see cref="First"/> along each row, top row first. Without an image its
/// tiles are squares of the map's tint.
/// </summary>
public sealed record Tileset(string? Image, int Columns, int Rows, int First)
{
    /// <summary>How many tiles it has.</summary>
    public int Count => Columns * Rows;

    /// <summary>One past its last tile's number.</summary>
    public int End => First + Count;

    /// <summary>Its image's file name without the folder or extension ("props"), or "Tiles" without an image.</summary>
    public string Name => Image is null ? "Tiles" : Path.GetFileNameWithoutExtension(Image.Replace('\\', '/'));

    /// <summary>True if <paramref name="tile"/> is one of its own tiles.</summary>
    public bool Contains(int tile) => tile >= First && tile < End;

    /// <summary>
    /// The part of <see cref="Image"/> showing <paramref name="tile"/>, as (u, v, width, height) in 0–1 image space.
    /// Numbers outside its tiles wrap round them.
    /// </summary>
    public Vector4 Rect(int tile)
    {
        var index = ((tile - First) % Count + Count) % Count;
        return new Vector4((float)(index % Columns) / Columns, (float)(index / Columns) / Rows, 1f / Columns, 1f / Rows);
    }

    /// <summary>
    /// Reads a line of <see cref="Tilemap.MoreTilesets"/>, "Tiles/props.png 4x4", as a tileset whose tiles start at
    /// <paramref name="first"/>; null for a blank line. Without its tiles across and down, it's one tile.
    /// </summary>
    public static Tileset? Parse(string? line, int first)
    {
        var text = (line ?? "").Trim();
        if (text.Length == 0)
            return null;

        var (columns, rows) = (1, 1);
        var space = text.LastIndexOfAny([' ', '\t']);
        var size = space < 0 ? "" : text[(space + 1)..];
        var x = size.IndexOfAny(['x', 'X', '×']);
        if (x > 0 && int.TryParse(size[..x], out var c) && int.TryParse(size[(x + 1)..], out var r) && c > 0 && r > 0)
        {
            (columns, rows) = (Math.Min(c, 1024), Math.Min(r, 1024));
            text = text[..space].TrimEnd();
        }

        return new Tileset(text, columns, rows, first);
    }

    /// <summary>The line <see cref="Parse"/> reads back, as <see cref="Tilemap.MoreTilesets"/> holds it: "Tiles/props.png 4x4".</summary>
    public string ToLine() => $"{Image} {Columns}x{Rows}";
}

/// <summary>How a <see cref="Tilemap"/>'s cells are laid out; see <see cref="Tilemap.Shape"/>.</summary>
public enum TileShape
{
    /// <summary>Squares in rows and columns.</summary>
    Square,

    /// <summary>Diamonds twice as wide as they're tall, for an isometric view.</summary>
    Isometric,

    /// <summary>
    /// Hexagons pointing up and down, a tile wide and tall, in rows three quarters of a tile apart with every odd row
    /// shifted half a tile right: for board games, strategy and tactics.
    /// </summary>
    Hex,
}

/// <summary>A run of tiles that animate one after another; see <see cref="Tilemap.AnimatedTiles"/>.</summary>
public readonly record struct TileAnimation(int First, int Last, float FramesPerSecond);
