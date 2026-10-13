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
    [Names(NameKind.Layer, Many = true)]
    [Group("Working It")]
    public string? Layers { get; set; } = "Player";

    /// <summary>
    /// An item it needs to turn on, e.g. "Key": whoever works it must have one in their <see cref="Inventory"/>, and it's
    /// used up. It only takes it once; after that this is cleared and it works like any other switch.
    /// </summary>
    public string? Key { get; set; }

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
/// A place to push a crate to, for Sokoban-style puzzles, with no code: it's covered while a grid crate (a
/// <see cref="Pushable"/> with a <see cref="GridMovement"/>) rests in its cell, and once every goal with the same
/// <see cref="AllCoveredMessage"/> is covered, that message is sent: the puzzle solved. Put it on the floor under where
/// the crates go; it needs no collider (a solid one would block them). A <see cref="Counter"/> with Count Goals shows how
/// many are covered.
/// </summary>
/// <remarks>
/// A crate that starts on a goal covers it from the start, without its sound or messages. Only a crate that has finished
/// gliding into the cell covers it.
/// </remarks>
[Category("Level")]
[WorksWith(typeof(Pushable), Note = "grid crates that cover it")]
[WorksWith(typeof(Counter), Note = "Count Goals shows how many are covered")]
[ComponentName("Griff.Goal")]
public sealed class Goal : Component
{
    /// <summary>The kinds of crate that cover it (each a pushable's Kind), separated by commas, e.g. "Red, Blue". Empty takes any crate.</summary>
    public string? Accepts { get; set; }

    /// <summary>The color a crate turns while it rests on it, so the player sees it's in place. Transparent leaves crates alone.</summary>
    public Color CoveredTint { get; set; } = Color.FromBytes(0x9C, 0xE0, 0x8A);

