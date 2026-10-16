using System.Globalization;
using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.Strategy;

/// <summary>
/// A team in a strategy game: its name (the <see cref="Unit.Team"/> and <see cref="Building.Team"/> of everything it
/// owns, the same teams every genre shares, see <see cref="Teams"/>), its color, and the resources it has to spend. Teams
/// with different names are enemies. A team is beaten when it has no buildings left.
/// </summary>
/// <remarks>
/// Resources are items, the one idea every genre shares (see <see cref="Items"/>), kept a row each, "Gold 200", as an
/// <see cref="Inventory"/> keeps its items, so they show (and can be changed) in the Inspector while the game runs, and
/// rewind with everything else. They're the team's items: anything else on its team without items of its own (a
/// platformer hero, a card hand) collects into them and pays from them. Any name works for a resource: whatever a
/// <see cref="Resource"/>'s Kind or a pickup's Item says.
/// </remarks>
[Category("Strategy")]
[WorksWith(typeof(Opponent), Note = "lets the computer play this team")]
[ComponentName("Griff.Faction")]
[ComponentVersion(2)]
public sealed class Faction : Component, ITeamMember, IHoldsItems
{
    // Names in the order first added (kept at 0, so the resource bar still shows them), and the rows they read as.
    private readonly List<(string Name, int Count)> _stock = [("Gold", 250), ("Wood", 100)];
    private IReadOnlyList<string>? _rows;

    /// <summary>The team's name, e.g. "Player" or "Enemy". Units and buildings with this Team belong to it.</summary>
    [Names(NameKind.Kind, Scope = "Team", Defines = true)]
    public string? Team { get; set; } = "Player";

    /// <summary>The team's color: selection rings, its dots on the minimap.</summary>
    public Color Color { get; set; } = Color.FromBytes(0x4C, 0x9A, 0xFF);

    /// <summary>What it has to spend, a row each, e.g. "Gold 250" and "Wood 100". Starts as what it starts with.</summary>
    [ItemCounts]
    public IReadOnlyList<string> Resources
    {
        get => _rows ??= _stock.Select(s => s.Name + " " + s.Count.ToString(CultureInfo.InvariantCulture)).ToList();
        set
        {
            _stock.Clear();
            _rows = null;
            foreach (var row in value ?? [])
            {
                var (item, count) = Items.ReadOne(row);
                if (item.Length > 0)
                    Put(item, Math.Max(0, count + Amount(item)));
            }
        }
    }

    // Resources were text, "Gold 250, Wood 100", before they were items a row each.
    [UpgradeFrom(1)]
    private static void ResourcesAsRows(SavedData data)
    {
        if (data.Get<string>(nameof(Resources)) is not { } text)
            return;
        var rows = new List<string>();
        foreach (var part in text.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var (item, count) = Items.ReadOne(part);
            rows.Add(item + " " + Math.Max(0, count).ToString(CultureInfo.InvariantCulture));
        }

        data.Set<IReadOnlyList<string>>(nameof(Resources), rows);
    }

    /// <summary>The most supply it can ever have, however many farms it builds.</summary>
    public int SupplyLimit { get; set; } = 100;

    /// <summary>Sent (with the team's name) when it loses its last building.</summary>
    [Names(NameKind.Message)]
    public string? DefeatMessage { get; set; } = "Defeated";

    /// <summary>True once it has no buildings left.</summary>
    [DoNotSerialize]
    [Sent]
    public bool IsDefeated { get; internal set; }

    /// <summary>Raised when it loses its last building.</summary>
    public event Action<Faction>? Defeated;

    /// <summary>How much of <paramref name="kind"/> (e.g. "Gold") it has. Read as "Amount:Gold".</summary>
    [Output(Example = "Gold")]
    public int Amount(string kind)
    {
        var name = kind.AsSpan().Trim();
        foreach (var (item, count) in _stock)
        {
            if (item.AsSpan().Equals(name, StringComparison.OrdinalIgnoreCase))
                return count;
        }

        return 0;
    }

    /// <summary>How many units its team has left in the scene.</summary>
    [DoNotSerialize]
    [Output]
    public int Units => CountTeam<Unit>(u => u.Team);

