using System.Numerics;
using Griff.Input;

namespace Griff.Scenes;

/// <summary>
/// Moves the entity after another one, smoothly: a camera after the player, a pet after its owner. No code needed: put it
/// on the camera and name the <see cref="Target"/>. With <see cref="StayInside"/>, a camera never shows past the edges
/// of the level. Applied while the game runs, after physics, so it follows where things ended up this frame.
/// </summary>
[Category("Movement")]
[WorksWith(typeof(Camera), Note = "a camera that follows the player")]
[ComponentName("Follow")]
public sealed class Follow : Component, IViewFocus
{
    private Entity? _target;
    private bool _placed;
    private Vector2? _lastTargetPosition;
    private Vector2 _lead;

    // A slide over to a new room: where it set off from, how long it's been going, and how long it takes (real seconds).
    private (Vector2 From, float Elapsed, float Seconds)? _slide;

    /// <summary>The name of the entity to follow, e.g. "Player".</summary>
    [EntityName]
    public string? Target { get; set; } = "Player";

    /// <summary>How quickly it catches up: higher is snappier, 0 sticks exactly to the target.</summary>
    public float Smoothing { get; set; } = 6;

    /// <summary>Where to sit relative to the target, in world units, e.g. (0, 2) to see more above a platformer's hero.</summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    /// A box around the follower, in world units, that the target can move about in without the follower moving.
    /// (0, 0) follows every step; something like (2, 1) keeps a platformer's camera still for small hops.
    /// </summary>
    public Vector2 DeadZone { get; set; }

    /// <summary>Seconds of the target's movement to look ahead by, so a camera shows more of where the player is heading.</summary>
    public float LookAhead { get; set; }

    /// <summary>
    /// The name of an entity whose area the follower stays inside: a tilemap's grid, a box collider, or a sprite. On a
    /// camera the whole view stays inside, so the game never shows past the level's edges.
    /// </summary>
    [EntityName]
    public string? StayInside { get; set; }

    Entity? IViewFocus.Focus => _target;

    /// <summary>The entity being followed. Set it from a script to follow something without looking it up by name.</summary>
    [DoNotSerialize]
    public Entity? TargetEntity
    {
        get => _target;
        set
        {
            _target = value;
            _lastTargetPosition = null;
            _lead = Vector2.Zero;
        }
    }

    /// <summary>
    /// Jumps straight to the target next time instead of gliding over, e.g. after a teleport or a respawn (doors and
    /// checkpoints do this by themselves).
    /// </summary>
    public void Snap()
    {
        _placed = false;
        _slide = null;
    }

    /// <summary>
    /// The <see cref="Room"/> a camera is keeping to, in place of <see cref="StayInside"/>; set by the game as the target
    /// moves from room to room.
    /// </summary>
    [DoNotSerialize]
    public Room? CurrentRoom { get; internal set; }

    /// <summary>True while the camera is sliding over to a new room.</summary>
    public bool IsChangingRoom => _slide is not null;

    /// <summary>The entity it's following, looked up by <see cref="Target"/> if the one it had has gone.</summary>
    internal Entity? FindTarget()
    {
        var scene = Entity?.Scene;
        // In a network game, this player's own character comes first, once it's there.
        if (scene is not null && (_target?.Scene != scene || (scene.PreferNamed is { } prefer && _target.Name == Target && !prefer(_target))))
            TargetEntity = string.IsNullOrWhiteSpace(Target) ? null : scene.FindNamed(Target, near: Entity) ?? (_target?.Scene == scene ? _target : null);
        return _target;
    }

    /// <summary>
    /// Glides over to where it should be in <paramref name="seconds"/> of real time, rather than following smoothly: into a
    /// new room. Before it has been placed, or with no time, it jumps straight there.
    /// </summary>
    internal void SlideOver(float seconds)
    {
        if (_placed && seconds > 0 && Entity is not null)
            _slide = (Flat(Entity.Transform.WorldPosition), 0, seconds);
        else
            Snap();
    }

