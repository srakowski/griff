using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// Where a body's move through a tilemap's walls stops (see <see cref="TileWalls.KeepOff"/>): the middle of its collider
/// there, its velocity after meeting them, whether it landed on them and which side (-1 or 1) a wall stopped it on, if any.
/// </summary>
internal readonly record struct WallMove(Vector2 At, Vector2 Velocity, bool Grounded, int WallSide);

/// <summary>
/// The walls a solid tilemap's cells make for bodies where they aren't square boxes (see <see cref="Tilemap.Walls"/>): an
/// isometric map's diamonds, a hex map's hexagons. Each grid shape has a kind of its own, which works out from the map's
/// cells how a moving body is kept off them and what rays, overlaps and turning shapes meet, so physics asks every shape
/// the same questions and a new grid shape is one more of these. Made for one step, as the map is placed then.
/// </summary>
internal abstract class TileWalls
{
    protected TileWalls(Tilemap map)
    {
        Map = map;
        Width = map.Width;
        Height = map.Height;
    }

    public Tilemap Map { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The box round the whole map in the world: nothing beyond it meets the walls.</summary>
    public abstract Box Bounds { get; }

    public bool Blocked(int x, int y) => Map.IsBlocked(x, y);

    /// <summary>Whether a point in the world is inside a solid cell.</summary>
    public abstract bool BlockedAt(Vector2 world);

    public abstract bool Overlaps(Box box);

    public abstract bool Overlaps(Circle circle);

    /// <summary>
    /// Where a ray (a unit <paramref name="direction"/>) first goes into a solid cell within <paramref name="maxDistance"/>,
    /// and the side's normal in the world; cells it starts in are passed through.
    /// </summary>
    public abstract (float Distance, Vector2 Normal)? Raycast(Vector2 origin, Vector2 direction, float maxDistance);

    /// <summary>
    /// Keeps a body's collider (half its size <paramref name="half"/>, a circle of <paramref name="radius"/> if it's round)
    /// off the walls as its middle goes from <paramref name="from"/> to <paramref name="to"/>: where it gets to and how it
    /// meets them, its velocity stopped (or bounced back, by <paramref name="bounciness"/>, settling below
    /// <paramref name="settleSpeed"/>) into them and kept along them. Null when it doesn't meet them.
    /// </summary>
    public abstract WallMove? KeepOff(Vector2 from, Vector2 to, Vector2 half, float? radius, Vector2 velocity, float bounciness,
        float settleSpeed);

    /// <summary>
    /// Whether bodies meet the walls with their footprints (see <see cref="Footprint"/>) rather than their own shapes: an
    /// isometric map's walls are on the ground, which only what stands on it meets. A rope's points have no footprint, so
    /// they pass over such walls.
    /// </summary>
    public virtual bool MetByFootprints => false;

    /// <summary>
    /// What a body whose collider's middle is at <paramref name="center"/>, <paramref name="halfWidth"/> half as wide, meets
    /// the walls with when they're met by footprints (see <see cref="MetByFootprints"/>): on an isometric map, the diamond
    /// under it, as wide as it. Its corners go into <paramref name="corners"/>; how many there are.
    /// </summary>
    public virtual int Footprint(Vector2 center, float halfWidth, Span<Vector2> corners) =>
        throw new NotSupportedException("These walls are met by bodies' own shapes.");

    /// <summary>The columns and rows (clamped to the map) whose cells could touch a world box.</summary>
    public abstract (int X0, int Y0, int X1, int Y1) CellsNear(Box box);

    /// <summary>Cell x, y's corners in the world, into <paramref name="corners"/>; how many there are.</summary>
    public abstract int Cell(int x, int y, Span<Vector2> corners);

    /// <summary>
    /// True if the side of cell x, y facing <paramref name="normal"/> (out of it) is shared with another solid cell: inside
    /// the wall, not its surface.
    /// </summary>
    public virtual bool SharedSide(int x, int y, Vector2 normal) => false;

    // The box round two boxes.
    protected static Box Union(Box a, Box b)
    {
        var min = Vector2.Min(a.Min, b.Min);
        var max = Vector2.Max(a.Max, b.Max);
        return new Box((min + max) / 2, (max - min) / 2);
    }

    // A convex polygon against a box, by separating axes: the box's two and the polygon's edges' normals.
    protected static bool PolygonOverlapsBox(Vector2[] polygon, Box box)
    {
        Span<Vector2> corners = [box.Min, new Vector2(box.Max.X, box.Min.Y), box.Max, new Vector2(box.Min.X, box.Max.Y)];
        if (Separated(polygon, corners, Vector2.UnitX) || Separated(polygon, corners, Vector2.UnitY))
            return false;
        for (var i = 0; i < polygon.Length; i++)
        {
            var edge = polygon[(i + 1) % polygon.Length] - polygon[i];
            if (Separated(polygon, corners, new Vector2(-edge.Y, edge.X)))
                return false;
        }

        return true;
    }

    private static bool Separated(Vector2[] a, ReadOnlySpan<Vector2> b, Vector2 axis)
    {
        var (minA, maxA) = (float.PositiveInfinity, float.NegativeInfinity);
        foreach (var p in a)
            (minA, maxA) = (Math.Min(minA, Vector2.Dot(p, axis)), Math.Max(maxA, Vector2.Dot(p, axis)));
        var (minB, maxB) = (float.PositiveInfinity, float.NegativeInfinity);
        foreach (var p in b)
            (minB, maxB) = (Math.Min(minB, Vector2.Dot(p, axis)), Math.Max(maxB, Vector2.Dot(p, axis)));
        return maxA <= minB || maxB <= minA;
    }

    // A convex polygon (corners in order) against a circle: its middle inside, or an edge within its radius.
    protected static bool PolygonOverlapsCircle(Vector2[] polygon, Circle circle)
    {
        var inside = true;
        var sign = 0f;
        for (var i = 0; i < polygon.Length; i++)
        {
            var (a, b) = (polygon[i], polygon[(i + 1) % polygon.Length]);
            var edge = b - a;
            var cross = edge.X * (circle.Center.Y - a.Y) - edge.Y * (circle.Center.X - a.X);
            if (sign == 0)
                sign = MathF.Sign(cross);
            else if (MathF.Sign(cross) != sign && cross != 0)
                inside = false;

            var t = Math.Clamp(Vector2.Dot(circle.Center - a, edge) / Math.Max(1e-12f, edge.LengthSquared()), 0, 1);
            if (Vector2.DistanceSquared(a + edge * t, circle.Center) < circle.Radius * circle.Radius)
                return true;
        }

        return inside;
    }
}
