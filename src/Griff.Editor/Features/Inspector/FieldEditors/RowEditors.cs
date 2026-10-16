using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Editing;
using Griff.Scenes;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// Items and how many of each (<see cref="ItemCountsAttribute"/>), e.g. what an inventory holds: a row each, the item
/// picked from those the game gives (or typed, for a new one) and how many; pick one in the list under them to add it.
/// </summary>
public sealed class ItemCountsEditor : FieldEditor<ItemCountsAttribute>
{
    public ItemCountsEditor(PropertyField field, ItemCountsAttribute attribute)
        : base(field, attribute)
    {
        Refresh();
    }

    /// <summary>The items, a row each.</summary>
    public ObservableCollection<ItemCountRow> Rows { get; } = [];

    /// <summary>The items the game gives (a pickup's Item, a script's Add), to pick from.</summary>
    public IReadOnlyList<string> Items => [.. (Host.Names?.Items ?? []).Order(StringComparer.OrdinalIgnoreCase)];

    internal List<string> Entries => [.. Lines.Where(l => l.Trim().Length > 0)];

    /// <summary>Adds <paramref name="count"/> of <paramref name="item"/> on the end (or to its row, if it has one), as one undo step.</summary>
    public void Add(string item, int count = 1)
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new ArgumentException("Name an item, like Key.", nameof(item));
        var entries = Entries;
        var at = entries.FindIndex(e => ItemCountsAttribute.Read(e).Item.Equals(item.Trim(), StringComparison.OrdinalIgnoreCase));
        if (at >= 0)
            entries[at] = ItemCountsAttribute.Write(item, ItemCountsAttribute.Read(entries[at]).Count + Math.Max(1, count));
        else
            entries.Add(ItemCountsAttribute.Write(item, Math.Max(1, count)));
        SetLines(entries);
    }

    /// <summary>Sets row <paramref name="number"/> (from 1): its item and how many, as one undo step.</summary>
    public void Set(int number, string item, int count)
    {
        var entries = Entries;
        Check(number, entries.Count);
        entries[number - 1] = ItemCountsAttribute.Write(item, Math.Max(1, count));
        SetLines(entries);
    }

    /// <summary>Takes row <paramref name="number"/> (from 1) out, as one undo step.</summary>
    public void Remove(int number)
    {
        var entries = Entries;
        Check(number, entries.Count);
        entries.RemoveAt(number - 1);
        SetLines(entries);
    }

    /// <summary>Adds the item picked from the list on the end, one of it.</summary>
    public string? AddItem
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                Add(value);
            OnPropertyChanged();
        }
    }

    public override void Refresh()
    {
        Fit(Rows, Entries.Count, () => new ItemCountRow(this));
        OnPropertyChanged(string.Empty);
    }

    internal (string Item, int Count) Entry(int number) =>
        Entries.ElementAtOrDefault(number - 1) is { } line ? ItemCountsAttribute.Read(line) : ("", 1);

    internal void Change(int number, string item, int count, bool seal)
    {
        var entries = Entries;
        if (number < 1 || number > entries.Count || item.Trim().Length == 0)
            return;
        entries[number - 1] = ItemCountsAttribute.Write(item, Math.Max(1, count));
        SetLines(entries, seal);
    }

    private static void Check(int number, int count)
    {
        if (number < 1 || number > count)
            throw new ArgumentOutOfRangeException(nameof(number), count == 0 ? "There are no items yet: Add puts one in." : $"The items are numbered 1 to {count}.");
    }
}

/// <summary>One row of an <see cref="ItemCountsEditor"/>: an item and how many.</summary>
public sealed class ItemCountRow(ItemCountsEditor owner) : EditorRow(owner.Remove)
{
    public override string ToString() => $"{Number}: {Item} × {Count}";

    /// <summary>The item, picked from those the game gives or typed.</summary>
    public string Item
    {
        get => owner.Entry(Number).Item;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Trim() != Item)
                owner.Change(Number, value, owner.Entry(Number).Count, seal: true);
        }
    }

    /// <summary>How many.</summary>
    public double Count
    {
        get => owner.Entry(Number).Count;
        set => owner.Change(Number, Item, (int)Math.Round(value), seal: false);
    }

    /// <summary>The items to pick from (this one first, if the game doesn't give it anywhere else).</summary>
    public IReadOnlyList<string> Choices => owner.Items.Contains(Item, StringComparer.OrdinalIgnoreCase) || Item.Length == 0 ? owner.Items : [Item, .. owner.Items];

    /// <summary>Picks the item from <see cref="Choices"/> (the list beside its box).</summary>
    public string? Pick
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                Item = value;
            OnPropertyChanged();
        }
    }
}