    /// <summary>Moves toward the target by <paramref name="seconds"/> of smoothing. Called by the game after physics.</summary>
    /// <param name="viewAspect">The view's width over its height, for keeping a camera's view inside <see cref="StayInside"/>.</param>
    /// <param name="realSeconds">
    /// Real time passed, for sliding between rooms while the game is held still; <paramref name="seconds"/> if not given.
    /// </param>
    public void Advance(float seconds, float viewAspect, float? realSeconds = null)
    {
        var scene = Entity?.Scene;
        if (scene is null)
            return;
        FindTarget();

        var transform = Entity!.Transform;
        var current = Flat(transform.WorldPosition);
        var goal = current;

        if (_target is not null)
        {
            var target = Flat(_target.Transform.WorldPosition);
            UpdateLead(target, seconds);
            goal = target + Offset + _lead;

            // Snap there the first time, so a level doesn't open with the camera sliding over from wherever it was placed.
            if (_placed)
                goal = ApplyDeadZone(current, goal);
        }

        var bounds = CurrentRoom?.Entity?.Scene == scene ? CurrentRoom!.Area : Bounds(scene);
        if (bounds is { } area)
            goal = Clamp(goal, area, ViewHalfSize(viewAspect));

        if (_slide is { } slide && _placed)
        {
            var elapsed = slide.Elapsed + (realSeconds ?? seconds);
            var t = Math.Clamp(elapsed / slide.Seconds, 0, 1);
            SetWorld(transform, Vector2.Lerp(slide.From, goal, t * t * (3 - 2 * t)));
            _slide = t >= 1 ? null : slide with { Elapsed = elapsed };
            return;
        }

        var snapping = !_placed;
        var next = snapping || Smoothing <= 0 ? goal : Vector2.Lerp(current, goal, 1 - MathF.Exp(-Smoothing * seconds));
        if (_target is not null)
            _placed = true;
        if (next == current)
            return;
        SetWorld(transform, next);

        // Snapped to its target (a level starting, a respawn): drawn there at once, not glided over.
        if (snapping)
            transform.Jumped();
    }

    // A target's speed, from its body if it has one, else from how far it moved; smoothed so a stop doesn't jolt the view.
    private void UpdateLead(Vector2 target, float seconds)
    {
        if (LookAhead <= 0 || seconds <= 0)
        {
            _lead = Vector2.Zero;
            _lastTargetPosition = target;
            return;
        }

        var velocity = _target!.GetComponent<Body>()?.Velocity
                       ?? (_lastTargetPosition is { } last ? (target - last) / seconds : Vector2.Zero);
        _lastTargetPosition = target;
        _lead = Vector2.Lerp(_lead, velocity * LookAhead, 1 - MathF.Exp(-4 * seconds));
    }

    private Vector2 ApplyDeadZone(Vector2 current, Vector2 goal)
    {
        var half = Vector2.Abs(DeadZone) / 2;
        var delta = goal - current;
        return current + new Vector2(Outside(delta.X, half.X), Outside(delta.Y, half.Y));

        static float Outside(float delta, float half) => MathF.Abs(delta) <= half ? 0 : delta - MathF.CopySign(half, delta);
    }

    // Half the visible area for a camera; a plain follower is a point.
    private Vector2 ViewHalfSize(float aspect) =>
        Entity!.GetComponent<Camera>() is { } camera ? new Vector2(camera.Size * (aspect > 0 ? aspect : 1), camera.Size) : Vector2.Zero;

    private Box? Bounds(Scene scene)
    {
        if (string.IsNullOrWhiteSpace(StayInside))
            return null;

        return EntityNames.Find(scene, StayInside, new NameLookup(Entity)) is { } area ? AreaOf(area) : null;
    }

    /// <summary>The world-space box an entity covers: its tilemap's grid, else its box collider, else its sprite.</summary>
    public static Box? AreaOf(Entity entity)
    {
        var world = entity.Transform.WorldMatrix;
        if (entity.GetComponent<Tilemap>() is { Width: > 0 } tilemap)
            return tilemap.Bounds() is var (min, max) ? Transformed(min, max, world) : null;
        if (entity.GetComponent<BoxCollider>() is { } box)
            return box.WorldBox;
        if (entity.GetComponent<Sprite>() is { } sprite)
            return Transformed(-sprite.Size / 2, sprite.Size / 2, world);
        return null;
    }

    // The box around a local-space box's corners once placed in the world.
    private static Box Transformed(Vector2 min, Vector2 max, Matrix4x4 world)
    {
        Span<Vector2> corners = [min, max, new(min.X, max.Y), new(max.X, min.Y)];
        var low = new Vector2(float.MaxValue);
        var high = new Vector2(float.MinValue);
        foreach (var corner in corners)
        {
            var point = Vector2.Transform(corner, world);
            low = Vector2.Min(low, point);
            high = Vector2.Max(high, point);
        }

        return new Box((low + high) / 2, (high - low) / 2);
    }

