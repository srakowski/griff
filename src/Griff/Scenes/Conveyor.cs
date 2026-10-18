using System.Globalization;
using System.Numerics;

namespace Griff.Scenes;

/// <summary>What a cell of a <see cref="Conveyor"/> is.</summary>
public enum ConveyorCellKind
{
    /// <summary>A belt: it carries what's on it into the cell it points into. Walkers can't cross it while the conveyor is Solid.</summary>
    Belt,

    /// <summary>A belt running under a step-over: it carries items as a belt does, and walkers cross it.</summary>
    Crossing,

    /// <summary>A splitter: it hands what reaches it to the belts leading out of it in turn, passing over one that's full.</summary>
    Split,
}

/// <summary>
/// One cell of a <see cref="Conveyor"/>: its column and row (counted as a tilemap's are, row 0 at the top), the way it
/// carries (x right, y up, as a grid mover steps) and what it is.
/// </summary>
public readonly record struct ConveyorCell(int X, int Y, int WayX, int WayY, ConveyorCellKind Kind)
{
    /// <summary>Its column and row.</summary>
    public (int X, int Y) Cell => (X, Y);

    /// <summary>The way it carries: (1, 0) right, (0, 1) up, (-1, 0) left, (0, -1) down.</summary>
    public (int X, int Y) Way => (WayX, WayY);

    /// <summary>The cell it carries into (rows count down, so up is the row above).</summary>
    public (int X, int Y) Next => (X + WayX, Y - WayY);
}

/// <summary>
/// Belts on a grid that carry items: a factory's lines from machine to machine. Each cell of <see cref="Belts"/> points
/// one way and holds one item at a time; items glide along at <see cref="Speed"/> cells a second while the cell ahead is
/// free and wait when it isn't, so a full end backs the line up. A belt that runs into another's side merges, the two
/// taking turns; a <see cref="ConveyorCellKind.Split"/> cell hands items to the belts leading out of it in turn. At a
/// belt's end a <see cref="ConveyorPort"/> takes what arrives into a machine, and another puts what the machine makes
/// onto a belt; a script can put items on, take them off and read any cell.
/// </summary>
/// <remarks>
/// It lies on the cells of its <see cref="Map"/> (as a grid mover steps on them), so belts line up with the floor and
/// the walls. Items move in fixed steps of game time, so a line plays out the same at any game speed, sped up with
/// <c>Time.FastForward</c> or <c>Time.Scale</c>. Its items are drawn by the conveyor itself, a quad or an image each,
/// so hundreds cost no more than the sprites they look like. While it's <see cref="Solid"/>, belts and splitters are
/// walls to grid movers (<c>WalkTo</c> goes round them), and crossings aren't. It's saved with the scene, items too,
/// so it rewinds with everything else; in a network game the host moves the items and the players see them.
/// </remarks>
/// <example><code>
/// var belts = Find("Line").GetComponent&lt;Conveyor&gt;();
/// belts.LayPath([(2, 4), (9, 4), (9, 8)]);   // right along row 4, then down to row 8
/// belts.Lay(5, 4, 1, 0, ConveyorCellKind.Crossing); // walkers cross here
/// belts.Put(2, 4, "Deck");
/// belts.ReachedEnd += (_, cell, item) => Log($"{item} waits at {cell}");
/// </code></example>
[Category("Gameplay")]
[WorksWith(typeof(Tilemap), Note = "lies on its cells")]
[WorksWith(typeof(ConveyorPort), Note = "takes items off a belt's end into a machine, or puts them on")]
[WorksWith(typeof(GridMovement), Note = "walks round solid belts and over crossings")]
[ComponentName("Conveyor")]
public sealed class Conveyor : Component, IDrawable
{
    // The four ways a belt points (x right, y up), by number; a cell's way in from where its item came is one more than
    // its number, 0 for none.
    private static readonly (int X, int Y)[] Ways = [(1, 0), (0, 1), (-1, 0), (0, -1)];

    // The most fixed steps one step of the game moves the items, however long it is (a Time.Scale of 64 is 64).
    private const int MostTicksPerStep = 256;

    // How near an item has to be to its cell's middle to be there: what rounding leaves after a cell's worth of steps.
    private const float Near = 1e-5f;

    // The cells, in the order they were laid (the scene file's), and each one's place by its column and row.
    private ConveyorCell[] _belts = [];
    [NotState("worked out from the belts")]
    private Dictionary<(int X, int Y), int> _at = [];

    // Each cell's way on (one cell for a belt, the belts leading out for a splitter), the cells that feed it, and every
    // cell with the ends first (each after all it carries into), the order a step moves them in, so a full line moves
    // as one.
    [NotState("worked out from the belts")]
    private int[][] _outs = [];
    [NotState("worked out from the belts")]
    private int[][] _feeders = [];
    [NotState("worked out from the belts")]
    private int[] _order = [];

    // What's on each cell, how far it still has to go to the cell's middle (0 there, 1 at the middle of the cell before),
    // which way it came in (one more than the way's number, 0 for put down in the middle), the feeder a merge took from
    // last (one more than its place in the feeders), and which of its ways on a splitter tries next.
    private string?[] _items = [];
    private float[] _left = [];
    private byte[] _came = [];
    private byte[] _lastIn = [];
    private byte[] _nextOut = [];

    // Fixed steps of game time owed, and items set before their cells were laid (a scene file read in any order).
    private double _owed;
    [NotState("put down as soon as the belts are laid")]
    private List<(int X, int Y, string Item)> _unplaced = [];

    [NotState("worked out from the belts as they're asked for")]
    private IReadOnlyList<string>? _beltRows;
    [NotState("worked out from the items as they're asked for")]
    private IReadOnlyList<string>? _itemRows;
    [NotState("worked out from Item Images")]
    private Dictionary<string, (string? Image, Color? Color)> _looks = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _itemImages = [];

