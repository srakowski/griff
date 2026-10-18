using System.Numerics;

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
/// <see cref="Follow"/>. The PanUp, PanDown, PanLeft and PanRight actions (WASD, the arrow keys and the D-pad, rebound in
/// Game &gt; Controls or by the player) and a controller's stick pan, a mouse button drags the world along, the
/// wheel zooms toward the mouse between <see cref="ZoomMin"/> and <see cref="ZoomMax"/>, and the mouse at the screen's
/// edge can pan too. With <see cref="StayInside"/> the view never goes past the level's edges, however big it grows. The
/// wheel and drags are the HUD's while the mouse is over it (anything pinned with a <see cref="ScreenAnchor"/>, an open
/// panel, a button); set the camera's <see cref="Camera.HudSize"/> to keep the HUD's size as it zooms.
/// </summary>
/// <remarks>
/// It runs on real time, so the view moves while the game is paused, but not under a menu that pauses it. For a "look
/// at this" moment (a tour, a cutscene, a new building), <see cref="FocusOn(Entity, float?, float?)"/> takes the camera
/// over: it glides to a character and follows it at a closer Size, the player's pan, drag and wheel waiting, until
/// <see cref="Release"/> zooms back out and hands the camera back where it is. A Trigger or a Button can do both too.
/// </remarks>
[Category("Movement")]
[Requires(typeof(Camera))]
[ComponentName("CameraControls")]
[NetLocal]
public sealed class CameraControls : Component
{
    // While focused: the entity followed (null for a point, or once it's gone) and where it last was, the Size to zoom to
    // (0 keeps the camera's), and the glide over: where and at what Size it set off, how long it's been going and how long
    // it takes (real seconds).
    private bool _focused;
    private Entity? _focusEntity;
    private Vector2 _focusPoint;
    private float _focusZoom;
    private (Vector2 From, float FromSize, float Elapsed, float Seconds)? _glide;

    // Set by GlideTo: the focus ends on arrival, at the Size it has, leaving the camera the player's.
    private bool _handBack;

    // The Size the camera had before it was focused, and once released, the zoom back to it.
    private float _sizeBefore;
    private (float From, float Elapsed, float Seconds)? _zoomBack;

    /// <summary>
    /// How fast the keys and the screen's edges pan, in screen heights a second: 1 crosses the screen's height in a second,
    /// whatever the zoom.
    /// </summary>
    public float PanSpeed { get; set; } = 1;

    /// <summary>
    /// Whether the PanUp, PanDown, PanLeft and PanRight input actions (WASD, the arrow keys and the D-pad unless the game
    /// or the player rebinds them) and the left stick pan.
    /// </summary>
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
    public bool IsDragging => HeldAt is not null;

    // The world point the mouse took hold of as a drag began, while it's dragging (see Runtime.ViewSystem).
    internal Vector2? HeldAt { get; set; }

    /// <summary>
    /// The camera's Size while it's focused on something (see <see cref="FocusOn(Entity, float?, float?)"/>), smaller to
    /// look closer: what it zooms to when not told. 0 keeps the Size it has.
    /// </summary>
    public float FocusSize { get; set; }

    /// <summary>
    /// How many seconds (of real time) the camera takes to glide over to what it focuses on, zooming as it goes, and to zoom
    /// back once released. 0 jumps.
    /// </summary>
    public float FocusSeconds { get; set; } = 0.75f;

    /// <summary>
    /// True from <see cref="FocusOn(Entity, float?, float?)"/> until the camera is the player's again (after
    /// <see cref="Release"/>'s zoom back): the player's pan, drag and wheel wait meanwhile.
    /// </summary>
    public bool IsFocused => _focused || _zoomBack is not null;

    /// <summary>True while a <see cref="GlideTo(Vector2, float?, float?)"/> is under way: the player's pan, drag or wheel cancels it.</summary>
    public bool IsGliding => _focused && _handBack;

    /// <summary>The entity the camera is following while focused; null when it's on a point, or not focused.</summary>
    public Entity? FocusTarget => _focused ? _focusEntity : null;

    /// <summary>
    /// Takes the camera over to follow <paramref name="target"/>: it glides there in <paramref name="seconds"/> (or
    /// <see cref="FocusSeconds"/>), zooming to <paramref name="size"/> (or <see cref="FocusSize"/>, and with neither keeping
    /// its Size), then keeps it in the middle of the view as it moves, inside <see cref="StayInside"/>. The player's pan,
    /// drag and wheel wait until <see cref="Release"/>. Focusing on something else while focused glides on from there.
    /// </summary>
    public void FocusOn(Entity target, float? size = null, float? seconds = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        Focus(target, Flat(target.Transform.WorldPosition), size, seconds);
    }

