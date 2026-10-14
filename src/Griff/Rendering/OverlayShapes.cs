using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Rendering;

// Shapes and colors the genres' overlays and HUDs draw with, belonging to the screen rather than any entity.
internal static class OverlayShapes
{
    public static readonly Color Mine = Color.FromBytes(0x57, 0xD3, 0x6B);
    public static readonly Color Theirs = Color.FromBytes(0xE5, 0x53, 0x4B);
    public static readonly Color Neutral = Color.FromBytes(0xE3, 0xB3, 0x41);
    public static readonly Color HudBack = new(0.07f, 0.08f, 0.1f, 0.92f);
    public static readonly Color HudEdge = Color.FromBytes(0x3A, 0x3F, 0x4B);
    public static readonly Color HudText = Color.FromBytes(0xE6, 0xE8, 0xEC);
    public static readonly Color HudDim = Color.FromBytes(0x8A, 0x90, 0x9C);

    public static Vector2 Flat(Vector3 v) => new(v.X, v.Y);

    // A solid rectangle in the world.
    public static SpriteDraw Quad(Vector2 center, Vector2 size, Color color) => FrameDrawing.Quad(center, size, color);

    public static SpriteDraw Label(string text, Vector2 at, float size, Color color, TextAlign align) => FrameDrawing.Label(text, at, size, color, align);

    public static SpriteDraw LineDraw(Vector2 from, Vector2 to, float thickness, Color color) => FrameDrawing.Line(from, to, thickness, color);

    public static IEnumerable<SpriteDraw> Outline(Vector2 center, Vector2 size, float thickness, Color color) => FrameDrawing.Outline(center, size, thickness, color);

    public static IEnumerable<SpriteDraw> Circle(Vector2 center, float radius, float thickness, Color color) => FrameDrawing.Circle(center, radius, thickness, color);

    // A bar: dark behind, filled from the left.
    public static IEnumerable<SpriteDraw> Meter(Vector2 center, float width, float height, float fraction, Color fill)
    {
        yield return Quad(center, new Vector2(width + height * 0.3f, height * 1.3f), new Color(0, 0, 0, 0.7f));
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction > 0)
            yield return Quad(new Vector2(center.X - width / 2 + width * fraction / 2, center.Y), new Vector2(width * fraction, height), fill);
    }

    public static Color HealthColor(float fraction) =>
        fraction > 0.6f ? Mine : fraction > 0.3f ? Neutral : Theirs;
}
