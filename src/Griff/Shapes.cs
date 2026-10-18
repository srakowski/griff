using System.Numerics;

namespace Griff;

/// <summary>An axis-aligned rectangle in world space.</summary>
public readonly record struct Box(Vector2 Center, Vector2 HalfSize)
{
    public Vector2 Min => Center - HalfSize;

    public Vector2 Max => Center + HalfSize;

    public bool Overlaps(Box other) =>
        Min.X < other.Max.X && Max.X > other.Min.X &&
        Min.Y < other.Max.Y && Max.Y > other.Min.Y;

    public bool Contains(Vector2 point) =>
        point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;

    /// <summary>True if the circle and the box share any area.</summary>
    public bool Overlaps(Vector2 center, float radius) =>
        Vector2.DistanceSquared(Vector2.Clamp(center, Min, Max), center) < radius * radius;
}

/// <summary>A rectangle in world space turned by <see cref="Angle"/> radians about its center.</summary>
public readonly record struct TurnedBox(Vector2 Center, Vector2 HalfSize, float Angle)
{
    private Vector2 AxisX => new(MathF.Cos(Angle), MathF.Sin(Angle));

    private Vector2 AxisY => new(-MathF.Sin(Angle), MathF.Cos(Angle));

    /// <summary>The four corners, going round.</summary>
    public Vector2[] Corners()
    {
        var x = AxisX * HalfSize.X;
        var y = AxisY * HalfSize.Y;
        return [Center - x - y, Center + x - y, Center + x + y, Center - x + y];
    }

    /// <summary>The smallest upright box round it.</summary>
    public Box Bounds
    {
        get
        {
            var half = Vector2.Abs(AxisX * HalfSize.X) + Vector2.Abs(AxisY * HalfSize.Y);
            return new Box(Center, half);
        }
    }

    public bool Contains(Vector2 point)
    {
        var d = point - Center;
        return MathF.Abs(Vector2.Dot(d, AxisX)) <= HalfSize.X + 1e-5f && MathF.Abs(Vector2.Dot(d, AxisY)) <= HalfSize.Y + 1e-5f;
    }

    /// <summary>True if it and the upright box share any area (a separating-axis test).</summary>
    public bool Overlaps(Box box)
    {
        if (!Bounds.Overlaps(box))
            return false;
        var d = box.Center - Center;
        foreach (var axis in new[] { AxisX, AxisY })
        {
            var reach = box.HalfSize.X * MathF.Abs(axis.X) + box.HalfSize.Y * MathF.Abs(axis.Y);
            var own = axis == AxisX ? HalfSize.X : HalfSize.Y;
            if (MathF.Abs(Vector2.Dot(d, axis)) >= own + reach)
                return false;
        }

        return true;
    }
}

/// <summary>A circle in world space.</summary>
public readonly record struct Circle(Vector2 Center, float Radius)
{
    /// <summary>The smallest box around the circle.</summary>
    public Box Bounds => new(Center, new Vector2(Radius));

    public bool Contains(Vector2 point) => Vector2.DistanceSquared(Center, point) <= Radius * Radius;

    public bool Overlaps(Circle other) =>
        Vector2.DistanceSquared(Center, other.Center) < (Radius + other.Radius) * (Radius + other.Radius);

    public bool Overlaps(Box box) => box.Overlaps(Center, Radius);
}
