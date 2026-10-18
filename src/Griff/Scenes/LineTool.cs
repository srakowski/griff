using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// One cell of a line laid on a grid (see <see cref="LineTool"/> and <see cref="LineDrag"/>): its column and row (counted
/// as a tilemap's are, row 0 at the top), the way the line leaves it and the way it came in (x right, y up, as a grid
/// mover steps and a <see cref="Conveyor"/> carries), and whether it can go there.
/// </summary>
public readonly record struct LineCell(int X, int Y, int WayX, int WayY, int InX, int InY, bool Fits = true)
{
    /// <summary>Its column and row.</summary>
    public (int X, int Y) Cell => (X, Y);

    /// <summary>The way the line leaves it: (1, 0) right, (0, 1) up, (-1, 0) left, (0, -1) down.</summary>
    public (int X, int Y) Way => (WayX, WayY);

    /// <summary>The way the line comes into it: the same as <see cref="Way"/> but at a corner. The first cell's is its own way.</summary>
    public (int X, int Y) In => (InX, InY);

    /// <summary>True where the line turns: it comes in one way and leaves another.</summary>
    public bool IsCorner => InX != WayX || InY != WayY;

    /// <summary>The cell as a <see cref="Conveyor"/>'s, carrying the way the line runs.</summary>
    public ConveyorCell ToConveyor(ConveyorCellKind kind = ConveyorCellKind.Belt) => new(X, Y, WayX, WayY, kind);
}

/// <summary>Which way a single cell of a line points: the way R turns it, clockwise.</summary>
public enum LineWay
{
    /// <summary>To the right, (1, 0).</summary>
    Right,

    /// <summary>Down, (0, -1).</summary>
    Down,

    /// <summary>To the left, (-1, 0).</summary>
    Left,

    /// <summary>Up, (0, 1).</summary>
    Up,
}

/// <summary>Which leg a dragged line with a corner runs along first (see <see cref="LineDrag"/>).</summary>
public enum LineFirstLeg
{
    /// <summary>The way the drag started: across if it first left the start cell along its row, down or up if along its column.</summary>
    Started,

    /// <summary>The way it's dragged further, across or up and down, whichever is longer now.</summary>
    Longer,
}

/// <summary>
/// A line dragged on a grid from one cell to another, and the rule that lays it: straight along one axis, then (with
/// <see cref="Corners"/>) round one corner and along the other, each cell pointing on to the next. Which axis comes first
/// is <see cref="FirstLeg"/>'s, and <see cref="SwapCorner"/> turns it the other way round. A <see cref="LineTool"/> lays
/// lines this way in a game, and the editor's Line tool paints tiles the same way.
/// </summary>
/// <example>
/// <code>
/// var drag = new LineDrag((2, 4));
/// drag.MoveTo((9, 8));          // first along row 4 to column 9, then down to row 8
/// foreach (var cell in drag.Cells())
///     belts.Lay(cell.Cell, cell.Way);
/// </code>
/// </example>
public sealed class LineDrag
{
    // Whether it goes across first, once the drag has left the start cell (null while it's there), and whether R swapped it.
    private bool? _across;
    private bool _swapped;

    /// <summary>A drag starting at <paramref name="from"/>, not yet moved.</summary>
    public LineDrag((int X, int Y) from, LineFirstLeg firstLeg = LineFirstLeg.Started, bool corners = true)
    {
        (From, To, FirstLeg, Corners) = (from, from, firstLeg, corners);
    }

    /// <summary>The cell it started on: the line's first.</summary>
    public (int X, int Y) From { get; }

    /// <summary>The cell it's dragged to now.</summary>
    public (int X, int Y) To { get; private set; }

    /// <summary>Which leg comes first.</summary>
    public LineFirstLeg FirstLeg { get; }

    /// <summary>True to turn a corner to reach <see cref="To"/>; false keeps it straight, along the way it's dragged further.</summary>
    public bool Corners { get; }

    /// <summary>True if the line runs along the row first (then up or down the column), false if along the column first.</summary>
    public bool AcrossFirst => (_across ?? true) ^ _swapped;

    /// <summary>The line's last cell: <see cref="To"/>, or without <see cref="Corners"/>, as far as it goes along one axis.</summary>
    public (int X, int Y) End => Corners ? To : Longer(From, To) ? (To.X, From.Y) : (From.X, To.Y);

