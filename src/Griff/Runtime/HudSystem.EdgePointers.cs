using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// Edge pointers: on the main camera's screen once the camera and the anchored HUD have moved, each on the edge toward its
// target and turned to point at it, or over it (or hidden) while it's in view.
internal sealed partial class HudSystem
{
    internal void PointEdgePointers()
    {
        var pointers = Game.RunHere<EdgePointer>();
        if (pointers.Count == 0)
            return;

        var camera = Views.FindCamera(Game.Scene);
        var (center, half, scale) = ScreenLayout.ViewOf(camera, Game.ViewAspect);
        scale *= Game.UiScale;
        var shown = Game.Minigame;
        foreach (var pointer in pointers)
        {
            var entity = pointer.Entity!;
            // What it's inside being hidden hides it; behind the minigame that has the screen, it isn't seen.
            if (entity.Parent is { IsActive: false } || MinigameRoot.IsBehind(entity, shown))
                continue;
            pointer.ViewCamera = camera;
            Point(pointer, center, half, scale);
        }
    }

    private static void Point(EdgePointer pointer, Vector2 center, Vector2 half, float scale)
    {
        var entity = pointer.Entity!;
        var target = pointer.FindTarget();
        if (target is null || !target.IsActive)
        {
            Show(pointer, false);
            return;
        }

        var at = Flat(target.Transform.WorldPosition);
        pointer.Distance = Vector2.Distance(center, at);
        var reach = Vector2.Max(half - new Vector2(Math.Max(pointer.Margin, 0) * scale), Vector2.Zero);
        var over = pointer.InView == EdgePointerInView.OverTarget;
        var edge = EdgePointer.EdgePoint(center, reach, over ? at + pointer.OverTarget : at);
        if (edge is null && !over)
        {
            Show(pointer, false);
            return;
        }

        Show(pointer, true, edge is not null);
        var transform = entity.Transform;
        transform.ScreenScale = entity.Parent is { } inside && Views.IsOnScreen(inside, null) ? 1 : scale;
        Follow.SetWorld(transform, edge ?? at + pointer.OverTarget);
        var away = at - center;
        var degrees = edge is null || away == Vector2.Zero ? -90 : MathF.Atan2(away.Y, away.X) * 180 / MathF.PI;
        if (entity.Parent is { } parent)
            degrees -= MathF.Atan2(parent.Transform.WorldMatrix.M12, parent.Transform.WorldMatrix.M11) * 180 / MathF.PI;
        transform.Rotation = transform.Rotation with { Z = degrees };

        // A label inside it stays upright, wherever it's turned.
        foreach (var child in entity.Children)
        {
            if (child.GetComponent<Text>() is not null)
                child.Transform.Rotation = child.Transform.Rotation with { Z = -degrees };
        }
    }

    private static void Show(EdgePointer pointer, bool shown, bool onEdge = false)
    {
        pointer.IsOnEdge = onEdge;
        if (pointer.Entity!.IsEnabled != shown)
            pointer.Entity.IsEnabled = shown;
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
}
