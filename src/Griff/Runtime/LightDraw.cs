using System.Numerics;

namespace Griff.Runtime;

/// <summary>A light to draw: its middle in the world, how far it reaches, its color times its brightness, and its softness.</summary>
public readonly record struct LightDraw(Vector2 Center, float Radius, Color Color, float Softness)
{
    /// <summary>
    /// For a light with <see cref="Scenes.Light.Shadows"/>: the sides that cast its shadows (see
    /// <see cref="Runtime.Shadows.CastersFor"/>). Empty for a light without, or with nothing in its way.
    /// </summary>
    public IReadOnlyList<ShadowEdge> Casters { get; init; } = [];

    /// <summary>How much light it puts on <paramref name="point"/>, per channel.</summary>
    public Vector3 At(Vector2 point)
    {
        if (Casters.Count > 0 && !Shadows.Reaches(Casters, Center, point))
            return Vector3.Zero;
        var amount = Scenes.Light.Falloff(Vector2.Distance(point, Center), Radius, Softness);
        return new Vector3(Color.R, Color.G, Color.B) * amount;
    }

    /// <summary>
    /// The light reaching <paramref name="point"/>: <paramref name="ambient"/>, the light everything gets, plus each of
    /// <paramref name="lights"/>, each channel at most 1.
    /// </summary>
    public static Color Total(Color ambient, IReadOnlyList<LightDraw> lights, Vector2 point)
    {
        var sum = new Vector3(ambient.R, ambient.G, ambient.B);
        foreach (var light in lights)
            sum += light.At(point);
        sum = Vector3.Clamp(sum, Vector3.Zero, Vector3.One);
        return new Color(sum.X, sum.Y, sum.Z);
    }
}
