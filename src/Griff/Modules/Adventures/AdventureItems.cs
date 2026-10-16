using System.Globalization;
using System.Numerics;
using Griff.Data;
using Griff.Scenes;

namespace Griff.Modules.Adventures;

/// <summary>Where an <see cref="InventoryBar"/> goes on the screen.</summary>
public enum BarPlace
{
    /// <summary>Along the bottom of the screen.</summary>
    Bottom,

    /// <summary>Along the top of the screen.</summary>
    Top,
}

/// <summary>
/// An adventure's inventory, with no code: a bar along the bottom of the screen showing what the walker's
/// <see cref="Inventory"/> holds, each item's icon and name. Click an item to pick it up (the pointer turns into it), then
/// click a hotspot to walk over and use it there (the hotspot's <see cref="UseWith"/> for that item says what happens) or
/// another item to put the two together (<see cref="Combinations"/>); a right click on an item looks at it (its
/// <see cref="Descriptions"/> line), and a right click, or a click on it again, puts it back. Anything that doesn't go
/// together gets the hotspot's Wrong Item Line or this bar's <see cref="WrongLine"/>. Taps do the same on a touch screen,
/// and the keys and a controller go over the items with the walker's Items, Use, Look and Cancel actions.
/// </summary>
/// <remarks>
/// Items come from the <see cref="Inventory"/> (a hotspot's Use Gives, a Use With's Gives, a combination, a
/// <see cref="Pickup"/> or a script put them there). Each item's icon is its row in <see cref="Icons"/>, its row of the
/// <see cref="Table"/>, the picture of a hotspot in the scene that gives it, or else a tile with its name. Combinations can
/// come from a table too (<see cref="CombinationsTable"/>). What's on the pointer (<see cref="Holding"/>) is kept with the
/// scene, so rewinds and saves keep it.
/// </remarks>
[Category("Adventure")]
[Requires(typeof(Inventory), Note = "what it shows")]
[WorksWith(typeof(Walker), Note = "on the same entity: says what's looked at, and walks over to use what's picked")]
[WorksWith(typeof(UseWith), Note = "on a hotspot: what an item used on it does")]
[ComponentName("Griff.InventoryBar")]
public sealed class InventoryBar : Component, IPlaytestReport
{
    private IReadOnlyList<string> _descriptions = [];
    private IReadOnlyList<string> _icons = [];
    private IReadOnlyList<string> _combinations = [];
    private Dictionary<string, string> _described = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ItemIcon> _pictured = new(StringComparer.OrdinalIgnoreCase);
    private List<Combination> _combined = [];

