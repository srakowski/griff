using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// Something to climb: a <see cref="PlatformerMovement"/> touching its collider climbs it with up and down, without
/// gravity, and jumps off it with Jump. Give it a trigger collider as tall as the ladder. (Tilemaps have
/// <see cref="Tilemap.LadderTiles"/> for ladders painted into the level.) Put a one-way platform at the top to stand on;
/// climbing passes through it both ways.
/// </summary>
[Category("Level")]
[Requires(typeof(Collider), Note = "a trigger as tall as the ladder")]
[ComponentName("Griff.Ladder")]
public sealed class Ladder : Component
{
    /// <summary>Line the climber up with the middle of the ladder when it grabs on, so it fits through a gap in the floor.</summary>
    public bool Center { get; set; } = true;
}

/// <summary>
/// A crate that bodies push along by walking into it, with no code: sideways in a platformer, any way in a top-down game
/// (when its <see cref="Body"/>'s Gravity Scale is 0). It pushes crates in front of it in turn, and stops at walls. Needs
/// a <see cref="Body"/> and a solid collider. Give it a layer (say "Crate") to have it hold a pressure plate down. With a
/// <see cref="GridMovement"/> too it's a Sokoban crate: a grid mover stepping into it pushes it a cell (gliding along with
/// it), unless a wall or another crate is in the way, and resting on a <see cref="Goal"/> covers it.
/// </summary>
[Category("Level")]
[Requires(typeof(Body))]
[Requires(typeof(Collider), Note = "solid, not a trigger")]
[WorksWith(typeof(GridMovement), Note = "pushed a cell at a time by grid movers")]
[WorksWith(typeof(Goal), Note = "places on the grid to push it to")]
[ComponentName("Griff.Pushable")]
public sealed class Pushable : Component
{
    /// <summary>The collision layers that can push it, e.g. "Player". Empty lets any body push it.</summary>
    [Names(NameKind.Layer, Many = true)]
    public string? Layers { get; set; } = "Player";

    /// <summary>
    /// How fast it goes compared to whoever pushes it: 1 is as fast, 0.5 half as fast (a heavy crate), 0 won't budge. On a
    /// grid it always keeps pace with its pusher, and 0 still won't budge.
    /// </summary>
    public float Speed { get; set; } = 0.6f;

    /// <summary>
    /// On a grid, pushing it into another crate pushes that one too, so a row slides together. Off, a crate behind it
    /// blocks it, as in Sokoban.
    /// </summary>
    [Group("Grid")]
    public bool PushesRow { get; set; }

    /// <summary>What kind of crate it is, e.g. "Red": a <see cref="Goal"/>'s Accepts names the kinds that cover it.</summary>
    public string? Kind { get; set; }

    /// <summary>The goal it rests on, or null. Kept up to date while the game runs.</summary>
    [DoNotSerialize]
    public Entity? OnGoal { get; internal set; }

    /// <summary>Raised when a grid mover pushes it a cell, with the pusher and the way it's pushed.</summary>
    public event Action<Entity, Vector2>? Pushed;

    /// <summary>True if something on collision layer <paramref name="layer"/> can push it.</summary>
    public bool AcceptsLayer(string? layer)
    {
        var layers = CollisionLayers.Parse(Layers);
        return layers.Length == 0 || CollisionLayers.Contains(layers, layer);
    }

    // The color the goal it rests on turns it, drawn instead of its sprite's own (not saved, so a rewind never keeps it).
    internal Color? GoalTint { get; set; }

    internal void RaisePushed(Entity pusher, Vector2 way) => Pushed?.Invoke(pusher, way);
}

/// <summary>
/// A spring, jump pad or bumper, with no code: bodies touching it from the <see cref="Direction"/> side are sent flying that
/// way at <see cref="Strength"/>. Works on solid colliders (land on it) and triggers (pass through it). A
/// <see cref="PlatformerMovement"/> launched by it gets its air jumps and dash back.
/// </summary>
[Category("Level")]
[Requires(typeof(Collider))]
[ComponentName("Griff.Bouncer")]
public sealed class Bouncer : Component
{
    /// <summary>How fast it sends things off, in units per second.</summary>
    public float Strength { get; set; } = 20;

