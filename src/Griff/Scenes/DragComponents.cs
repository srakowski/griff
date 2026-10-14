using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// Something the player picks up with the mouse and drops somewhere else: a card, a puzzle piece, an item in an
/// inventory, a word to put in order. Press on it to pick it up (it's drawn on top while held), let go to drop it. Dropped
/// on a <see cref="DropTarget"/> that takes it, it lands in the target's middle; dropped on one that doesn't (full, or for
/// other kinds), it slides back where it came from. With a controller or keys, a <see cref="Grabber"/> picks it up instead.
/// </summary>
/// <remarks>
/// It's picked up by its collider's shape, or its sprite's if it has no collider, and lands by its middle. In a network
/// game the host moves it for whoever holds it, and two players can't hold the same thing.
/// </remarks>
[Category("Gameplay")]
[WorksWith(typeof(DropTarget), Note = "places to drop it")]
[WorksWith(typeof(Grabber), Note = "picks it up with a button")]
[ComponentName("Griff.Draggable")]
public sealed class Draggable : Component, IDrawEffect
{
    // Held, it (and what's inside it) draws over everything else.
    bool IDrawEffect.DrawsOnTop => IsHeld;

    /// <summary>What kind of thing it is, e.g. "Card" or "Red Gem": a <see cref="DropTarget"/>'s Accepts names the kinds it takes.</summary>
    public string? Kind { get; set; }

    /// <summary>
    /// True if it only goes on targets: let go anywhere else, it slides back where it was picked up. Otherwise it stays
    /// where it's let go.
    /// </summary>
    public bool OnlyOnTargets { get; set; }

    /// <summary>
    /// Let go off a target, it lands in the middle of a square this big (1 matches a tilemap's cells), so things line up.
    /// 0 leaves it where it's let go.
    /// </summary>
    public float Grid { get; set; }

    /// <summary>
    /// True if it stays on the first target it's dropped on that takes it: a jigsaw piece in its place, a word in its gap.
    /// It can't be picked up again.
    /// </summary>
    public bool StaysOnTarget { get; set; }

    /// <summary>Seconds it takes to slide into place: onto a target's middle, the grid, or back where it came from. 0 jumps.</summary>
    public float SlideTime { get; set; } = 0.1f;

    /// <summary>A sound played when it's picked up, e.g. "Sounds/pick.wav".</summary>
    [Group("Sound and Messages")]
    public string? PickUpSound { get; set; }

    /// <summary>A sound played when it's let go, e.g. "Sounds/drop.wav".</summary>
    public string? DropSound { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when it's picked up, with this entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? PickUpMessage { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when it's let go, with this entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? DropMessage { get; set; }

    /// <summary>True while someone holds it, with the mouse or a <see cref="Grabber"/>.</summary>
    [DoNotSerialize]
    public bool IsHeld { get; internal set; }

    /// <summary>The drop target it's on, or null. It's on a target while it rests with its middle inside the target's shape.</summary>
    [DoNotSerialize]
    public Entity? Target { get; internal set; }

    /// <summary>Raised when it's picked up.</summary>
    public event Action? PickedUp;

    /// <summary>Raised when it's let go, with the drop target it lands on, or null if it lands anywhere else (or goes back).</summary>
    public event Action<Entity?>? Dropped;

    // Its sprite's color before a grabber lit it up.
    internal Color? RestingTint { get; set; }

    internal void RaisePickedUp() => PickedUp?.Invoke();

    internal void RaiseDropped(Entity? target) => Dropped?.Invoke(target);
}

/// <summary>
/// A place to drop <see cref="Draggable"/> things: a slot in an inventory, a card's place on the table, a hole a puzzle
/// piece fits, a bin. Something dropped on it that it <see cref="Accepts"/> lands in its middle, until it holds
/// <see cref="Capacity"/> things. It lights up with <see cref="HoverTint"/> while something it would take is held over it.
/// </summary>
/// <remarks>
/// Its shape is its collider's, or its sprite's if it has no collider. What rests with its middle inside that shape is on
/// it, however it got there: dropped, placed there in the editor, or moved by a script.
/// </remarks>
[Category("Gameplay")]
[WorksWith(typeof(Draggable), Note = "things to drop on it")]
[ComponentName("Griff.DropTarget")]
public sealed class DropTarget : Component
{
    /// <summary>The kinds of thing it takes (each a draggable's Kind), separated by commas, e.g. "Red Gem, Blue Gem". Empty takes anything.</summary>
    public string? Accepts { get; set; }

    /// <summary>How many things it holds at once; more are turned away. 0 holds any number: a bin, a discard pile.</summary>
    public int Capacity { get; set; } = 1;

    /// <summary>True if things dropped on it move to its middle. Off, they stay where they're let go on it: a play area.</summary>
    public bool Snap { get; set; } = true;

    /// <summary>
    /// True if dropping something on it while it's full swaps them: what was on it goes where the dropped thing came from,
    /// as in an inventory. Only with a <see cref="Capacity"/> of 1.
    /// </summary>
    public bool Swap { get; set; }

    /// <summary>The color its sprite turns while something it would take is held over it. Transparent leaves it alone.</summary>
    [Group("Look and Messages")]
    public Color HoverTint { get; set; } = Color.FromBytes(0xFF, 0xE0, 0x82);

    /// <summary>A message sent with <c>Game.Send</c> when something lands on it, with this entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? DropMessage { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when something is taken off it, with this entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? TakeMessage { get; set; }