    /// <summary>Takes the camera over to look at a point in the world, as <see cref="FocusOn(Entity, float?, float?)"/> does a character.</summary>
    public void FocusOn(Vector2 point, float? size = null, float? seconds = null) => Focus(null, point, size, seconds);

    /// <summary>
    /// Takes the camera over to follow the entity with this name (the nearest, if there are several), zooming to
    /// <see cref="FocusSize"/> over <see cref="FocusSeconds"/>, until Release. Nothing happens if there's none.
    /// </summary>
    [Action]
    public void FocusOn(string? name)
    {
        if (Entity?.Scene is { } scene && !string.IsNullOrWhiteSpace(name) && EntityNames.Find(scene, name, new NameLookup(Entity)) is { } target)
            FocusOn(target);
    }

    /// <summary>
    /// Glides the camera over to <paramref name="target"/> in <paramref name="seconds"/> (or <see cref="FocusSeconds"/>) and
    /// leaves it the player's there: unlike <see cref="FocusOn(Entity, float?, float?)"/> it keeps no hold on the camera,
    /// so there is no Release to time. It keeps the camera's Size unless <paramref name="size"/> is given (it zooms to that
    /// as it goes, and stays). The player's pan keys, a drag or the wheel during the glide cancel it where the camera is.
    /// </summary>
    public void GlideTo(Entity target, float? size = null, float? seconds = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        Focus(target, Flat(target.Transform.WorldPosition), size ?? 0, seconds);
        _handBack = true;
    }

    /// <summary>Glides the camera over to a point in the world and leaves it the player's, as <see cref="GlideTo(Entity, float?, float?)"/> does.</summary>
    public void GlideTo(Vector2 point, float? size = null, float? seconds = null)
    {
        Focus(null, point, size ?? 0, seconds);
        _handBack = true;
    }

    /// <summary>
    /// Glides the camera over to the entity with this name (the nearest, if there are several) over
    /// <see cref="FocusSeconds"/> at the Size it has, and leaves it the player's there. Nothing happens if there's none.
    /// </summary>
    [Action]
    public void GlideTo(string? name)
    {
        if (Entity?.Scene is { } scene && !string.IsNullOrWhiteSpace(name) && EntityNames.Find(scene, name, new NameLookup(Entity)) is { } target)
            GlideTo(target);
    }

    /// <summary>Stops a <see cref="GlideTo(Entity, float?, float?)"/> where the camera is, with the camera the player's.</summary>
    internal void CancelGlide()
    {
        if (!IsGliding)
            return;
        _focused = false;
        _handBack = false;
        _focusEntity = null;
        _glide = null;
    }

    /// <summary>
    /// Hands the camera back to the player where it is: it zooms back to the Size it had before it was focused, over
    /// <see cref="FocusSeconds"/>, and then the player's pan, drag and wheel work again.
    /// </summary>
    [Action]
    public void Release()
    {
        if (!_focused)
            return;
        _focused = false;
        _handBack = false;
        _focusEntity = null;
        _glide = null;
        if (Entity?.GetComponent<Camera>() is not { } camera || _sizeBefore <= 0)
            return;
        if (FocusSeconds > 0 && camera.Size != _sizeBefore)
            _zoomBack = (camera.Size, 0, FocusSeconds);
        else
            camera.Size = _sizeBefore;
    }

    private void Focus(Entity? target, Vector2 point, float? size, float? seconds)
    {
        var camera = Entity?.GetComponent<Camera>();
        // A second focus (the next stop of a tour), or one while zooming back, still goes back to the Size from before the first.
        if (!IsFocused)
            _sizeBefore = camera?.Size ?? 0;
        _zoomBack = null;
        _focused = true;
        _focusEntity = target;
        _focusPoint = point;
        _focusZoom = MathF.Max(size ?? FocusSize, 0);
        _handBack = false;
        HeldAt = null;
        var from = Entity is { } entity ? Flat(entity.Transform.WorldPosition) : point;
        _glide = (from, camera?.Size ?? 0, 0, MathF.Max(seconds ?? FocusSeconds, 0));
    }