    /// <summary>True while it's still on the cell it started on: a single cell.</summary>
    public bool IsSingle => End == From;

    /// <summary>Drags it to <paramref name="cell"/>: true if the line changed.</summary>
    public bool MoveTo((int X, int Y) cell)
    {
        var (was, wasAcross) = (End, AcrossFirst);
        To = cell;
        if (cell == From)
            (_across, _swapped) = (null, false);
        else if (FirstLeg == LineFirstLeg.Longer || _across is null)
            _across = Longer(From, cell);
        return End != was || AcrossFirst != wasAcross;
    }

    /// <summary>Turns the corner the other way round: along the column first instead of the row, or the other way (what R does while dragging).</summary>
    public void SwapCorner() => _swapped = !_swapped;

    /// <summary>
    /// The line's cells from <see cref="From"/> to <see cref="End"/>. A single cell points <paramref name="single"/> (right
    /// if not given); the last of a longer line carries on the way the one before it does.
    /// </summary>
    public IReadOnlyList<LineCell> Cells((int X, int Y)? single = null) =>
        Path(From, End, AcrossFirst, IsSingle ? single ?? (1, 0) : null);

    /// <summary>
    /// The cells of a line from <paramref name="from"/> to <paramref name="to"/>: along the row first, then up or down
    /// the column (or the column first, with <paramref name="acrossFirst"/> false), each pointing on to the next. The last
    /// carries on the way the one before it does, or turns to <paramref name="lastWay"/> (a single cell points that way,
    /// or right).
    /// </summary>
    public static IReadOnlyList<LineCell> Path((int X, int Y) from, (int X, int Y) to, bool acrossFirst = true, (int X, int Y)? lastWay = null)
    {
        var points = new List<(int X, int Y)> { from };
        var corner = acrossFirst ? (to.X, from.Y) : (from.X, to.Y);
        foreach (var target in new[] { corner, to })
        {
            var (x, y) = points[^1];
            while ((x, y) != target)
            {
                (x, y) = (x + Math.Sign(target.Item1 - x), y + Math.Sign(target.Item2 - y));
                points.Add((x, y));
            }
        }

        var cells = new LineCell[points.Count];
        var way = lastWay ?? (1, 0);
        var into = (X: 0, Y: 0);
        for (var i = 0; i < points.Count; i++)
        {
            // The way on to the next cell (rows count down, ways count up), or for the last, the way it came in.
            if (i + 1 < points.Count)
                way = (points[i + 1].X - points[i].X, points[i].Y - points[i + 1].Y);
            else if (i > 0)
                way = lastWay ?? into;
            if (i == 0)
                into = way;
            cells[i] = new LineCell(points[i].X, points[i].Y, way.X, way.Y, into.X, into.Y);
            into = way;
        }

        return cells;
    }

    /// <summary>The way (x right, y up) a <see cref="LineWay"/> points.</summary>
    public static (int X, int Y) StepOf(LineWay way) => way switch
    {
        LineWay.Down => (0, -1),
        LineWay.Left => (-1, 0),
        LineWay.Up => (0, 1),
        _ => (1, 0),
    };

    // Dragged at least as far across as up or down.
    private static bool Longer((int X, int Y) from, (int X, int Y) to) => Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y);
}

/// <summary>
/// The tiles a line laid on this tilemap is drawn with: a straight piece and a corner, each drawn once and turned to
/// follow the line (see <see cref="TileTurn"/>), and if you like, pieces for its two ends. A <see cref="LineTool"/> lays
/// them in the game, and the editor's Line tool (L, with the tilemap selected) paints with them, so belts, pipes, rails
/// or wires go down by dragging.
/// </summary>
/// <remarks>
/// Draw the straight piece running left to right, pointing right (the way the line runs), and the corner coming in from
/// the left, moving right, and going out at the bottom, moving down: a clockwise turn. Turning the other way, it's laid
/// mirrored.
/// </remarks>
/// <example>
/// <code>
/// var tiles = Game.Find("Belts").GetComponent&lt;LineTiles&gt;();
/// tiles.Lay(LineDrag.Path((2, 4), (9, 8)));
/// </code>
/// </example>
[Category("Level")]
[Requires(typeof(Tilemap))]
[WorksWith(typeof(LineTool), Note = "lays them in the game")]
public sealed class LineTiles : Component
{
    /// <summary>The straight piece, drawn pointing right. None lays no tiles (the editor's Line tool then paints the brush's tile).</summary>
    [TileNumbers]
    public int StraightTile { get; set; } = -1;