    // Where each item was drawn as the last step began, and that step: it's drawn between there and where it is.
    [NotState("where items were as the step began, for drawing")]
    private Vector2[] _from = [];
    [NotState("the step the items last moved in, for drawing")]
    private long _movedIn = -1;

    // What happened in the step that the game raises once it's done: items at an end, taken in, put on.
    [NotState("filled and raised within a step")]
    private readonly List<ConveyorEvent> _events = [];

    // The ports on its cells this step: what takes from each cell a belt points into, and where each putting port puts.
    [NotState("gathered each step")]
    private readonly Dictionary<(int X, int Y), ConveyorPort> _takers = [];
    [NotState("gathered each step")]
    private readonly List<(ConveyorPort Port, int Cell, int From)> _givers = [];
    [NotState("filled and emptied within a step")]
    private readonly List<int> _arrivedAtEnds = [];

    // The tilemap it lies on, and what it was found for.
    [NotState("found again as it's asked for")]
    private Tilemap? _map;
    [NotState("found again as it's asked for")]
    private (Scene, long, int, string?)? _mapKey;

    /// <summary>
    /// The tilemap whose cells it lies on. Empty uses the tilemap on its own entity or the one it's inside, else the first
    /// in the scene; with none, cells are world units centered on whole numbers, as for a grid mover.
    /// </summary>
    [EntityName]
    public string? Map { get; set; }

    /// <summary>
    /// Its cells, one a row: the column and row (counted as a tilemap's are, row 0 at the top), the way it carries
    /// (right, left, up or down) and, for one that isn't a plain belt, crossing or split, e.g. "4 2 right",
    /// "5 2 right crossing", "9 2 down split". Lay them from a script with <see cref="Lay(int, int, int, int, ConveyorCellKind)"/>
    /// and <see cref="LayPath"/>.
    /// </summary>
    [PlainText("cells laid a row each, as a tilemap's rows are, and by scripts as the game runs")]
    public IReadOnlyList<string> Belts
    {
        get => _beltRows ??= _belts.Select(RowOf).ToList();
        set
        {
            var cells = new List<ConveyorCell>();
            foreach (var row in value ?? [])
            {
                if (ReadCell(row) is { } cell)
                    cells.Add(cell);
            }

            Relay(cells);
        }
    }

    /// <summary>Cells an item moves a second, up to one a step of the game.</summary>
    public float Speed { get; set; } = 2;

    /// <summary>While ticked, items move and ports take and put; untick it (or switch it off) to stop the line where it is.</summary>
    [Powered]
    public bool Running { get; set; } = true;

    /// <summary>
    /// Belts and splitters are walls to grid movers, as solid colliders are: one walking somewhere goes round them, and
    /// crosses at a crossing. Untick it to let them walk over every belt.
    /// </summary>
    [Group("Walking")]
    public bool Solid { get; set; } = true;

    /// <summary>
    /// The collision layer its belts are on, like a collider's: a grid mover with Blocking Layers is stopped only by the
    /// layers it names, e.g. "Obstacle". Empty is "Default".
    /// </summary>
    [ShownWhen(nameof(Solid))]
    [Names(NameKind.Layer)]
    public string? Layer { get; set; }

    /// <summary>
    /// What's on it, one a row: the cell's column and row and the item, e.g. "4 2 Deck". Set it to start with items on the
    /// belts; the game keeps it up to date as they move.
    /// </summary>
    [Group("Items")]
    [PlainText("what's on each cell: the game writes it as the items move")]
    public IReadOnlyList<string> Items
    {
        get => _itemRows ??= ItemRows();
        set
        {
            Array.Clear(_items);
            Array.Clear(_left);
            Array.Clear(_came);
            _unplaced.Clear();
            foreach (var row in value ?? [])
            {
                if (ReadItem(row) is { } item)
                    _unplaced.Add(item);
            }

            PlaceUnplaced();
            ItemsChanged();
        }
    }

    /// <summary>How big an item is drawn, as a share of a cell.</summary>
    public float ItemSize { get; set; } = 0.5f;

    /// <summary>The color an item is drawn in when <see cref="ItemImages"/> has nothing for it.</summary>
    public Color ItemColor { get; set; } = Color.FromBytes(0xF2, 0xE8, 0xC9);

