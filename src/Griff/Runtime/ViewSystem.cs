using System.Numerics;
using Griff.Input;
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

    // Cameras with CameraControls, on real time: the player's, but not under a menu that pauses the game here; the game's
    // while it has one focused on something.
    internal void RunCameraControls()
    {
        var controls = Game.Scene.ActiveNow<CameraControls>();
        if (controls.Count == 0)
            return;
        var blocked = Game.LocalInputBlocked;
        bool? onHud = null;
        foreach (var control in controls)
        {
            if (control.IsGliding && !blocked && Interrupts(control, Game.Input, onHud ??= MouseOnHud()))
                control.CancelGlide();
            if (control.IsFocused)
                control.AdvanceFocus(Game.Time.UnscaledDelta, AspectOf(control.Entity!));
            else if (!blocked)
                Advance(control, Game.Input, Game.Time.UnscaledDelta, AspectOf(control.Entity!), onHud ??= MouseOnHud(), Game.EdgeScroll, Game.PanSpeed);
        }
    }

    // Whether the player is asking to pan, drag or zoom the camera: what cancels a GlideTo.
    private static bool Interrupts(CameraControls controls, IInput input, bool mouseOnHud) =>
        (controls.PanWithKeys && KeyPan(input) != Vector2.Zero)
        || (input.IsMouseInView && !mouseOnHud
            && (Dragging(controls.DragButton, input.WasPressed) || (controls.ZoomStep > 0 && input.Scroll != 0)));

    // Pans and zooms a camera by what `input` says over `seconds` of real time, on a view `aspect` times as wide as it's
    // high. While `mouseOnHud`, the wheel and a new drag are the HUD's.
    private static void Advance(CameraControls controls, IInput input, float seconds, float aspect, bool mouseOnHud, bool? edgeScroll, float panScale)
    {
        if (controls.Entity is not { Scene: not null } entity || entity.GetComponent<Camera>() is not { } camera)
            return;
        var transform = entity.Transform;
        var position = new Vector2(transform.WorldPosition.X, transform.WorldPosition.Y);
        var start = position;
        var size = camera.Size;
        aspect = aspect > 0 ? aspect : 1;
        var extent = new Vector2(size * aspect, size);

        // Where the mouse is across the view, from -1 to 1 each way, and the world point under it through the camera as it is
        // now. Not MouseWorld, found through the camera as last drawn (a step behind while it moves): a drag going by that
        // would chase the camera's own last move and swing back and forth, worse with more steps a frame.
        var across = input.MouseOnScreen ?? (input.MouseWorld - position) / extent;
        var mouse = input.MouseOnScreen is { } onScreen ? Through(transform, size, aspect, onScreen) : input.MouseWorld;
        var mouseFree = input.IsMouseInView && !mouseOnHud;

        var pan = controls.PanWithKeys ? KeyPan(input) : Vector2.Zero;
        if ((edgeScroll ?? controls.EdgeScroll) && mouseFree && controls.HeldAt is null)
        {
            // The edge is the view's outer 3%, where the mouse isn't on the HUD: reaching for a button along the bottom
            // doesn't pan the world away, as the wheel and a drag don't.
            pan += new Vector2(across.X >= 0.97f ? 1 : across.X <= -0.97f ? -1 : 0, across.Y >= 0.97f ? 1 : across.Y <= -0.97f ? -1 : 0);
        }

        if (pan != Vector2.Zero && seconds > 0)
            position += (pan.LengthSquared() > 1 ? Vector2.Normalize(pan) : pan) * controls.PanSpeed * panScale * size * 2 * seconds;

        // A drag keeps the point it took hold of under the mouse.
        if (!Dragging(controls.DragButton, input.IsDown))
            controls.HeldAt = null;
        else if (controls.HeldAt is { } held)
            position += held - mouse;
        else if (mouseFree && Dragging(controls.DragButton, input.WasPressed))
            controls.HeldAt = mouse;

        if (controls.ZoomStep > 0 && input.Scroll != 0 && mouseFree && controls.HeldAt is null)
        {
            var (low, high) = (MathF.Max(MathF.Min(controls.ZoomMin, controls.ZoomMax), 0.01f), MathF.Max(MathF.Max(controls.ZoomMin, controls.ZoomMax), 0.01f));
            var zoomed = Math.Clamp(size * MathF.Pow(1 - Math.Clamp(controls.ZoomStep, 0, 0.9f), input.Scroll), low, high);
            if (zoomed != size)
            {
                // Toward the mouse: the point under it stays there.
                position = mouse + (position - mouse) * (zoomed / size);
                camera.Size = size = zoomed;
            }
        }

        if (controls.Bounds() is { } bounds)
        {
            var inside = Follow.Clamp(position, bounds, new Vector2(size * aspect, size));
            // Dragged past the edge, it takes hold of the point under the mouse at the edge, so dragging back moves it at once.
            if (inside != position && controls.HeldAt is { } held)
                controls.HeldAt = held + inside - position;
            position = inside;
        }

        if (position != start)
            Follow.SetWorld(transform, position);
    }

    // The world point at `onScreen` (-1..1 across the view) through a camera as it is now, `size` high and `aspect` times
    // as wide (turned as it is).
    private static Vector2 Through(Transform camera, float size, float aspect, Vector2 onScreen)
    {
        var offset = onScreen * new Vector2(size * aspect, size);
        var rotation = camera.RotationQuaternion;
        if (rotation != Quaternion.Identity)
            offset = Vector2.Transform(offset, rotation);
        return new Vector2(camera.WorldPosition.X, camera.WorldPosition.Y) + offset;
    }

    // The way the PanUp, PanDown, PanLeft and PanRight actions point, or else the left stick's (keys win while held, as
    // they do for Input.Move), never longer than 1.
    private static Vector2 KeyPan(IInput input)
    {
        var x = (input.IsDown("PanRight") ? 1f : 0f) - (input.IsDown("PanLeft") ? 1f : 0f);
        var y = (input.IsDown("PanUp") ? 1f : 0f) - (input.IsDown("PanDown") ? 1f : 0f);
        var pan = new Vector2(x != 0 ? x : input.LeftStick.X, y != 0 ? y : input.LeftStick.Y);
        return pan.LengthSquared() > 1 ? Vector2.Normalize(pan) : pan;
    }

    // Whether the drag button is down (or was pressed), as `button` says of each mouse button.
    private static bool Dragging(CameraDrag drag, Func<MouseButton, bool> button) => drag switch
    {
        CameraDrag.Left => button(MouseButton.Left),
        CameraDrag.Middle => button(MouseButton.Middle),
        CameraDrag.Right => button(MouseButton.Right),
        CameraDrag.MiddleOrRight => button(MouseButton.Middle) || button(MouseButton.Right),
        _ => false,
    };

    // The mouse is over the HUD: a button, something a system shows there, or anything pinned to the screen with a sprite
    // under the mouse (a bar's backdrop, an open panel).
    private bool MouseOnHud()
    {
        if (Game.MouseOnUi)
            return true;
        var camera = Views.FindCamera(Game.Scene);
        // What's pinned to the screen is where the camera is now, so the mouse is looked for through it.
        var mouse = Game.Input.MouseOnScreen is { } onScreen && camera is not null
            ? Through(camera.Transform, camera.GetComponent<Camera>()?.Size ?? Views.DefaultCameraSize, Game.ViewAspect > 0 ? Game.ViewAspect : 1, onScreen)
            : Game.Input.MouseWorld;
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
        var clip = input.MouseOnScreen ?? new Vector2(onScreen.X, onScreen.Y) / onScreen.W;
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
