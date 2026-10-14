/// <summary>Measuring on the arena's floor.</summary>
public static class Sweep
{
    /// <summary>How far apart two points are, across the floor.</summary>
    public static float Distance(Vector3 from, Vector3 to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