    // Keeps a view of half-size `half` centered on `point` inside `bounds`; a view bigger than the bounds is centered on them.
    internal static Vector2 Clamp(Vector2 point, Box bounds, Vector2 half) =>
        new(ClampAxis(point.X, bounds.Min.X + half.X, bounds.Max.X - half.X),
            ClampAxis(point.Y, bounds.Min.Y + half.Y, bounds.Max.Y - half.Y));

    private static float ClampAxis(float value, float min, float max) => min > max ? (min + max) / 2 : Math.Clamp(value, min, max);

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);

    internal static void SetWorld(Transform transform, Vector2 world)
    {
        var target = new Vector3(world, transform.WorldPosition.Z);
        if (transform.Entity?.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
            target = Vector3.Transform(target, inverse);
        transform.Position = new Vector3(target.X, target.Y, transform.Position.Z);
    }
}

/// <summary>What a <see cref="CameraControls"/>' mouse drag pans with.</summary>
public enum CameraDrag
{
    /// <summary>Dragging doesn't pan.</summary>
    None,
    Left,
    Middle,
    Right,

    /// <summary>The middle or the right button, whichever is held.</summary>
    MiddleOrRight,
}

/// <summary>
/// Pan and zoom with no code, for management, building and strategy games: put it on the camera, in place of a
/// <see cref="Follow"/>. The arrow keys and WASD (or a controller's stick) pan, a mouse button drags the world along, the
/// wheel zooms toward the mouse between <see cref="ZoomMin"/> and <see cref="ZoomMax"/>, and the mouse at the screen's
/// edge can pan too. With <see cref="StayInside"/> the view never goes past the level's edges, however big it grows. The
/// wheel and drags are the HUD's while the mouse is over it (anything pinned with a <see cref="ScreenAnchor"/>, an open
/// panel, a button); set the camera's <see cref="Camera.HudSize"/> to keep the HUD's size as it zooms.
/// </summary>
/// <remarks>It runs on real time, so the view moves while the game is paused, but not under a menu that pauses it.</remarks>
[Category("Movement")]
[Requires(typeof(Camera))]
[ComponentName("CameraControls")]
[NetLocal]
public sealed class CameraControls : Component
{
    // The world point the mouse took hold of as a drag began, while it's dragging.
    private Vector2? _held;

    /// <summary>
    /// How fast the keys and the screen's edges pan, in screen heights a second: 1 crosses the screen's height in a second,
    /// whatever the zoom.
    /// </summary>
    public float PanSpeed { get; set; } = 1;

    /// <summary>Whether the arrow keys, WASD, the D-pad and the left stick pan.</summary>
    public bool PanWithKeys { get; set; } = true;

    /// <summary>The mouse button that drags the world along with the mouse, or None.</summary>
    public CameraDrag DragButton { get; set; } = CameraDrag.MiddleOrRight;

    /// <summary>Whether resting the mouse at the screen's edge pans that way.</summary>
    public bool EdgeScroll { get; set; }

    /// <summary>The closest the wheel zooms in: the camera's smallest Size.</summary>
    public float ZoomMin { get; set; } = 3;

    /// <summary>The furthest the wheel zooms out: the camera's largest Size.</summary>
    public float ZoomMax { get; set; } = 16;

    /// <summary>How much each notch of the wheel zooms: 0.15 shows 15% less of the world (or more). 0 leaves the wheel alone.</summary>
    public float ZoomStep { get; set; } = 0.15f;

    /// <summary>
    /// The name of an entity whose area the view stays inside: a tilemap's grid, a box collider, or a sprite, as it is each
    /// frame, so the view can reach more as the level grows.
    /// </summary>
    [EntityName]
    public string? StayInside { get; set; }

    /// <summary>True while a mouse button is dragging the view.</summary>
    public bool IsDragging => _held is not null;