    /// <summary>How many buildings its team has left in the scene, built or going up.</summary>
    [DoNotSerialize]
    [Output]
    public int Buildings => CountTeam<Building>(b => b.Team);

    private int CountTeam<T>(Func<T, string?> teamOf) where T : Component
    {
        if (Entity?.Scene is not { } scene)
            return 0;
        var count = 0;
        foreach (var member in scene.ActiveNow<T>())
        {
            if (Teams.Same(teamOf(member), Team))
                count++;
        }

        return count;
    }

    /// <summary>Adds <paramref name="amount"/> of <paramref name="kind"/>, e.g. gold brought back to a town hall (never below 0).</summary>
    public void Add(string kind, int amount)
    {
        if (string.IsNullOrWhiteSpace(kind) || amount == 0)
            return;
        Put(kind.Trim(), Math.Max(0, Amount(kind) + amount));
    }

    /// <summary>True if it has everything a cost like "150 Gold, 50 Wood" asks for.</summary>
    public bool CanAfford(string? cost) => Lacking(cost) is null;

    /// <summary>Pays a cost like "150 Gold, 50 Wood" if it can afford it; returns whether it did.</summary>
    public bool Spend(string? cost)
    {
        if (!CanAfford(cost))
            return false;
        foreach (var (kind, amount) in Items.Totals(cost))
            Add(kind, -amount);
        return true;
    }

    /// <summary>Gives back a cost it paid, e.g. when training is cancelled.</summary>
    public void Refund(string? cost)
    {
        foreach (var (kind, amount) in Items.Totals(cost))
            Add(kind, amount);
    }

    /// <summary>What it's short of to pay <paramref name="cost"/>, e.g. "Wood", or null if it can pay.</summary>
    public string? Lacking(string? cost)
    {
        foreach (var (kind, amount) in Items.Totals(cost))
        {
            if (Amount(kind) < amount)
                return kind;
        }

        return null;
    }

    /// <summary>Its resources and how much of each, in the order it first had them.</summary>
    public IReadOnlyList<(string Name, int Count)> Stock => _stock;

    string? IHoldsItems.ForTeam => Team;

    bool IHoldsItems.Keeps(string item) => true;

    int IHoldsItems.Count(string item) => Amount(item);

    void IHoldsItems.Add(string item, int count) => Add(item, count);

    bool IHoldsItems.Take(string item, int count)
    {
        if (count <= 0 || Amount(item) < count)
            return false;
        Add(item, -count);
        return true;
    }

    private void Put(string kind, int amount)
    {
        _rows = null;
        for (var i = 0; i < _stock.Count; i++)
        {
            if (_stock[i].Name.Equals(kind, StringComparison.OrdinalIgnoreCase))
            {
                _stock[i] = (_stock[i].Name, amount);
                return;
            }
        }

        _stock.Add((kind, amount));
    }

    internal void RaiseDefeated() => Defeated?.Invoke(this);
}

/// <summary>
/// Hotkey letters, and resources read and written the way <see cref="Items"/> reads every genre's items: "150 Gold, 50
/// Wood" (or "Gold 150").
/// </summary>
public static class Costs
{
    /// <summary>The keys a unit's or building's Hotkey can be: the letters A to Z.</summary>
    public static IReadOnlyList<string> Letters { get; } = [.. Enumerable.Range('A', 26).Select(c => ((char)c).ToString())];

    /// <summary>The amounts in <paramref name="text"/>, by resource; empty for nothing (or nothing readable). See <see cref="Items.Totals"/>.</summary>
    public static Dictionary<string, int> Parse(string? text)
    {
        var amounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (item, count) in Items.Totals(text))
            amounts[item] = count;
        return amounts;
    }

    /// <summary>"Gold 150, Wood 50".</summary>
    public static string Format(IEnumerable<KeyValuePair<string, int>> amounts) =>
        string.Join(", ", amounts.Select(a => $"{a.Key} {a.Value.ToString(CultureInfo.InvariantCulture)}"));

    /// <summary>"150 Gold, 50 Wood": the way a price reads (see <see cref="Items.Describe"/>).</summary>
    public static string Describe(string? cost) => Items.Describe(cost);
}

