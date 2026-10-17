using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Puts what's pinned to the screen (<see cref="ScreenAnchor"/>) at its point on the main camera's view: the game each
/// frame (on the window's shape), and the editor after each edit (on a 16:9 screen), so the Scene view shows a HUD where
/// the game will. With the camera's <see cref="Camera.HudSize"/> set, it's drawn <see cref="Camera.HudScale"/> times its
/// size, so it keeps its size on the screen as the camera zooms.
/// </summary>
public static class ScreenLayout
{
    /// <summary>
    /// What <paramref name="camera"/> (the main camera, or none) shows on a screen <paramref name="aspect"/> times as wide
    /// as it's high, to lay out what's anchored to it: its middle (shake included), how far it reaches out to its edges,
    /// and how many times its own size the HUD is drawn (see <see cref="Camera.HudScale"/>).
    /// </summary>
    public static (Vector2 Center, Vector2 Half, float Scale) ViewOf(Entity? camera, float aspect)
    {
        var component = camera?.GetComponent<Camera>();
        var center = camera is null ? Vector2.Zero : new Vector2(camera.Transform.WorldPosition.X, camera.Transform.WorldPosition.Y);
        center += component?.ShakeOffset ?? Vector2.Zero;
        var halfHeight = component?.Size ?? Views.DefaultCameraSize;
        return (center, new Vector2(halfHeight * (aspect > 0 ? aspect : 1), halfHeight), component?.HudScale ?? 1);
    }

    /// <summary>
    /// Puts <paramref name="anchor"/>'s entity at its point on the view (see <see cref="ViewOf"/>), keeping its Z, and
    /// draws it <paramref name="scale"/> times its size; inside something anchored, it's already drawn that size.
    /// </summary>
    internal static void Place(ScreenAnchor anchor, Vector2 center, Vector2 half, float scale)
    {
        var entity = anchor.Entity!;
        var transform = entity.Transform;
        transform.ScreenScale = entity.Parent is { } inside && Views.IsOnScreen(inside, null) ? 1 : scale;
        var target = new Vector3(anchor.PointOn(center, half, scale), transform.WorldPosition.Z);
        if (entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
            target = Vector3.Transform(target, inverse);
        transform.Position = new Vector3(target.X, target.Y, transform.Position.Z);
    }

    /// <summary>
    /// The <see cref="ScreenAnchor.Offset"/> that puts <paramref name="anchor"/>'s entity at <paramref name="position"/>, a
    /// position in its parent's space, on the view <paramref name="camera"/> shows at <paramref name="aspect"/>: for
    /// dragging it in the editor.
    /// </summary>
    public static Vector2 OffsetFor(ScreenAnchor anchor, Vector3 position, Entity? camera, float aspect)
    {
        var (center, half, scale) = ViewOf(camera, aspect);
        var world = anchor.Entity?.Parent is { } parent ? Vector3.Transform(position, parent.Transform.WorldMatrix) : position;
        return (new Vector2(world.X, world.Y) - center - ScreenAnchor.Direction(anchor.Anchor) * half) / scale;
    }

    /// <summary>
    /// Puts everything anchored in <paramref name="scene"/> where the game shows it on its main camera's view, on a
    /// screen <paramref name="aspect"/> times as wide as it's high. Parents go before children; an entity's first anchor
    /// places it.
    /// </summary>
    public static void LayOutAll(Scene scene, float aspect)
    {
        var anchors = scene.All<ScreenAnchor>();
        if (anchors.Count == 0)
            return;
        var (center, half, scale) = ViewOf(Views.FindCamera(scene), aspect);
        foreach (var anchor in anchors)
        {
            if (anchor.Entity!.GetComponent<ScreenAnchor>() == anchor)
                Place(anchor, center, half, scale);
        }
    }
}
