using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Shapes and colors the genres' overlays and HUDs draw with, belonging to the screen rather than any entity.
/// </summary>
public static class OverlayShapes
{
    /// <summary>The color of what is the player's own.</summary>
    public static readonly Color Mine = Color.FromBytes(0x57, 0xD3, 0x6B);
    /// <summary>The color of what belongs to an opponent.</summary>
    public static readonly Color Theirs = Color.FromBytes(0xE5, 0x53, 0x4B);
    /// <summary>The color of what belongs to no one, or is half-way between good and bad.</summary>
    public static readonly Color Neutral = Color.FromBytes(0xE3, 0xB3, 0x41);
    /// <summary>The dark background of a HUD panel.</summary>
    public static readonly Color HudBack = new(0.07f, 0.08f, 0.1f, 0.92f);
    /// <summary>The edge of a HUD panel.</summary>
    public static readonly Color HudEdge = Color.FromBytes(0x3A, 0x3F, 0x4B);
    /// <summary>The color of a HUD's text.</summary>
    public static readonly Color HudText = Color.FromBytes(0xE6, 0xE8, 0xEC);
    /// <summary>The color of a HUD's less important text.</summary>
    public static readonly Color HudDim = Color.FromBytes(0x8A, 0x90, 0x9C);

    /// <summary>A position in 3D without its height: the 2D point the world draws it at.</summary>
    public static Vector2 Flat(Vector3 v) => new(v.X, v.Y);

    /// <summary>A solid rectangle in the world.</summary>
    public static SpriteDraw Quad(Vector2 center, Vector2 size, Color color) => FrameDrawing.Quad(center, size, color);

    /// <summary>Text drawn at a position (see <see cref="FrameDrawing.Label"/>).</summary>
    public static SpriteDraw Label(string text, Vector2 at, float size, Color color, TextAlign align) => FrameDrawing.Label(text, at, size, color, align);

    /// <summary>A line between two points (see <see cref="FrameDrawing.Line"/>).</summary>
    public static SpriteDraw LineDraw(Vector2 from, Vector2 to, float thickness, Color color) => FrameDrawing.Line(from, to, thickness, color);

    /// <summary>The outline of a rectangle (see <see cref="FrameDrawing.Outline"/>).</summary>
    public static IEnumerable<SpriteDraw> Outline(Vector2 center, Vector2 size, float thickness, Color color) => FrameDrawing.Outline(center, size, thickness, color);

    /// <summary>The outline of a circle (see <see cref="FrameDrawing.Circle"/>).</summary>
    public static IEnumerable<SpriteDraw> Circle(Vector2 center, float radius, float thickness, Color color) => FrameDrawing.Circle(center, radius, thickness, color);

    /// <summary>A bar: dark behind, filled from the left by <paramref name="fraction"/>.</summary>
    public static IEnumerable<SpriteDraw> Meter(Vector2 center, float width, float height, float fraction, Color fill)
    {
        yield return Quad(center, new Vector2(width + height * 0.3f, height * 1.3f), new Color(0, 0, 0, 0.7f));
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction > 0)
            yield return Quad(new Vector2(center.X - width / 2 + width * fraction / 2, center.Y), new Vector2(width * fraction, height), fill);
    }

    /// <summary>The color a health bar fills with: <see cref="Mine"/> while healthy, <see cref="Neutral"/> while hurt, <see cref="Theirs"/> when nearly gone.</summary>
    public static Color HealthColor(float fraction) =>
        fraction > 0.6f ? Mine : fraction > 0.3f ? Neutral : Theirs;
}