    /// <summary>The corner piece, drawn coming in from the left and going out at the bottom. None lays the straight piece at corners too.</summary>
    [TileNumbers]
    public int CornerTile { get; set; } = -1;

    /// <summary>The piece for a line's first cell, drawn pointing right, the way the line leaves it. None lays the straight piece.</summary>
    [TileNumbers]
    public int StartTile { get; set; } = -1;

    /// <summary>The piece for a line's last cell, drawn with the line coming in from the left. None lays the straight piece.</summary>
    [TileNumbers]
    public int EndTile { get; set; } = -1;

    /// <summary>
    /// The tile, and its turn, for cell <paramref name="index"/> of <paramref name="line"/>: the corner at a corner, the
    /// start and end pieces at its ends (a line of more than one cell), else the straight piece; -1 for the tile if it has
    /// none.
    /// </summary>
    public (int Tile, TileTurn Turn) TileFor(IReadOnlyList<LineCell> line, int index)
    {
        ArgumentNullException.ThrowIfNull(line);
        var cell = line[index];
        if (cell.IsCorner && CornerTile >= 0)
            return (CornerTile, TileTurns.Corner(cell.In, cell.Way));
        if (line.Count > 1 && index == 0 && StartTile >= 0)
            return (StartTile, TileTurns.Pointing(cell.WayX, cell.WayY));
        if (line.Count > 1 && index == line.Count - 1 && EndTile >= 0)
            return (EndTile, TileTurns.Pointing(cell.InX, cell.InY));
        return (StraightTile, TileTurns.Pointing(cell.WayX, cell.WayY));
    }

    /// <summary>
    /// Lays <paramref name="line"/>'s cells that fit on its tilemap (cells of this map), each with its tile turned to
    /// follow the line. Cells left of or above the grid are skipped; the grid grows right and down to fit.
    /// </summary>
    public void Lay(IReadOnlyList<LineCell> line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Entity?.GetComponent<Tilemap>() is not { } map)
            return;
        for (var i = 0; i < line.Count; i++)
        {
            var cell = line[i];
            if (!cell.Fits || cell.X < 0 || cell.Y < 0 || TileFor(line, i) is not (>= 0 and var tile, var turn))
                continue;
            map.SetTile(cell.X, cell.Y, tile, turn);
        }
    }

    /// <summary>The Line Tiles that go with <paramref name="map"/>: on its own entity, or on the map it's a layer of.</summary>
    public static LineTiles? For(Tilemap? map) =>
        map?.Entity?.GetComponent<LineTiles>() ?? map?.BaseMap.Entity?.GetComponent<LineTiles>();
}