/// <summary>What a unit is doing.</summary>
public enum UnitOrder
{
    /// <summary>Standing still, ready to fight anything that comes near.</summary>
    Idle,

    /// <summary>Walking somewhere, ignoring enemies on the way.</summary>
    Move,

    /// <summary>Walking somewhere, fighting whatever it meets on the way.</summary>
    AttackMove,

    /// <summary>Going after one target until it's gone.</summary>
    Attack,

    /// <summary>Standing its ground: fighting only what comes within reach.</summary>
    Hold,

    /// <summary>A worker taking resources from a <see cref="Resource"/>.</summary>
    Gather,

    /// <summary>A worker carrying what it gathered to a depot.</summary>
    Return,

    /// <summary>A worker putting up a building.</summary>
    Build,
}

/// <summary>
/// Something a team can select and order about with no code: click it (or drag a box round it) and right-click where it
/// should go or what it should attack. It walks around buildings, rocks and walls on its own, and units stand apart
/// rather than on top of each other. Give it a <see cref="Health"/> to be hurt, a <see cref="Fighter"/> to fight, and a
/// <see cref="Worker"/> to gather and build.
/// </summary>
/// <remarks>
/// A unit made by a <see cref="Producer"/> is a prefab; its <see cref="Cost"/>, <see cref="TrainTime"/>,
/// <see cref="Supply"/> and <see cref="Hotkey"/> say what training it takes. Scripts can order it about with
/// <see cref="MoveTo"/>, <see cref="AttackMoveTo"/>, <see cref="Attack"/>, <see cref="Stop"/> and <see cref="Hold"/>.
/// </remarks>
[Category("Strategy")]
[WorksWith(typeof(Health), Note = "so it can be hurt")]
[WorksWith(typeof(Fighter), Note = "so it can fight")]
[WorksWith(typeof(Worker), Note = "so it can gather and build")]
[ComponentName("Griff.Unit")]
public sealed class Unit : Component, ITeamMember
{
    // The strategy game that runs it this frame (see StrategySystem), if any.
    internal StrategyState? InGame { get; set; }

    /// <summary>The team it's on: a <see cref="Faction"/>'s Team, e.g. "Player" or "Enemy".</summary>
    [Names(NameKind.Kind, Scope = "Team")]
    public string? Team { get; set; } = "Player";

    /// <summary>World units per second.</summary>
    public float Speed { get; set; } = 3;

    /// <summary>How much room it takes up: for clicking it, its selection ring, and keeping others off it.</summary>
    [Distance]
    public float Radius { get; set; } = 0.4f;

    /// <summary>How far it sees through the fog of war, and how close an enemy has to come for it to notice.</summary>
    [Distance]
    public float Sight { get; set; } = 6;

    /// <summary>The supply it takes up. A team can only have as much as its buildings provide.</summary>
    public int Supply { get; set; } = 1;

    /// <summary>What training it costs, e.g. "50 Gold" or "60 Gold, 20 Wood".</summary>
    [Counts(NameKind.Item, CountFirst = true, What = "Resource")]
    public string? Cost { get; set; } = "50 Gold";

    /// <summary>Seconds a <see cref="Producer"/> takes to train it.</summary>
    public float TrainTime { get; set; } = 3;

    /// <summary>The key that trains it from a building, e.g. "W". Empty uses its first letter.</summary>
    [ChoiceOf(nameof(Keys), Noun = "a letter key")]
    public string? Hotkey { get; set; }

    // The keys a hotkey can be: the letters.
    private static IReadOnlyList<string> Keys() => Costs.Letters;

    /// <summary>What it's doing now.</summary>
    [DoNotSerialize]
    [Sent]
    public UnitOrder Order { get; internal set; }

    /// <summary>Where it's headed, for <see cref="UnitOrder.Move"/> and <see cref="UnitOrder.AttackMove"/>.</summary>
    [DoNotSerialize]
    public Vector2 Destination { get; internal set; }