    /// <summary>A sound played when a crate covers it, e.g. "Sounds/click.wav".</summary>
    [Group("Sound and Messages")]
    public string? Sound { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when a crate covers it, with this entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? CoverMessage { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when its crate is pushed off it, with this entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? UncoverMessage { get; set; }

    /// <summary>
    /// A message sent once every goal with this same All Covered Message is covered, with this entity as its data: every
    /// crate home, the level solved. It's sent again if they're all covered again after one is uncovered.
    /// </summary>
    [Names(NameKind.Message)]
    public string? AllCoveredMessage { get; set; }

    /// <summary>The crate resting on it, or null.</summary>
    [DoNotSerialize]
    public Entity? CoveredBy { get; internal set; }

    /// <summary>True while a crate it accepts rests on it.</summary>
    [DoNotSerialize]
    public bool IsCovered => CoveredBy is not null;

    /// <summary>Raised when a crate covers it, with the crate.</summary>
    public event Action<Entity>? Covered;

    /// <summary>Raised when its crate is pushed off it, with the crate.</summary>
    public event Action<Entity>? Uncovered;

    /// <summary>True if crates of kind <paramref name="kind"/> (a pushable's Kind) cover it.</summary>
    public bool AcceptsKind(string? kind)
    {
        var kinds = (Accepts ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return kinds.Length == 0 || kinds.Contains((kind ?? "").Trim(), StringComparer.OrdinalIgnoreCase);
    }

    // Whether the game has looked at it yet: what covers it at the start isn't a change.
    internal bool Seen { get; set; }

    internal void RaiseCovered(Entity crate) => Covered?.Invoke(crate);

    internal void RaiseUncovered(Entity crate) => Uncovered?.Invoke(crate);
}

/// <summary>
/// Undo and restart for grid puzzles, with no code: each step a grid player takes (with any crates it pushes) is a move.
/// The <see cref="UndoAction"/> (Z, U or the gamepad's left shoulder) puts everything back where it was before the last
/// move, and the <see cref="RestartAction"/> (R or the left trigger) back where the level started, which Undo takes back
/// too. One in the scene is enough: put it on the player or on the level. A <see cref="Counter"/> with Count Moves shows
/// how many moves have been taken.
/// </summary>
/// <remarks>
/// It puts back every grid mover: players, crates, and chasers and patrols (where they were on their routes), each facing
/// the way it did. Things collected or destroyed since stay gone, and the rest (counters, switches, inventories, whose turn
/// it is) stays as it is; tick <see cref="RestartReloads"/> for a restart that starts the whole scene over. In a network
/// game there's no undo, and a restart starts the scene over for everyone.
/// </remarks>
[Category("Level")]
[WorksWith(typeof(GridMovement), Note = "the players, crates and enemies it puts back")]
[WorksWith(typeof(Counter), Note = "Count Moves shows how many moves have been taken")]
[ComponentName("Griff.MoveHistory")]
public sealed class MoveHistory : Component
{
    /// <summary>The input action that takes the last move back: "Undo" is Z, U or the gamepad's left shoulder. Empty only undoes from scripts.</summary>
    [Names(NameKind.Action)]
    public string? UndoAction { get; set; } = "Undo";

    /// <summary>Holding Undo keeps taking moves back, one after another.</summary>
    public bool HoldToRepeat { get; set; } = true;

    /// <summary>The input action that starts the level over: "Restart" is R or the gamepad's left trigger. Empty only restarts from scripts.</summary>
    [Names(NameKind.Action)]
    public string? RestartAction { get; set; } = "Restart";

    /// <summary>
    /// Restart reloads the scene from its file, so everything starts over (pickups, counters, scripts too), and Undo can't
    /// take it back. Off, it puts the grid movers back where they started, as one more move Undo can take back.
    /// </summary>
    public bool RestartReloads { get; set; }

    /// <summary>Seconds to fade out (and back in) as a restart reloads the scene. 0 cuts straight there.</summary>
    [ShownWhen(nameof(RestartReloads))]
    public float Fade { get; set; } = 0.3f;

    /// <summary>The most moves it remembers, dropping the oldest. 0 remembers them all.</summary>
    public int Limit { get; set; }

    /// <summary>A sound played as a move is taken back, e.g. "Sounds/undo.wav".</summary>
    [Group("Sound and Messages")]
    public string? UndoSound { get; set; }

    /// <summary>A sound played as the level starts over.</summary>
    public string? RestartSound { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> as a move is taken back, with the moves now taken as its data.</summary>
    [Names(NameKind.Message)]
    public string? UndoMessage { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> as the level starts over.</summary>
    [Names(NameKind.Message)]
    public string? RestartMessage { get; set; }

    /// <summary>How many moves have been taken: one more with each, one fewer with each undo, 0 after a restart.</summary>
    [DoNotSerialize]
    public int Moves { get; internal set; }

    /// <summary>True while there's a move (or a restart) to take back.</summary>
    [DoNotSerialize]
    public bool CanUndo => Past.Count > 0;

    /// <summary>Raised after a move is taken back.</summary>
    public event Action<MoveHistory>? Undone;

    /// <summary>Raised after the level starts over (or as it starts to reload, with <see cref="RestartReloads"/>).</summary>
    public event Action<MoveHistory>? Restarted;

    /// <summary>Takes the last move back at the start of the next frame, as pressing Undo does.</summary>
    public void Undo() => UndoRequests++;

    /// <summary>Starts the level over at the start of the next frame, as pressing Restart does.</summary>
    public void Restart() => RestartRequested = true;

    /// <summary>
    /// Counts a move now, remembering where every grid mover is so Undo can put them back here: for a script's own moves
    /// (a lever pulled, a block teleported). Grid players' steps count by themselves.
    /// </summary>
    public void Record()
    {
        if (Entity?.Scene is not { } scene)
            return;
        Begin(scene);
        Remember(MoveSnapshot.Take(scene, Moves));
        Moves++;
    }

    // Where the level started, taken on its first frame (or the first after a rewind).
    internal void Begin(Scene scene) => Start ??= MoveSnapshot.Take(scene, 0);

    internal void Remember(MoveSnapshot snapshot)
    {
        Past.Add(snapshot);
        if (Limit > 0 && Past.Count > Limit)
            Past.RemoveRange(0, Past.Count - Limit);
    }

    internal MoveSnapshot? Start { get; set; }

    // The moments Undo goes back to, the latest last.
    internal List<MoveSnapshot> Past { get; } = [];

    internal int UndoRequests { get; set; }

    internal bool RestartRequested { get; set; }

    // How long Undo has been held, and when it next repeats.
    internal float HeldFor { get; set; }

    internal float NextRepeat { get; set; }

    internal void RaiseUndone() => Undone?.Invoke(this);

    internal void RaiseRestarted() => Restarted?.Invoke(this);
}

/// <summary>Where every grid mover in a scene was at one moment, for a <see cref="MoveHistory"/> to put them back.</summary>
internal sealed class MoveSnapshot
{
    private readonly List<Mover> _movers = [];

    private MoveSnapshot(int moves) => Moves = moves;

    /// <summary>The moves taken at that moment.</summary>
    public int Moves { get; }

    private sealed record Mover(GridMovement Movement, (int X, int Y) Cell, Vector2 Facing, PatrolState? Patrol, (bool Chasing, bool Near)? Chase);

    private sealed record PatrolState(int Leg, Vector2 Away, float WaitLeft, (int X, int Y)? LastCell);

    public static MoveSnapshot Take(Scene scene, int moves)
    {
        var snapshot = new MoveSnapshot(moves);
        foreach (var entity in scene.Entities.Where(e => e.IsActive))
        {
            if (entity.GetComponent<GridMovement>() is not { } m)
                continue;
            // A mover partway through a step is in the cell it's heading into.
            var cell = m.Started ? m.Cell : m.CellAt(new Vector2(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y));
            var patrol = entity.GetComponent<GridPatrol>() is { } p ? new PatrolState(p.Leg, p.Away, p.WaitLeft, p.LastCell) : null;
            var chase = entity.GetComponent<GridChase>() is { } c ? (c.IsChasing, c.WasNear) : ((bool, bool)?)null;
            snapshot._movers.Add(new Mover(m, cell, m.Facing, patrol, chase));
        }

        return snapshot;
    }

    /// <summary>Puts each grid mover still in <paramref name="scene"/> back in its cell, standing still, facing the way it did.</summary>
    public void Restore(Scene scene)
    {
        foreach (var (m, cell, facing, patrol, chase) in _movers)
        {
            var entity = m.Entity;
            if (entity?.Scene != scene)
                continue;
            var world = m.CellCenter(cell);
            var target = new Vector3(world, entity.Transform.WorldPosition.Z);
            if (entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
                target = Vector3.Transform(target, inverse);
            entity.Transform.Position = new Vector3(target.X, target.Y, entity.Transform.Position.Z);
            if (entity.GetComponent<Body>() is { } body)
                body.Velocity = Vector2.Zero;

            (m.Started, m.Stepping, m.Cell, m.Facing, m.Progress, m.Glide) = (true, false, cell, facing, 0, null);
            (m.Queued, m.Buffered, m.Rest, m.BumpedWay) = (null, null, 0, (0, 0));
            if (patrol is not null && entity.GetComponent<GridPatrol>() is { } p)
                (p.Leg, p.Away, p.WaitLeft, p.LastCell) = (patrol.Leg, patrol.Away, patrol.WaitLeft, patrol.LastCell);
            if (chase is { } was && entity.GetComponent<GridChase>() is { } c)
                (c.IsChasing, c.WasNear) = was;
        }
    }
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
    private IReadOnlyList<string> _uses = [];
    private IReadOnlyList<(string Item, IReadOnlyList<(string Verb, string Argument)> Effects)> _effects = [];

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

    /// <summary>
    /// What its items do when they're used, one per line: the item, a colon, and what it does, several separated by
    /// commas: "heal 3" gives back hit points, "spawn Explosion" spawns a prefab where it stands, "send Teleport" sends a
    /// message (with this entity as its data). E.g. "Potion: heal 3", "Bomb: spawn Explosion, send Boom". Using one takes
    /// it out; a potion that only heals isn't used up at full health.
    /// </summary>
    [Group("Using Items")]
    public IReadOnlyList<string> ItemUses
    {
        get => _uses;
        set
        {
            _uses = value ?? [];
            _effects = ItemEffects.Parse(_uses);
        }
    }

    /// <summary>The number keys use what it holds: 1 the first item it has with a use (in the order it got them), 2 the second, up to 9.</summary>
    public bool NumberKeys { get; set; } = true;

    /// <summary>A sound played as an item is used, e.g. "Sounds/drink.wav".</summary>
    public string? UseSound { get; set; }

    /// <summary>Raised when what it holds changes, with the item's name.</summary>
    public event Action<string>? Changed;

    /// <summary>Raised as an item is used, with its name.</summary>
    public event Action<string>? Used;

    /// <summary>The items it holds that have a use (see <see cref="ItemUses"/>), in the order it got them: what the number keys use.</summary>
    public IReadOnlyList<string> Usable => _items.Where(i => i.Count > 0 && EffectsOf(i.Name).Count > 0).Select(i => i.Name).ToList();

    /// <summary>What <paramref name="item"/> does when used, as <see cref="ItemUses"/> says: each a verb ("heal", "spawn", "send") and what it takes.</summary>
    public IReadOnlyList<(string Verb, string Argument)> EffectsOf(string item) =>
        _effects.FirstOrDefault(e => string.Equals(e.Item, item?.Trim(), StringComparison.OrdinalIgnoreCase)).Effects ?? [];

    /// <summary>Uses one <paramref name="item"/> at the start of the next frame, as its number key does: on its turn, if it holds one.</summary>
    public void Use(string item) => UseRequests.Add(item);

    internal List<string> UseRequests { get; } = [];

    internal void RaiseUsed(string item) => Used?.Invoke(item);

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

/// <summary>Reads <see cref="Inventory.ItemUses"/>: "Potion: heal 3, send Gulp" lines into each item and its effects.</summary>
internal static class ItemEffects
{
    /// <summary>The verbs an effect can start with.</summary>
    public static readonly string[] Verbs = ["heal", "spawn", "send"];

    public static IReadOnlyList<(string Item, IReadOnlyList<(string Verb, string Argument)> Effects)> Parse(IReadOnlyList<string> lines)
    {
        var uses = new List<(string, IReadOnlyList<(string, string)>)>();
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var effects = new List<(string, string)>();
            foreach (var part in line[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var space = part.IndexOf(' ');
                var verb = (space > 0 ? part[..space] : part).ToLowerInvariant();
                var argument = space > 0 ? part[(space + 1)..].Trim() : "";
                if (Verbs.Contains(verb) && argument.Length > 0)
                    effects.Add((verb, argument));
            }

            if (effects.Count > 0)
                uses.Add((line[..colon].Trim(), effects));
        }

        return uses;
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
        foreach (var (world, source) in sprite.Pieces())
            _copies.Add(new TrailCopy(world, source, sprite.Image, sprite.Tint, 0));
    }

    /// <summary>How solid a copy is now: <see cref="Opacity"/> fading to nothing over <see cref="Fade"/>.</summary>
    public float OpacityOf(TrailCopy copy) => Fade > 0 ? Math.Clamp(Opacity, 0, 1) * Math.Clamp(1 - copy.Age / Fade, 0, 1) : 0;
}

/// <summary>One copy left by a <see cref="Trail"/>: where it was drawn, what it showed, and how many seconds ago.</summary>
public readonly record struct TrailCopy(Matrix4x4 World, Vector4 Source, string? Image, Color Tint, float Age);