/// <summary>
/// Lets the player lay a line on the grid by dragging, as a factory game's belt tool does: press on a cell, drag, let
/// go. The line runs from the cell pressed on to the one under the mouse, along one axis and round a corner, each cell
/// pointing the way the line runs; a see-through ghost shows where it goes, <see cref="GhostColor"/> where it fits and
/// <see cref="BlockedColor"/> where it can't. Letting go lays the cells that fit on a <see cref="Conveyor"/> as belts
/// and on a <see cref="Tiles"/> map with its <see cref="LineTiles"/>, and raises <see cref="Placed"/>, so a script can
/// lay something else (build orders, walls) or charge for it. R (the Rotate action) turns a single cell, or swaps the
/// corner while dragging; a right-click (Command) or untick <see cref="Controllable"/> drops the line.
/// </summary>
/// <remarks>
/// <para>
/// Put it on the camera (or anywhere) and untick <see cref="Controllable"/> while another tool is in hand: while it's
/// ticked, a <see cref="SelectionControls"/> leaves the mouse to it. Where a cell can't go is up to
/// <see cref="StayOnMap"/>, <see cref="BlockedByWalls"/> (solid tiles and colliders, as grid movers see them, but not
/// the belts of its own conveyor, which a line laid over turns), <see cref="BlockedByMovers"/>,
/// <see cref="MaxCells"/> (what the money covers, say) and a script's <see cref="CanLay"/>.
/// </para>
/// <para>
/// The drag is this machine's own, and isn't kept by a rewind; what it lays is the scene's, like anything else a script
/// changes. In a network game, have a client's script send the host what <see cref="Placed"/> gives, as a command.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var tool = Find("Main Camera").GetComponent&lt;LineTool&gt;();
/// tool.CanLay = cell =&gt; !Zones.Has(cell.Cell);
/// tool.MaxCells = Money / 40;
/// tool.Placed += (_, cells) =&gt; { foreach (var c in cells) OrderBelt(c.Cell, c.Way); Money -= 40 * cells.Count; };
/// </code>
/// </example>
[Category("Gameplay")]
[WorksWith(typeof(Conveyor), Note = "lays belts on it")]
[WorksWith(typeof(LineTiles), Note = "lays a tilemap's line tiles")]
[WorksWith(typeof(SelectionControls), Note = "leaves the mouse to it while it's in hand")]
public sealed class LineTool : Component
{
    /// <summary>
    /// Untick it while another tool is in hand (or none): it leaves the mouse alone and drops a line being dragged. Its
    /// Focus action ticks it and unticks it on everything else the player plays.
    /// </summary>
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>
    /// The tilemap whose cells it lays on. Empty uses its <see cref="Conveyor"/>'s map, else its <see cref="Tiles"/> map,
    /// else the tilemap on its own entity or the one it's inside, else the first in the scene; with none, cells are world
    /// units centered on whole numbers, as for a grid mover.
    /// </summary>
    [EntityName]
    public string? Map { get; set; }

    /// <summary>A conveyor to lay the line on as belts when it's let go, turning belts already there. Empty lays no belts.</summary>
    [EntityName]
    public string? Conveyor { get; set; }

    /// <summary>What it lays on the <see cref="Conveyor"/>: belts, crossings (walkers step over), splitters or unders (a line passes beneath).</summary>
    public ConveyorCellKind BeltKind { get; set; }

    /// <summary>
    /// A tilemap with <see cref="LineTiles"/> to lay the line's tiles on when it's let go, straight pieces and corners
    /// turned to follow it: a layer for belts, pipes or rails. Empty lays no tiles.
    /// </summary>
    [EntityName]
    public string? Tiles { get; set; }

    /// <summary>True to turn a corner to reach the mouse; untick it for straight lines only, across or up and down, whichever way it's dragged further (a wall tool).</summary>
    public bool Corners { get; set; } = true;

    /// <summary>Which leg of a line with a corner comes first: the way the drag started (the default), or the way it's dragged further.</summary>
    public LineFirstLeg FirstLeg { get; set; }

    /// <summary>Which way a single cell points (a click without dragging). The Rotate action turns it a quarter clockwise.</summary>
    public LineWay Way { get; set; }

    /// <summary>The input action held to drag a line, e.g. "Select" (the left mouse button).</summary>
    [Names(NameKind.Action)]
    public string? Action { get; set; } = "Select";

    /// <summary>The input action that turns a single cell a quarter clockwise, or swaps the corner while dragging, e.g. "Rotate" (R).</summary>
    [Names(NameKind.Action)]
    public string? RotateAction { get; set; } = "Rotate";

    /// <summary>The input action that drops a line being dragged without laying it, e.g. "Command" (the right mouse button).</summary>
    [Names(NameKind.Action)]
    public string? CancelAction { get; set; } = "Command";

    /// <summary>True if cells off the map's grid can't take the line.</summary>
    public bool StayOnMap { get; set; } = true;

    /// <summary>True if walls block it, as they do grid movers: solid tiles, solid colliders and other conveyors' solid belts.</summary>
    public bool BlockedByWalls { get; set; } = true;

    /// <summary>Collision layers whose solid colliders block it, e.g. "Machines, Walls". Empty: any solid collider does.</summary>
    [Names(NameKind.Layer, Many = true)]
    public string? BlockingLayers { get; set; }

    /// <summary>True if a cell with a grid mover in it (a worker standing there), or heading into it, can't take the line.</summary>
    public bool BlockedByMovers { get; set; }

    /// <summary>The most cells a line can have: those past it are blocked (what the money covers, set by a script). 0 for no limit.</summary>
    public int MaxCells { get; set; }