    /// <summary>
    /// What items look like, one a row: the item, a colon, and an image (relative to Assets) or a color, e.g.
    /// "Deck: Sprites/deck.png", "Report: #FFA300". An item not named here uses its first word's ("Deck 17" looks like
    /// "Deck"), else <see cref="ItemColor"/>.
    /// </summary>
    [PlainText("an item and the image or color it's drawn with, a row each")]
    public IReadOnlyList<string> ItemImages
    {
        get => _itemImages;
        set
        {
            _itemImages = value ?? [];
            _looks = new Dictionary<string, (string?, Color?)>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _itemImages)
            {
                var colon = row.IndexOf(':');
                if (colon <= 0)
                    continue;
                var (name, look) = (row[..colon].Trim(), row[(colon + 1)..].Trim());
                if (name.Length == 0 || look.Length == 0)
                    continue;
                _looks[name] = look.StartsWith('#') && Color.TryParse(look, out var color) ? (null, color) : (look, null);
            }
        }
    }

    /// <summary>
    /// Draws each cell as a plain belt with an arrow the way it carries (crossings lighter, splitters with two arrows), for
    /// a line with no art of its own yet. Untick it when a tilemap draws the belts.
    /// </summary>
    [Group("Look")]
    public bool ShowBelts { get; set; } = true;

    /// <summary>The color <see cref="ShowBelts"/> draws belts in; its arrows are lighter.</summary>
    [ShownWhen(nameof(ShowBelts))]
    public Color BeltColor { get; set; } = Color.FromBytes(0x3A, 0x40, 0x4C);

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/>.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Sorts each item by its own height, when the camera sorts by Y: an item lower down the screen is drawn in front of a
    /// worker standing behind it.
    /// </summary>
    public bool SortByY { get; set; }

    /// <summary>
    /// Raised once the step is over for each item that reached the end of a belt with nothing there to take it: its cell
    /// and the item. It waits there, holding the line up, until something takes it (<see cref="Take"/>).
    /// </summary>
    public event Action<Conveyor, (int X, int Y), string>? ReachedEnd;

    /// <summary>Its cells, in the order they were laid.</summary>
    public IReadOnlyList<ConveyorCell> Cells => _belts;

    /// <summary>How many items are on it.</summary>
    public int Count
    {
        get
        {
            var count = 0;
            foreach (var item in _items)
                count += item is null ? 0 : 1;
            return count;
        }
    }

    /// <summary>Every item on it and its cell, in the order the cells were laid.</summary>
    public IReadOnlyList<(int X, int Y, string Item)> Contents
    {
        get
        {
            var contents = new List<(int X, int Y, string Item)>();
            for (var i = 0; i < _belts.Length; i++)
            {
                if (_items[i] is { } item)
                    contents.Add((_belts[i].X, _belts[i].Y, item));
            }

            return contents;
        }
    }

    int? IDrawable.DrawOrder => Order;

    // ---- Laying belts ----

    /// <summary>
    /// Lays a cell at column <paramref name="x"/>, row <paramref name="y"/> carrying the way (<paramref name="wayX"/>,
    /// <paramref name="wayY"/>): (1, 0) right, (0, 1) up, (-1, 0) left, (0, -1) down. A cell already there is turned (an
    /// item on it stays).
    /// </summary>
    public void Lay(int x, int y, int wayX, int wayY, ConveyorCellKind kind = ConveyorCellKind.Belt)
    {
        if (WayIndex(wayX, wayY) < 0)
            throw new ArgumentException($"A belt carries right (1, 0), up (0, 1), left (-1, 0) or down (0, -1), not ({wayX}, {wayY}).");
        var cells = _belts.ToList();
        var cell = new ConveyorCell(x, y, wayX, wayY, kind);
        if (_at.TryGetValue((x, y), out var at))
            cells[at] = cell;
        else
            cells.Add(cell);
        Relay(cells);
    }

    /// <summary>Lays a cell at <paramref name="cell"/> carrying <paramref name="way"/> (see <see cref="Lay(int, int, int, int, ConveyorCellKind)"/>).</summary>
    public void Lay((int X, int Y) cell, (int X, int Y) way, ConveyorCellKind kind = ConveyorCellKind.Belt) =>
        Lay(cell.X, cell.Y, way.X, way.Y, kind);

    /// <summary>
    /// Lays <paramref name="cells"/> all at once (each as <see cref="Lay(int, int, int, int, ConveyorCellKind)"/> does,
    /// turning a cell already there): a line from a <see cref="LineTool"/>, gaps and all.
    /// </summary>
    public void Lay(IEnumerable<ConveyorCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var all = _belts.ToList();
        var places = new Dictionary<(int X, int Y), int>(_at);
        foreach (var cell in cells)
        {
            if (WayIndex(cell.WayX, cell.WayY) < 0)
                throw new ArgumentException($"A belt carries right (1, 0), up (0, 1), left (-1, 0) or down (0, -1), not {cell.Way}.");
            if (places.TryGetValue(cell.Cell, out var at))
                all[at] = cell;
            else
            {
                places[cell.Cell] = all.Count;
                all.Add(cell);
            }
        }

        Relay(all);
    }

    /// <summary>
    /// Lays a belt through <paramref name="points"/>, each cell carrying on to the next: between two points that aren't
    /// next to each other it goes along the row first, then up or down the column, so the corners of a line are its points,
    /// e.g. <c>LayPath([(2, 4), (9, 4), (9, 8)])</c>. The last cell carries on the way the one before it does (or
    /// <paramref name="lastWay"/>). Cells already there are turned to follow it.
    /// </summary>
    public void LayPath(IReadOnlyList<(int X, int Y)> points, ConveyorCellKind kind = ConveyorCellKind.Belt, (int X, int Y)? lastWay = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (lastWay is { } given && WayIndex(given.X, given.Y) < 0)
            throw new ArgumentException($"A belt carries right (1, 0), up (0, 1), left (-1, 0) or down (0, -1), not {given}.");
        var path = new List<(int X, int Y)>();
        foreach (var point in points)
        {
            if (path.Count == 0)
            {
                path.Add(point);
                continue;
            }

            var (x, y) = path[^1];
            while (x != point.X)
                path.Add((x += Math.Sign(point.X - x), y));
            while (y != point.Y)
                path.Add((x, y += Math.Sign(point.Y - y)));
        }

        if (path.Count == 0)
            return;
        var cells = _belts.ToList();
        var places = new Dictionary<(int X, int Y), int>(_at);
        var way = lastWay ?? (1, 0);
        for (var i = 0; i < path.Count; i++)
        {
            if (i + 1 < path.Count)
                way = (path[i + 1].X - path[i].X, path[i].Y - path[i + 1].Y);
            else if (lastWay is null && i == 0)
                way = (1, 0);
            var cell = new ConveyorCell(path[i].X, path[i].Y, way.X, way.Y, kind);
            if (places.TryGetValue(path[i], out var at))
                cells[at] = cell;
            else
            {
                places[path[i]] = cells.Count;
                cells.Add(cell);
            }
        }

        // The last cell carries on the way the one before it does, unless it was told otherwise.
        if (lastWay is { } last)
        {
            var end = places[path[^1]];
            cells[end] = cells[end] with { WayX = last.X, WayY = last.Y };
        }

        Relay(cells);
    }

    /// <summary>Takes up the cell at column <paramref name="x"/>, row <paramref name="y"/>: returns the item that was on it, if any.</summary>
    public string? Remove(int x, int y)
    {
        if (!_at.TryGetValue((x, y), out var at))
            return null;
        var item = _items[at];
        _items[at] = null;
        var cells = _belts.ToList();
        cells.RemoveAt(at);
        Relay(cells);
        return item;
    }

    /// <summary>Takes up every cell, and the items on them.</summary>
    public void Clear() => Relay([]);

    /// <summary>The cell at column <paramref name="x"/>, row <paramref name="y"/>, or null if there's none.</summary>
    public ConveyorCell? CellAt(int x, int y) => _at.TryGetValue((x, y), out var at) ? _belts[at] : null;

    /// <summary>True if a belt or splitter (not a crossing) is at the cell: what stops a grid mover while it's <see cref="Solid"/>.</summary>
    public bool BlocksWalking((int X, int Y) cell) => _at.TryGetValue(cell, out var at) && _belts[at].Kind != ConveyorCellKind.Crossing;

    /// <summary>The tilemap whose cells it lies on (see <see cref="Map"/>), or null for world units.</summary>
    public Tilemap? FindMap()
    {
        if (Entity?.Scene is not { } scene)
            return null;
        // Found again only when the scene's entities or tilemaps, or its Map, have changed: it's asked every step and frame.
        var key = (scene, scene.ActiveVersion, scene.ActiveNow<Tilemap>().Count, Map);
        if (_mapKey == key)
            return _map;
        Tilemap? map;
        if (!string.IsNullOrWhiteSpace(Map))
            map = EntityNames.Find(scene, Map, new NameLookup(Entity))?.GetComponent<Tilemap>()?.BaseMap;
        else
        {
            map = null;
            for (var e = Entity; e is not null && map is null; e = e.Parent)
                map = e.GetComponent<Tilemap>()?.BaseMap;
            map ??= scene.Entities.Where(e => e.IsActive).Select(e => e.GetComponent<Tilemap>()).FirstOrDefault(t => t is { LayerOf: null });
        }

        (_map, _mapKey) = (map?.Entity is null ? null : map, key);
        return _map;
    }

    /// <summary>The cells it lies on (see <see cref="Map"/>).</summary>
    public GridCells Grid => GridCells.Of(FindMap());

    /// <summary>The cell a world position is in.</summary>
    public (int X, int Y) CellOf(Vector2 world) => Grid.CellAt(world);

    // ---- Items ----

    /// <summary>
    /// Puts <paramref name="item"/> in the middle of the cell at column <paramref name="x"/>, row <paramref name="y"/>,
    /// to move on from there: false if there's no belt there, something's on it, or the item that just left it isn't a
    /// cell away yet.
    /// </summary>
    public bool Put(int x, int y, string item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
        if (!_at.TryGetValue((x, y), out var at) || !HasRoom(at))
            return false;
        (_items[at], _left[at], _came[at]) = (item.Trim(), 0, 0);
        _from[at] = PositionOf(at, Grid);
        ItemsChanged();
        return true;
    }

    /// <summary>The item on the cell at column <paramref name="x"/>, row <paramref name="y"/>, or null.</summary>
    public string? ItemAt(int x, int y) => _at.TryGetValue((x, y), out var at) ? _items[at] : null;

    /// <summary>Takes the item off the cell at column <paramref name="x"/>, row <paramref name="y"/>: returns it, or null if there was none.</summary>
    public string? Take(int x, int y)
    {
        if (!_at.TryGetValue((x, y), out var at) || _items[at] is not { } item)
            return null;
        (_items[at], _left[at], _came[at]) = (null, 0, 0);
        ItemsChanged();
        return item;
    }

    /// <summary>
    /// True if an item on the cell has reached its middle and couldn't move on: the line is backed up there, or it's at an
    /// end with nothing taking it.
    /// </summary>
    public bool IsWaiting(int x, int y) => _at.TryGetValue((x, y), out var at) && _items[at] is not null && _left[at] <= Near;

    /// <summary>Where the item on the cell is in the world now (it glides from cell to cell), or null if there's none.</summary>
    public Vector2? ItemPosition(int x, int y) =>
        _at.TryGetValue((x, y), out var at) && _items[at] is not null ? PositionOf(at, Grid) : null;

    // ---- Moving ----

    // Moves the items on by `delta` seconds of game time in fixed steps of `tick` seconds, so the line is where it would
    // be however the time was split up; `ports` are the scene's, to take from and put on the cells they're at. What
    // happened goes into Events for the game to raise.
    internal void Advance(float delta, float tick, IReadOnlyList<ConveyorPort> ports)
    {
        _events.Clear();
        if (!Running || !(delta > 0) || !(tick > 0) || _belts.Length == 0)
            return;
        // A step of 1x, 4x or 16x the usual length is that many fixed steps exactly, whatever rounding left on its length,
        // so the line is in the same place after the same game time at any speed.
        var steps = delta / (double)tick;
        var whole = Math.Round(steps);
        _owed += Math.Abs(steps - whole) < 1e-3 ? whole : steps;
        var ticks = (int)Math.Floor(_owed + 1e-9);
        if (ticks <= 0)
            return;
        _owed -= ticks;
        ticks = Math.Min(ticks, MostTicksPerStep);
        var grid = Grid;
        GatherPorts(ports, grid);
        for (var i = 0; i < _belts.Length; i++)
        {
            if (_items[i] is not null)
                _from[i] = PositionOf(i, grid);
        }

        for (var t = 0; t < ticks; t++)
            Tick(tick, grid);
        if (Entity?.Scene is { } scene)
            _movedIn = scene.Step;
    }

    // What the last Advance did, for the game to raise.
    internal IReadOnlyList<ConveyorEvent> Events => _events;

    internal void RaiseReachedEnd((int X, int Y) cell, string item) => ReachedEnd?.Invoke(this, cell, item);

    private void Tick(float seconds, GridCells grid)
    {
        var step = Math.Min(Speed * seconds, 1f);
        if (!(step > 0))
            step = 0;

        // Every item goes on toward its cell's middle, and one there already past it, by as far as it would go if it can
        // move on; one that reaches it with nowhere to go is at an end.
        _arrivedAtEnds.Clear();
        for (var i = 0; i < _belts.Length; i++)
        {
            if (_items[i] is null)
                continue;
            var was = _left[i];
            _left[i] -= step;
            if (was > Near && _left[i] <= Near && _outs[i].Length == 0)
                _arrivedAtEnds.Add(i);
        }

        // Then the ends first, each cell after those it carries into, so a full line moves up as one.
        foreach (var i in _order)
        {
            if (_items[i] is not null && _left[i] <= Near)
                MoveOn(i);
        }

        foreach (var i in _arrivedAtEnds)
        {
            if (_items[i] is { } item && _left[i] <= Near)
                _events.Add(new ConveyorEvent(ConveyorEventKind.ReachedEnd, null, _belts[i].Cell, item));
        }

        // Ports put what their machines made on the belts they're at, where there's room.
        foreach (var (port, cell, from) in _givers)
        {
            if (!port.Open)
                continue;
            if (from < 0 ? !HasRoom(cell) : _items[cell] is not null)
            {
                port.Blocked |= port.HasToGive();
                continue;
            }

            if (port.NextToGive() is not { } item)
                continue;
            _items[cell] = item;
            (_left[cell], _came[cell]) = from < 0 ? (0f, (byte)0) : (1f, (byte)(from + 1));
            _from[cell] = PositionOf(cell, grid);
            ItemsChanged();
            _events.Add(new ConveyorEvent(ConveyorEventKind.Sent, port, _belts[cell].Cell, item));
        }

        // What couldn't move waits in its cell's middle.
        for (var i = 0; i < _belts.Length; i++)
        {
            if (_left[i] <= Near)
                _left[i] = 0;
        }
    }

    // The item on cell `i`, at its middle, goes on if it can: a belt into the cell ahead, a splitter into the next of its
    // ways on with room, and at an end into the port there.
    private void MoveOn(int i)
    {
        var outs = _outs[i];
        if (outs.Length == 0)
        {
            var cell = _belts[i];
            if (!(_takers.TryGetValue(cell.Next, out var port) || _takers.TryGetValue(cell.Cell, out port)))
                return;
            var item = _items[i]!;
            if (!port.TryTake(item))
            {
                port.Blocked = true;
                return;
            }

            (_items[i], _left[i], _came[i]) = (null, 0, 0);
            ItemsChanged();
            _events.Add(new ConveyorEvent(ConveyorEventKind.Received, port, cell.Cell, item));
            return;
        }

        if (_belts[i].Kind != ConveyorCellKind.Split)
        {
            if (CanEnter(outs[0], i))
                Shift(i, outs[0]);
            return;
        }

        var first = _nextOut[i] % outs.Length;
        for (var k = 0; k < outs.Length; k++)
        {
            var o = outs[(first + k) % outs.Length];
            if (!CanEnter(o, i))
                continue;
            Shift(i, o);
            _nextOut[i] = (byte)((first + k + 1) % outs.Length);
            return;
        }
    }

    // Whether cell `to` takes the item from `from` now: it's empty, and where two belts merge into it, the one it took from
    // last waits while the other has one ready, so they take turns.
    private bool CanEnter(int to, int from)
    {
        if (_items[to] is not null)
            return false;
        var feeders = _feeders[to];
        if (feeders.Length < 2 || _lastIn[to] != Array.IndexOf(feeders, from) + 1)
            return true;
        foreach (var other in feeders)
        {
            if (other != from && _items[other] is not null && _left[other] <= Near && Wants(other, to))
                return false;
        }

        return true;
    }

    // Whether an item can be put down in the middle of cell `at`: nothing's on it, and what last left it for a cell ahead
    // has got to that cell's middle, a cell away.
    private bool HasRoom(int at)
    {
        if (_items[at] is not null)
            return false;
        foreach (var o in _outs[at])
        {
            if (_items[o] is not null && _left[o] > Near && _came[o] != 0 &&
                (_belts[o].X - Ways[_came[o] - 1].X, _belts[o].Y + Ways[_came[o] - 1].Y) == _belts[at].Cell)
                return false;
        }

        return true;
    }

    // Whether cell `from`'s item would go into `to` next: a belt's only way on, or a splitter's next.
    private bool Wants(int from, int to)
    {
        var outs = _outs[from];
        return outs.Length > 0 && (_belts[from].Kind != ConveyorCellKind.Split ? outs[0] == to : outs[_nextOut[from] % outs.Length] == to);
    }

    private void Shift(int from, int to)
    {
        var (dx, dy) = (_belts[to].X - _belts[from].X, _belts[from].Y - _belts[to].Y);
        (_items[to], _left[to], _came[to], _from[to]) = (_items[from], 1 + Math.Min(_left[from], 0), (byte)(WayIndex(dx, dy) + 1), _from[from]);
        (_items[from], _left[from], _came[from]) = (null, 0, 0);
        ItemsChanged();
        var feeders = _feeders[to];
        if (feeders.Length > 1)
            _lastIn[to] = (byte)(Array.IndexOf(feeders, from) + 1);
    }

    // The ports on its cells: those taking from a belt pointing into their cell (or ending on it), and those putting onto
    // the belt on their cell or one next to it leading away.
    private void GatherPorts(IReadOnlyList<ConveyorPort> ports, GridCells grid)
    {
        _takers.Clear();
        _givers.Clear();
        foreach (var port in ports)
        {
            if (port.Entity is not { } entity)
                continue;
            var at = entity.Transform.WorldPosition;
            var cell = grid.CellAt(new Vector2(at.X, at.Y));
            if (port.Kind == ConveyorPortKind.In)
            {
                _takers.TryAdd(cell, port);
                continue;
            }

            if (_at.TryGetValue(cell, out var on))
            {
                _givers.Add((port, on, -1));
                continue;
            }

            for (var w = 0; w < Ways.Length; w++)
            {
                var (wx, wy) = Ways[w];
                if (_at.TryGetValue((cell.X + wx, cell.Y - wy), out var next) && _belts[next].Way == (wx, wy))
                {
                    _givers.Add((port, next, w));
                    break;
                }
            }
        }
    }

    // ---- Drawing ----

    void IDrawable.Draw(Drawing drawing)
    {
        if (_belts.Length == 0)
            return;
        var grid = Grid;
        var across = grid.InWorld(Vector2.UnitX).Length();
        var up = grid.InWorld(Vector2.UnitY).Length();
        var view = drawing.View;
        var margin = MathF.Max(across, up);
        bool Seen(Vector2 p) => !drawing.CanCull ||
            (MathF.Abs(p.X - view.Center.X) <= view.HalfSize.X + margin && MathF.Abs(p.Y - view.Center.Y) <= view.HalfSize.Y + margin);

        if (ShowBelts)
        {
            var belt = drawing.Tint(this, BeltColor);
            var arrow = drawing.Tint(this, Color.Lerp(BeltColor, Color.White, 0.45f));
            var crossing = drawing.Tint(this, Color.Lerp(BeltColor, Color.White, 0.25f));
            var thick = drawing.LineWidth;
            foreach (var cell in _belts)
            {
                var center = grid.Center(cell.Cell);
                if (!Seen(center))
                    continue;
                drawing.Quad(center, new Vector2(across, up) * 0.9f, cell.Kind == ConveyorCellKind.Crossing ? crossing : belt);
                var way = grid.InWorld(new Vector2(cell.WayX, cell.WayY));
                var side = new Vector2(-way.Y, way.X);
                Chevron(center + way * 0.12f);
                if (cell.Kind == ConveyorCellKind.Split)
                    Chevron(center - way * 0.12f);
                void Chevron(Vector2 tip)
                {
                    drawing.Line(tip - way * 0.18f + side * 0.2f, tip, thick, arrow);
                    drawing.Line(tip - way * 0.18f - side * 0.2f, tip, thick, arrow);
                }
            }
        }

        var blend = drawing.Blend.For(_movedIn);
        var size = new Vector2(across, up) * ItemSize;
        var z = Entity!.Transform.WorldPosition.Z;
        for (var i = 0; i < _belts.Length; i++)
        {
            if (_items[i] is not { } item)
                continue;
            var at = PositionOf(i, grid);
            if (blend.IsBetween)
                at = blend.Between(_from[i], at);
            if (!Seen(at))
                continue;
            var (image, color) = LookOf(item);
            if (SortByY)
                drawing.SortAt(at.Y - size.Y / 2);
            drawing.Image(Matrix4x4.CreateScale(size.X, size.Y, 1) * Matrix4x4.CreateTranslation(at.X, at.Y, z), image,
                drawing.Tint(this, color));
        }
    }

    private (string? Image, Color Color) LookOf(string item)
    {
        if (_looks.Count > 0)
        {
            if (!_looks.TryGetValue(item, out var look))
            {
                var space = item.IndexOf(' ');
                if (space > 0)
                    _looks.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(item.AsSpan(0, space), out look);
            }

            if (look.Image is { } image)
                return (image, Color.White);
            if (look.Color is { } color)
                return (null, color);
        }

        return (null, ItemColor);
    }

    // Where the item on cell `i` is: as far back toward the cell it came from as it still has to go.
    private Vector2 PositionOf(int i, GridCells grid)
    {
        var cell = _belts[i];
        var center = grid.Center(cell.Cell);
        if (_came[i] == 0 || !(_left[i] > 0))
            return center;
        var (wx, wy) = Ways[_came[i] - 1];
        var back = grid.Center((cell.X - wx, cell.Y + wy));
        return Vector2.Lerp(center, back, Math.Min(_left[i], 1));
    }

    // ---- Keeping its cells ----

    // Lays `cells` (the last of any on the same cell wins), keeping each item where its cell still is, and works out
    // how the cells lead into each other.
    private void Relay(List<ConveyorCell> cells)
    {
        var at = new Dictionary<(int X, int Y), int>(cells.Count);
        var kept = new List<ConveyorCell>(cells.Count);
        foreach (var cell in cells)
        {
            if (at.TryGetValue(cell.Cell, out var already))
                kept[already] = cell;
            else
            {
                at[cell.Cell] = kept.Count;
                kept.Add(cell);
            }
        }

        var n = kept.Count;
        var (items, left, came, lastIn, nextOut, from) = (new string?[n], new float[n], new byte[n], new byte[n], new byte[n], new Vector2[n]);
        for (var i = 0; i < n; i++)
        {
            if (!_at.TryGetValue(kept[i].Cell, out var old))
                continue;
            (items[i], left[i], came[i], lastIn[i], nextOut[i], from[i]) = (_items[old], _left[old], _came[old], _lastIn[old], _nextOut[old], _from[old]);
            // An item that was coming in from a cell that's gone is in the middle now.
            if (came[i] != 0 && !at.ContainsKey((kept[i].X - Ways[came[i] - 1].X, kept[i].Y + Ways[came[i] - 1].Y)))
                (left[i], came[i]) = (0, 0);
        }

        (_belts, _at, _items, _left, _came, _lastIn, _nextOut, _from) = ([.. kept], at, items, left, came, lastIn, nextOut, from);
        Link();
        PlaceUnplaced();
        _beltRows = null;
        ItemsChanged();
    }

    // Works out each cell's ways on, its feeders and the order they move in.
    private void Link()
    {
        var n = _belts.Length;
        var outs = new int[n][];
        var feeders = new List<int>[n];
        for (var i = 0; i < n; i++)
            feeders[i] = [];
        var ways = new List<int>(4);
        for (var i = 0; i < n; i++)
        {
            var cell = _belts[i];
            ways.Clear();
            if (cell.Kind == ConveyorCellKind.Split)
            {
                // Ahead, then to the left of the way it carries, then to the right: each a belt leading away from it.
                var w = WayIndex(cell.WayX, cell.WayY);
                foreach (var turn in (ReadOnlySpan<int>)[0, 1, 3])
                {
                    var (wx, wy) = Ways[(w + turn) % 4];
                    if (_at.TryGetValue((cell.X + wx, cell.Y - wy), out var next) && _belts[next].Next != cell.Cell &&
                        (turn == 0 || _belts[next].Way == (wx, wy)))
                        ways.Add(next);
                }
            }
            else if (_at.TryGetValue(cell.Next, out var next) && _belts[next].Next != cell.Cell)
                ways.Add(next);

            outs[i] = [.. ways];
            foreach (var o in outs[i])
                feeders[o].Add(i);
        }

        // The ends first: each cell after every cell it carries into (a loop is broken where it's first come to).
        var order = new List<int>(n);
        var state = new byte[n];
        var stack = new Stack<(int Cell, int Next)>();
        for (var s = 0; s < n; s++)
        {
            if (state[s] != 0)
                continue;
            state[s] = 1;
            stack.Push((s, 0));
            while (stack.Count > 0)
            {
                var (c, k) = stack.Pop();
                if (k < outs[c].Length)
                {
                    stack.Push((c, k + 1));
                    var o = outs[c][k];
                    if (state[o] == 0)
                    {
                        state[o] = 1;
                        stack.Push((o, 0));
                    }
                }
                else
                {
                    state[c] = 2;
                    order.Add(c);
                }
            }
        }

        (_outs, _feeders, _order) = (outs, [.. feeders.Select(f => f.ToArray())], [.. order]);
    }

    private void PlaceUnplaced()
    {
        if (_unplaced.Count == 0 || _belts.Length == 0)
            return;
        var grid = Entity is null ? default : Grid;
        _unplaced.RemoveAll(u =>
        {
            if (!_at.TryGetValue((u.X, u.Y), out var at))
                return false;
            if (_items[at] is null)
            {
                (_items[at], _left[at], _came[at]) = (u.Item, 0, 0);
                if (Entity is not null)
                    _from[at] = PositionOf(at, grid);
            }

            return true;
        });
    }

    private void ItemsChanged() => _itemRows = null;

    private List<string> ItemRows()
    {
        var rows = new List<string>();
        for (var i = 0; i < _belts.Length; i++)
        {
            if (_items[i] is { } item)
                rows.Add(string.Create(CultureInfo.InvariantCulture, $"{_belts[i].X} {_belts[i].Y} {item}"));
        }

        // Ones read before their belts were laid aren't lost.
        foreach (var (x, y, item) in _unplaced)
            rows.Add(string.Create(CultureInfo.InvariantCulture, $"{x} {y} {item}"));
        return rows;
    }

    private static int WayIndex(int x, int y) => Array.IndexOf(Ways, (x, y));

    private static string RowOf(ConveyorCell cell)
    {
        var way = WayIndex(cell.WayX, cell.WayY) switch { 0 => "right", 1 => "up", 2 => "left", _ => "down" };
        var row = string.Create(CultureInfo.InvariantCulture, $"{cell.X} {cell.Y} {way}");
        return cell.Kind switch
        {
            ConveyorCellKind.Crossing => row + " crossing",
            ConveyorCellKind.Split => row + " split",
            _ => row,
        };
    }

    // "4 2 right", "5 2 down crossing", "9 2 up split" (or >, <, ^ and v for the ways); null for a row that doesn't read.
    private static ConveyorCell? ReadCell(string? row)
    {
        var parts = (row ?? "").Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
            return null;
        (int X, int Y)? way = parts[2].ToLowerInvariant() switch
        {
            "right" or "r" or ">" => (1, 0),
            "up" or "u" or "^" => (0, 1),
            "left" or "l" or "<" => (-1, 0),
            "down" or "d" or "v" => (0, -1),
            _ => null,
        };
        if (way is not { } w)
            return null;
        var kind = parts.Length < 4 ? ConveyorCellKind.Belt : parts[3].ToLowerInvariant() switch
        {
            "crossing" or "cross" => ConveyorCellKind.Crossing,
            "split" or "splitter" => ConveyorCellKind.Split,
            _ => ConveyorCellKind.Belt,
        };
        return new ConveyorCell(x, y, w.X, w.Y, kind);
    }

    // "4 2 Deck": the cell, then the item (the rest of the row).
    private static (int X, int Y, string Item)? ReadItem(string? row)
    {
        var parts = (row ?? "").Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
            return null;
        return (x, y, parts[2]);
    }
}

