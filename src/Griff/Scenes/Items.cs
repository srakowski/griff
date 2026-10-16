using System.Collections.Concurrent;
using System.Globalization;

namespace Griff.Scenes;

/// <summary>
/// A component that holds items and resources: an <see cref="Inventory"/>, a strategy faction's resources, a card hand's
/// mana. Every genre's items are the one idea (see <see cref="Items"/>), so what a pickup gives, a recipe takes, a unit
/// costs or a card costs is paid from whichever holds it. A script component can implement it too.
/// </summary>
public interface IHoldsItems
{
    /// <summary>
    /// The team these are the items of (see <see cref="Teams"/>), so anything on that team without items of its own
    /// collects and pays with them, e.g. a faction's gold; empty when they're only its entity's own.
    /// </summary>
    string? ForTeam { get; }

    /// <summary>True if it holds this kind of item at all: an inventory holds anything, a card hand only its mana.</summary>
    bool Keeps(string item);

    /// <summary>How many of <paramref name="item"/> it holds (names ignore case).</summary>
    int Count(string item);

    /// <summary>Puts <paramref name="count"/> of <paramref name="item"/> in.</summary>
    void Add(string item, int count = 1);

    /// <summary>Takes <paramref name="count"/> of <paramref name="item"/> out, if it has that many; returns whether it did.</summary>
    bool Take(string item, int count = 1);
}

/// <summary>
/// Items and resources, the one idea every genre shares: an item is a name ("Key", "Gold", "Dirt", "Mana") and how many,
/// held by anything that holds items (see <see cref="IHoldsItems"/>). Lists of them read the same everywhere: "Coin 3,
/// Map", "150 Gold, 50 Wood" (the count before or after the name, 1 if it doesn't say), separated by commas, semicolons or
/// "+". A cost is such a list: what a strategy unit takes to train, a card to play beside its mana, a pickup to buy in a
/// shop. Something pays from (and collects into) the items on it or the entity it's inside, else its team's (an
/// inventory or faction whose For Team is its team).
/// </summary>
public static class Items
{
    private static readonly ConcurrentDictionary<string, (string Item, int Count)[]> Read = new(StringComparer.Ordinal);

    /// <summary>
    /// The items in <paramref name="text"/> in order, each a name and how many: "Coin 3, Map", "150 Gold; 50 Wood",
    /// "Rope + Hook". Parts with a count of 0 or less are left out; the same item twice stays twice.
    /// </summary>
    public static List<(string Item, int Count)> Parse(string? text)
    {
        var items = new List<(string, int)>();
        foreach (var part in (text ?? "").Split([',', ';', '+'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var (item, count) = ReadOne(part);
            if (item.Length > 0 && count > 0)
                items.Add((item, count));
        }

        return items;
    }

    /// <summary>
    /// <paramref name="text"/> read as <see cref="Parse"/> reads it, with each item once (counts added up): what a cost
    /// asks for. Kept once read, so reading the same text every frame makes no garbage.
    /// </summary>
    public static IReadOnlyList<(string Item, int Count)> Totals(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        if (Read.TryGetValue(text, out var known))
            return known;
        var totals = new List<(string Item, int Count)>();
        foreach (var (item, count) in Parse(text))
        {
            var i = totals.FindIndex(t => t.Item.Equals(item, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
                totals[i] = (totals[i].Item, totals[i].Count + count);
            else
                totals.Add((item, count));
        }

        if (Read.Count > 4096)
            Read.Clear();
        return Read[text] = [.. totals];
    }

    /// <summary>One item and how many: "Key 2", "150 Gold" or "Map" (one). The name may have spaces ("Iron Ore 5").</summary>
    public static (string Item, int Count) ReadOne(string? text)
    {
        var part = text?.Trim() ?? "";
        var last = part.LastIndexOf(' ');
        if (last > 0 && int.TryParse(part.AsSpan(last + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var after))
            return (part[..last].TrimEnd(), after);
        var first = part.IndexOf(' ');
        if (first > 0 && int.TryParse(part.AsSpan(0, first), NumberStyles.Integer, CultureInfo.InvariantCulture, out var before))
            return (part[(first + 1)..].TrimStart(), before);
        return (part, 1);
    }

    /// <summary>One item written as a row of a list: "Key 2", or "Key" for one; with <paramref name="countFirst"/>, "2 Key" ("1 Key").</summary>
    public static string Write(string item, int count, bool countFirst = false) =>
        countFirst ? string.Create(CultureInfo.InvariantCulture, $"{count} {item.Trim()}")
        : count == 1 ? item.Trim()
        : string.Create(CultureInfo.InvariantCulture, $"{item.Trim()} {count}");

    /// <summary>"150 Gold, 50 Wood": the way a cost reads, or "Free" for nothing.</summary>
    public static string Describe(string? cost)
    {
        var totals = Totals(cost);
        return totals.Count == 0 ? "Free" : string.Join(", ", totals.Select(t => Write(t.Item, t.Count, countFirst: true)));
    }

    /// <summary>
    /// What holds <paramref name="item"/> for <paramref name="entity"/>: something holding items on it or the entity it's
    /// inside, else what holds <paramref name="team"/>'s items (see <see cref="IHoldsItems.ForTeam"/>). Null if nothing does.
    /// </summary>
    public static IHoldsItems? HolderOf(Entity? entity, string item, string? team)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            var components = e.Components;
            for (var i = 0; i < components.Count; i++)
            {
                if (components[i] is IHoldsItems holder && holder.Keeps(item))
                    return holder;
            }
        }

        if (string.IsNullOrWhiteSpace(team) || entity?.Scene is not { } scene)
            return null;
        foreach (var holder in scene.ActiveNow<IHoldsItems>())
        {
            if (Teams.Allies(holder.ForTeam, team) && holder.Keeps(item))
                return holder;
        }

        return null;
    }

    /// <summary>How many of <paramref name="item"/> <paramref name="entity"/> has to pay with (see <see cref="HolderOf"/>).</summary>
    public static int Count(Entity? entity, string item, string? team) => HolderOf(entity, item, team)?.Count(item) ?? 0;

    /// <summary>The first item of <paramref name="cost"/> that <paramref name="entity"/> hasn't enough of, e.g. "Wood"; null if it can pay.</summary>
    public static string? Lacking(Entity? entity, string? cost, string? team)
    {
        foreach (var (item, count) in Totals(cost))
        {
            if (Count(entity, item, team) < count)
                return item;
        }

        return null;
    }

    /// <summary>True if <paramref name="entity"/> has everything <paramref name="cost"/> asks for, e.g. "150 Gold, 50 Wood".</summary>
    public static bool CanPay(Entity? entity, string? cost, string? team) => Lacking(entity, cost, team) is null;

    /// <summary>Pays <paramref name="cost"/> if <paramref name="entity"/> can; returns whether it did. Nothing is taken if it can't pay it all.</summary>
    public static bool Pay(Entity? entity, string? cost, string? team)
    {
        if (!CanPay(entity, cost, team))
            return false;
        foreach (var (item, count) in Totals(cost))
            HolderOf(entity, item, team)?.Take(item, count);
        return true;
    }
}
