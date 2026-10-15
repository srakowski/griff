using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// Polygon colliders as everything that isn't a body that turns meets them: overlaps (for moving the old way, triggers and
// queries), how far apart two shapes are along an axis when they just touch (for pushing a body moved the old way back out
// of one), and rays. A polygon is the convex pieces it's made of: two shapes overlap where any of their pieces do.
public sealed partial class Physics
{
    private readonly partial record struct Area
    {
        public bool IsPolygon => Polygon is not null;

        // A box, turned or not, a circle or a polygon's pieces as convex shapes: how many there are.
        private int PieceCount => Polygon?.PieceCount ?? 1;

        // Piece `piece`'s corners, anticlockwise: one of a polygon's pieces, or a box's four corners (into `room`); none for
        // a circle.
        private ReadOnlySpan<Vector2> Corners(int piece, Span<Vector2> room)
        {
            if (Polygon is { } polygon)
                return polygon.WorldPiece(piece);
            if (IsCircle)
                return default;
            var (half, axis) = IsTurned ? (Vector2.Abs(Half), Axis) : (Vector2.Abs(Bounds.HalfSize), Vector2.UnitX);
            var (x, y) = (axis * half.X, new Vector2(-axis.Y, axis.X) * half.Y);
            room[0] = Center - x - y;
            room[1] = Center + x - y;
            room[2] = Center + x + y;
            room[3] = Center - x + y;
            return room[..4];
        }

        // With a polygon: any of their pieces overlapping by more than the skin.
        private bool PolygonOverlaps(Area other, float skin)
        {
            Span<Vector2> roomA = stackalloc Vector2[4];
            Span<Vector2> roomB = stackalloc Vector2[4];
            for (var i = 0; i < PieceCount; i++)
            {
                var a = Corners(i, roomA);
                for (var j = 0; j < other.PieceCount; j++)
                {
                    var b = other.Corners(j, roomB);
                    var overlaps = a.Length == 0 ? CircleInConvex(b, Circle, skin)
                        : b.Length == 0 ? CircleInConvex(a, other.Circle, skin)
                        : ConvexOverlap(a, b, skin);
                    if (overlaps)
                        return true;
                }
            }

            return false;
        }

        // Two convex shapes overlap by more than `skin` along every side's direction of either.
        private static bool ConvexOverlap(ReadOnlySpan<Vector2> a, ReadOnlySpan<Vector2> b, float skin) =>
            !SeparatedBy(a, a, b, skin) && !SeparatedBy(b, a, b, skin);

        private static bool SeparatedBy(ReadOnlySpan<Vector2> sides, ReadOnlySpan<Vector2> a, ReadOnlySpan<Vector2> b, float skin)
        {
            for (var i = 0; i < sides.Length; i++)
            {
                if (OutwardNormal(sides, i) is not { } normal)
                    continue;
                var (aMin, aMax) = Project(a, normal);
                var (bMin, bMax) = Project(b, normal);
                if (aMax - bMin <= skin || bMax - aMin <= skin)
                    return true;
            }

            return false;
        }

        // A circle overlapping a convex shape by more than `skin`: its middle inside it, or nearer its edge than its radius.
        private static bool CircleInConvex(ReadOnlySpan<Vector2> convex, Circle circle, float skin) =>
            Inside(convex, circle.Center) || Vector2.Distance(Nearest(convex, circle.Center, out _), circle.Center) < circle.Radius - skin;

        // A polygon's along an axis: the stretch of offsets (this one's middle less the other's) over which any two of their
        // pieces overlap, keeping the offset across it they have now. Where some pieces overlap now, theirs; else all.
        private (float Low, float High)? PolygonChord(Area other, int axis)
        {
            var along = axis == 0 ? Vector2.UnitX : Vector2.UnitY;
            var now = Component(Center - other.Center, axis);
            var (lowNow, highNow, lowAny, highAny) = (float.PositiveInfinity, float.NegativeInfinity, float.PositiveInfinity, float.NegativeInfinity);
            Span<Vector2> roomA = stackalloc Vector2[4];
            Span<Vector2> roomB = stackalloc Vector2[4];
            for (var i = 0; i < PieceCount; i++)
            {
                var a = Corners(i, roomA);
                for (var j = 0; j < other.PieceCount; j++)
                {
                    var b = other.Corners(j, roomB);
                    var (low, high) = a.Length == 0 ? CircleChord(Circle, b, along)
                        : b.Length == 0 ? CircleChord(other.Circle, a, -along)
                        : ConvexChord(a, b, along);
                    if (!(low < high))
                        continue;
                    (lowAny, highAny) = (MathF.Min(lowAny, low), MathF.Max(highAny, high));
                    if (low < 0 && high > 0)
                        (lowNow, highNow) = (MathF.Min(lowNow, low), MathF.Max(highNow, high));
                }
            }

            if (lowNow < highNow)
                return (now + lowNow, now + highNow);
            return lowAny < highAny ? (now + lowAny, now + highAny) : null;
        }

        // How far convex shape a can move along `along` (either way) and still overlap b: the stretch of how far, from where
        // it is now, by every side's direction of either.
        private static (float Low, float High) ConvexChord(ReadOnlySpan<Vector2> a, ReadOnlySpan<Vector2> b, Vector2 along)
        {
            var (low, high) = (float.NegativeInfinity, float.PositiveInfinity);
            Narrow(a, a, b, along, ref low, ref high);
            Narrow(b, a, b, along, ref low, ref high);
            return (low, high);
        }

        private static void Narrow(ReadOnlySpan<Vector2> sides, ReadOnlySpan<Vector2> a, ReadOnlySpan<Vector2> b, Vector2 along, ref float low, ref float high)
        {
            for (var i = 0; i < sides.Length; i++)
            {
                if (OutwardNormal(sides, i) is not { } normal)
                    continue;
                var (aMin, aMax) = Project(a, normal);
                var (bMin, bMax) = Project(b, normal);
                var facing = Vector2.Dot(along, normal);
                if (MathF.Abs(facing) < 1e-6f)
                {
                    // Moving along this side's direction doesn't change whether they overlap across it.
                    if (aMax <= bMin || bMax <= aMin)
                        (low, high) = (0, 0);
                    continue;
                }

                var (t1, t2) = ((bMin - aMax) / facing, (bMax - aMin) / facing);
                if (t1 > t2)
                    (t1, t2) = (t2, t1);
                (low, high) = (MathF.Max(low, t1), MathF.Min(high, t2));
            }
        }

        // How far a circle can move along `along` (either way) and still overlap a convex shape: within its radius of every
        // side's line, then, where that leaves it past a corner, within its radius of the corner.
        private static (float Low, float High) CircleChord(Circle circle, ReadOnlySpan<Vector2> convex, Vector2 along)
        {
            var (center, radius) = (circle.Center, circle.Radius);
            var (low, high) = (float.NegativeInfinity, float.PositiveInfinity);
            for (var i = 0; i < convex.Length; i++)
            {
                if (OutwardNormal(convex, i) is not { } normal)
                    continue;
                var facing = Vector2.Dot(normal, along);
                var out_ = Vector2.Dot(normal, center - convex[i]);
                if (MathF.Abs(facing) < 1e-9f)
                {
                    if (out_ >= radius)
                        return (0, 0);
                    continue;
                }

                var bound = (radius - out_) / facing;
                if (facing > 0)
                    high = MathF.Min(high, bound);
                else
                    low = MathF.Max(low, bound);
            }

            if (!(low < high))
                return (low, high);
            return (Rounded(convex, circle, along, low, far: false), Rounded(convex, circle, along, high, far: true));
        }

        // An end of a circle's stretch along a convex shape's sides: where it's out past a corner, on that corner's rounding.
        private static float Rounded(ReadOnlySpan<Vector2> convex, Circle circle, Vector2 along, float t, bool far)
        {
            var at = circle.Center + along * t;
            var nearest = Nearest(convex, at, out var corner);
            if (!corner || Vector2.Distance(nearest, at) <= circle.Radius)
                return t;
            var m = circle.Center - nearest;
            var b = Vector2.Dot(m, along);
            var discriminant = b * b - (m.LengthSquared() - circle.Radius * circle.Radius);
            if (discriminant < 0)
                return t;
            var root = MathF.Sqrt(discriminant);
            return far ? -b + root : -b - root;
        }

        // A polygon hit by a ray: where it first goes into one of its pieces, unless it starts inside it.
        private static (float Distance, Vector2 Normal)? IntersectPolygon(PolygonCollider polygon, Vector2 origin, Vector2 direction)
        {
            if (polygon.Contains(origin))
                return null;
            (float Distance, Vector2 Normal)? best = null;
            for (var piece = 0; piece < polygon.PieceCount; piece++)
            {
                var corners = polygon.WorldPiece(piece);
                var (enter, exit, normal) = (float.NegativeInfinity, float.PositiveInfinity, Vector2.Zero);
                var missed = false;
                for (var i = 0; i < corners.Length && !missed; i++)
                {
                    if (OutwardNormal(corners, i) is not { } side)
                        continue;
                    var towards = Vector2.Dot(side, direction);
                    var inside = Vector2.Dot(side, corners[i] - origin);
                    if (MathF.Abs(towards) < 1e-12f)
                    {
                        missed = inside < 0;
                        continue;
                    }

                    var t = inside / towards;
                    if (towards < 0 && t > enter)
                        (enter, normal) = (t, side);
                    else if (towards > 0)
                        exit = MathF.Min(exit, t);
                }

                if (missed || enter > exit || enter < 0 || normal == Vector2.Zero)
                    continue;
                if (best is null || enter < best.Value.Distance)
                    best = (enter, normal);
            }

            return best;
        }

        // The outward normal of side i of an anticlockwise convex shape (null for a side of no length).
        private static Vector2? OutwardNormal(ReadOnlySpan<Vector2> corners, int i)
        {
            var side = corners[(i + 1) % corners.Length] - corners[i];
            var length = side.Length();
            return length > 1e-12f ? new Vector2(side.Y, -side.X) / length : null;
        }

        private static (float Min, float Max) Project(ReadOnlySpan<Vector2> corners, Vector2 normal)
        {
            var (min, max) = (float.PositiveInfinity, float.NegativeInfinity);
            foreach (var corner in corners)
            {
                var at = Vector2.Dot(corner, normal);
                (min, max) = (MathF.Min(min, at), MathF.Max(max, at));
            }

            return (min, max);
        }

        private static bool Inside(ReadOnlySpan<Vector2> corners, Vector2 point)
        {
            for (var i = 0; i < corners.Length; i++)
            {
                if (Cross(corners[(i + 1) % corners.Length] - corners[i], point - corners[i]) < 0)
                    return false;
            }

            return corners.Length > 0;
        }

        // The nearest point of a convex shape's edge to `point`, and whether it's one of its corners.
        private static Vector2 Nearest(ReadOnlySpan<Vector2> corners, Vector2 point, out bool corner)
        {
            var (best, nearest) = (float.PositiveInfinity, point);
            corner = false;
            for (var i = 0; i < corners.Length; i++)
            {
                var (a, b) = (corners[i], corners[(i + 1) % corners.Length]);
                var side = b - a;
                var t = Math.Clamp(Vector2.Dot(point - a, side) / MathF.Max(1e-12f, side.LengthSquared()), 0, 1);
                var on = a + side * t;
                var distance = Vector2.DistanceSquared(on, point);
                if (distance < best)
                    (best, nearest, corner) = (distance, on, t <= 0 || t >= 1);
            }

            return nearest;
        }
    }
}
