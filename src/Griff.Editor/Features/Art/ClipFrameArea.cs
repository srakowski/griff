using System.Windows;
using Vector2 = System.Numerics.Vector2;

namespace Griff.Editor.Features.Art;

/// <summary>
/// Where the Clip Editor's frame is on something laid over it (see <see cref="ClipEditorWindow.FrameArea"/>), for an
/// overlay to draw over it and take the mouse there: the picture's top-left corner and size there, how big it is in world
/// units (the sprite's Size, what overlays measure in, from the entity's middle), and its pixels.
/// </summary>
public readonly record struct ClipFrameArea(Point Origin, Size Size, Vector2 Units, Size Pixels)
{
    /// <summary>Where a point of the frame, in world units from the entity's middle, is drawn.</summary>
    public Point ToScreen(Vector2 world) =>
        new(Origin.X + (world.X / Units.X + 0.5) * Size.Width, Origin.Y + (0.5 - world.Y / Units.Y) * Size.Height);

    /// <summary>The point of the frame, in world units, drawn at <paramref name="screen"/>.</summary>
    public Vector2 ToWorld(Point screen) =>
        new((float)(((screen.X - Origin.X) / Size.Width - 0.5) * Units.X), (float)((0.5 - (screen.Y - Origin.Y) / Size.Height) * Units.Y));

    /// <summary>The nearest edge between the picture's pixels to <paramref name="screen"/>, in world units.</summary>
    public Vector2 Snapped(Point screen)
    {
        var (x, y) = ((screen.X - Origin.X) / Size.Width * Pixels.Width, (screen.Y - Origin.Y) / Size.Height * Pixels.Height);
        return ToWorld(new Point(Origin.X + Math.Round(x) / Pixels.Width * Size.Width, Origin.Y + Math.Round(y) / Pixels.Height * Size.Height));
    }

    /// <summary>A pixel of the picture, in world units.</summary>
    public Vector2 PixelSize => Pixels.Width > 0 && Pixels.Height > 0
        ? new Vector2(Units.X / (float)Pixels.Width, Units.Y / (float)Pixels.Height)
        : Units / 32;
}
