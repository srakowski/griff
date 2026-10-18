using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// The rooms walls make on a tilemap's grid, for heat, air, occupancy or anything else that goes by room: every patch of
/// floor cells joined side by side (four ways; six on a hex map) without crossing a wall. Walls are the map's solid tiles
/// on any of its layers (as grid movers see them), its <see cref="WallTiles"/> even where they can be walked through (a
/// door counts as shut), and solid colliders on <see cref="WallLayers"/>. A room that reaches past the map's edge (or, with
/// <see cref="EmptyIsOutside"/>, an empty cell) is open to the outside. Put it on the tilemap whose cells make the rooms.
/// </summary>
/// <remarks>
/// <para>
/// Not a camera's <see cref="Room"/>: these are found from the walls, not placed. They're worked out again only when the
/// walls change (a tile painted, a wall collider added, moved or taken away), and <see cref="Changed"/> says when they have,
/// at the end of that step. A script that changes walls and wants the new rooms in the same step calls
/// <see cref="Refresh"/>.
/// </para>
/// <para>
/// Each room keeps its <see cref="WallRoom.Id"/> as walls go up and come down: split in two, the bigger part keeps it;
/// joined, the room takes the id of the one it has most of. A new room gets a new id.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var rooms = Game.Find("Floor").GetComponent&lt;WallRooms&gt;();
/// var room = rooms.RoomOf(Entity);
/// if (room is { Open: false })
///     Heat[room.Id] += Power * dt / room.Area;
/// rooms.Changed += all => Game.Log($"{all.Count} rooms now");
/// </code>
/// </example>
[Category("Level")]
[Requires(typeof(Tilemap))]
public sealed class WallRooms : Component, IDrawable
{
    /// <summary>The map's solid tiles (on it and its layers) are walls, as they are to grid movers. Untick it to count only <see cref="WallTiles"/> and <see cref="WallLayers"/>.</summary>
    public bool SolidTiles { get; set; } = true;

    /// <summary>
    /// Tiles that are walls to rooms even where they can be walked through, on the map or any of its layers: a door's
    /// tiles, so a door counts as shut. Tile numbers and ranges, e.g. "300-301". Empty for none.
    /// </summary>
    [TileNumbers]
    public string? WallTiles { get; set; }

    /// <summary>
    /// Collision layers whose solid colliders are walls too, over a cell's middle, e.g. "Walls, Doors": walls built as
    /// entities rather than painted. Grid movers (workers walking about) never are. Empty for none.
    /// </summary>
    [Names(NameKind.Layer, Many = true)]
    public string? WallLayers { get; set; }

