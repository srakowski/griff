using System.Globalization;

namespace Griff.Scenes;

/// <summary>
/// What an entity is carrying: keys, potions, the parts of a machine, gold, blocks. A <see cref="Pickup"/> with an
/// <see cref="Pickup.Item"/> puts things in the inventory of whoever collects it (adding one if they don't have one);
/// locked <see cref="Door"/>s and <see cref="Switch"/>es use up its keys. A <see cref="Counter"/> with an
/// <see cref="Counter.Item"/> shows how many there are. Items are the one idea every genre shares (see
/// <see cref="Scenes.Items"/>): what a recipe takes, a strategy unit or a card costs or a shop's pickup sells for is paid
/// from it, and with <see cref="ForTeam"/> it holds a whole team's.
/// </summary>
/// <remarks>
/// It's saved with the scene, so it rewinds with everything else and you can start a playtest holding a key; with
/// <see cref="SaveAs"/> it's kept in the save data too, from one run (and one scene) to the next.
/// </remarks>
[Category("Gameplay")]
[ComponentName("Griff.Inventory")]
public sealed class Inventory : Component, IHoldsItems, ICarried
{
    // Names in the order first added, so the Inspector's list doesn't shuffle.
    private readonly List<(string Name, int Count)> _items = [];
    private IReadOnlyList<string>? _rows;
    private IReadOnlyList<string> _uses = [];
    private IReadOnlyList<(string Item, IReadOnlyList<(string Verb, string Argument)> Effects)> _effects = [];

    /// <summary>What it holds, one kind per row: a name and how many, e.g. "Key 2" (just "Key" is one).</summary>
    [ItemCounts]
    public IReadOnlyList<string> Items
    {
        get => _rows ??= _items.Select(i => i.Count == 1 ? i.Name : $"{i.Name} {i.Count.ToString(CultureInfo.InvariantCulture)}").ToList();
        set
        {
            _items.Clear();
            _rows = null;
            Version++;
            foreach (var row in value ?? [])
            {
                var (item, count) = Scenes.Items.ReadOne(row);
                AddQuietly(item, count);
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
    [ItemUses]
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

    /// <summary>
    /// Keeps what it holds in the game's save data under this name, e.g. "Items", so it's still there the next time the
    /// game runs, and an inventory saved as the same name in the next scene starts with it (a player walking from room to
    /// room). Empty keeps nothing. Game → Clear Save Data forgets it while you test.
    /// </summary>
    [Group("Saving")]
    [Names(NameKind.Saved, Defines = true)]
    public string? SaveAs { get; set; }

    /// <summary>
    /// Holds a team's items, e.g. "Player": anything on that team (see <see cref="Teams"/>) without an inventory of its
    /// own collects into it and pays from it, so a platformer hero's coins buy a strategy army or pay for cards. Empty
    /// holds only its own entity's.
    /// </summary>
    [Group("Team")]
    [Names(NameKind.Kind, Scope = "Team")]
    public string? ForTeam { get; set; }

    /// <summary>Set by the game once it has taken what it holds from the save data, so a rewind doesn't do it again. Leave it be.</summary>
    public bool Loaded { get; set; }

    public override bool Uses(string property) => property != nameof(Loaded);

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

    // What it holds, without a copy (for what reads it every frame), and a number that goes up whenever that changes.
    internal IReadOnlyList<(string Name, int Count)> Held => _items;

    internal int Version { get; private set; }

    // The Version last kept in the save data.
    internal int SavedVersion { get; set; } = -1;

    // Its rows one per line in the save data.
    void ICarried.Load(ICarryStore data, string key)
    {
        if (data.GetString(key) is { } saved)
            Items = saved.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        SavedVersion = Version;
    }

    bool ICarried.Save(ICarryStore data, string key, bool always)
    {
        if (!always && SavedVersion == Version)
            return false;
        SavedVersion = Version;
        data.Set(key, string.Join('\n', Items));
        return true;
    }

    /// <summary>How many of <paramref name="item"/> it holds (names ignore case). Read as "Count:Key".</summary>
    [Output(Example = "Key")]
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

    /// <summary>Puts in the items listed, e.g. "Gold 50, Key": something won, bought or found.</summary>
    [Action]
    public void AddItems(string items)
    {
        foreach (var (item, count) in Scenes.Items.Parse(items))
            Add(item, count);
    }

    /// <summary>Takes out the items listed, e.g. "Gold 50", if it holds them all; otherwise takes nothing.</summary>
    [Action]
    public void TakeItems(string items)
    {
        var wanted = Scenes.Items.Totals(items);
        if (wanted.Any(w => !Has(w.Item, w.Count)))
            return;
        foreach (var (item, count) in wanted)
            Take(item, count);
    }

    bool IHoldsItems.Keeps(string item) => true;

    /// <summary>Empties it.</summary>
    public void Clear()
    {
        var names = _items.Select(i => i.Name).ToList();
        _items.Clear();
        _rows = null;
        Version++;
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
        Version++;
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

    // Without garbage, so its count can be read every frame.
    private int IndexOf(string item)
    {
        var name = item.AsSpan().Trim();
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].Name.AsSpan().Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
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
            if (ItemUsesAttribute.Read(line) is not { } use)
                continue;
            var effects = use.Effects.Where(e => Verbs.Contains(e.Verb) && e.Argument.Length > 0).ToList();
            if (effects.Count > 0)
                uses.Add((use.Item, effects));
        }

        return uses;
    }
}