/// <summary>
/// What items do when they're used (<see cref="ItemUsesAttribute"/>), e.g. an inventory's Item Uses: a row an item, its
/// name picked from those the game gives, and under it what it does, a row each: heal some hit points, spawn a prefab or
/// send a message, each picked from the project's.
/// </summary>
public sealed class ItemUsesEditor : FieldEditor<ItemUsesAttribute>
{
    public ItemUsesEditor(PropertyField field, ItemUsesAttribute attribute)
        : base(field, attribute)
    {
        Refresh();
    }

    /// <summary>The items' uses, a row each.</summary>
    public ObservableCollection<ItemUseRow> Rows { get; } = [];

    /// <summary>What an item can do: heal, spawn, send.</summary>
    public IReadOnlyList<string> Verbs => ItemUsesAttribute.Verbs;

    /// <summary>The items the game gives, to pick from.</summary>
    public IReadOnlyList<string> Items => [.. (Host.Names?.Items ?? []).Order(StringComparer.OrdinalIgnoreCase)];

    internal List<string> Entries => [.. Lines.Where(l => l.Trim().Length > 0)];

    internal (string Item, List<(string Verb, string Argument)> Effects) Entry(int number) =>
        Entries.ElementAtOrDefault(number - 1) is { } line && ItemUsesAttribute.Read(line) is { } use ? (use.Item, use.Effects.ToList()) : ("", []);

    /// <summary>What <paramref name="verb"/> can be given: the project's prefabs to spawn, its messages to send; none to heal.</summary>
    internal IReadOnlyList<string> ArgumentsFor(string verb) => verb switch
    {
        "spawn" => [.. (Host.Names?.Prefabs ?? Host.PrefabNames).Order(StringComparer.OrdinalIgnoreCase)],
        "send" => Host.Names is { } names ? [.. names.Choices(new NamesAttribute(NameKind.Message)).Select(c => c.Name)] : [],
        _ => [],
    };