/// <summary>Whether a <see cref="ConveyorPort"/> takes items off a belt or puts them on.</summary>
public enum ConveyorPortKind
{
    /// <summary>It takes what a belt brings to it: a machine's way in.</summary>
    In,

    /// <summary>It puts items onto a belt: a machine's way out.</summary>
    Out,
}

/// <summary>
/// Where a machine meets a <see cref="Conveyor"/>, at the cell it stands on: an In port takes what reaches the end of a
/// belt pointing into its cell (or ending on it) into the machine, and an Out port puts what the machine made onto the
/// belt on its cell, or one next to it leading away. Put one on the machine, or on an entity inside it where its way in
/// or out is, so a machine's prefab brings its ports with it.
/// </summary>
/// <remarks>
/// It takes into and gives from what holds items on its entity or the one it's inside (an <see cref="Inventory"/>), so a
/// machine with an inventory needs no code: an In port with <see cref="Holds"/> stops taking once the machine has that
/// many, and the line backs up behind it. With nothing holding items, an In port takes everything it's offered (for a
/// script listening to <see cref="Received"/>), and an Out port puts only what a script hands it with <see cref="Give"/>.
/// An item it won't take waits at the belt's end (it's <see cref="Blocked"/>) until something takes it off.
/// </remarks>
[Category("Gameplay")]
[WorksWith(typeof(Conveyor), Note = "the belts it takes from or puts on")]
[WorksWith(typeof(Inventory), Note = "what it takes into and gives from")]
[ComponentName("ConveyorPort")]
public sealed class ConveyorPort : Component
{
    private readonly List<string> _toGive = [];