    /// <summary>
    /// Pans and zooms the camera by what <paramref name="input"/> says over <paramref name="seconds"/> of real time, on a
    /// view <paramref name="aspect"/> times as wide as it's high. While <paramref name="mouseOnHud"/>, the wheel and a new
    /// drag are the HUD's. Called by the game each frame.
    /// </summary>
    public void Advance(Runtime.IInput input, float seconds, float aspect, bool mouseOnHud)
    {
        if (Entity?.Scene is not { } scene || Entity.GetComponent<Camera>() is not { } camera)
            return;
        var transform = Entity.Transform;
        var position = new Vector2(transform.WorldPosition.X, transform.WorldPosition.Y);
        var start = position;
        var size = camera.Size;
        aspect = aspect > 0 ? aspect : 1;
        var mouse = input.MouseWorld;
        var mouseFree = input.IsMouseInView && !mouseOnHud;

        var pan = PanWithKeys ? input.Move : Vector2.Zero;
        if (EdgeScroll && input.IsMouseInView && _held is null)
        {
            // Where the mouse is across the view, from -1 to 1 each way; the edge is its outer 3%.
            var across = (mouse - position) / new Vector2(size * aspect, size);
            pan += new Vector2(across.X >= 0.97f ? 1 : across.X <= -0.97f ? -1 : 0, across.Y >= 0.97f ? 1 : across.Y <= -0.97f ? -1 : 0);
        }

        if (pan != Vector2.Zero && seconds > 0)
            position += (pan.LengthSquared() > 1 ? Vector2.Normalize(pan) : pan) * PanSpeed * size * 2 * seconds;

        // A drag keeps the point it took hold of under the mouse.
        if (!Held(input))
            _held = null;
        else if (_held is { } held)
            position += held - mouse;
        else if (mouseFree && Pressed(input))
            _held = mouse;

        if (ZoomStep > 0 && input.Scroll != 0 && mouseFree && _held is null)
        {
            var (low, high) = (MathF.Max(MathF.Min(ZoomMin, ZoomMax), 0.01f), MathF.Max(MathF.Max(ZoomMin, ZoomMax), 0.01f));
            var zoomed = Math.Clamp(size * MathF.Pow(1 - Math.Clamp(ZoomStep, 0, 0.9f), input.Scroll), low, high);
            if (zoomed != size)
            {
                // Toward the mouse: the point under it stays there.
                position = mouse + (position - mouse) * (zoomed / size);
                camera.Size = size = zoomed;
            }
        }

        if (!string.IsNullOrWhiteSpace(StayInside) && EntityNames.Find(scene, StayInside, new NameLookup(Entity)) is { } area &&
            Follow.AreaOf(area) is { } bounds)
            position = Follow.Clamp(position, bounds, new Vector2(size * aspect, size));
        if (position != start)
            Follow.SetWorld(transform, position);
    }

    private bool Held(Runtime.IInput input) => DragButton switch
    {
        CameraDrag.Left => input.IsDown(MouseButton.Left),
        CameraDrag.Middle => input.IsDown(MouseButton.Middle),
        CameraDrag.Right => input.IsDown(MouseButton.Right),
        CameraDrag.MiddleOrRight => input.IsDown(MouseButton.Middle) || input.IsDown(MouseButton.Right),
        _ => false,
    };

    private bool Pressed(Runtime.IInput input) => DragButton switch
    {
        CameraDrag.Left => input.WasPressed(MouseButton.Left),
        CameraDrag.Middle => input.WasPressed(MouseButton.Middle),
        CameraDrag.Right => input.WasPressed(MouseButton.Right),
        CameraDrag.MiddleOrRight => input.WasPressed(MouseButton.Middle) || input.WasPressed(MouseButton.Right),
        _ => false,
    };
}

/// <summary>
/// Makes a background layer scroll slower than the world as the camera moves, for depth: far hills at 0.2, near trees at
/// 0.7. Everything the entity and its children draw shifts with it. With <see cref="RepeatX"/> or <see cref="RepeatY"/>,
/// its sprite repeats to fill the view, so a short strip of sky or hills never runs out.
/// </summary>
/// <remarks>
/// Applied when the game is drawn, so it never changes the entity's transform: colliders, buttons and the Scene view
/// all use where the transform puts it. The layer is drawn exactly there when the camera is at the world's origin.
/// </remarks>
[Category("Rendering")]
[WorksWith(typeof(Sprite), Note = "to repeat a strip of background")]
[ComponentName("Parallax")]
[NetLocal]
public sealed class Parallax : Component
{
    /// <summary>
    /// How much the layer moves with the world on X and Y: 1 like everything else, 0 stuck to the camera (a distant sky),
    /// and in between for layers further back.
    /// </summary>
    public Vector2 Factor { get; set; } = new(0.5f, 0.5f);

    /// <summary>Repeats the entity's sprite side by side to fill the view's width.</summary>
    public bool RepeatX { get; set; }