    /// <summary>What it's attacking, gathering from or building, if anything.</summary>
    [DoNotSerialize]
    public Entity? Target { get; internal set; }

    /// <summary>True while the player has it selected.</summary>
    [DoNotSerialize]
    public bool IsSelected { get; internal set; }

    /// <summary>Raised when it gets where it was told to go.</summary>
    public event Action<Unit>? Arrived;

    /// <summary>Walks to <paramref name="position"/>, ignoring enemies on the way.</summary>
    public void MoveTo(Vector2 position) => Command(UnitOrder.Move, position, null);

    /// <summary>Walks to <paramref name="position"/>, fighting whatever it meets on the way.</summary>
    public void AttackMoveTo(Vector2 position) => Command(UnitOrder.AttackMove, position, null);

    /// <summary>Goes after <paramref name="target"/> until it's gone.</summary>
    public void Attack(Entity target) => Command(UnitOrder.Attack, Vector2.Zero, target);

    /// <summary>Stops whatever it's doing.</summary>
    public void Stop() => Command(UnitOrder.Idle, Vector2.Zero, null);

    /// <summary>Stands its ground, fighting only what comes within reach.</summary>
    public void Hold() => Command(UnitOrder.Hold, Vector2.Zero, null);

    internal void Command(UnitOrder order, Vector2 destination, Entity? target)
    {
        (Order, Destination, Target) = (order, destination, target);
        Path = [];
        PathGoal = null;
        Stuck = 0;
        if (order is UnitOrder.Move or UnitOrder.AttackMove or UnitOrder.Idle or UnitOrder.Hold)
            Chasing = null;
        if (order is not UnitOrder.Build && GetWorker() is { } worker)
            worker.GatherLeft = -1;
    }

    internal void RaiseArrived() => Arrived?.Invoke(this);

    private Worker? GetWorker() => Entity?.GetComponent<Worker>();

    // The way there: waypoints in the world, and the goal they were found for.
    internal List<Vector2> Path { get; set; } = [];

    internal Vector2? PathGoal { get; set; }

    internal float PathAge { get; set; }

    // Seconds spent trying to get somewhere without getting closer.
    internal float Stuck { get; set; }

    internal float BestDistance { get; set; } = float.PositiveInfinity;

    // An enemy it noticed while idle, holding or attack-moving: it fights that, then carries on.
    internal Entity? Chasing { get; set; }

    // An enemy it couldn't get at, left alone for a few seconds.
    internal Entity? Shunned { get; set; }

    internal float ShunLeft { get; set; }

    // Who hit it last, so an idle unit fights back.
    internal Entity? HitBy { get; set; }
}

/// <summary>
/// A building: it stands still, takes up its <see cref="Size"/> on the map (units walk around it), and belongs to a team.
/// Workers put it up when it's placed (see <see cref="Worker.Builds"/>); a <see cref="Producer"/> on it trains units, a
/// <see cref="Fighter"/> makes it a tower, and <see cref="Depot"/> lets workers bring resources back to it.
/// </summary>
[Category("Strategy")]
[WorksWith(typeof(Producer), Note = "trains units")]
[WorksWith(typeof(Health), Note = "so it can be destroyed")]
[WorksWith(typeof(Fighter), Note = "makes it a tower")]
[ComponentName("Griff.Building")]
public sealed class Building : Component, INetState, ITeamMember
{
    // The strategy game that runs it this frame (see StrategySystem), if any.
    internal StrategyState? InGame { get; set; }

    // A building with no rally point. (Not NaN, which never equals itself, so it would be sent again and again.)
    private static readonly Vector2 NoRally = new(float.MaxValue);

    // Its rally point, for everyone's view of it.
    void INetState.Network(NetState state) => state.Add(() => Rally ?? NoRally, rally => Rally = rally != NoRally ? rally : null);

    /// <summary>The team it's on: a <see cref="Faction"/>'s Team.</summary>
    [Names(NameKind.Kind, Scope = "Team")]
    public string? Team { get; set; } = "Player";