    /// <summary>
    /// Adds what <paramref name="item"/> does: <paramref name="verb"/> (heal, spawn or send) with <paramref name="argument"/>
    /// (how much, which prefab, which message), after what it does already, or on a row of its own; one undo step.
    /// </summary>
    public void Add(string item, string verb = "heal", string? argument = null)
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new ArgumentException("Name an item, like Potion.", nameof(item));
        var effect = Effect(verb, argument);
        var entries = Entries;
        var at = entries.FindIndex(e => ItemUsesAttribute.Read(e)?.Item.Equals(item.Trim(), StringComparison.OrdinalIgnoreCase) == true);
        if (at >= 0)
            entries[at] = ItemUsesAttribute.Write(item, [.. ItemUsesAttribute.Read(entries[at])!.Value.Effects, effect]);
        else
            entries.Add(ItemUsesAttribute.Write(item, [effect]));
        SetLines(entries);
    }

    /// <summary>Renames row <paramref name="number"/>'s item (from 1), as one undo step.</summary>
    public void SetItem(int number, string item) => Change(number, e => (item.Trim(), e.Effects), seal: true);

    /// <summary>Sets what row <paramref name="number"/>'s item does, its effect <paramref name="effect"/> (both from 1), as one undo step.</summary>
    public void SetEffect(int number, int effect, string verb, string? argument)
    {
        var made = Effect(verb, argument);
        Change(number, e =>
        {
            CheckEffect(effect, e.Effects.Count);
            e.Effects[effect - 1] = made;
            return e;
        }, seal: true);
    }

    /// <summary>Takes effect <paramref name="effect"/> of row <paramref name="number"/> out (the row too, with its last), as one undo step.</summary>
    public void RemoveEffect(int number, int effect)
    {
        if (Entry(number).Effects.Count <= 1)
        {
            CheckEffect(effect, Entry(number).Effects.Count);
            Remove(number);
            return;
        }

        Change(number, e =>
        {
            CheckEffect(effect, e.Effects.Count);
            e.Effects.RemoveAt(effect - 1);
            return e;
        }, seal: true);
    }

    /// <summary>Takes row <paramref name="number"/> (from 1) out, as one undo step.</summary>
    public void Remove(int number)
    {
        var entries = Entries;
        if (number < 1 || number > entries.Count)
            throw new ArgumentOutOfRangeException(nameof(number), entries.Count == 0 ? "There are no uses yet: Add makes one." : $"The rows are numbered 1 to {entries.Count}.");
        entries.RemoveAt(number - 1);
        SetLines(entries);
    }

    /// <summary>Adds a use for the item picked from the list: healing 1, to set.</summary>
    public string? AddItem
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                Add(value, "heal", "1");
            OnPropertyChanged();
        }
    }

    public override void Refresh()
    {
        Fit(Rows, Entries.Count, () => new ItemUseRow(this));
        foreach (var row in Rows)
            row.RefreshEffects();
        OnPropertyChanged(string.Empty);
    }

    internal void Change(int number, Func<(string Item, List<(string Verb, string Argument)> Effects), (string Item, List<(string Verb, string Argument)> Effects)> change, bool seal)
    {
        var entries = Entries;
        if (number < 1 || number > entries.Count)
            throw new ArgumentOutOfRangeException(nameof(number), $"The rows are numbered 1 to {entries.Count}.");
        var (item, effects) = change(Entry(number));
        if (item.Length == 0)
            return;
        entries[number - 1] = ItemUsesAttribute.Write(item, effects);
        SetLines(entries, seal);
    }

    private static (string, string) Effect(string verb, string? argument)
    {
        var word = verb.Trim().ToLowerInvariant();
        if (!ItemUsesAttribute.Verbs.Contains(word))
            throw new ArgumentException($"An item can {string.Join(", ", ItemUsesAttribute.Verbs)}, not \"{verb}\".", nameof(verb));
        var what = string.IsNullOrWhiteSpace(argument) ? word == "heal" ? "1" : "" : argument.Trim();
        if (what.Length == 0)
            throw new ArgumentException(word == "spawn" ? "Name the prefab it spawns." : "Name the message it sends.", nameof(argument));
        return (word, what);
    }

    private static void CheckEffect(int effect, int count)
    {
        if (effect < 1 || effect > count)
            throw new ArgumentOutOfRangeException(nameof(effect), $"Its effects are numbered 1 to {count}.");
    }
}

/// <summary>One item of an <see cref="ItemUsesEditor"/>: its name and what it does, a row each.</summary>
public sealed class ItemUseRow(ItemUsesEditor owner) : EditorRow(owner.Remove)
{
    public override string ToString() => $"{Number}: {Item}: {string.Join(", ", Effects.Select(e => $"{e.Verb} {e.Argument}"))}";

    internal ItemUsesEditor Owner => owner;

    /// <summary>The item, picked from those the game gives or typed.</summary>
    public string Item
    {
        get => owner.Entry(Number).Item;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Trim() != Item)
                owner.SetItem(Number, value);
        }
    }

    /// <summary>The items to pick from (this one first, if the game doesn't give it anywhere else).</summary>
    public IReadOnlyList<string> Choices => owner.Items.Contains(Item, StringComparer.OrdinalIgnoreCase) || Item.Length == 0 ? owner.Items : [Item, .. owner.Items];

    /// <summary>Picks the item from <see cref="Choices"/> (the list beside its box).</summary>
    public string? Pick
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                Item = value;
            OnPropertyChanged();
        }
    }

    /// <summary>What it does, a row each.</summary>
    public ObservableCollection<ItemEffectRow> Effects { get; } = [];

    /// <summary>Adds another thing it does: healing 1, to set.</summary>
    public IRelayCommand AddEffectCommand => _addEffect ??= new RelayCommand(() => owner.Add(Item, "heal", "1"));

    private IRelayCommand? _addEffect;

    internal void RefreshEffects()
    {
        var count = owner.Entry(Number).Effects.Count;
        while (Effects.Count > count)
            Effects.RemoveAt(Effects.Count - 1);
        while (Effects.Count < count)
            Effects.Add(new ItemEffectRow(this, Effects.Count + 1));
        foreach (var effect in Effects)
            effect.Refresh();
    }
}