    /// <summary>Repeats the entity's sprite top to bottom to fill the view's height.</summary>
    public bool RepeatY { get; set; }

    /// <summary>How far the layer is shifted when seen from a camera at <paramref name="camera"/>.</summary>
    public Vector2 OffsetFor(Vector2 camera) => camera * (Vector2.One - Factor);
}

/// <summary>
/// Draws the camera on its entity into part of the screen, over the main camera's view: a minimap in a corner, one
/// player's half of a split screen, a first-person window (a camera with a First Person View) inside a 2D game. The part
/// is given as fractions of the screen from its top left, so it keeps its place at any window size; the camera's own
/// <see cref="Camera.Size"/> is half the view's height, its width following the part's shape. Whatever rides inside this
/// camera (its children) is this view's own HUD, drawn in it and not in the main view; the main view's HUD (anything
/// with a <see cref="ScreenAnchor"/>, or inside the main camera) stays on top of every view, with menus and fades.
/// </summary>
/// <remarks>
/// The main view is the first camera without a viewport; with none, the first camera fills the screen and its viewport
/// draws over it too. A viewport is drawing only: it changes nothing in the game, so rewinds and network games are as
/// ever. Scripts can ask where the mouse is in a view with <c>Game.MouseIn</c>.
/// </remarks>
[Category("Rendering")]
[Requires(typeof(Camera))]
[WorksWith(typeof(Follow), Note = "to follow a player in their half of the screen")]
[ComponentName("Viewport")]
[NetLocal]
public sealed class Viewport : Component
{
    /// <summary>Where the view's left edge is, as a fraction of the screen's width from its left (0 to 1).</summary>
    public float Left { get; set; } = 0.72f;

    /// <summary>Where the view's top edge is, as a fraction of the screen's height from its top (0 to 1).</summary>
    public float Top { get; set; } = 0.04f;

    /// <summary>How wide the view is, as a fraction of the screen's width: 0.5 for half of a split screen.</summary>
    public float Width { get; set; } = 0.25f;

    /// <summary>How tall the view is, as a fraction of the screen's height.</summary>
    public float Height { get; set; } = 0.25f;

    /// <summary>Which views go over which where they overlap: higher is drawn later, on top.</summary>
    public int Order { get; set; }

    /// <summary>A frame drawn round the view; transparent for none.</summary>
    public Color Border { get; set; } = Color.FromBytes(0xF4, 0xF4, 0xF4, 0xC0);

    /// <summary>Shows the view; untick it (or switch it off) to hide it, e.g. a map shown while a key is held. Its camera never becomes the main one.</summary>
    [Powered]
    public bool Shown { get; set; } = true;

    // The part of the screen it covers, clamped to the screen: left, top, width and height as fractions of it.
    internal (float Left, float Top, float Width, float Height) Area
    {
        get
        {
            var left = Math.Clamp(Finite(Left), 0, 1);
            var top = Math.Clamp(Finite(Top), 0, 1);
            return (left, top, Math.Clamp(Finite(Width), 0, 1 - left), Math.Clamp(Finite(Height), 0, 1 - top));
        }
    }

    /// <summary>True if it shows anything: <see cref="Shown"/>, and some of the screen to show it in.</summary>
    public bool IsShowing => Shown && Area is { Width: > 0, Height: > 0 };

    /// <summary>The view's width over its height on a screen <paramref name="screenAspect"/> wide (its width over its height).</summary>
    public float AspectOn(float screenAspect)
    {
        var (_, _, width, height) = Area;
        return height > 0 && screenAspect > 0 ? width * screenAspect / height : screenAspect;
    }

    /// <summary>
    /// Takes the view's clip space (-1..1 across it) to the whole screen's, onto the part it covers: what a renderer puts
    /// after the view's camera projection.
    /// </summary>
    public Matrix4x4 ToScreen()
    {
        var (left, top, width, height) = Area;
        return Matrix4x4.CreateScale(width, height, 1) * Matrix4x4.CreateTranslation(2 * left + width - 1, 1 - 2 * top - height, 0);
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;
}

/// <summary>
/// Any component that says what the view is centred on, such as a <see cref="Follow"/> on a camera: a screen effect that
/// closes in on the action (the iris) looks there, with no entity called anything in particular. Any component can be one.
/// </summary>
public interface IViewFocus
{
    /// <summary>What the view is centred on right now, or null.</summary>
    Entity? Focus { get; }
}