    /// <summary>
    /// Cells with no tile on the map or any of its layers are outside, like the cells past its edge, so a room next to
    /// one is open: a floor painted in the shape of a building, or a ship's hull with a hole in it.
    /// </summary>
    public bool EmptyIsOutside { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> whenever the rooms change, with this entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? Message { get; set; }

    /// <summary>Tints each closed room a color of its own (open ones aren't tinted), to see where the walls close rooms off.</summary>
    public bool Show { get; set; }

    /// <summary>The rooms, open and closed, by <see cref="WallRoom.Id"/>.</summary>
    [DoNotSerialize]
    public IReadOnlyList<WallRoom> Rooms => Known.Rooms;

    /// <summary>How many rooms the walls close off: those not open to the outside.</summary>
    [Output]
    [DoNotSerialize]
    public int Closed
    {
        get
        {
            var count = 0;
            foreach (var room in Known.Rooms)
                count += room.Open ? 0 : 1;
            return count;
        }
    }

    /// <summary>The area, in cells, of the room the entity called this stands in; 0 when it's in none (on a wall, or off the map).</summary>
    [Output(Example = "Furnace")]
    public int RoomArea(string entity) => RoomOfNamed(entity)?.Area ?? 0;

    /// <summary>The <see cref="WallRoom.Id"/> of the room the entity called this stands in; 0 when it's in none.</summary>
    [Output(Example = "Furnace")]
    public int RoomId(string entity) => RoomOfNamed(entity)?.Id ?? 0;

    /// <summary>Raised at the end of a step in which the rooms changed (or were first found), with the rooms now.</summary>
    public event Action<IReadOnlyList<WallRoom>>? Changed;

    /// <summary>The room with id <paramref name="id"/>, or null if there's none now.</summary>
    public WallRoom? Room(int id)
    {
        foreach (var room in Known.Rooms)
        {
            if (room.Id == id)
                return room;
        }

        return null;
    }

    /// <summary>The room a cell of the map is in, or null for a wall, an outside cell or one off the map.</summary>
    public WallRoom? RoomAt((int X, int Y) cell) => Known.RoomAt(cell);

    /// <summary>The room a point in the world is in, or null for none.</summary>
    public WallRoom? RoomAt(Vector2 world) => RoomAt(CellAt(world));

    /// <summary>The room an entity stands in (the cell its position is in), or null for none.</summary>
    public WallRoom? RoomOf(Entity entity)
    {
        var at = entity.Transform.WorldPosition;
        return RoomAt(new Vector2(at.X, at.Y));
    }

    /// <summary>Every active entity standing in <paramref name="room"/>, in hierarchy order: the map and its layers aside.</summary>
    public List<Entity> EntitiesIn(WallRoom room)
    {
        var found = new List<Entity>();
        foreach (var entity in Entity?.Scene?.Entities ?? [])
        {
            if (entity.IsActive && !IsMap(entity) && RoomOf(entity) == room)
                found.Add(entity);
        }

        return found;
    }

    /// <summary>The <typeparamref name="T"/> components on active entities standing in <paramref name="room"/>: its machines, heaters or workers.</summary>
    public List<T> EntitiesIn<T>(WallRoom room) where T : Component
    {
        var found = new List<T>();
        if (Entity?.Scene is not { } scene)
            return found;
        foreach (var component in scene.Active<T>())
        {
            if (component.Entity is { } entity && !IsMap(entity) && RoomOf(entity) == room)
                found.Add(component);
        }

        return found;
    }

    /// <summary>The cell of the map a world position is in.</summary>
    public (int X, int Y) CellAt(Vector2 world) => GridCells.Of(Map).CellAt(world);

    /// <summary>The world position of the middle of a cell of the map.</summary>
    public Vector2 CellCenter((int X, int Y) cell) => GridCells.Of(Map).Center(cell);

    /// <summary>
    /// Works the rooms out again now if the walls have changed, rather than at the end of the step: for a script that
    /// changes walls and asks about rooms straight after. <see cref="Changed"/> is still raised at the end of the step.
    /// </summary>
    public void Refresh() => Current();

    /// <summary>The tilemap whose cells make the rooms: the one on its entity (the map it's a layer of, for a layer).</summary>
    [DoNotSerialize]
    public Tilemap? Map => Entity?.GetComponent<Tilemap>()?.BaseMap;

    int? IDrawable.DrawOrder => null;

    int IDrawable.DrawStep => DrawSteps.Tiles + 50;

    void IDrawable.Draw(Drawing drawing)
    {
        if (!Show || Map is not { Entity: { } mapEntity } map)
            return;
        var layout = Current();
        var world = mapEntity.Transform.WorldMatrix;
        var square = map.Shape == TileShape.Square;
        var size = map.TileSize;
        foreach (var room in layout.Rooms)
        {
            if (room.Open)
                continue;
            var tint = Color.FromHsv(room.Id * 137.5f % 360, 0.6f, 1, 0.35f);
            var cells = room.Cells;
            for (var i = 0; i < cells.Count; i++)
            {
                // A square map's runs along a row are one rectangle; other shapes a smaller square in each cell.
                var first = cells[i];
                while (square && i + 1 < cells.Count && cells[i + 1] == (cells[i].X + 1, first.Y))
                    i++;
                var last = cells[i];
                var center = (map.CellCenter(first.X, first.Y) + map.CellCenter(last.X, last.Y)) / 2;
                var scale = square ? new Vector3(size * (last.X - first.X + 1), size, 1) : new Vector3(size * 0.6f, size * 0.6f, 1);
                drawing.Image(Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(center.X, center.Y, 0) * world, null, tint);
            }
        }
    }

    // ----- Working the rooms out -----

    // What the rooms were last said to be (see Commit): small, so a rewind snapshot keeps it cheaply, and enough to give the
    // rooms back their ids after one.
    private List<RoomStamp> _stamps = [];
    private int _nextId = 1;

    [NotState("worked out again from the walls and the stamps")]
    private Layout? _layout;

    [NotState("worked out again from the walls and the stamps")]
    private Layout? _committed;

    [NotState("filled again each time the walls are looked at")]
    private List<WallKey> _key = [], _scratchKey = [];

    [NotState("read again from WallLayers")]
    private (string? Text, string[] Names) _wallLayers = (null, []);

    // The rooms: while the game runs, as last worked out (again at the end of each step the walls changed, or on Refresh);
    // in the editor, as the walls are now.
    private Layout Known => _layout is { } layout && Entity?.Scene is { Step: > 0 } ? layout : Current();

    private bool IsMap(Entity entity) => Map?.Entity is { } map && entity.IsSelfOrDescendantOf(map);

    private WallRoom? RoomOfNamed(string name) =>
        Entity?.Scene is { } scene && EntityNames.Find(scene, name, new NameLookup(Entity)) is { } entity ? RoomOf(entity) : null;

    // The rooms for the walls as they are now: the last ones, unless the walls (or what the rooms were said to be) changed.
    internal Layout Current()
    {
        (_scratchKey, _key) = (_key, _scratchKey);
        LookAtWalls(_key);
        if (_layout is { } layout && _key.SequenceEqual(_scratchKey) && layout.BasedOn.SequenceEqual(_stamps))
            return layout;
        return _layout = Find();
    }

    /// <summary>
    /// Takes the rooms as they are now as what they are (in the end-of-step check): true, raising <see cref="Changed"/>,
    /// if they're not what they were.
    /// </summary>
    internal bool Commit()
    {
        var layout = Current();
        _committed = layout;
        if (layout.Stamps.SequenceEqual(_stamps))
            return false;
        (_stamps, _nextId) = (layout.Stamps, layout.NextId);
        layout.BasedOn = _stamps;
        Changed?.Invoke(layout.Rooms);
        return true;
    }

    // Everything the rooms are found from, each wall source with what it's at: comparing it with last time's is how a
    // change is seen without finding the rooms again.
    private void LookAtWalls(List<WallKey> key)
    {
        key.Clear();
        key.Add(new WallKey(WallTiles, (SolidTiles ? 1 : 0) | (EmptyIsOutside ? 2 : 0), default, default));
        key.Add(new WallKey(WallLayers, 0, default, default));
        if (Map is not { Entity: { } mapEntity } map)
            return;
        foreach (var layer in map.Layers())
        {
            if (layer.Entity is { IsActive: true } entity)
                key.Add(new WallKey(layer.Tiles, map.SettingsVersion * 2L + (layer.Solid ? 1 : 0), entity.Transform.WorldMatrix, default));
        }

        key.Add(new WallKey(null, 0, mapEntity.Transform.WorldMatrix, default));
        var layers = ParsedWallLayers();
        if (layers.Length == 0 || mapEntity.Scene is not { } scene)
            return;
        foreach (var collider in scene.Active<Collider>())
        {
            if (IsWall(collider, layers))
                key.Add(new WallKey(collider, 0, collider.Entity!.Transform.WorldMatrix, collider.WorldBounds));
        }
    }

    // Its Wall Layers as names, read once each time they change (looking at the walls every step makes no garbage).
    private string[] ParsedWallLayers()
    {
        if (!ReferenceEquals(_wallLayers.Text, WallLayers))
            _wallLayers = (WallLayers, CollisionLayers.Parse(WallLayers));
        return _wallLayers.Names;
    }

    private static bool IsWall(Collider collider, string[] layers) =>
        !collider.IsTrigger && collider is not BoxCollider { OneWay: true } && CollisionLayers.Contains(layers, collider.Layer) &&
        collider.Entity?.GetComponent<GridMovement>() is null;

    // Which cells are walls, floor or outside, then the floor's rooms, each given the id it had (see MatchIds).
    private Layout Find()
    {
        if (Map is not { Entity: { } mapEntity } map)
            return new Layout(0, 0, [], [], [], _stamps, _nextId);
        var layers = map.Layers().Where(l => l.Entity?.IsActive == true).ToList();
        if (layers.Count == 0)
            return new Layout(0, 0, [], [], [], _stamps, _nextId);
        var (width, height) = (layers.Max(l => l.Width), layers.Max(l => l.Height));
        var kinds = new CellKind[width * height];
        var grid = GridCells.Of(map);
        var wallTiles = Tilemap.ParseTileSet(WallTiles);

        // Each layer's cell for one of the map's: the same, unless the layer's been moved off its cells.
        var places = layers.Select(l => Matrix4x4.Invert(l.Entity!.Transform.WorldMatrix, out var toLocal)
            ? (Layer: l, Same: l.Entity.Transform.WorldMatrix == mapEntity.Transform.WorldMatrix, ToLocal: toLocal)
            : (Layer: l, Same: true, ToLocal: Matrix4x4.Identity)).ToList();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (wall, any) = (false, false);
                foreach (var (layer, same, toLocal) in places)
                {
                    var (lx, ly) = same ? (x, y) : layer.CellAt(Vector2.Transform(grid.Center((x, y)), toLocal));
                    var tile = layer.GetTile(lx, ly);
                    any |= tile >= 0;
                    wall |= (SolidTiles && layer.IsBlocked(lx, ly)) || Tilemap.InSet(wallTiles, tile);
                }

                kinds[y * width + x] = wall ? CellKind.Wall : EmptyIsOutside && !any ? CellKind.Outside : CellKind.Floor;
            }
        }