    /// <summary>
    /// A message sent once every drop target with this same All Full Message is full (holds its Capacity, or something for
    /// a Capacity of 0), with this entity as its data: every piece in place, the puzzle solved. It's sent again if they
    /// fill up again after one is emptied.
    /// </summary>
    [Names(NameKind.Message)]
    public string? AllFullMessage { get; set; }

    /// <summary>What's on it, in the order they arrived.</summary>
    [DoNotSerialize]
    public IReadOnlyList<Entity> Contents => _contents;

    /// <summary>True when it holds its <see cref="Capacity"/> (or, with a Capacity of 0, anything at all).</summary>
    [DoNotSerialize]
    public bool IsFull => Capacity > 0 ? _contents.Count >= Capacity : _contents.Count > 0;

    /// <summary>Raised when something lands on it.</summary>
    public event Action<Entity>? Received;

    /// <summary>Raised when something is taken off it.</summary>
    public event Action<Entity>? Taken;

    /// <summary>True if it takes things of kind <paramref name="kind"/> (a draggable's Kind).</summary>
    public bool AcceptsKind(string? kind)
    {
        var kinds = (Accepts ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return kinds.Length == 0 || kinds.Contains((kind ?? "").Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private readonly List<Entity> _contents = [];

    internal void SetContents(IEnumerable<Entity> contents)
    {
        _contents.Clear();
        _contents.AddRange(contents);
    }

    // What the game last saw on it, to notice what arrived and left; its sprite's color before it lit up.
    internal List<Entity>? Seen { get; set; }

    internal Color? RestingTint { get; set; }

    internal void RaiseReceived(Entity thing) => Received?.Invoke(thing);

    internal void RaiseTaken(Entity thing) => Taken?.Invoke(thing);
}

/// <summary>
/// Picks up <see cref="Draggable"/> things with a button and carries them, for games played with a controller or keys: a
/// cursor moved with the stick or the D-pad (give it a <see cref="TopDownMovement"/> or a <see cref="GridMovement"/>), or
/// a character who carries things about. Press <see cref="Action"/> to pick up what's under its hand (or else the nearest
/// thing within <see cref="Reach"/>), and again to put it down where it's held, onto a <see cref="DropTarget"/> as a
/// mouse would.
/// </summary>
/// <remarks>
/// It holds things by their middle at its hand: its own middle, moved by <see cref="HoldOffset"/>. A drop the mouse would
/// send back (onto a full target or one for other kinds, or off targets for something that only goes on them) does
/// nothing, so it keeps holding the thing. Dropped onto a full target that swaps, it picks up what was there. In a network
/// game each player's grabber answers to its own player.
/// </remarks>
[Category("Gameplay")]
[WorksWith(typeof(Draggable), Note = "things to pick up")]
[WorksWith(typeof(DropTarget), Note = "places to put them")]
[ComponentName("Griff.Grabber")]
public sealed class Grabber : Component
{
    /// <summary>The input action that picks things up and puts them down, e.g. "Interact" (E, Enter or the gamepad's Y) or "Jump" (Space or A).</summary>
    [Names(NameKind.Action)]
    public string? Action { get; set; } = "Interact";

    /// <summary>
    /// True if it only carries things while <see cref="Action"/> is held, putting them down when it's let go, as a mouse
    /// does. Otherwise one press picks up and the next puts down.
    /// </summary>
    public bool HoldToCarry { get; set; }

    /// <summary>
    /// How far from its hand a thing's middle can be and still be picked up, in world units, if nothing is right under its
    /// hand. 0 only picks up what's under it.
    /// </summary>
    [Distance(Center = nameof(HoldOffset))]
    public float Reach { get; set; } = 1;

    /// <summary>
    /// Where it holds things, from its middle, in world units: X is how far in front of it (on the side it faces: left or
    /// right, or up or down in a top-down game), Y how far up. (0, 1) is over a character's head, (0.8, 0) in front of it,
    /// (0, 0) a cursor's middle.
    /// </summary>
    public Vector2 HoldOffset { get; set; }

    /// <summary>The color what it would pick up turns, so the player can see what a press takes. Transparent leaves things alone.</summary>
    [Group("Look and Messages")]
    public Color Highlight { get; set; } = Color.FromBytes(0xFF, 0xE0, 0x82);

    /// <summary>A sound played when a press does nothing: nothing in reach, or nowhere to put what it holds.</summary>
    public string? RefuseSound { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when it picks something up, with what it picked up as its data.</summary>
    [Names(NameKind.Message)]
    public string? PickUpMessage { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when it puts something down, with what it put down as its data.</summary>
    [Names(NameKind.Message)]
    public string? DropMessage { get; set; }

    /// <summary>What it's carrying (a <see cref="Draggable"/>'s entity), or null.</summary>
    [DoNotSerialize]
    public Entity? Holding { get; internal set; }

    /// <summary>What a press would pick up now (lit with <see cref="Highlight"/>), or null. Null while it's carrying something.</summary>
    [DoNotSerialize]
    public Entity? InReach { get; internal set; }

    /// <summary>Raised when it picks something up, with what it picked up.</summary>
    public event Action<Entity>? PickedUp;

    /// <summary>Raised when it puts something down, with what it put down and the drop target it landed on (or null).</summary>
    public event Action<Entity, Entity?>? Dropped;

    /// <summary>Acts as if <see cref="Action"/> were pressed: picks up what's in reach, or puts down what it holds. For scripts.</summary>
    public void Press() => Pressed = true;

    internal bool Pressed { get; set; }

    internal void RaisePickedUp(Entity thing) => PickedUp?.Invoke(thing);

    internal void RaiseDropped(Entity thing, Entity? target) => Dropped?.Invoke(thing, target);
}
