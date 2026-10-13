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