    /// <summary>What the walker says on looking at each item, one per row: the item, a colon and the line, e.g. "Key: A rusty old key."</summary>
    [Group("Items")]
    [ItemLines]
    public IReadOnlyList<string> Descriptions
    {
        get => _descriptions;
        set
        {
            _descriptions = value?.ToArray() ?? [];
            _described = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (item, line) in Rows(_descriptions))
                _described.TryAdd(item, line);
        }
    }

    /// <summary>
    /// Each item's picture, one per row: the item, a colon and an image in Assets, e.g. "Key: Sprites/key.png", or a frame of
    /// a sprite sheet with its columns and rows and the frame, "Rope: Sprites/items.png 4x2 5". An item with none uses the
    /// picture of a hotspot in the scene that gives it, or a tile with its name.
    /// </summary>
    [ItemIcons]
    public IReadOnlyList<string> Icons
    {
        get => _icons;
        set
        {
            _icons = value?.ToArray() ?? [];
            _pictured = new Dictionary<string, ItemIcon>(StringComparer.OrdinalIgnoreCase);
            foreach (var (item, icon) in Rows(_icons))
            {
                if (ItemIcon.Parse(icon) is { } parsed)
                    _pictured.TryAdd(item, parsed);
            }
        }
    }

    /// <summary>
    /// Items that go together, one per row: what they make, "=", the two items joined with "+", and after a colon what the
    /// walker says, e.g. "Grappling Hook = Rope + Hook: I tie the rope to the hook." Both are used up and what it makes is
    /// put in; to keep one, make it again ("Knife + Short Rope = Knife + Rope").
    /// </summary>
    [Combinations]
    public IReadOnlyList<string> Combinations
    {
        get => _combinations;
        set
        {
            _combinations = value?.ToArray() ?? [];
            _combined = _combinations.Select(Combination.Parse).OfType<Combination>().ToList();
        }
    }

    /// <summary>
    /// A table of items, e.g. "Items": a row for each, named like the item, whose Description column is what's said on
    /// looking at it and whose Icon column is its picture (as in <see cref="Icons"/>). The rows above win over it.
    /// </summary>
    [Group("From Tables")]
    [Names(NameKind.Table)]
    public string? Table { get; set; }

    /// <summary>
    /// A table of combinations, e.g. "Combinations": a row for each, named like what it makes, whose Combine column is the
    /// two items ("Rope + Hook") and whose Line column is what the walker says. <see cref="Combinations"/> win over it.
    /// </summary>
    [Names(NameKind.Table)]
    public string? CombinationsTable { get; set; }

    /// <summary>
    /// What the walker says when an item is used on a hotspot that has no <see cref="UseWith"/> for it (and no Wrong Item Line
    /// of its own), or two items that don't go together are put together. Empty says nothing.
    /// </summary>
    [Group("Lines")]
    [PlainText("what the walker says, its own words")]
    public string? WrongLine { get; set; } = "That doesn't work.";

    /// <summary>Along the bottom of the screen or the top.</summary>
    [Group("Look")]
    public BarPlace Place { get; set; }

    /// <summary>The most items shown at once, up to 12: arrows at its ends scroll through more.</summary>
    public int Slots { get; set; } = 7;

    /// <summary>Shows the bar while it has nothing in it too; otherwise it shows up with the first item.</summary>
    public bool ShowEmpty { get; set; }

    /// <summary>Writes each item's name under its icon.</summary>
    public bool ShowNames { get; set; } = true;

    /// <summary>The color of the bar behind the items.</summary>
    public Color BarColor { get; set; } = Color.FromBytes(0x16, 0x1A, 0x24, 0xD8);

    /// <summary>The color of the items' names, and of the line round the one the keys are on.</summary>
    public Color TextColor { get; set; } = Color.FromBytes(0xF2, 0xEC, 0xDA);

    /// <summary>A sound played as an item is put in it, e.g. "Sounds/get.wav".</summary>
    [Group("Sounds")]
    public string? GetSound { get; set; }

    /// <summary>A sound played as an item is picked up onto the pointer.</summary>
    public string? PickSound { get; set; }

    /// <summary>A sound played as two items are put together.</summary>
    public string? CombineSound { get; set; }

    /// <summary>The item on the pointer, picked from the bar to use on something; empty for none. Read as the "Holding" output.</summary>
    [Group("State")]
    [Output]
    [PlainText("the item on the pointer: the game sets it as items are picked")]
    public string? Holding { get; set; }

    /// <summary>How far the bar is scrolled: the first item shown, counting from 0.</summary>
    public int Scroll { get; set; }

    /// <summary>The item the keys or a controller are on, counting from 0, while they're on the bar; -1 while they're not.</summary>
    public int Cursor { get; set; } = -1;

    /// <summary>Raised as an item is picked up onto the pointer (with its name) or put back (with null).</summary>
    public event Action<string?>? Picked;

    /// <summary>Raised as two items are put together, with the two and what they made (nothing, for two that don't go together).</summary>
    public event Action<string, string, IReadOnlyList<string>>? Combined;

    /// <summary>Raised as an item is looked at, with its name.</summary>
    public event Action<string>? Looked;

    /// <summary>Picks <paramref name="item"/> up onto the pointer, if the inventory holds one, as clicking it does.</summary>
    [Action]
    public void Pick(string? item)
    {
        var inventory = Entity?.GetComponent<Inventory>();
        if (string.IsNullOrWhiteSpace(item) || inventory is null || !inventory.Has(item))
            return;
        var name = inventory.Contents.First(c => c.Name.Equals(item.Trim(), StringComparison.OrdinalIgnoreCase)).Name;
        if (Holding == name)
            return;
        Holding = name;
        Picked?.Invoke(name);
    }

    /// <summary>Puts back the item on the pointer.</summary>
    [Action]
    public void PutBack()
    {
        if (Holding is null)
            return;
        Holding = null;
        Picked?.Invoke(null);
    }

    /// <summary>Puts two items together, "Rope + Hook", as clicking one with the other does, at the start of the next frame.</summary>
    [Action]
    public void Combine(string? items) => Asked.Add((true, items?.Trim() ?? ""));

    /// <summary>Has the walker look at <paramref name="item"/> and say its description, at the start of the next frame.</summary>
    [Action]
    public void LookAt(string? item) => Asked.Add((false, item?.Trim() ?? ""));

    /// <summary>What's said on looking at <paramref name="item"/>: its row of <see cref="Descriptions"/>, or of the table; null for none.</summary>
    public string? DescriptionOf(string item, TableSet? tables = null) =>
        _described.TryGetValue(item.Trim(), out var line) ? line : FromTable(tables, Table, item, "Description");

    /// <summary>The picture <paramref name="item"/> has of its own (its row of <see cref="Icons"/>, or of the table), or null.</summary>
    public ItemIcon? IconOf(string item, TableSet? tables = null) =>
        _pictured.TryGetValue(item.Trim(), out var icon) ? icon : ItemIcon.Parse(FromTable(tables, Table, item, "Icon"));

    /// <summary>What <paramref name="first"/> and <paramref name="second"/> make together (either way round), or null if they don't go together.</summary>
    public Combination? CombinationOf(string first, string second, TableSet? tables = null)
    {
        foreach (var combination in _combined)
        {
            if (combination.Joins(first, second))
                return combination;
        }

        if (string.IsNullOrWhiteSpace(CombinationsTable) || TableOf(tables, CombinationsTable) is not { } table)
            return null;
        var combine = table.ColumnIndex("Combine");
        var line = table.ColumnIndex("Line");
        if (combine < 0)
            return null;
        foreach (var row in table.Rows)
        {
            if (Combination.Parse($"{row.Name} = {row[combine].AsText}{(line >= 0 && !row[line].IsEmpty ? ": " + row[line].AsText : "")}") is { } found &&
                found.Joins(first, second))
                return found;
        }

        return null;
    }

    // Asked for by Combine and Look At: carried out by the game at its next step. Not saved.
    internal List<(bool Combine, string What)> Asked { get; } = [];

    // The item picked by pressing on it, while the button's still down: let go over a hotspot (or another item), it's used there.
    internal string? Dragged { get; set; }

    internal void RaiseCombined(string first, string second, IReadOnlyList<string> made) => Combined?.Invoke(first, second, made);

    internal void RaiseLooked(string item) => Looked?.Invoke(item);

    internal void RaisePicked(string? item) => Picked?.Invoke(item);

    // A row of a table, by the item's name; null if there's no table, row, column or value.
    private static string? FromTable(TableSet? tables, string? name, string item, string column)
    {
        if (string.IsNullOrWhiteSpace(name) || TableOf(tables, name) is not { } table || table.Find(item.Trim()) is not { } row)
            return null;
        var i = table.ColumnIndex(column);
        return i < 0 || row[i].IsEmpty ? null : row[i].AsText;
    }

    private static Data.Table? TableOf(TableSet? tables, string name)
    {
        try
        {
            return tables?.Find(name.Trim());
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    // "Item: rest" rows, each trimmed; rows without a colon are left out.
    private static IEnumerable<(string Item, string Text)> Rows(IEnumerable<string> rows)
    {
        foreach (var row in rows)
        {
            var colon = row.IndexOf(':');
            if (colon <= 0)
                continue;
            var (item, text) = (row[..colon].Trim(), row[(colon + 1)..].Trim());
            if (item.Length > 0 && text.Length > 0)
                yield return (item, text);
        }
    }

    /// <summary>A line for the Inspector about rows it can't read.</summary>
    public override string? SetupNote()
    {
        var bad = _combinations.FirstOrDefault(row => !string.IsNullOrWhiteSpace(row) && Combination.Parse(row) is null);
        if (bad is not null)
            return $"\"{bad.Trim()}\" isn't a combination: write what they make, \"=\", and the two items joined with \"+\".";
        var icon = _icons.FirstOrDefault(row => !string.IsNullOrWhiteSpace(row) && (row.IndexOf(':') <= 0 || ItemIcon.Parse(row[(row.IndexOf(':') + 1)..]) is null));
        return icon is null ? null : $"\"{icon.Trim()}\" isn't an icon: write the item, a colon and an image.";
    }

    string IPlaytestReport.Report() => string.IsNullOrEmpty(Holding) ? "" : $", {Holding} on the pointer";
}

/// <summary>
/// What using an item on a <see cref="Hotspot"/> does, with no code: a key on a door, a coin given to a beggar, a bone
/// thrown to a dog. Put one on the hotspot for each item that does something there. When the walker uses its
/// <see cref="Item"/> on the hotspot (picked from the <see cref="InventoryBar"/>), it walks over and says its
/// <see cref="Line"/>, does its action on its targets, sets its flag, sends its message, starts the hotspot's dialogue
/// (<see cref="Talks"/>), gives up the item (unless it <see cref="KeepsItem"/>) and gets what it <see cref="Gives"/>.
/// </summary>
/// <remarks>
/// An item with no Use With on a hotspot gets its Wrong Item Line, or the bar's Wrong Line. With <see cref="NeedsFlag"/>, it
/// only counts while a flag is set (or isn't): put two on a door for the same key, one needing "DoorLocked".
/// </remarks>
[Category("Adventure")]
[Requires(typeof(Hotspot), Note = "what the item is used on")]
[WorksWith(typeof(InventoryBar), Note = "on the walker: where the item is picked from")]
[ComponentName("Griff.UseWith")]
public sealed class UseWith : Component
{
    private string? _item;
    private List<string> _items = [];

    /// <summary>The item that's used on the hotspot, e.g. "Key"; several separated by commas each do the same.</summary>
    [Names(NameKind.Item, Many = true)]
    public string? Item
    {
        get => _item;
        set
        {
            _item = value;
            _items = (value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        }
    }

    /// <summary>A flag it needs to count, e.g. "DoorLocked", or "not DoorOpen"; while it doesn't hold, the item is the wrong one here. Empty always counts.</summary>
    [Names(NameKind.Flag, Condition = true)]
    public string? NeedsFlag { get; set; }

    /// <summary>What the walker says, e.g. "The key turns. It's open!"</summary>
    [Group("What It Does")]
    [PlainText("what the walker says, its own words")]
    public string? Line { get; set; }

    /// <summary>Starts the <see cref="Dialogue"/> on the hotspot's entity: giving something to a character.</summary>
    public bool Talks { get; set; }

    /// <summary>An action done on its <see cref="Targets"/> (or the hotspot itself): a door's "Open", a switch's "Toggle"...</summary>
    [ActionName(On = nameof(Targets))]
    public string? Do { get; set; }

    /// <summary>What Do is done on, by name, separated by commas (those inside the hotspot first). Empty does it on the hotspot's entity.</summary>
    [EntityName(Many = true)]
    public string? Targets { get; set; }

    /// <summary>What Do is done with, for an action that takes a value. Empty uses the action's own default.</summary>
    [ActionValue(nameof(Do))]
    public string? With { get; set; }

    /// <summary>A flag set, e.g. "DoorOpen"; "not Name" clears one. Flags are kept in the save data.</summary>
    [Names(NameKind.Flag, Condition = true, Defines = true)]
    public string? Flag { get; set; }

    /// <summary>A message sent, with the walker as its data.</summary>
    [Names(NameKind.Message)]
    public string? Message { get; set; }

    /// <summary>Keeps the item rather than giving it up: a key that opens more than one door, a torch to light things with.</summary>
    public bool KeepsItem { get; set; }

    /// <summary>Items put in the walker's inventory, separated by commas, each with how many if more than one: "Coin 3, Map".</summary>
    [Counts(NameKind.Item, Defines = true)]
    public string? Gives
    {
        get => _gives;
        set => (_gives, Given) = (value, Items.Parse(value));
    }

    private string? _gives;

    // Its Gives, read once each time it changes.
    internal List<(string Item, int Count)> Given { get; private set; } = [];

    /// <summary>A sound played, e.g. "Sounds/unlock.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>How many times it has been done. The game counts them.</summary>
    [Group("State")]
    [Output]
    public int TimesUsed { get; set; }

    /// <summary>Raised as it's done, with the walker and the item.</summary>
    public event Action<Walker, string>? Done;

    /// <summary>True if <paramref name="item"/> is one it's for.</summary>
    public bool Takes(string item)
    {
        foreach (var mine in _items)
        {
            if (mine.Equals(item.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>A line for the Inspector when it has no item.</summary>
    public override string? SetupNote() => _items.Count == 0 ? "Give it the Item that's used on the hotspot." : null;

    internal void RaiseDone(Walker walker, string item) => Done?.Invoke(walker, item);
}

/// <summary>Two items that go together (see <see cref="InventoryBar.Combinations"/>): what they make, and what the walker says.</summary>
public sealed record Combination(string First, string Second, IReadOnlyList<(string Item, int Count)> Makes, string? Line)
{
    /// <summary>True if it's <paramref name="a"/> and <paramref name="b"/>, either way round.</summary>
    public bool Joins(string a, string b) =>
        (First.Equals(a.Trim(), StringComparison.OrdinalIgnoreCase) && Second.Equals(b.Trim(), StringComparison.OrdinalIgnoreCase)) ||
        (First.Equals(b.Trim(), StringComparison.OrdinalIgnoreCase) && Second.Equals(a.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads "Grappling Hook = Rope + Hook: I tie them together." (what it makes may be nothing, or several joined with "+"); null if it isn't one.</summary>
    public static Combination? Parse(string? row)
    {
        var equals = row?.IndexOf('=') ?? -1;
        if (row is null || equals < 0)
            return null;
        var right = row[(equals + 1)..];
        var colon = right.IndexOf(':');
        var line = colon >= 0 ? right[(colon + 1)..].Trim() : null;
        var takes = Items.Parse(colon >= 0 ? right[..colon] : right);
        if (takes.Count != 2 || takes.Any(t => t.Count != 1))
            return null;
        return new Combination(takes[0].Item, takes[1].Item, Items.Parse(row[..equals]), string.IsNullOrEmpty(line) ? null : line);
    }
}

/// <summary>An item's picture on an <see cref="InventoryBar"/>: an image in Assets, the part of it to show, and its width over its height.</summary>
public readonly record struct ItemIcon(string Image, Vector4 Source, float Aspect = 1)
{
    /// <summary>Reads "Sprites/key.png", or a frame of a sprite sheet, "Sprites/items.png 4x2 5" (4 across, 2 down, frame 5); null for nothing.</summary>
    public static ItemIcon? Parse(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame) &&
            parts[^2].Split('x', 'X') is [var across, var down] &&
            int.TryParse(across, NumberStyles.Integer, CultureInfo.InvariantCulture, out var columns) &&
            int.TryParse(down, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rows) && columns > 0 && rows > 0)
        {
            var image = string.Join(' ', parts[..^2]);
            var cells = columns * rows;
            frame = (frame % cells + cells) % cells;
            return new ItemIcon(image, new Vector4((float)(frame % columns) / columns, (float)(frame / columns) / rows, 1f / columns, 1f / rows));
        }

        return new ItemIcon(text, new Vector4(0, 0, 1, 1));
    }
}

