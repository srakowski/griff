using System.Numerics;
using System.Runtime.InteropServices;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// One side of something that blocks light, in the world. Its outside is on the left going from <see cref="A"/> to
/// <see cref="B"/>, so the sides of a shape go round it clockwise. <see cref="Owner"/> is what it belongs to: an entity
/// (its colliders), or a <see cref="Tilemap"/> (the tile at <see cref="Cell"/>).
/// </summary>
public readonly record struct ShadowEdge(Vector2 A, Vector2 B, object? Owner = null)
{
    /// <summary>For a side of a tile, the tile's cell (column, row) in the <see cref="Tilemap"/> that's its <see cref="Owner"/>.</summary>
    public (int X, int Y) Cell { get; init; }

    // Whether this side and `other` belong to the same shape: an entity, or one tile.
    internal bool SameShape(in ShadowEdge other) => ReferenceEquals(Owner, other.Owner) && Cell == other.Cell;

    /// <summary>Which way the side faces: out of the shape it belongs to.</summary>
    public Vector2 Outward => Vector2.Normalize(new Vector2(A.Y - B.Y, B.X - A.X));

    /// <summary>
    /// True if the side faces away from <paramref name="light"/>. Shadows start at these, so whatever casts them is lit on
    /// the side facing the light, and dark behind.
    /// </summary>
    public bool FacesAwayFrom(Vector2 light) => Vector2.Dot(new Vector2(A.Y - B.Y, B.X - A.X), (A + B) / 2 - light) > 0;

    /// <summary>True if the straight line from <paramref name="from"/> to <paramref name="to"/> crosses this side.</summary>
    public bool Crosses(Vector2 from, Vector2 to)
    {
        var r = to - from;
        var s = B - A;
        var denominator = Cross(r, s);
        if (MathF.Abs(denominator) < 1e-9f)
            return false;
        var t = Cross(A - from, s) / denominator;
        var u = Cross(A - from, r) / denominator;
        return t is > 1e-4f and < 1 - 1e-4f && u is >= 0 and <= 1;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}

/// <summary>
/// What blocks the light of a <see cref="Light"/> with <see cref="Light.Shadows"/> on: solid tiles (slopes included, and
/// hex maps' hexagons) and solid colliders (boxes, circles and polygons, as they're turned). Triggers, one-way platforms, background, ladder and one-way tiles let light through.
/// </summary>
public static class Shadows
{
    // Circles block light as a polygon with this many sides.
    private const int CircleSides = 12;

    /// <summary>Every side of everything in the scene that blocks light.</summary>
    public static List<ShadowEdge> Edges(Scene scene)
    {
        var edges = new List<ShadowEdge>();
        AddEdges(scene, edges);
        return edges;
    }

    /// <summary>
    /// Adds every side of everything in the scene that blocks light to <paramref name="edges"/>: the solid tilemaps' tiles,
    /// then the colliders, each in hierarchy order. Once <paramref name="edges"/> has room for them it makes no garbage, so
    /// a dark scene's frame can work them out afresh each time.
    /// </summary>
    public static void AddEdges(Scene scene, List<ShadowEdge> edges)
    {
        foreach (var tilemap in scene.ActiveNow<Tilemap>())
        {
            // Isometric maps don't block light yet.
            if (!tilemap.Solid)
                continue;
            if (tilemap.Shape == TileShape.Square)
                AddTiles(edges, tilemap);
            else if (tilemap.Shape == TileShape.Hex)
                AddHexTiles(edges, tilemap);
        }

        foreach (var collider in scene.ActiveNow<Collider>())
        {
            if (collider.IsTrigger || collider is BoxCollider { OneWay: true })
                continue;
            var entity = collider.Entity!;
            if (collider is BoxCollider box)
            {
                // Clockwise round it, as it's turned.
                var (a, b, c, d) = box.WorldCorners;
                Add(edges, a, d, entity);
                Add(edges, d, c, entity);
                Add(edges, c, b, entity);
                Add(edges, b, a, entity);
            }
            else if (collider is CircleCollider circle)
            {
                AddCircle(edges, Physics.BoundsOf(circle), entity);
            }
            else if (collider is PolygonCollider polygon)
            {
                // Clockwise round it: its corners go round anticlockwise, so each side runs from a corner back to the one before.
                var corners = polygon.WorldPoints;
                for (var i = 0; i < corners.Count; i++)
                    Add(edges, corners[i], corners[(i + corners.Count - 1) % corners.Count], entity);
            }
        }
    }

    /// <summary>
    /// The sides that cast <paramref name="light"/>'s shadows: those within its reach facing away from it, leaving out the
    /// light's own entity (a lantern's holder) and anything the light is inside (a torch set into a wall).
    /// </summary>
    public static List<ShadowEdge> CastersFor(IReadOnlyList<ShadowEdge> edges, Light light)
    {
        var casters = new List<ShadowEdge>();
        AddCasters(edges is List<ShadowEdge> list ? CollectionsMarshal.AsSpan(list) : edges.ToArray(), light, casters);
        return casters;
    }

    /// <summary>
    /// Adds the sides of <paramref name="edges"/> that cast <paramref name="light"/>'s shadows to
    /// <paramref name="casters"/>, as <see cref="CastersFor"/> picks them, making no garbage once it has room for them.
    /// </summary>
    public static void AddCasters(ReadOnlySpan<ShadowEdge> edges, Light light, List<ShadowEdge> casters)
    {
        var center = light.WorldCenter;
        var owner = light.Entity;
        var radius = light.Radius;
        // A shape's sides come together: whether the light is in it (or carried by it) is worked out once for them all.
        var shape = -1;
        var inside = false;
        for (var i = 0; i < edges.Length; i++)
        {
            ref readonly var edge = ref edges[i];
            if (!edge.FacesAwayFrom(center) || DistanceTo(edge, center) >= radius)
                continue;
            if (edge.Owner is not null)
            {
                if (shape < 0 || !edges[shape].SameShape(edge))
                    (shape, inside) = (i, IsOwnedBy(edge.Owner, owner) || Contains(edge, center));
                if (inside)
                    continue;
            }

            casters.Add(edge);
        }
    }

    /// <summary>True if nothing in <paramref name="casters"/> stands between <paramref name="light"/> and <paramref name="point"/>.</summary>
    public static bool Reaches(IReadOnlyList<ShadowEdge> casters, Vector2 light, Vector2 point)
    {
        foreach (var edge in casters)
        {
            if (edge.Crosses(light, point))
                return false;
        }

        return true;
    }

    /// <summary>
    /// The shadow <paramref name="edge"/> casts from <paramref name="light"/> as a polygon, in order round it: the side's
    /// two ends, then points <paramref name="reach"/> beyond its far end, its middle and its near end. The middle point
    /// keeps the far side from cutting back towards a light right next to a long side; <paramref name="reach"/> twice the
    /// light's radius always covers the rest of the light.
    /// </summary>
    public static (Vector2 A, Vector2 B, Vector2 FarB, Vector2 FarMiddle, Vector2 FarA) Shadow(ShadowEdge edge, Vector2 light, float reach)
    {
        var a = edge.A;
        var b = edge.B;
        var middle = (a + b) / 2;
        return (a, b, b + Away(b) * reach, middle + Away(middle) * (reach + Vector2.Distance(middle, light)), a + Away(a) * reach);

        Vector2 Away(Vector2 p) => p - light is var d && d.LengthSquared() > 1e-12f ? Vector2.Normalize(d) : Vector2.Zero;
    }

    // Whether the shape an edge belongs to (an entity's colliders, or a tile) has the point inside it.
    private static bool Contains(in ShadowEdge edge, Vector2 point)
    {
        if (edge.Owner is Entity entity)
        {
            foreach (var component in entity.ComponentList)
            {
                if (component is not Collider { IsTrigger: false } collider)
                    continue;
                if (collider.Contains(point))
                    return true;
            }

            return false;
        }

        if (edge.Owner is Tilemap tilemap && Matrix4x4.Invert(tilemap.Entity!.Transform.WorldMatrix, out var toLocal))
        {
            var local = Vector2.Transform(point, toLocal);
            return tilemap.CellAt(local) == edge.Cell;
        }

        return false;
    }

    private static bool IsOwnedBy(object shape, Entity? owner) =>
        owner is not null && shape is Entity entity && (entity.IsSelfOrDescendantOf(owner) || owner.IsSelfOrDescendantOf(entity));

    private static float DistanceTo(ShadowEdge edge, Vector2 point)
    {
        var ab = edge.B - edge.A;
        var length = ab.LengthSquared();
        var t = length > 0 ? Math.Clamp(Vector2.Dot(point - edge.A, ab) / length, 0, 1) : 0;
        return Vector2.Distance(point, edge.A + ab * t);
    }

    private static void Add(List<ShadowEdge> edges, Vector2 a, Vector2 b, object owner) => edges.Add(new ShadowEdge(a, b, owner));

    // A side of a tile: the map owns it, with the tile's cell.
    private static void Add(List<ShadowEdge> edges, Vector2 a, Vector2 b, Tilemap tilemap, (int X, int Y) cell) =>
        edges.Add(new ShadowEdge(a, b, tilemap) { Cell = cell });

    // Clockwise: up the left side, along the top, down the right, back along the bottom.
    private static void AddBox(List<ShadowEdge> edges, Vector2 min, Vector2 max, object owner)
    {
        var topLeft = new Vector2(min.X, max.Y);
        var bottomRight = new Vector2(max.X, min.Y);
        Add(edges, min, topLeft, owner);
        Add(edges, topLeft, max, owner);
        Add(edges, max, bottomRight, owner);
        Add(edges, bottomRight, min, owner);
    }

    private static void AddCircle(List<ShadowEdge> edges, Circle circle, object owner)
    {
        for (var i = 0; i < CircleSides; i++)
        {
            // Clockwise means decreasing angle.
            var from = -MathF.Tau * i / CircleSides;
            var to = -MathF.Tau * (i + 1) / CircleSides;
            Add(edges, circle.Center + new Vector2(MathF.Cos(from), MathF.Sin(from)) * circle.Radius,
                circle.Center + new Vector2(MathF.Cos(to), MathF.Sin(to)) * circle.Radius, owner);
        }
    }

    // Each solid hexagon's sides that face open space. A cell's outline runs clockwise from its top left corner, and its
    // side from corner i to corner i + 1 is the one it shares with neighbor i.
    private static void AddHexTiles(List<ShadowEdge> edges, Tilemap tilemap)
    {
        var world = tilemap.Entity!.Transform.WorldMatrix;
        var (width, height) = (tilemap.Width, tilemap.Height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!tilemap.IsBlocked(x, y))
                    continue;
                for (var side = 0; side < 6; side++)
                {
                    var (nx, ny) = Tilemap.NeighborOf(x, y, side);
                    if (!tilemap.IsBlocked(nx, ny))
                        Add(edges, Vector2.Transform(tilemap.HexCorner(x, y, side), world), Vector2.Transform(tilemap.HexCorner(x, y, (side + 1) % 6), world), tilemap, (x, y));
                }
            }
        }
    }

    // Each solid tile's sides that face open space, and each slope's sloping side (with its flat sides where open).
    private static void AddTiles(List<ShadowEdge> edges, Tilemap tilemap)
    {
        var world = tilemap.Entity!.Transform.WorldMatrix;
        var half = tilemap.TileSize / 2;
        var (width, height) = (tilemap.Width, tilemap.Height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                AddTile(edges, tilemap, world, half, x, y);
        }
    }

    // One cell's sides, as AddTiles says.
    private static void AddTile(List<ShadowEdge> edges, Tilemap tilemap, in Matrix4x4 world, float half, int x, int y)
    {
        if (tilemap.GetTile(x, y) < 0)
            return;
        var slope = tilemap.SlopeAt(x, y);
        if (!tilemap.IsBlocked(x, y) && slope == 0)
            return;
        var center = tilemap.CellCenter(x, y);
        var bottomLeft = Vector2.Transform(center + new Vector2(-half, -half), world);
        var topLeft = Vector2.Transform(center + new Vector2(-half, half), world);
        var topRight = Vector2.Transform(center + new Vector2(half, half), world);
        var bottomRight = Vector2.Transform(center + new Vector2(half, -half), world);
        var cell = (x, y);

        // Rows count down the screen: y - 1 is the cell above.
        var openLeft = !tilemap.IsBlocked(x - 1, y);
        var openAbove = !tilemap.IsBlocked(x, y - 1);
        var openRight = !tilemap.IsBlocked(x + 1, y);
        var openBelow = !tilemap.IsBlocked(x, y + 1);
        if (slope > 0)
        {
            // Rising: solid below the line from bottom left to top right.
            Add(edges, bottomLeft, topRight, tilemap, cell);
            if (openRight)
                Add(edges, topRight, bottomRight, tilemap, cell);
            if (openBelow)
                Add(edges, bottomRight, bottomLeft, tilemap, cell);
            return;
        }

        if (slope < 0)
        {
            // Falling: solid below the line from top left to bottom right.
            if (openLeft)
                Add(edges, bottomLeft, topLeft, tilemap, cell);
            Add(edges, topLeft, bottomRight, tilemap, cell);
            if (openBelow)
                Add(edges, bottomRight, bottomLeft, tilemap, cell);
            return;
        }

        if (openLeft)
            Add(edges, bottomLeft, topLeft, tilemap, cell);
        if (openAbove)
            Add(edges, topLeft, topRight, tilemap, cell);
        if (openRight)
            Add(edges, topRight, bottomRight, tilemap, cell);
        if (openBelow)
            Add(edges, bottomRight, bottomLeft, tilemap, cell);
    }
}