    /// <summary>
    /// Moves a focused camera on by <paramref name="seconds"/> of real time, on a view <paramref name="aspect"/> times as
    /// wide as it's high: gliding and zooming to what it's on, following it, or zooming back once released. Called by the
    /// game in place of the player's controls while <see cref="IsFocused"/>.
    /// </summary>
    internal void AdvanceFocus(float seconds, float aspect)
    {
        if (Entity is not { Scene: not null } entity || entity.GetComponent<Camera>() is not { } camera)
            return;
        var transform = entity.Transform;
        var start = Flat(transform.WorldPosition);
        var position = start;
        var size = camera.Size;
        var jumped = false;

        if (_zoomBack is { } back)
        {
            var elapsed = back.Elapsed + seconds;
            var t = Eased(elapsed, back.Seconds);
            size = back.From + (_sizeBefore - back.From) * t;
            _zoomBack = t >= 1 ? null : back with { Elapsed = elapsed };
        }
        else if (_focused)
        {
            // Following an entity that has gone: it stays on where that last was.
            if (_focusEntity is { } followed)
            {
                if (followed.Scene == entity.Scene)
                    _focusPoint = Flat(followed.Transform.WorldPosition);
                else
                    _focusEntity = null;
            }

            var zoom = _focusZoom > 0 ? _focusZoom : size;
            var goal = _focusPoint;
            if (_glide is { } glide)
            {
                var elapsed = glide.Elapsed + seconds;
                var t = Eased(elapsed, glide.Seconds);
                size = glide.FromSize > 0 ? glide.FromSize + (zoom - glide.FromSize) * t : zoom;
                goal = Keep(goal, size, aspect);
                position = Vector2.Lerp(glide.From, goal, t);
                jumped = glide.Seconds <= 0;
                _glide = t >= 1 ? null : glide with { Elapsed = elapsed };
                if (t >= 1 && _handBack)
                {
                    // Arrived: the camera is the player's again, at the Size it has.
                    _focused = false;
                    _handBack = false;
                    _focusEntity = null;
                }
            }
            else
            {
                size = zoom;
                position = goal;
            }
        }

        camera.Size = size;
        position = Keep(position, size, aspect);
        if (position == start)
            return;
        Follow.SetWorld(transform, position);
        if (jumped)
            transform.Jumped();
    }

    // Where a view of `size` centred on `point` goes to stay inside StayInside.
    private Vector2 Keep(Vector2 point, float size, float aspect) =>
        Bounds() is { } bounds ? Follow.Clamp(point, bounds, new Vector2(size * (aspect > 0 ? aspect : 1), size)) : point;

    /// <summary>The area <see cref="StayInside"/> names, as it is now, or null.</summary>
    internal Box? Bounds() =>
        Entity?.Scene is { } scene && !string.IsNullOrWhiteSpace(StayInside) && EntityNames.Find(scene, StayInside, new NameLookup(Entity)) is { } area
            ? Follow.AreaOf(area)
            : null;

    // How far along an ease in and out is after `elapsed` of `seconds`, from 0 to 1; done at once with no time.
    private static float Eased(float elapsed, float seconds)
    {
        var t = seconds > 0 ? Math.Clamp(elapsed / seconds, 0, 1) : 1;
        return t * t * (3 - 2 * t);
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
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
/// Draws the entity and everything inside it as one thing among the rest: a pallet with a stack of crates on it, a
/// machine with its operator, a truck with its label. The group sorts at its root's Order and Z and, with the camera's
/// <see cref="Camera.SortByY"/>, at where the root stands (the bottom of its sprite, or its position if it has none),
/// so a character walking past draws in front of the whole stack while lower down and behind all of it once above.
/// Inside the group, its parts draw by their own Order, then in Hierarchy order, wherever they are on the screen.
/// </summary>
/// <remarks>
/// Without it, each part of a tall thing sorts at its own feet, so the top of a stack sorts behind its bottom and a
/// character can end up between them. A group inside a group sorts as part of the outer one. The root's Order is the
/// group's; with nothing drawn on the root, it's that of its first part that draws. A part being dragged (see
/// <see cref="Draggable"/>) still draws over everything. A tilemap inside a group draws as one, its rows not sorting
/// apart.
/// </remarks>
[Category("Rendering")]
[WorksWith(typeof(Sprite), Note = "on the root, whose feet the group sorts at")]
[ComponentName("SortGroup")]
public sealed class SortGroup : Component
{
    /// <summary>
    /// How far above the root's feet the group sorts, in world units (below for less than 0): for a root whose picture
    /// has empty space under where it stands, or a group whose root draws nothing. Only matters with the camera's
    /// <see cref="Camera.SortByY"/>.
    /// </summary>
    public float GroundOffset { get; set; }
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