        // Wall colliders, over the cells whose middles they cover.
        var wallLayers = ParsedWallLayers();
        if (wallLayers.Length > 0 && mapEntity.Scene is { } scene)
        {
            foreach (var collider in scene.Active<Collider>())
            {
                if (!IsWall(collider, wallLayers))
                    continue;
                var bounds = collider.WorldBounds;
                var (minX, minY, maxX, maxY) = (int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
                foreach (var corner in (ReadOnlySpan<Vector2>)[bounds.Min, bounds.Max, new(bounds.Min.X, bounds.Max.Y), new(bounds.Max.X, bounds.Min.Y)])
                {
                    var (cx, cy) = grid.CellAt(corner);
                    (minX, minY, maxX, maxY) = (Math.Min(minX, cx), Math.Min(minY, cy), Math.Max(maxX, cx), Math.Max(maxY, cy));
                }

                for (var y = Math.Max(0, minY - 1); y <= Math.Min(height - 1, maxY + 1); y++)
                {
                    for (var x = Math.Max(0, minX - 1); x <= Math.Min(width - 1, maxX + 1); x++)
                    {
                        if (collider.Contains(grid.Center((x, y))))
                            kinds[y * width + x] = CellKind.Wall;
                    }
                }
            }
        }

        // The rooms: floor joined side by side, flooded from the top left.
        var labels = new int[width * height];
        Array.Fill(labels, -1);
        var found = new List<(List<(int X, int Y)> Cells, bool Open)>();
        var queue = new Queue<int>();
        for (var start = 0; start < labels.Length; start++)
        {
            if (kinds[start] != CellKind.Floor || labels[start] >= 0)
                continue;
            var (cells, open) = (new List<(int X, int Y)>(), false);
            labels[start] = found.Count;
            queue.Enqueue(start);
            while (queue.TryDequeue(out var index))
            {
                var cell = (X: index % width, Y: index / width);
                cells.Add(cell);
                foreach (var (dx, dy) in grid.Neighbours(cell, diagonal: false))
                {
                    var (nx, ny) = (cell.X + dx, cell.Y + dy);
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    {
                        open = true;
                        continue;
                    }

                    var next = ny * width + nx;
                    if (kinds[next] == CellKind.Outside)
                        open = true;
                    else if (kinds[next] == CellKind.Floor && labels[next] < 0)
                    {
                        labels[next] = found.Count;
                        queue.Enqueue(next);
                    }
                }
            }

            cells.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
            found.Add((cells, open));
        }

        var (ids, nextId) = MatchIds(found.Select(f => f.Cells).ToList(), labels, width);
        var rooms = new List<WallRoom>(found.Count);
        for (var i = 0; i < found.Count; i++)
            rooms.Add(new WallRoom(ids[i], found[i].Cells, found[i].Open));
        var byId = Enumerable.Range(0, rooms.Count).OrderBy(i => rooms[i].Id).ToArray();
        var rank = new int[rooms.Count];
        for (var i = 0; i < byId.Length; i++)
            rank[byId[i]] = i;
        for (var i = 0; i < labels.Length; i++)
            labels[i] = labels[i] < 0 ? -1 : rank[labels[i]];
        var sorted = byId.Select(i => rooms[i]).ToList();
        var stamps = sorted.Select(RoomStamp.Of).ToList();
        return new Layout(width, height, labels, sorted, stamps, _stamps, nextId);
    }

