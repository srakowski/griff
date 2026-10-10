using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Rendering;

/// <summary>
/// One side of something that blocks light, in the world. Its outside is on the left going from <see cref="A"/> to
/// <see cref="B"/>, so the sides of a shape go round it clockwise. <see cref="Owner"/> is the shape it belongs to: an
/// entity, or a tile cell.
/// </summary>
public readonly record struct ShadowEdge(Vector2 A, Vector2 B, object? Owner = null)
{
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
/// What blocks the light of a <see cref="Light"/> with <see cref="Light.Shadows"/> on: solid tiles (slopes included) and
/// solid colliders. Triggers, one-way platforms, background, ladder and one-way tiles let light through.
/// </summary>
public static class Shadows
{
    // Circles block light as a polygon with this many sides.
    private const int CircleSides = 12;

    /// <summary>Every side of everything in the scene that blocks light.</summary>
    public static List<ShadowEdge> Edges(Scene scene)
    {
        var edges = new List<ShadowEdge>();
        foreach (var entity in scene.Entities.Where(SceneRenderer.IsActive))
        {
            if (entity.GetComponent<Tilemap>() is { Solid: true } tilemap)
                AddTiles(edges, tilemap);
            foreach (var collider in entity.GetComponents<Collider>())
            {
                if (collider.IsTrigger || collider is BoxCollider { OneWay: true })
                    continue;
                if (collider is BoxCollider box)
                {
                    var bounds = Physics.BoundsOf(box);
                    AddBox(edges, bounds.Min, bounds.Max, entity);
                }
                else if (collider is CircleCollider circle)
                {
                    AddCircle(edges, Physics.BoundsOf(circle), entity);
                }
            }
        }

        return edges;
    }

    /// <summary>
    /// The sides that cast <paramref name="light"/>'s shadows: those within its reach facing away from it, leaving out the
    /// light's own entity (a lantern's holder) and anything the light is inside (a torch set into a wall).
    /// </summary>
    public static List<ShadowEdge> CastersFor(IReadOnlyList<ShadowEdge> edges, Light light)
    {
        var center = light.WorldCenter;
        var owner = light.Entity;
        var radius = light.Radius;
        var casters = new List<ShadowEdge>();
        var inside = new Dictionary<object, bool>();
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (!edge.FacesAwayFrom(center) || DistanceTo(edge, center) >= radius)
                continue;
            if (edge.Owner is { } shape)
            {
                if (!inside.TryGetValue(shape, out var isInside))
                    inside[shape] = isInside = IsOwnedBy(shape, owner) || Contains(shape, center);
                if (isInside)
                    continue;
            }

            casters.Add(edge);
        }

        return casters;
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

    // Whether a shape that blocks light (an entity's colliders, or a tile cell) has the point inside it.
    private static bool Contains(object shape, Vector2 point)
    {
        if (shape is Entity entity)
        {
            return entity.GetComponents<Collider>().Any(c => !c.IsTrigger && c switch
            {
                BoxCollider box => Physics.BoundsOf(box).Contains(point),
                CircleCollider circle => Physics.BoundsOf(circle).Contains(point),
                _ => false,
            });
        }

        if (shape is TileCell(var tilemap, var x, var y) && Matrix4x4.Invert(tilemap.Entity!.Transform.WorldMatrix, out var toLocal))
        {
            var local = Vector2.Transform(point, toLocal);
            return (int)MathF.Floor(local.X / tilemap.TileSize) == x && (int)MathF.Floor(-local.Y / tilemap.TileSize) == y;
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

    // The owner of a tile's sides.
    private sealed record TileCell(Tilemap Tilemap, int X, int Y);

    private static void Add(List<ShadowEdge> edges, Vector2 a, Vector2 b, object owner) => edges.Add(new ShadowEdge(a, b, owner));

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

    // Each solid tile's sides that face open space, and each slope's sloping side (with its flat sides where open).
    private static void AddTiles(List<ShadowEdge> edges, Tilemap tilemap)
    {
        var world = tilemap.Entity!.Transform.WorldMatrix;
        var half = tilemap.TileSize / 2;
        foreach (var (x, y, tile) in tilemap.Cells())
        {
            var slope = tilemap.SlopeOf(tile);
            if (!tilemap.IsBlocked(x, y) && slope == 0)
                continue;
            var center = tilemap.CellCenter(x, y);
            var bottomLeft = Vector2.Transform(center + new Vector2(-half, -half), world);
            var topLeft = Vector2.Transform(center + new Vector2(-half, half), world);
            var topRight = Vector2.Transform(center + new Vector2(half, half), world);
            var bottomRight = Vector2.Transform(center + new Vector2(half, -half), world);
            var cell = new TileCell(tilemap, x, y);

            // Rows count down the screen: y - 1 is the cell above.
            var openLeft = !tilemap.IsBlocked(x - 1, y);
            var openAbove = !tilemap.IsBlocked(x, y - 1);
            var openRight = !tilemap.IsBlocked(x + 1, y);
            var openBelow = !tilemap.IsBlocked(x, y + 1);
            if (slope > 0)
            {
                // Rising: solid below the line from bottom left to top right.
                Add(edges, bottomLeft, topRight, cell);
                if (openRight)
                    Add(edges, topRight, bottomRight, cell);
                if (openBelow)
                    Add(edges, bottomRight, bottomLeft, cell);
                continue;
            }

            if (slope < 0)
            {
                // Falling: solid below the line from top left to bottom right.
                if (openLeft)
                    Add(edges, bottomLeft, topLeft, cell);
                Add(edges, topLeft, bottomRight, cell);
                if (openBelow)
                    Add(edges, bottomRight, bottomLeft, cell);
                continue;
            }

            if (openLeft)
                Add(edges, bottomLeft, topLeft, cell);
            if (openAbove)
                Add(edges, topLeft, topRight, cell);
            if (openRight)
                Add(edges, topRight, bottomRight, cell);
            if (openBelow)
                Add(edges, bottomRight, bottomLeft, cell);
        }
    }
}