    /// <summary>The ground it covers, in world units; units walk around it.</summary>
    public Vector2 Size { get; set; } = new(3, 3);

    /// <summary>How far it sees through the fog of war.</summary>
    [Distance]
    public float Sight { get; set; } = 7;

    /// <summary>What it costs to build, e.g. "150 Gold, 50 Wood".</summary>
    [Group("Building It")]
    [Counts(NameKind.Item, CountFirst = true, What = "Resource")]
    public string? Cost { get; set; } = "150 Gold";

    /// <summary>Seconds a worker takes to put it up.</summary>
    public float BuildTime { get; set; } = 10;

    /// <summary>The supply it provides its team once it's up: farms and houses.</summary>
    [Group("What It Gives")]
    public int Supply { get; set; }

    /// <summary>Whether workers bring gathered resources back here: a town hall.</summary>
    public bool Depot { get; set; }

    /// <summary>The key that builds it, e.g. "B". Empty uses its first letter.</summary>
    [Group("Building It")]
    [ChoiceOf(nameof(Keys), Noun = "a letter key")]
    public string? Hotkey { get; set; }

    // The keys a hotkey can be: the letters.
    private static IReadOnlyList<string> Keys() => Costs.Letters;

    /// <summary>How far along it is, from 0 (just placed) to 1 (finished). Buildings in a scene start finished.</summary>
    [Group("State")]
    public float Progress
    {
        get => _progress.Value;
        set => _progress.Set(this, value);
    }

    // Kept as it was when the step began too, so it's drawn going up between steps.
    private Smoothed<float> _progress = new(1);

    // Its progress in a frame drawn as `blend` says.
    internal float ProgressDrawn(StepBlend blend) => blend.Of(_progress);

    /// <summary>True once it's finished: only then does it train, take resources, provide supply or shoot.</summary>
    public bool IsBuilt => Progress >= 1;

    /// <summary>Where units it trains walk to. Right-click with it selected to set it.</summary>
    [DoNotSerialize]
    public Vector2? Rally { get; set; }

    /// <summary>A resource units it trains go and gather from, if the rally point was set on one.</summary>
    [DoNotSerialize]
    public Entity? RallyTarget { get; set; }

    /// <summary>True while the player has it selected.</summary>
    [DoNotSerialize]
    public bool IsSelected { get; internal set; }

    /// <summary>Raised when it's finished.</summary>
    public event Action<Building>? Finished;

    internal void RaiseFinished() => Finished?.Invoke(this);
}

/// <summary>
/// Trains units at a <see cref="Building"/>: select it and press a unit's hotkey (or click its button) to queue one up. The
/// team pays the unit's cost straight away, and needs the supply for it. Finished units walk out to the rally point.
/// </summary>
[Category("Strategy")]
[Requires(typeof(Building))]
[ComponentName("Griff.Producer")]
public sealed class Producer : Component, INetState
{
    // What it's training, for everyone's command card.
    void INetState.Network(NetState state) => state.Add<IReadOnlyList<string>>(() => QueueList.ToArray(), queue =>
    {
        QueueList.Clear();
        QueueList.AddRange(queue ?? []);
    });

    /// <summary>The prefabs it trains, e.g. "Worker" or "Soldier, Archer". Each needs a <see cref="Unit"/>.</summary>
    [Names(NameKind.Prefab, Many = true)]
    public string? Trains { get; set; } = "Worker";

    /// <summary>How many it can have queued up at once.</summary>
    public int QueueLimit { get; set; } = 5;

    /// <summary>What it's training, first in line first.</summary>
    [DoNotSerialize]
    public IReadOnlyList<string> Queue => QueueList;

    /// <summary>How many units it has queued up, the one in training included.</summary>
    [DoNotSerialize]
    [Output]
    public int Queued => QueueList.Count;

    /// <summary>How far along the first in line is, from 0 to 1.</summary>
    [Output]
    [DoNotSerialize]
    [Sent]
    public float Progress
    {
        get => _progress.Value;
        internal set => _progress.Set(this, value);
    }

    // Kept as it was when the step began too, so it's drawn filling between steps (and starting the next one over as it is).
    private Smoothed<float> _progress;