    // Each room found takes the id of the room it has most of (one id to one room, the most shared first): from the rooms
    // last said, as worked out then, or, after a rewind put back what they were, by where each began (its top left cell).
    private (int[] Ids, int NextId) MatchIds(List<List<(int X, int Y)>> rooms, int[] labels, int width)
    {
        var pairs = new List<(int Shared, int Room, int Id)>();
        if (_committed is { } before && before.Stamps.SequenceEqual(_stamps))
        {
            for (var r = 0; r < rooms.Count; r++)
            {
                var shared = new Dictionary<int, int>();
                foreach (var cell in rooms[r])
                {
                    if (before.RoomAt(cell) is { } old)
                        shared[old.Id] = shared.GetValueOrDefault(old.Id) + 1;
                }

                pairs.AddRange(shared.Select(s => (s.Value, r, s.Key)));
            }
        }
        else
        {
            var height = width == 0 ? 0 : labels.Length / width;
            foreach (var stamp in _stamps)
            {
                if (stamp.X >= 0 && stamp.Y >= 0 && stamp.X < width && stamp.Y < height && labels[stamp.Y * width + stamp.X] is var r and >= 0)
                    pairs.Add((stamp.Area, r, stamp.Id));
            }
        }

        var ids = new int[rooms.Count];
        var used = new HashSet<int>();
        foreach (var (_, room, id) in pairs.OrderByDescending(p => p.Shared).ThenBy(p => p.Room).ThenBy(p => p.Id))
        {
            if (ids[room] == 0 && used.Add(id))
                ids[room] = id;
        }

        var nextId = _nextId;
        for (var r = 0; r < ids.Length; r++)
        {
            if (ids[r] == 0)
                ids[r] = nextId++;
        }

        return (ids, nextId);
    }