    /// <summary>The ghost's tint where the line fits.</summary>
    public Color GhostColor { get; set; } = new(0.35f, 0.9f, 0.45f, 0.45f);

    /// <summary>The ghost's tint where the line can't go.</summary>
    public Color BlockedColor { get; set; } = new(1f, 0.3f, 0.3f, 0.5f);

    /// <summary>A message sent with <c>Game.Send</c> when a line is laid, with how many cells as its data.</summary>
    [Names(NameKind.Message)]
    public string? Message { get; set; }

    /// <summary>
    /// A script's say in where the line can go, besides walls and the map: return false for a cell that can't take it (a
    /// storage zone, a machine's footprint). Null lets every cell the other rules allow.
    /// </summary>
    [DoNotSerialize]
    public Func<LineCell, bool>? CanLay { get; set; }

    /// <summary>True while a line is being dragged.</summary>
    [DoNotSerialize]
    public bool IsDragging => Drag is not null;

    /// <summary>
    /// The ghost's cells, each saying whether it <see cref="LineCell.Fits"/>: the line being dragged, or the single cell
    /// under the mouse while it's in hand. Empty with the mouse off the grid or on the HUD.
    /// </summary>
    [DoNotSerialize]
    public IReadOnlyList<LineCell> Cells { get; internal set; } = [];

    /// <summary>The cell under the mouse while it's in hand, or null.</summary>
    [DoNotSerialize]
    public (int X, int Y)? Hover { get; internal set; }

    /// <summary>Raised whenever the line being dragged changes, with its cells (each saying whether it fits).</summary>
    public event Action<LineTool, IReadOnlyList<LineCell>>? Dragging;

    /// <summary>
    /// Raised when a line is let go, after its belts and tiles are laid, with the cells that fit (the ones laid). Not raised
    /// when none fit.
    /// </summary>
    public event Action<LineTool, IReadOnlyList<LineCell>>? Placed;

    // The line being dragged, and what its map and conveyor were found for.
    [NotState("the player's drag, this machine's own")]
    private LineDrag? _drag;

    internal LineDrag? Drag
    {
        get => _drag;
        set => _drag = value;
    }

    [NotState("found again as it's asked for")]
    private (Tilemap? Map, Conveyor? Conveyor, LineTiles? Tiles) _found;
    [NotState("found again as it's asked for")]
    private (Scene, long, string?, string?, string?)? _foundFor;

    /// <summary>The cells it lays on (see <see cref="Map"/>).</summary>
    public GridCells Grid => GridCells.Of(Found().Map);

    /// <summary>The tilemap whose cells it lays on (see <see cref="Map"/>), or null for world units.</summary>
    public Tilemap? FindMap() => Found().Map;

    /// <summary>The conveyor it lays belts on (see <see cref="Conveyor"/>), or null.</summary>
    public Conveyor? FindConveyor() => Found().Conveyor;

    /// <summary>The line tiles it lays (see <see cref="Tiles"/>), or null.</summary>
    public LineTiles? FindTiles() => Found().Tiles;

    /// <summary>Turns the single cell's <see cref="Way"/> a quarter clockwise, or while a line is dragged, swaps its corner: what the Rotate action does.</summary>
    [Action]
    public void Rotate()
    {
        if (Drag is { IsSingle: false } drag)
            drag.SwapCorner();
        else
            Way = (LineWay)(((int)Way + 1) % 4);
    }

    /// <summary>Drops the line being dragged without laying it.</summary>
    [Action]
    public void Cancel() => Drag = null;

    /// <summary>
    /// The line from <paramref name="from"/> to <paramref name="to"/> as this tool would lay it (along the row first, or
    /// the column with <paramref name="acrossFirst"/> false), each cell saying whether it <see cref="LineCell.Fits"/>.
    /// </summary>
    public IReadOnlyList<LineCell> Line((int X, int Y) from, (int X, int Y) to, bool acrossFirst = true) =>
        Check(LineDrag.Path(from, to, acrossFirst, from == to ? LineDrag.StepOf(Way) : null));

