using System.Globalization;
using System.Numerics;

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
    public string? Layers { get; set; } = "Player";

    /// <summary>
    /// An item it needs to turn on, e.g. "Key": whoever works it must have one in their <see cref="Inventory"/>, and it's
    /// used up. It only takes it once; after that this is cleared and it works like any other switch.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>Seconds it stays on before turning itself off again: a timed button. 0 stays on.</summary>
    public float OnTime { get; set; }

    /// <summary>A sound played when it turns on or off, e.g. "Sounds/click.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when it turns on, with this entity as its data.</summary>
    public string? OnMessage { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when it turns off, with this entity as its data.</summary>
    public string? OffMessage { get; set; }

    /// <summary>The color its sprite turns while it's on. Transparent leaves it alone.</summary>
    public Color OnTint { get; set; } = Color.FromBytes(0x57, 0xAB, 0x5A);

    /// <summary>The sprite-sheet frame its sprite shows while it's on (a lever pulled the other way). -1 leaves it alone.</summary>
    public int OnFrame { get; set; } = -1;

    /// <summary>
    /// The name of an entity to show only while it can be worked with Interact, e.g. a "Press E" hint above a lever. It's
    /// hidden the rest of the time.
    /// </summary>
    [EntityName]
    public string? Prompt { get; set; }

    /// <summary>Raised when it turns on or off, with whether it's now on.</summary>
    public event Action<bool>? Changed;

    /// <summary>True if something on collision layer <paramref name="layer"/> can work it.</summary>
    public bool AcceptsLayer(string? layer)
    {
        var layers = CollisionLayers.Parse(Layers);
        return layers.Length == 0 || CollisionLayers.Contains(layers, layer);
    }

    /// <summary>
    /// The entities a list of names (<see cref="Targets"/> or <see cref="Hides"/>) means: for each name, those inside
    /// <paramref name="owner"/> (itself included) if there are any, else every entity in the scene called that.
    /// </summary>
    public static IEnumerable<Entity> Named(Entity owner, string? names)
    {
        foreach (var name in (names ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var inside = owner.SelfAndDescendants().Where(e => e.Name == name).ToList();
            var found = inside.Count > 0 || owner.Scene is null ? inside : owner.Scene.Entities.Where(e => e.Name == name).ToList();
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

/// <summary>
/// What an entity is carrying: keys, potions, the parts of a machine. A <see cref="Pickup"/> with an
/// <see cref="Pickup.Item"/> puts things in the inventory of whoever collects it (adding one if they don't have one);
/// locked <see cref="Door"/>s and <see cref="Switch"/>es use up its keys. A <see cref="Counter"/> with an
/// <see cref="Counter.Item"/> shows how many there are.
/// </summary>
/// <remarks>It's saved with the scene, so it rewinds with everything else and you can start a playtest holding a key.</remarks>
[Category("Gameplay")]
[ComponentName("Griff.Inventory")]
public sealed class Inventory : Component
{
    // Names in the order first added, so the Inspector's list doesn't shuffle.
    private readonly List<(string Name, int Count)> _items = [];
    private IReadOnlyList<string>? _rows;

    /// <summary>What it holds, one kind per row: a name and how many, e.g. "Key 2" (just "Key" is one).</summary>
    public IReadOnlyList<string> Items
    {
        get => _rows ??= _items.Select(i => i.Count == 1 ? i.Name : $"{i.Name} {i.Count.ToString(CultureInfo.InvariantCulture)}").ToList();
        set
        {
            _items.Clear();
            _rows = null;
            foreach (var row in value ?? [])
            {
                var text = row.Trim();
                var space = text.LastIndexOf(' ');
                if (space > 0 && int.TryParse(text[(space + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                    AddQuietly(text[..space], count);
                else
                    AddQuietly(text, 1);
            }
        }
    }

    /// <summary>Raised when what it holds changes, with the item's name.</summary>
    public event Action<string>? Changed;

    /// <summary>Everything it holds and how many, in the order each was first added.</summary>
    public IReadOnlyList<(string Name, int Count)> Contents => _items.ToList();

    /// <summary>How many of <paramref name="item"/> it holds (names ignore case).</summary>
    public int Count(string item) => IndexOf(item) is var i and >= 0 ? _items[i].Count : 0;

    /// <summary>True if it holds at least <paramref name="count"/> of <paramref name="item"/>.</summary>
    public bool Has(string item, int count = 1) => Count(item) >= count;

    /// <summary>Puts <paramref name="count"/> of <paramref name="item"/> in.</summary>
    public void Add(string item, int count = 1)
    {
        if (AddQuietly(item, count))
            Changed?.Invoke(item.Trim());
    }

    /// <summary>Takes <paramref name="count"/> of <paramref name="item"/> out, if it has that many; returns whether it did.</summary>
    public bool Take(string item, int count = 1)
    {
        if (count <= 0 || !Has(item, count))
            return false;
        AddQuietly(item, -count);
        Changed?.Invoke(item.Trim());
        return true;
    }

    /// <summary>Empties it.</summary>
    public void Clear()
    {
        var names = _items.Select(i => i.Name).ToList();
        _items.Clear();
        _rows = null;
        foreach (var name in names)
            Changed?.Invoke(name);
    }

    private bool AddQuietly(string item, int count)
    {
        item = item?.Trim() ?? "";
        if (item.Length == 0 || count == 0)
            return false;
        var i = IndexOf(item);
        var total = (i >= 0 ? _items[i].Count : 0) + count;
        _rows = null;
        if (i >= 0 && total <= 0)
            _items.RemoveAt(i);
        else if (i >= 0)
            _items[i] = (_items[i].Name, total);
        else if (total > 0)
            _items.Add((item, total));
        else
            return false;
        return true;
    }

    private int IndexOf(string item)
    {
        item = item?.Trim() ?? "";
        return _items.FindIndex(i => string.Equals(i.Name, item, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Leaves fading copies of its entity's <see cref="Sprite"/> behind as it moves: dashes, speed, ghosts, bullets with
/// streaks. Copies are left every <see cref="Interval"/> seconds while it moves at least <see cref="MinSpeed"/>.
/// </summary>
[Category("Rendering")]
[Requires(typeof(Sprite), Note = "the copies left behind")]
[ComponentName("Griff.Trail")]
public sealed class Trail : Component
{
    private readonly List<TrailCopy> _copies = [];
    private Vector2? _last;
    private float _sinceCopy;

    /// <summary>Seconds between copies.</summary>
    public float Interval { get; set; } = 0.04f;

    /// <summary>Seconds each copy takes to fade away.</summary>
    public float Fade { get; set; } = 0.25f;

    /// <summary>How see-through a fresh copy is, from 0 (invisible) to 1 (as solid as the sprite).</summary>
    public float Opacity { get; set; } = 0.5f;

    /// <summary>A color painted over the copies, e.g. a cyan dash. Transparent keeps the sprite's own look.</summary>
    public Color Color { get; set; } = Color.Transparent;

    /// <summary>Only leave copies while moving at least this fast, in world units per second: just the dash, not the walk.</summary>
    public float MinSpeed { get; set; } = 1;

    /// <summary>Untick it (from a script, say at the end of a dash) to stop leaving copies; those already left still fade.</summary>
    public bool Emitting { get; set; } = true;

    /// <summary>The copies on screen, oldest first.</summary>
    public IReadOnlyList<TrailCopy> Copies => _copies;

    /// <summary>Ages the copies and leaves a new one when it's due. Called by the game.</summary>
    internal void Advance(float seconds)
    {
        if (seconds <= 0)
            return;
        foreach (var i in Enumerable.Range(0, _copies.Count))
            _copies[i] = _copies[i] with { Age = _copies[i].Age + seconds };
        _copies.RemoveAll(c => c.Age >= Fade);

        var position = Entity!.Transform.WorldPosition;
        var here = new Vector2(position.X, position.Y);
        var speed = _last is { } last ? Vector2.Distance(here, last) / seconds : 0;
        _last = here;
        _sinceCopy += seconds;

        if (!Emitting || speed < MinSpeed || _sinceCopy < Math.Max(Interval, 0.005f) || !(Fade > 0) ||
            Entity.GetComponent<Sprite>() is not { } sprite)
            return;
        _sinceCopy = 0;
        foreach (var (world, source) in Rendering.SceneRenderer.SpritePieces(Entity, sprite))
            _copies.Add(new TrailCopy(world, source, sprite.Image, sprite.Tint, 0));
    }

    /// <summary>How solid a copy is now: <see cref="Opacity"/> fading to nothing over <see cref="Fade"/>.</summary>
    public float OpacityOf(TrailCopy copy) => Fade > 0 ? Math.Clamp(Opacity, 0, 1) * Math.Clamp(1 - copy.Age / Fade, 0, 1) : 0;
}

/// <summary>One copy left by a <see cref="Trail"/>: where it was drawn, what it showed, and how many seconds ago.</summary>
public readonly record struct TrailCopy(Matrix4x4 World, Vector4 Source, string? Image, Color Tint, float Age);