/// <summary>One thing an item does in an <see cref="ItemUsesEditor"/>: heal, spawn or send, and how much or what.</summary>
public sealed class ItemEffectRow(ItemUseRow row, int number) : ObservableObject
{
    public override string ToString() => $"{Number}: {Verb} {Argument}";

    /// <summary>Where it is among the item's effects, from 1.</summary>
    public int Number { get; } = number;

    private (string Verb, string Argument) Effect => row.Owner.Entry(row.Number).Effects.ElementAtOrDefault(Number - 1);

    /// <summary>heal, spawn or send.</summary>
    public string Verb
    {
        get => Effect.Verb ?? "";
        set
        {
            if (value is null || value == Verb)
                return;
            // A new verb keeps an amount for heal, and starts a prefab or message empty to pick.
            var argument = value == "heal" ? "1" : row.Owner.ArgumentsFor(value) is [var first, ..] ? first : value == "spawn" ? "Prefab" : "Message";
            row.Owner.SetEffect(row.Number, Number, value, argument);
        }
    }

    /// <summary>How much it heals, the prefab it spawns or the message it sends.</summary>
    public string Argument
    {
        get => Effect.Argument ?? "";
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Trim() != Argument)
                row.Owner.SetEffect(row.Number, Number, Verb, value);
        }
    }

    /// <summary>What it can be given: the project's prefabs or messages; none for an amount.</summary>
    public IReadOnlyList<string> Choices => row.Owner.ArgumentsFor(Verb) is var choices && (choices.Contains(Argument, StringComparer.OrdinalIgnoreCase) || Argument.Length == 0)
        ? choices
        : [Argument, .. choices];

    /// <summary>True when there's something to pick: a prefab to spawn or a message to send.</summary>
    public bool HasChoices => row.Owner.ArgumentsFor(Verb).Count > 0;

    /// <summary>Picks the prefab or message from <see cref="Choices"/> (the list beside its box).</summary>
    public string? Pick
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                Argument = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The verbs, to pick from.</summary>
    public IReadOnlyList<string> Verbs => row.Owner.Verbs;

    /// <summary>"editor:effect:2:1", for demos: its item's row and its own number.</summary>
    public string Key => $"{row.Number}:{Number}";

    public IRelayCommand RemoveCommand => _remove ??= new RelayCommand(() => row.Owner.RemoveEffect(row.Number, Number));

    private IRelayCommand? _remove;

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>
/// Tiles and how many of each (<see cref="TileCountsAttribute"/>), e.g. a match board's gems to collect: a row each with the
/// tile's picture and how many, and the tiles to pick from (the gems, or the tileset's) to add one or take it out.
/// </summary>
public sealed class TileCountsEditor : FieldEditor<TileCountsAttribute>
{
    /// <summary>How many a tile added starts at.</summary>
    public const int StartCount = 10;

    public TileCountsEditor(PropertyField field, TileCountsAttribute attribute)
        : base(field, attribute)
    {
        Refresh();
    }

    /// <summary>The tiles counted, a row each.</summary>
    public ObservableCollection<TileCountRow> Rows { get; } = [];

    /// <summary>The tiles to pick from: the component's own tile numbers (a board's gems), or its tilemap's first tileset's.</summary>
    public IReadOnlyList<TileChoice> Tiles { get; private set; } = [];

    /// <summary>The pairs as they are.</summary>
    public IReadOnlyList<(int Tile, int Count)> Pairs => Field.IsMixed ? [] : TileCountsAttribute.Read(Text);

    /// <summary>Adds <paramref name="tile"/> with <paramref name="count"/> on the end, or takes it out if it's there; one undo step.</summary>
    public void Toggle(int tile, int count = StartCount)
    {
        var pairs = Pairs.ToList();
        if (pairs.RemoveAll(p => p.Tile == tile) == 0)
            pairs.Add((tile, Math.Max(1, count)));
        SetText(TileCountsAttribute.Write(pairs));
    }

    /// <summary>Sets how many of row <paramref name="number"/>'s tile (from 1), as one undo step.</summary>
    public void Set(int number, int count)
    {
        var pairs = Pairs.ToList();
        if (number < 1 || number > pairs.Count)
            throw new ArgumentOutOfRangeException(nameof(number), pairs.Count == 0 ? "There are none yet: Toggle a tile to add it." : $"The rows are numbered 1 to {pairs.Count}.");
        pairs[number - 1] = (pairs[number - 1].Tile, Math.Max(0, count));
        SetText(TileCountsAttribute.Write(pairs));
    }