    // Its progress in a frame drawn as `blend` says.
    internal float ProgressDrawn(StepBlend blend) => _progress.Value >= _progress.Was ? blend.Of(_progress) : _progress.Value;

    /// <summary>Raised with each unit it finishes.</summary>
    public event Action<Entity>? Trained;

    /// <summary>The prefab names in <see cref="Trains"/>.</summary>
    public IReadOnlyList<string> Options() =>
        Formats.Names(Trains);

    internal List<string> QueueList { get; } = [];

    // What each one in the queue cost, to give back if it's cancelled.
    internal List<string?> Paid { get; } = [];

    internal void RaiseTrained(Entity unit) => Trained?.Invoke(unit);

    /// <summary>
    /// Queues up a unit it trains, one of <see cref="Trains"/> (the first without one), as its button does: its team pays for
    /// it and needs the supply. A trigger, a card or a wire can train an army.
    /// </summary>
    [Action]
    public void Train([Names(NameKind.Prefab)] string? unit = null) =>
        Asked.Add(string.IsNullOrWhiteSpace(unit) ? Options() is [var first, ..] ? first : "" : unit.Trim());

    /// <summary>Cancels the last unit queued, giving back what it cost.</summary>
    [Action("Cancel")]
    public void CancelLast() => Asked.Add(null);

    // What its actions asked for, done in the next step: a unit to train, or null to cancel the last.
    internal List<string?> Asked { get; } = [];
}

/// <summary>
/// Lets a <see cref="Unit"/> gather and build. Right-click a <see cref="Resource"/> and it fetches loads of it back to the
/// nearest depot (a <see cref="Building"/> with Depot ticked) over and over; select it and press a building's hotkey to
/// place one of its <see cref="Builds"/>.
/// </summary>
[Category("Strategy")]
[Requires(typeof(Unit))]
[ComponentName("Griff.Worker")]
public sealed class Worker : Component
{
    /// <summary>How much it carries back each trip.</summary>
    public int Carry { get; set; } = 8;

    /// <summary>Seconds it takes to gather a load.</summary>
    public float GatherTime { get; set; } = 1.5f;

    /// <summary>The building prefabs it can put up, e.g. "Farm, Barracks, Tower". Each needs a <see cref="Building"/>.</summary>
    [Names(NameKind.Prefab, Many = true)]
    public string? Builds { get; set; } = "Farm, Barracks";

    /// <summary>What it's carrying, e.g. "Gold", or empty.</summary>
    [DoNotSerialize]
    public string? Carrying { get; internal set; }

    /// <summary>How much it's carrying.</summary>
    [DoNotSerialize]
    [Sent]
    public int Load { get; internal set; }

    /// <summary>The resource it goes back to after dropping off a load.</summary>
    [DoNotSerialize]
    public Entity? Source { get; internal set; }

    /// <summary>The building prefabs in <see cref="Builds"/>.</summary>
    public IReadOnlyList<string> Options() =>
        Formats.Names(Builds);

    /// <summary>Sends it to gather from <paramref name="resource"/>.</summary>
    public void Gather(Entity resource)
    {
        Source = resource;
        Entity?.GetComponent<Unit>()?.Command(UnitOrder.Gather, Vector2.Zero, resource);
    }

    // Seconds left gathering the load it's working on; below 0 when it isn't.
    internal float GatherLeft { get; set; } = -1;

    // The color of what it carries (its resource's), shown as a little load over it.
    internal Color CarryColor { get; set; } = Color.White;

    // What it gathered last, to find more of once its resource runs out.
    internal string? LastKind { get; set; }
}

/// <summary>
/// Something workers gather: a gold mine, a tree. Each trip takes a load away; when <see cref="Amount"/> runs out it's
/// gone. Give it a sprite and a collider (or a sprite alone) the size it covers; units walk around it.
/// </summary>
[Category("Strategy")]
[ComponentName("Griff.Resource")]
public sealed class Resource : Component
{
    /// <summary>What it gives, e.g. "Gold" or "Wood": an item, the same as a pickup's or an inventory's (see <see cref="Items"/>).</summary>
    [Names(NameKind.Item, Defines = true)]
    public string? Kind { get; set; } = "Gold";

