using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>A shape drawn by <see cref="DebugDraw"/>, in world space.</summary>
public abstract record DebugShape(Color Color);

public sealed record DebugLine(Vector2 From, Vector2 To, Color Color) : DebugShape(Color);

public sealed record DebugCircle(Vector2 Center, float Radius, Color Color) : DebugShape(Color);

public sealed record DebugText(Vector2 Position, string Text, float Size, Color Color) : DebugShape(Color);

/// <summary>
/// Draws lines, boxes, circles and text over the game for one frame, to see what code is thinking: a raycast, an enemy's
/// sight range, a path. Call it every frame you want the shape shown (from Update), or pass <c>seconds</c> to keep it
/// up that long. Shapes are drawn over everything, with lines the same thickness however far the camera zooms.
/// </summary>
/// <example>
/// <code>
/// Debug.Line(eye, eye + toPlayer * 10, seen is null ? Color.Green : Color.Red);
/// Debug.Circle(Transform.Position, SightRange);
/// Debug.Text(Transform.Position + new Vector3(0, 1, 0), state.ToString());
/// </code>
/// </example>
public sealed class DebugDraw
{
    private readonly List<(DebugShape Shape, long Frame, double Until)> _shapes = [];
    private readonly Time _time;

    internal DebugDraw(Time time) => _time = time;

    /// <summary>The color used when none is given.</summary>
    public static readonly Color DefaultColor = Color.FromBytes(0x3D, 0xDC, 0x84);

    /// <summary>Set to false to stop drawing debug shapes (they're still accepted, just not shown), e.g. for a release build.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The shapes to draw this frame.</summary>
    public IEnumerable<DebugShape> Shapes => Enabled ? _shapes.Select(s => s.Shape) : [];

    /// <summary>A line from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public void Line(Vector2 from, Vector2 to, Color? color = null, float seconds = 0) =>
        Add(new DebugLine(from, to, color ?? DefaultColor), seconds);

    /// <summary>A line from <paramref name="from"/> to <paramref name="to"/>; Z is ignored.</summary>
    public void Line(Vector3 from, Vector3 to, Color? color = null, float seconds = 0) =>
        Line(Flat(from), Flat(to), color, seconds);

    /// <summary>A line from <paramref name="origin"/> going <paramref name="direction"/> (its length is the line's length).</summary>
    public void Ray(Vector2 origin, Vector2 direction, Color? color = null, float seconds = 0) =>
        Line(origin, origin + direction, color, seconds);

    /// <summary>The outline of a box of <paramref name="size"/> centered on <paramref name="center"/>.</summary>
    public void Box(Vector2 center, Vector2 size, Color? color = null, float seconds = 0)
    {
        var half = size / 2;
        var corners = new[] { center + new Vector2(-half.X, -half.Y), center + new Vector2(half.X, -half.Y), center + half, center + new Vector2(-half.X, half.Y) };
        for (var i = 0; i < 4; i++)
            Line(corners[i], corners[(i + 1) % 4], color, seconds);
    }

    /// <summary>The outline of <paramref name="box"/>, e.g. a collider's <c>Physics.BoundsOf</c>.</summary>
    public void Box(Box box, Color? color = null, float seconds = 0) => Box(box.Center, box.HalfSize * 2, color, seconds);

    /// <summary>The outline of a circle.</summary>
    public void Circle(Vector2 center, float radius, Color? color = null, float seconds = 0) =>
        Add(new DebugCircle(center, Math.Abs(radius), color ?? DefaultColor), seconds);

    /// <summary>The outline of a circle; Z is ignored.</summary>
    public void Circle(Vector3 center, float radius, Color? color = null, float seconds = 0) => Circle(Flat(center), radius, color, seconds);

    /// <summary>A small cross marking <paramref name="point"/>.</summary>
    public void Point(Vector2 point, Color? color = null, float seconds = 0, float size = 0.2f)
    {
        Line(point - new Vector2(size, size) / 2, point + new Vector2(size, size) / 2, color, seconds);
        Line(point - new Vector2(size, -size) / 2, point + new Vector2(size, -size) / 2, color, seconds);
    }

    /// <summary>Lines joining <paramref name="points"/> in order, e.g. a path from <see cref="Tilemap.FindPath"/>.</summary>
    public void Path(IEnumerable<Vector2> points, Color? color = null, float seconds = 0)
    {
        Vector2? previous = null;
        foreach (var point in points)
        {
            if (previous is { } from)
                Line(from, point, color, seconds);
            previous = point;
        }
    }

    /// <summary>A line of text centered on <paramref name="position"/>, <paramref name="size"/> world units high.</summary>
    public void Text(Vector2 position, object? text, Color? color = null, float seconds = 0, float size = 0.5f) =>
        Add(new DebugText(position, text?.ToString() ?? "null", size, color ?? Color.White), seconds);

    /// <summary>A line of text centered on <paramref name="position"/>; Z is ignored.</summary>
    public void Text(Vector3 position, object? text, Color? color = null, float seconds = 0, float size = 0.5f) =>
        Text(Flat(position), text, color, seconds, size);

    /// <summary>Forgets every shape, including ones meant to last longer.</summary>
    public void Clear() => _shapes.Clear();

    /// <summary>Drops shapes whose time is up. Called by the game at the start of each frame, after time moves on.</summary>
    internal void BeginFrame()
    {
        if (_shapes.Count > 0)
            _shapes.RemoveAll(s => s.Frame < _time.Frame && s.Until <= _time.Total);
    }

    // A shape lasts at least until the next frame starts (counted in frames, so it goes even while Time.Scale is 0),
    // and for timed shapes until that much game time has passed.
    private void Add(DebugShape shape, float seconds) =>
        _shapes.Add((shape, _time.Frame, seconds > 0 ? _time.Total + seconds : double.NegativeInfinity));

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
}