    /// <summary>Takes row <paramref name="number"/> (from 1) out, as one undo step.</summary>
    public void Remove(int number)
    {
        var pairs = Pairs.ToList();
        if (number < 1 || number > pairs.Count)
            throw new ArgumentOutOfRangeException(nameof(number), $"The rows are numbered 1 to {pairs.Count}.");
        pairs.RemoveAt(number - 1);
        SetText(TileCountsAttribute.Write(pairs));
    }

    public override void Refresh()
    {
        Fit(Rows, Pairs.Count, () => new TileCountRow(this));
        Tiles = BuildTiles();
        OnPropertyChanged(string.Empty);
    }

    internal void SetLive(int number, int count)
    {
        var pairs = Pairs.ToList();
        if (number < 1 || number > pairs.Count)
            return;
        pairs[number - 1] = (pairs[number - 1].Tile, Math.Max(0, count));
        SetTextLive(TileCountsAttribute.Write(pairs));
    }

    internal ImageSource? Picture(int tile) => TilePictures.MapOf(Component) is { } map ? TilePictures.Of(Host.Art, map, tile) : null;

    internal Brush Fill => TilePictures.MapOf(Component) is { } map ? TilePictures.Tint(map) : Brushes.Gray;

    private List<TileChoice> BuildTiles()
    {
        var picked = Pairs.Select(p => p.Tile).ToHashSet();
        var own = ComponentModel.Of(Component).Saved
            .Where(p => p.Type == typeof(string) && p.Editor is TileNumbersAttribute)
            .SelectMany(p => TileNumbersAttribute.Read(p.GetValue(Component) as string)).Distinct().ToList();
        if (own.Count == 0 && TilePictures.MapOf(Component) is { } map && map.Tilesets() is [var first, ..])
            own = [.. Enumerable.Range(first.First, Math.Min(first.Count, 64))];
        var fill = Fill;
        return [.. own.Select(t => new TileChoice(t, Picture(t), fill, picked.Contains(t), n => Toggle(n)))];
    }
}

/// <summary>One row of a <see cref="TileCountsEditor"/>: a tile, its picture, and how many.</summary>
public sealed class TileCountRow(TileCountsEditor owner) : EditorRow(owner.Remove)
{
    public override string ToString() => $"{Number}: {Name} × {Count}";

    private (int Tile, int Count) Pair => owner.Pairs.ElementAtOrDefault(Number - 1);

    /// <summary>"Tile 3".</summary>
    public string Name => $"Tile {Pair.Tile}";

    public ImageSource? Picture => owner.Picture(Pair.Tile);

    public Brush Fill => owner.Fill;

    /// <summary>How many of it.</summary>
    public double Count
    {
        get => Pair.Count;
        set => owner.SetLive(Number, (int)Math.Round(value));
    }
}

/// <summary>
/// Which properties take which of a table's columns (<see cref="TableSetsAttribute"/>), a Table Values' Sets: a row each,
/// the property picked from the entity's components and the column from the table's; pick a property in the list under
/// them to add it, taking the column named like it.
/// </summary>
public sealed class TableSetsEditor : FieldEditor<TableSetsAttribute>
{
    /// <summary>What the column list calls the column named like the property.</summary>
    public const string SameName = "(named like it)";

    public TableSetsEditor(PropertyField field, TableSetsAttribute attribute)
        : base(field, attribute)
    {
        Refresh();
    }

    /// <summary>The lines, a row each.</summary>
    public ObservableCollection<TableSetRow> Rows { get; } = [];

    /// <summary>What can be set: each property of the entity's other components that takes a table's value, as "Health.Max".</summary>
    public IReadOnlyList<string> Properties => Component is TableValues values ? values.Settable() : [];

    /// <summary>The table's columns (and <see cref="SameName"/> first), or just that while the table isn't known.</summary>
    public IReadOnlyList<string> Columns =>
        [SameName, .. Host.Names?.ColumnsOf(ComponentModel.Of(Component).Property(Attribute.Table)?.GetValue(Component) as string) ?? []];

    /// <summary>What it does with no lines: every property named like a column takes it.</summary>
    public string Note => Rows.Count == 0 ? "None: every property named like one of the table's columns takes it." : "";

