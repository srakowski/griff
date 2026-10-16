using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Where the game is seen from: a camera's aspect, the mouse in a view, the view a HUD is laid out for and what the
/// player looks through.
/// </summary>
internal sealed class ViewSystem : GameSystem
{
    public override string Name => "Views";

    public static (Vector2 Center, float HalfHeight, float Aspect)? ViewOf(IInput input) =>
        input is RemoteInput { HasView: true } remote ? remote.View : null;

    public (Vector2 Center, float HalfHeight, float Aspect) HudView()
    {
        if (Game.ActingFor?.Remote is { HasView: true } input)
            return input.View;
        var camera = Views.FindCamera(Game.Scene);
        var size = camera?.GetComponent<Camera>()?.Size ?? Views.DefaultCameraSize;
        var center = camera is null ? Vector2.Zero : new Vector2(camera.Transform.WorldPosition.X, camera.Transform.WorldPosition.Y);
        return (center, size, Game.ViewAspect);
    }

    public bool LooksThrough(Entity entity)
    {
        for (var i = 0; i < Game.Systems.Count; i++)
        {
            if (Game.Systems[i].SeesThrough(entity))
                return true;
        }

        return false;
    }

    public float AspectOf(Entity camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return camera.GetComponent<Viewport>() is { } viewport && camera != Views.FindCamera(Game.Scene) ? viewport.AspectOn(Game.ViewAspect) : Game.ViewAspect;
    }

    public bool MouseIn(Entity camera, out Vector2 world)
    {
        ArgumentNullException.ThrowIfNull(camera);
        var input = Game.Input;
        world = input.MouseWorld;
        var main = Views.FindCamera(Game.Scene);
        if (camera == main)
            return input.IsMouseInView;
        if (camera.Scene != Game.Scene || !camera.IsActive || camera.GetComponent<Viewport>() is not { IsShowing: true } viewport ||
            camera.GetComponent<Camera>() is not { } lens)
            return false;

        // Where the mouse is on the screen, through the main camera; then back into the world through this one.
        var aspect = Game.ViewAspect > 0 ? Game.ViewAspect : 1;
        var mainSize = main?.GetComponent<Camera>()?.Size ?? Views.DefaultCameraSize;
        var onScreen = Vector4.Transform(new Vector4(input.MouseWorld, 0, 1), Views.ViewProjection(main, mainSize, aspect));
        var clip = new Vector2(onScreen.X, onScreen.Y) / onScreen.W;
        var (left, top, width, height) = viewport.Area;
        var (across, down) = ((clip.X + 1) / 2, (1 - clip.Y) / 2);
        var inside = input.IsMouseInView && across >= left && across <= left + width && down >= top && down <= top + height;

        var toScreen = Views.ViewProjection(camera, lens.Size, viewport.AspectOn(aspect)) * viewport.ToScreen();
        if (Matrix4x4.Invert(toScreen, out var back))
        {
            var seen = Vector4.Transform(new Vector4(clip, 0, 1), back);
            world = new Vector2(seen.X, seen.Y) / seen.W;
        }

        return inside;
    }
}