    private enum CellKind : byte
    {
        Floor,
        Wall,
        Outside,
    }

    // One wall source and where it is: settings (a name list and flags), a layer's tiles and settings version, or a collider.
    private readonly record struct WallKey(object? Source, long Version, Matrix4x4 World, Box Bounds);

    // A room as the rooms were last said to be: its id, where it begins (its top left cell), its area, whether it's open,
    // and a number made from all its cells, so a change of shape is seen too.
    internal readonly record struct RoomStamp(int Id, int X, int Y, int Area, bool Open, int Shape)
    {
        public static RoomStamp Of(WallRoom room)
        {
            // FNV-1a, the same on every machine (HashCode isn't), so a network game's machines agree.
            var shape = 2166136261u;
            foreach (var (x, y) in room.Cells)
                shape = ((shape ^ (uint)x) * 16777619u ^ (uint)y) * 16777619u;
            return new RoomStamp(room.Id, room.Cells[0].X, room.Cells[0].Y, room.Area, room.Open, (int)shape);
        }
    }

    // The rooms for one look at the walls: which room each cell is in (an index into Rooms, or -1), and the stamps they
    // were matched to (BasedOn) and make (Stamps).
    internal sealed class Layout(int width, int height, int[] labels, List<WallRoom> rooms, List<RoomStamp> stamps, List<RoomStamp> basedOn, int nextId)
    {
        public List<WallRoom> Rooms { get; } = rooms;
        public List<RoomStamp> Stamps { get; } = stamps;
        public List<RoomStamp> BasedOn { get; set; } = basedOn;
        public int NextId { get; } = nextId;

        public WallRoom? RoomAt((int X, int Y) cell) =>
            cell.X >= 0 && cell.Y >= 0 && cell.X < width && cell.Y < height && labels[cell.Y * width + cell.X] is var i and >= 0 ? Rooms[i] : null;
    }
}

/// <summary>
/// One room a <see cref="WallRooms"/> found: an id it keeps as walls change, its cells (top row first, left to right), its
/// area and whether it's open to the outside. A new one is made each time the rooms change, so keep its id rather than it.
/// </summary>
public sealed class WallRoom
{
    internal WallRoom(int id, IReadOnlyList<(int X, int Y)> cells, bool open) => (Id, Cells, Open) = (id, cells, open);

    /// <summary>Its number, from 1, kept as walls go up and come down (see <see cref="WallRooms"/>).</summary>
    public int Id { get; }

    /// <summary>Its cells of the map, top row first, left to right.</summary>
    public IReadOnlyList<(int X, int Y)> Cells { get; }

    /// <summary>How many cells it has.</summary>
    public int Area => Cells.Count;

    /// <summary>True if it reaches the outside: past the map's edge, or an empty cell with <see cref="WallRooms.EmptyIsOutside"/>.</summary>
    public bool Open { get; }

    /// <summary>True if walls close it off all round: not <see cref="Open"/>.</summary>
    public bool Closed => !Open;

    public override string ToString() => $"Room {Id}: {Area} cells{(Open ? ", open" : "")}";
}