    internal List<TableValues.Line> Entries => Component is TableValues values ? [.. ReadAll(values)] : [];

    // Its lines read, from the field's value (blank and # lines left out, as the game reads them).
    private IEnumerable<TableValues.Line> ReadAll(TableValues values) =>
        Lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l =>
        {
            var equals = l.IndexOf('=');
            var target = (equals < 0 ? l : l[..equals]).Trim();
            var column = equals < 0 ? null : l[(equals + 1)..].Trim();
            var dot = target.LastIndexOf('.');
            var (component, property) = dot < 0 ? (null, target) : (target[..dot].Trim(), target[(dot + 1)..].Trim());
            return new TableValues.Line(component, property, string.IsNullOrEmpty(column) ? property : column);
        });

    /// <summary>Adds a line: <paramref name="property"/> ("Health.Max") takes <paramref name="column"/> (null for the one named like it), as one undo step.</summary>
    public void Add(string property, string? column = null)
    {
        if (string.IsNullOrWhiteSpace(property))
            throw new ArgumentException("Name a property, like Health.Max.", nameof(property));
        Save([.. Entries.Select(Write), TableValues.WriteLine(property, column == SameName ? null : column)]);
    }

    /// <summary>Sets line <paramref name="number"/> (from 1), as one undo step.</summary>
    public void Set(int number, string property, string? column)
    {
        var lines = Entries.Select(Write).ToList();
        Check(number, lines.Count);
        lines[number - 1] = TableValues.WriteLine(property, column == SameName ? null : column);
        Save(lines);
    }

    /// <summary>Takes line <paramref name="number"/> (from 1) out, as one undo step.</summary>
    public void Remove(int number)
    {
        var lines = Entries.Select(Write).ToList();
        Check(number, lines.Count);
        lines.RemoveAt(number - 1);
        Save(lines);
    }

    /// <summary>Adds the property picked from the list, taking the column named like it.</summary>
    public string? AddProperty
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                Add(value);
            OnPropertyChanged();
        }
    }

    public override void Refresh()
    {
        Fit(Rows, Entries.Count, () => new TableSetRow(this));
        OnPropertyChanged(string.Empty);
    }

    internal static string Target(TableValues.Line line) => line.Component is null ? line.Property : $"{line.Component}.{line.Property}";

    internal static string Write(TableValues.Line line) => TableValues.WriteLine(Target(line), line.Column);

    private void Save(List<string> lines) => SetLines(lines);

    private static void Check(int number, int count)
    {
        if (number < 1 || number > count)
            throw new ArgumentOutOfRangeException(nameof(number), count == 0 ? "There are no lines yet: Add makes one." : $"The lines are numbered 1 to {count}.");
    }
}

/// <summary>One line of a <see cref="TableSetsEditor"/>: a property and the column it takes.</summary>
public sealed class TableSetRow(TableSetsEditor owner) : EditorRow(owner.Remove)
{
    public override string ToString() => $"{Number}: {Property} = {Column}";

    private TableValues.Line? Line => owner.Entries.ElementAtOrDefault(Number - 1);

    /// <summary>The property, "Health.Max" (or "Speed" for every Speed).</summary>
    public string Property
    {
        get => Line is { } line ? TableSetsEditor.Target(line) : "";
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value != Property)
                owner.Set(Number, value, Line?.Column == Line?.Property ? null : Line?.Column);
        }
    }

    /// <summary>The column it takes, or <see cref="TableSetsEditor.SameName"/> for the one named like it.</summary>
    public string Column
    {
        get => Line is { } line ? line.Column == line.Property ? TableSetsEditor.SameName : line.Column : TableSetsEditor.SameName;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value != Column)
                owner.Set(Number, Property, value);
        }
    }

    /// <summary>What it can set (this one first, if the entity hasn't got it).</summary>
    public IReadOnlyList<string> Properties => owner.Properties.Contains(Property) || Property.Length == 0 ? owner.Properties : [Property, .. owner.Properties];

    /// <summary>The table's columns (this one first, if the table hasn't got it).</summary>
    public IReadOnlyList<string> Columns => owner.Columns.Contains(Column, StringComparer.OrdinalIgnoreCase) ? owner.Columns : [Column, .. owner.Columns];
}