    /// <summary>How much is left.</summary>
    public int Amount { get; set; } = 1500;

    /// <summary>Whether units walk around it. Untick it for something they can stand on.</summary>
    public bool Blocks { get; set; } = true;
}

/// <summary>
/// Fights for a <see cref="Unit"/> or a <see cref="Building"/> (a tower): it attacks enemies that come within its unit's
/// sight on its own, and whatever it's told to. Up close it hits straight away; with a <see cref="Projectile"/> it fires
/// one that flies to the target and hurts it on arrival.
/// </summary>
[Category("Strategy")]
[ComponentName("Griff.Fighter")]
public sealed class Fighter : Component
{
    /// <summary>Hit points each hit takes off the target's <see cref="Health"/>.</summary>
    public float Damage { get; set; } = 2;

    /// <summary>How close it has to be to hit, from its edge to the target's, in world units.</summary>
    public float Range { get; set; } = 0.4f;

    /// <summary>Seconds between hits.</summary>
    public float Cooldown { get; set; } = 1;

    /// <summary>A prefab it fires at the target, e.g. "Arrow". Empty hits straight away.</summary>
    [Names(NameKind.Prefab)]
    public string? Projectile { get; set; }

    /// <summary>How fast its projectile flies, in world units per second.</summary>
    [ShownWhen(nameof(Projectile))]
    public float ProjectileSpeed { get; set; } = 12;

    /// <summary>A sound played with each hit or shot, e.g. "Sounds/swing.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>The enemy it's hitting, if any.</summary>
    [DoNotSerialize]
    public Entity? Target { get; internal set; }

    /// <summary>Raised with each hit or shot, with its target.</summary>
    public event Action<Entity>? Swung;

    internal float CooldownLeft { get; set; }

    internal void RaiseSwung(Entity target) => Swung?.Invoke(target);
}

/// <summary>
/// The player's strategy controls, on the camera: click or drag a box to select units, right-click to send them (to walk,
/// attack, gather or build), hotkeys and the command card for everything else. It shows the team's resources and supply,
/// what's selected, a minimap, and pans the camera with the arrow keys, the screen's edges and the minimap, zooming with
/// the mouse wheel.
/// </summary>
/// <remarks>
/// Keys: A then click attacks-moves, S stops, H holds position, M then click moves, Esc cancels. Ctrl and a number keeps the
/// selection as a group, the number picks it again. Shift adds to the selection, or queues up buildings to place.
/// Double-click a unit to pick all of its kind on screen.
/// </remarks>
[Category("Strategy")]
[Requires(typeof(Camera))]
[WorksWith(typeof(FogOfWar), Note = "hides what the team can't see")]
[ComponentName("Griff.Commander")]
[NetLocal]
public sealed class Commander : Component, IWorldPlaced
{
    /// <summary>The team the player commands: a <see cref="Faction"/>'s Team.</summary>
    [Names(NameKind.Kind, Scope = "Team")]
    public string? Team { get; set; } = "Player";

    /// <summary>The middle of the map, in world units. The camera, minimap and pathfinding stay within the map.</summary>
    [Group("Map and Camera")]
    public Vector2 MapCenter { get; set; }

    /// <summary>How big the map is, in world units.</summary>
    public Vector2 MapSize { get; set; } = new(48, 32);

    /// <summary>
    /// The map in the world, from <see cref="MapCenter"/> and <see cref="MapSize"/>: the strategy game it commands is played
    /// here. Units and buildings outside it aren't its (another commander's, or no strategy game's), and its fog of war
    /// covers only it.
    /// </summary>
    [Area]
    public Box MapArea => new(MapCenter, Vector2.Abs(MapSize) / 2);

    // Played as a minigame, its map moves with the rest of its scene.
    void IWorldPlaced.Shift(Vector2 by) => MapCenter += by;

    /// <summary>How fast the camera pans, in world units per second.</summary>
    public float ScrollSpeed { get; set; } = 16;

