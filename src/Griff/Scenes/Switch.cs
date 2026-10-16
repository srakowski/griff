namespace Griff.Scenes;

/// <summary>How a <see cref="Switch"/> is worked.</summary>
public enum SwitchMode
{
    /// <summary>Interact (E, Enter, the gamepad's Y) while touching it flips it on, and again off: a lever.</summary>
    Lever,

    /// <summary>On while something stands on it (or touches it), off again when it steps off: a pressure plate.</summary>
    Plate,

    /// <summary>Touching it turns it on for good (or for <see cref="Switch.OnTime"/>): a button, a target to shoot.</summary>
    Button,
}

/// <summary>
/// Turns things on and off with no code: levers, pressure plates, buttons, locks. While it's on, its
/// <see cref="Targets"/> are on (a mover moves, a spawner spawns, a door unlocks, anything else appears) and its
/// <see cref="Hides"/> are hidden (a barrier, a wall of spikes). While it's off, it's the other way round, from the start
/// of the game. Needs a collider, usually a trigger.
/// </summary>
/// <remarks>
/// Names are looked for inside the switch first, so a locked block can hide itself; otherwise every entity in the scene
/// with that name is switched. If several switches name the same entity, it's on only while all of them are, so a
/// gate can need two plates held down.
/// </remarks>
[Category("Level")]
[Requires(typeof(Collider), Note = "usually a trigger")]
[ComponentName("Griff.Switch")]
public sealed class Switch : Component
{
    /// <summary>How it's switched: a lever flipped with Interact, a plate held down by standing on it, or a button pressed once.</summary>
    public SwitchMode Mode { get; set; }

    /// <summary>True while it's on. Tick it to start switched on; scripts can set it too.</summary>
    [Output("On")]
    public bool IsOn { get; set; }

    /// <summary>
    /// What it turns on, by name, separated by commas: a <see cref="Mover"/> moves (a <see cref="MoverMode.Once"/> one
    /// slides back when it's switched off again), a <see cref="Spawner"/> or <see cref="Shooter"/> runs, particles emit,
    /// an animation plays, a <see cref="Door"/> unlocks; anything else appears.
    /// </summary>
    [EntityName(Many = true)]
    public string? Targets { get; set; }

    /// <summary>What it hides while it's on, by name, separated by commas: a barrier, a locked block (even itself).</summary>
    [EntityName(Many = true)]
    public string? Hides { get; set; }

    /// <summary>The collision layers that work it, e.g. "Player" or "Player, Crate". Empty lets anything with a <see cref="Body"/>.</summary>
    [Names(NameKind.Layer, Many = true)]
    [Group("Working It")]
    public string? Layers { get; set; } = "Player";

    /// <summary>
    /// An item it needs to turn on, e.g. "Key": whoever works it must have one in their <see cref="Inventory"/>, and it's
    /// used up. It only takes it once; after that this is cleared and it works like any other switch.
    /// </summary>
    [Names(NameKind.Item)]
    public string? Key { get; set; }

    /// <summary>
    /// A flag that has to be set before it can be worked, e.g. "PowerOn" (set by a trigger's Set Flag, a dialogue's
    /// "[set PowerOn]" or a script's <c>Game.SetFlag</c>), or "not PowerOn" for while it isn't. Until then levers, plates
    /// and buttons do nothing and its prompt stays hidden; a trigger's or script's On still turns it on.
    /// </summary>
    [Names(NameKind.Flag, Condition = true)]
    public string? NeedsFlag { get; set; }

    /// <summary>Seconds it stays on before turning itself off again: a timed button. 0 stays on.</summary>
    public float OnTime { get; set; }

    /// <summary>A sound played when it turns on or off, e.g. "Sounds/click.wav".</summary>
    [Group("Sound and Messages")]
    public string? Sound { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when it turns on, with this entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? OnMessage { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when it turns off, with this entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? OffMessage { get; set; }

    /// <summary>The color its sprite turns while it's on. Transparent leaves it alone.</summary>
    [Group("Look")]
    public Color OnTint { get; set; } = Color.FromBytes(0x57, 0xAB, 0x5A);

    /// <summary>The sprite-sheet frame its sprite shows while it's on (a lever pulled the other way). -1 leaves it alone.</summary>
    public int OnFrame { get; set; } = -1;

    /// <summary>
    /// The name of an entity to show only while it can be worked with Interact, e.g. a "Press E" hint above a lever. It's
    /// hidden the rest of the time.
    /// </summary>
    [EntityName]
    [Group("Working It")]
    public string? Prompt { get; set; }

    /// <summary>
    /// Steps to the music of a <see cref="BeatClock"/>: flips itself on or off on every this many beats, as well as however
    /// it's worked (though a plate stays as it's held): spikes that come and go, a gate that opens on the bar. 0 doesn't.
    /// </summary>
    [Group("Beat")]
    public int Beats { get; set; }

    /// <summary>Steps this many beats later: with two on every 2 beats, one 1 beat later, they take turns.</summary>
    [ShownWhen(nameof(Beats))]
    public int BeatOffset { get; set; }

    /// <summary>The entity whose <see cref="BeatClock"/> it steps to. Empty follows the first in the scene.</summary>
    [EntityName]
    [ShownWhen(nameof(Beats))]
    public string? Clock { get; set; }

    /// <summary>Raised when it turns on or off, with whether it's now on.</summary>
    public event Action<bool>? Changed;

    /// <summary>True if something on collision layer <paramref name="layer"/> can work it.</summary>
    public bool AcceptsLayer(string? layer)
    {
        var layers = CollisionLayers.Parse(Layers);
        return layers.Length == 0 || CollisionLayers.Contains(layers, layer);
    }

    /// <summary>Turns it on, as working it does (sounding, sending its On Message and switching its targets).</summary>
    [Action("On")]
    public void TurnOn() => IsOn = true;

    /// <summary>Turns it off.</summary>
    [Action("Off")]
    public void TurnOff() => IsOn = false;

    /// <summary>Turns it on if it's off, and off if it's on.</summary>
    [Action]
    public void Toggle() => IsOn = !IsOn;

    /// <summary>
    /// The entities a list of names (<see cref="Targets"/> or <see cref="Hides"/>) means: for each name, those inside
    /// <paramref name="owner"/> (itself included) if there are any, else every entity in the scene called that.
    /// </summary>
    public static IEnumerable<Entity> Named(Entity owner, string? names)
    {
        foreach (var name in (names ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var found = EntityNames.All(owner, name);
            foreach (var entity in found)
                yield return entity;
        }
    }

    internal void RaiseChanged() => Changed?.Invoke(IsOn);

    // What the game last saw, to notice changes made by scripts; how long it has been on; its sprite before it was on.
    internal bool? Shown { get; set; }

    internal float OnFor { get; set; }

    internal Color? RestingTint { get; set; }

    internal int? RestingFrame { get; set; }
}