    /// <summary>In takes items off a belt's end; Out puts them on a belt.</summary>
    public ConveyorPortKind Kind { get; set; }

    /// <summary>
    /// The items it takes or puts, e.g. "Deck, Report"; empty for any. An In port leaves others waiting at the belt's
    /// end; an Out port with none named puts whatever its inventory holds.
    /// </summary>
    [Names(NameKind.Item, Many = true)]
    public string? Items { get; set; }

    /// <summary>
    /// For an In port: it stops taking an item once the machine holds this many of it, so the line backs up until the
    /// machine uses some. 0 for no limit.
    /// </summary>
    [ShownWhen(nameof(Kind), Is = [ConveyorPortKind.In])]
    public int Holds { get; set; }

    /// <summary>While ticked it takes or puts items; untick it (or switch it off) to hold the line.</summary>
    [Powered]
    public bool Open { get; set; } = true;

    /// <summary>A message sent as it takes or puts an item, with the item's name as its data (a <see cref="Counter"/> can count them).</summary>
    [Names(NameKind.Message)]
    public string? Message { get; set; }

    /// <summary>
    /// A script's say in what an In port takes, besides <see cref="Items"/>: return false to leave the item waiting at the
    /// belt's end. Null takes whatever Items allows.
    /// </summary>
    [DoNotSerialize]
    public Func<string, bool>? Accepts { get; set; }