    /// <summary>Whether resting the mouse at the screen's edge pans the camera.</summary>
    public bool EdgeScroll { get; set; } = true;

    /// <summary>The closest the mouse wheel zooms in: the camera's smallest Size.</summary>
    public float MinZoom { get; set; } = 5;

    /// <summary>The furthest the mouse wheel zooms out: the camera's largest Size.</summary>
    public float MaxZoom { get; set; } = 13;

    /// <summary>
    /// Untick it to have the controls ignore the mouse and keys (selecting, orders, panning) while the game plays on:
    /// another part of the scene has the player's input. Its Focus action ticks it and unticks it on everything else the
    /// player plays, and another's Focus takes it back.
    /// </summary>
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>Whether to show the resources bar, selection, command card and minimap.</summary>
    [Group("Screens")]
    public bool Hud { get; set; } = true;

    /// <summary>A panel opened when every other team is beaten, e.g. "Victory".</summary>
    [EntityName]
    public string? VictoryPanel { get; set; } = "Victory";

    /// <summary>A panel opened when the player's team is beaten, e.g. "Defeat".</summary>
    [EntityName]
    public string? DefeatPanel { get; set; } = "Defeat";

    /// <summary>A sound played when the player gives an order, e.g. "Sounds/select.wav".</summary>
    [Group("Sounds")]
    public string? OrderSound { get; set; }

    /// <summary>A sound played when something's finished: a unit trained, a building up.</summary>
    public string? ReadySound { get; set; }
}

/// <summary>
/// Hides what the <see cref="Commander"/>'s team can't see. Where its units and buildings have never been is black; where
/// they've been but can't see now is dimmed, with enemy units in it hidden. Goes on the camera with the Commander.
/// </summary>
[Category("Strategy")]
[Requires(typeof(Commander))]
[ComponentName("Griff.FogOfWar")]
[NetLocal]
public sealed class FogOfWar : Component
{
    /// <summary>The size of a square of fog, in world units. Smaller is smoother but slower.</summary>
    public float CellSize { get; set; } = 0.5f;

    /// <summary>The color of ground never seen.</summary>
    public Color Unexplored { get; set; } = Color.FromBytes(0x08, 0x0A, 0x0E);

    /// <summary>The color laid over ground seen before but not now.</summary>
    public Color Explored { get; set; } = new(0.03f, 0.04f, 0.06f, 0.55f);
}

/// <summary>
/// Lets the computer play a <see cref="Faction"/>: it keeps its workers gathering, trains more, puts up the buildings in
/// <see cref="Builds"/>, trains an army, and sends it to attack once it's big enough, a bigger one each time.
/// </summary>
[Category("Strategy")]
[Requires(typeof(Faction))]
[ComponentName("Griff.Opponent")]
public sealed class Opponent : Component
{
    /// <summary>How many workers it keeps.</summary>
    public int Workers { get; set; } = 6;

    /// <summary>Buildings it puts up, in order, e.g. "Farm, Barracks". It adds farms as it runs short of supply.</summary>
    [Names(NameKind.Prefab, Many = true)]
    public string? Builds { get; set; } = "Barracks, Farm";

    /// <summary>Units it trains for its army, taking turns, e.g. "Soldier, Archer".</summary>
    [Names(NameKind.Prefab, Many = true)]
    public string? Army { get; set; } = "Soldier, Archer";

    /// <summary>Seconds between the soldiers it starts training: higher is an easier game. 0 trains as fast as it can pay.</summary>
    public float TrainEvery { get; set; }

    /// <summary>How big its army has to be before it attacks.</summary>
    public int AttackWith { get; set; } = 6;

    /// <summary>How many more it waits for each time after that.</summary>
    public int WaveGrowth { get; set; } = 2;

    /// <summary>Seconds before its first attack, however big its army.</summary>
    public float FirstAttack { get; set; } = 120;

    /// <summary>How many attacks it has sent.</summary>
    [DoNotSerialize]
    public int Waves { get; internal set; }

    internal float ThinkLeft { get; set; }

    internal int NextArmy { get; set; }

    internal float TrainLeft { get; set; }
}
