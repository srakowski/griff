using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Where the game is seen from: a camera's aspect, the mouse in a view, the view a HUD is laid out for, what the player
/// looks through, and cameras the player pans and zooms.
/// </summary>
internal sealed class ViewSystem : GameSystem
{
    public override string Name => "Views";

    public static (Vector2 Center, float HalfHeight, float Aspect)? ViewOf(IInput input) =>
        input is RemoteInput { HasView: true } remote ? remote.View : null;

    public (Vector2 Center, float HalfHeight, float Aspect) HudViewOf(IInput input) => ViewOf(input) ?? HudView();

    public (Vector2 Center, float HalfHeight, float Aspect) HudView()
    {
        var camera = Views.FindCamera(Game.Scene);
        var size = camera?.GetComponent<Camera>()?.Size ?? Views.DefaultCameraSize;
        var center = camera is null ? Vector2.Zero : new Vector2(camera.Transform.WorldPosition.X, camera.Transform.WorldPosition.Y);
        return (center, size, Game.ViewAspect);
    }

    // Cameras with CameraControls, on real time, but not under a menu that pauses the game here.
    internal void RunCameraControls()
    {
        var controls = Game.Scene.ActiveNow<CameraControls>();
        if (controls.Count == 0 || Game.LocalInputBlocked)
            return;
        var onHud = MouseOnHud();
        foreach (var control in controls)
            control.Advance(Game.Input, Game.Time.UnscaledDelta, AspectOf(control.Entity!), onHud);
    }

    // The mouse is over the HUD: a button, something a system shows there, or anything pinned to the screen with a sprite
    // under the mouse (a bar's backdrop, an open panel).
    private bool MouseOnHud()
    {
        if (Game.MouseOnUi)
            return true;
        var mouse = Game.Input.MouseWorld;
        var camera = Views.FindCamera(Game.Scene);
        foreach (var sprite in Game.Scene.ActiveNow<Sprite>())
        {
            var entity = sprite.Entity!;
            if (Views.IsOnScreen(entity, camera) && Matrix4x4.Invert(entity.Transform.WorldMatrix, out var toSprite) &&
                Vector2.Transform(mouse, toSprite) is var at && MathF.Abs(at.X) <= MathF.Abs(sprite.Size.X) / 2 && MathF.Abs(at.Y) <= MathF.Abs(sprite.Size.Y) / 2)
                return true;
        }

        return false;
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