    /// <summary>
    /// True if in the last step it held the line up: an In port left an item waiting it wouldn't take (the wrong item, or
    /// the machine's full), or an Out port had something to put on a belt that had no room.
    /// </summary>
    public bool Blocked { get; internal set; }

    /// <summary>What a script has handed an Out port with <see cref="Give"/> that isn't on the belt yet, first first.</summary>
    public IReadOnlyList<string> Waiting => _toGive;

    /// <summary>Raised once the step is over for each item an In port took, with the item.</summary>
    public event Action<ConveyorPort, string>? Received;

    /// <summary>Raised once the step is over for each item an Out port put on a belt, with the item.</summary>
    public event Action<ConveyorPort, string>? Sent;

    /// <summary>Hands an Out port <paramref name="item"/> to put on its belt as soon as there's room (after any it already has).</summary>
    public void Give(string item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
        _toGive.Add(item.Trim());
    }

    /// <summary>True if <see cref="Items"/> lets it take or put <paramref name="item"/> (its first word counts too: "Deck" lets "Deck 17" by).</summary>
    public bool Lets(string item)
    {
        var names = Formats.Names(Items);
        if (names.Length == 0)
            return true;
        var space = item.IndexOf(' ');
        foreach (var name in names)
        {
            if (string.Equals(name, item, StringComparison.OrdinalIgnoreCase) ||
                (space > 0 && item.AsSpan(0, space).Equals(name, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    // An In port takes `item` if it's open and lets it, a script agrees and the machine has room: into what holds items
    // on its entity, if anything does.
    internal bool TryTake(string item)
    {
        if (!Open || !Lets(item) || Accepts?.Invoke(item) == false)
            return false;
        if (Scenes.Items.HolderOf(Entity, item, null) is not { } holder)
            return true;
        if (Holds > 0 && holder.Count(item) >= Holds)
            return false;
        holder.Add(item);
        return true;
    }

    // Whether an Out port has something to put on its belt.
    internal bool HasToGive() => _toGive.Count > 0 || NextHeld(take: false) is not null;

    // What an Out port puts on its belt next, taken from what a script gave it, else from what its machine holds.
    internal string? NextToGive()
    {
        if (_toGive.Count > 0)
        {
            var item = _toGive[0];
            _toGive.RemoveAt(0);
            return item;
        }

        return NextHeld(take: true);
    }

    private string? NextHeld(bool take)
    {
        foreach (var name in Formats.Names(Items))
        {
            if (Scenes.Items.HolderOf(Entity, name, null) is { } holder && holder.Count(name) > 0)
                return !take || holder.Take(name) ? name : null;
        }

        if (!string.IsNullOrWhiteSpace(Items))
            return null;
        for (var e = Entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Inventory>() is not { } inventory)
                continue;
            foreach (var (name, count) in inventory.Held)
            {
                if (count > 0)
                    return !take || inventory.Take(name) ? name : null;
            }
        }

        return null;
    }

    internal void RaiseReceived(string item) => Received?.Invoke(this, item);

    internal void RaiseSent(string item) => Sent?.Invoke(this, item);
}

/// <summary>What a conveyor's step did that the game raises once it's over.</summary>
internal enum ConveyorEventKind : byte
{
    ReachedEnd,
    Received,
    Sent,
}

/// <summary>One thing a conveyor's step did: an item at an end, taken in by a port, or put on by one.</summary>
internal readonly record struct ConveyorEvent(ConveyorEventKind Kind, ConveyorPort? Port, (int X, int Y) Cell, string Item);