    /// <summary>
    /// Lays the cells of <paramref name="line"/> that fit, as letting go does: belts on its <see cref="Conveyor"/> and
    /// tiles on its <see cref="Tiles"/> map. It doesn't raise <see cref="Placed"/>.
    /// </summary>
    public void Lay(IReadOnlyList<LineCell> line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var fits = line.Where(c => c.Fits).ToList();
        if (fits.Count == 0)
            return;
        if (FindConveyor() is { } conveyor)
            conveyor.Lay(fits.Select(c => c.ToConveyor(BeltKind)));
        if (FindTiles() is { Entity: { } tilesEntity } tiles && tilesEntity.GetComponent<Tilemap>() is { } tilesMap)
        {
            // Cells of its grid, as cells of the tiles' map (the same, unless that map lies elsewhere).
            var grid = Grid;
            var theirs = GridCells.Of(tilesMap);
            tiles.Lay([.. line.Select(c => theirs.CellAt(grid.Center(c.Cell)) is var (x, y) ? c with { X = x, Y = y } : c)]);
        }
    }

    /// <summary>
    /// <paramref name="line"/> with each cell saying whether it can take the line: on the map, clear of walls and movers
    /// as this tool's settings say, within <see cref="MaxCells"/>, and allowed by <see cref="CanLay"/>.
    /// </summary>
    public IReadOnlyList<LineCell> Check(IReadOnlyList<LineCell> line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var grid = Grid;
        var walls = BlockedByWalls || BlockedByMovers || StayOnMap
            ? GridWalls.Gather(Entity?.Scene, grid, StayOnMap, Entity, null, null, BlockingLayersOrNull(), moversByLayer: false, except: null, FindConveyor())
            : null;
        var result = new LineCell[line.Count];
        for (var i = 0; i < line.Count; i++)
        {
            var cell = line[i];
            var fits = !(MaxCells > 0 && i >= MaxCells) && !OffMap(walls, cell.Cell) &&
                       !(BlockedByWalls && walls!.IsWall(cell.Cell)) && !(BlockedByMovers && walls!.MoverIn(cell.Cell) is not null);
            if (fits && CanLay is { } rule)
                fits = rule(cell with { Fits = true });
            result[i] = cell with { Fits = fits };
        }

        return result;
    }

    // Off the map it has to stay on (GridWalls counts that a wall).
    private bool OffMap(GridWalls? walls, (int X, int Y) cell) =>
        StayOnMap && walls?.Size is var (width, height) && (cell.X < 0 || cell.Y < 0 || cell.X >= width || cell.Y >= height);

    private string[]? BlockingLayersOrNull() => CollisionLayers.Parse(BlockingLayers) is { Length: > 0 } layers ? layers : null;

    internal void RaiseDragging() => Dragging?.Invoke(this, Cells);

    internal void RaisePlaced(IReadOnlyList<LineCell> laid) => Placed?.Invoke(this, laid);

    // Its map, conveyor and line tiles, found again only when the scene's entities or its names have changed.
    private (Tilemap? Map, Conveyor? Conveyor, LineTiles? Tiles) Found()
    {
        if (Entity?.Scene is not { } scene)
            return default;
        var key = (scene, scene.ActiveVersion, Map, Conveyor, Tiles);
        if (_foundFor == key)
            return _found;
        var lookup = new NameLookup(Entity);
        var conveyor = string.IsNullOrWhiteSpace(Conveyor) ? null : EntityNames.Find(scene, Conveyor, lookup)?.GetComponent<Conveyor>();
        var tiles = string.IsNullOrWhiteSpace(Tiles) ? null : EntityNames.Find(scene, Tiles, lookup)?.GetComponent<LineTiles>();
        Tilemap? map;
        if (!string.IsNullOrWhiteSpace(Map))
            map = EntityNames.Find(scene, Map, lookup)?.GetComponent<Tilemap>()?.BaseMap;
        else
        {
            map = conveyor?.FindMap() ?? tiles?.Entity?.GetComponent<Tilemap>()?.BaseMap;
            for (var e = Entity; e is not null && map is null; e = e.Parent)
                map = e.GetComponent<Tilemap>()?.BaseMap;
            map ??= scene.Entities.Where(e => e.IsActive).Select(e => e.GetComponent<Tilemap>()).FirstOrDefault(t => t is { LayerOf: null });
        }

        (_found, _foundFor) = ((map?.Entity is null ? null : map, conveyor, tiles), key);
        return _found;
    }
}