    /// <summary>Which way it sends them: (0, 1) is up, (1, 0) right, (1, 1) up and to the right.</summary>
    public Vector2 Direction { get; set; } = Vector2.UnitY;

    /// <summary>The collision layers it launches, e.g. "Player". Empty launches any body.</summary>
    [Names(NameKind.Layer, Many = true)]
    public string? Layers { get; set; }

    /// <summary>A sound played on each bounce, e.g. "Sounds/boing.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>Squash and stretch its entity with each bounce.</summary>
    public bool Squash { get; set; } = true;

    /// <summary>Raised with whatever it launched.</summary>
    public event Action<Entity>? Bounced;

    /// <summary>True if it launches things on collision layer <paramref name="layer"/>.</summary>
    public bool AcceptsLayer(string? layer)
    {
        var layers = CollisionLayers.Parse(Layers);
        return layers.Length == 0 || CollisionLayers.Contains(layers, layer);
    }

    /// <summary>The <see cref="Direction"/> as a unit vector (up if it's zero).</summary>
    public Vector2 Way => Direction.LengthSquared() > 1e-8f ? Vector2.Normalize(Direction) : Vector2.UnitY;

    internal void RaiseBounced(Entity entity) => Bounced?.Invoke(entity);

    // Each body it launched lately, and how long ago, so one bounce sounds and squashes once however many frames it touches.
    internal Dictionary<Entity, float> Recent { get; } = [];

    internal Vector3? RestingScale { get; set; }
}

/// <summary>
/// A platform that gives way, with no code: once something on its <see cref="Layers"/> stands on it, it shakes for
/// <see cref="Delay"/> seconds and falls away, coming back <see cref="Respawn"/> seconds later. Needs a collider.
/// </summary>
[Category("Level")]
[Requires(typeof(Collider))]
[ComponentName("Griff.Crumble")]
public sealed class Crumble : Component
{
    /// <summary>Seconds it shakes, once stood on, before it falls away.</summary>
    public float Delay { get; set; } = 0.5f;

    /// <summary>Seconds until it comes back. 0 never comes back.</summary>
    public float Respawn { get; set; } = 3;

    /// <summary>The collision layers that set it off by standing on it, e.g. "Player". Empty is any body.</summary>
    [Names(NameKind.Layer, Many = true)]
    public string? Layers { get; set; } = "Player";

    /// <summary>A sound played as it falls away, e.g. "Sounds/crumble.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>A prefab spawned where it was as it falls away, e.g. a burst of "Rubble".</summary>
    [Names(NameKind.Prefab)]
    public string? Effect { get; set; }

    /// <summary>True while it's gone (its entity is hidden). Saved, so it stays gone when the game rewinds.</summary>
    [Group("State")]
    public bool Crumbled { get; set; }

    /// <summary>True while it's shaking, about to go.</summary>
    [DoNotSerialize]
    public bool IsShaking => !Crumbled && !float.IsNaN(Left);

    /// <summary>How far it's shaken off its place right now; it's drawn there, while its collider stays put.</summary>
    [DoNotSerialize]
    public Vector2 ShakeOffset { get; internal set; }

    /// <summary>Raised when it falls away.</summary>
    public event Action<Crumble>? Fell;

    /// <summary>Raised when it comes back.</summary>
    public event Action<Crumble>? Returned;

    /// <summary>True if something on collision layer <paramref name="layer"/> sets it off.</summary>
    public bool AcceptsLayer(string? layer)
    {
        var layers = CollisionLayers.Parse(Layers);
        return layers.Length == 0 || CollisionLayers.Contains(layers, layer);
    }

    internal void RaiseFell() => Fell?.Invoke(this);

    internal void RaiseReturned() => Returned?.Invoke(this);

    // Seconds left shaking, or gone; NaN when neither has started.
    internal float Left { get; set; } = float.NaN;

    internal float Age { get; set; }
}
