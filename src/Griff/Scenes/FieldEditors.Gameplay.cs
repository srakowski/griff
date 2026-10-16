using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Griff.Scenes;

// Field editors for the gameplay, UI, story, wiring, data and multiplayer components' fields (see FieldEditorAttribute):
// text with holes, number formats, lists of names, letters counted, items and what they do, gems counted, an action's
// value, a dialogue's lines and which properties take which columns.

/// <summary>
/// Marks a string property as text with holes the game fills in as it shows it, e.g. a counter's "Score: {0}" or a save
/// slot's "{date} {time}": the Inspector has a button for each hole, says what each is, and shows how it reads now. A hole
/// can carry a number format after a colon ("{0:000}"). A hole it doesn't have is flagged.
/// </summary>
/// <param name="holes">Each hole and what it is, as "{0} the count" or "{slot} the slot's name".</param>
public sealed partial class FormatAttribute(params string[] holes) : FieldEditorAttribute
{
    /// <summary>The holes, each with what it is: ("{0}", "the count").</summary>
    public IReadOnlyList<(string Hole, string Meaning)> Holes { get; } = [.. holes.Select(Split)];

    /// <summary>
    /// A method of the component taking the text (a string, or null) and giving how it reads with what the component holds
    /// now, e.g. a counter's "Score: 12"; null when there's none.
    /// </summary>
    public string? Preview { get; init; }

    /// <summary>
    /// The holes are put in by replacing them (a choice's {0}), rather than as a .NET format string, so a brace on its own
    /// is fine.
    /// </summary>
    public bool Replaced { get; init; }

    public override string Describe() =>
        $"Text with holes the game fills in: {string.Join(", ", Holes.Select(h => $"{h.Hole} {h.Meaning}"))}" +
        (Holes.All(h => IsNumbered(h.Hole)) ? ", each with a number format after a colon if it says, e.g. {0:000}" : "") +
        ". The Inspector has a button for each and shows how it reads now.";

    public override string? Problem(Component component, object? value)
    {
        if (value is not string { Length: > 0 } text)
            return null;
        var names = Holes.Select(h => Name(h.Hole)).ToList();
        foreach (Match hole in HolePattern().Matches(text))
        {
            var name = hole.Groups[1].Value.Split(':')[0].Trim();
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                return $"\"{hole.Value}\" isn't one of its holes: they're {string.Join(", ", Holes.Select(h => h.Hole))}";
        }

        if (!Replaced && Holes.All(h => IsNumbered(h.Hole)))
        {
            try
            {
                _ = string.Format(CultureInfo.InvariantCulture, text, [.. Holes.Select(_ => (object)0)]);
            }
            catch (FormatException)
            {
                return $"\"{text}\" doesn't read: put holes in whole, like {Holes[0].Hole}, and write a brace on its own twice ({{{{ or }}}})";
            }
        }

        return null;
    }

    /// <summary>How <paramref name="component"/>'s text reads with <paramref name="format"/> as its value now; null when it can't say.</summary>
    public string? ReadsAs(Component component, string? format)
    {
        if (Preview is null)
            return null;
        var method = ComponentModel.Of(component).Method(Preview, typeof(string)) is { IsStatic: false } own ? own : null;
        try
        {
            return method?.Invoke(component, [format]) as string;
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    // "{0} the count" as ("{0}", "the count").
    private static (string, string) Split(string hole)
    {
        var end = hole.IndexOf('}');
        return end < 0 ? (hole.Trim(), "") : (hole[..(end + 1)].Trim(), hole[(end + 1)..].Trim());
    }

    // "{0}" as "0", "{slot}" as "slot".
    private static string Name(string hole) => hole.Trim().TrimStart('{').TrimEnd('}').Trim();

    private static bool IsNumbered(string hole) => int.TryParse(Name(hole), NumberStyles.None, CultureInfo.InvariantCulture, out _);

    // A hole: braces round something, not doubled.
    [GeneratedRegex(@"(?<!\{)\{([^{}]+)\}(?!\})")]
    private static partial Regex HolePattern();
}

/// <summary>
/// Marks a string property as how a number is written, a .NET number format: "0" whole, "0.00" two decimals, "0.##" up
/// to two, "000" three digits. The Inspector offers the common ones and shows a number written that way.
/// </summary>
public sealed class NumberFormatAttribute : FieldEditorAttribute
{
    /// <summary>The formats offered, each with what it does.</summary>
    public static IReadOnlyList<(string Format, string Meaning)> Common { get; } =
    [
        ("0", "whole numbers"), ("0.0", "one decimal"), ("0.00", "two decimals"), ("0.##", "up to two decimals"),
        ("000", "three digits at least"), ("#,0", "thousands with commas"), ("0%", "a percent (0.5 is 50%)"),
    ];

    /// <summary>A number property of the component to show written in the format, e.g. a display's "Value"; null for a sample.</summary>
    public string? Sample { get; init; }

    public override string Describe() =>
        $"How a number is written: {string.Join(", ", Common.Select(c => $"\"{c.Format}\" {c.Meaning}"))}. The Inspector offers these and shows a number written that way.";

    /// <summary>The number to show <paramref name="component"/>'s format with: its <see cref="Sample"/>, or 1234.5678 when that's 0 or there's none.</summary>
    public double SampleOf(Component component)
    {
        var value = Sample is null ? null : ComponentModel.Of(component).Property(Sample)?.GetValue(component);
        var number = value is IConvertible convertible ? convertible.ToDouble(CultureInfo.InvariantCulture) : 0;
        return number == 0 ? 1234.5678 : number;
    }

    /// <summary><paramref name="number"/> written in <paramref name="format"/>, as the game writes it.</summary>
    public static string Write(double number, string? format)
    {
        try
        {
            return number.ToString(string.IsNullOrEmpty(format) ? "0.##" : format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>
/// Marks a property as a list of names in order: a string separated by commas (a turn order's "Player, Enemy") or a list of
/// strings, one a line (a choice's options). The Inspector lists them a row each, to rename, add, take out and move up;
/// a name given twice is flagged.
/// </summary>
public sealed class NameListAttribute : FieldEditorAttribute
{
    /// <summary>What each name is, e.g. "side" or "option", for the Inspector's buttons and the descriptions.</summary>
    public string Noun { get; init; } = "name";

    /// <summary>The fewest there can be: taking one out stops there.</summary>
    public int Least { get; init; }

    public override bool Fits(Type type) => type == typeof(string) || type == typeof(IReadOnlyList<string>);

    public override string FitsText => "a string (names separated by commas) or a list of strings (one a line)";

    public override string Describe() =>
        $"{char.ToUpperInvariant(Noun[0])}{Noun[1..]}s in order (as a list, one a line; as text, separated by commas){(Least > 0 ? $", at least {Least}" : "")}. The Inspector lists them a row each to rename, add, take out and reorder.";

    public override string? Problem(Component component, object? value)
    {
        var names = Read(value);
        var twice = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        return twice is not null ? $"\"{twice.Key}\" is there twice: each {Noun} needs a name of its own" : null;
    }

    /// <summary>The names a value holds, in order, blank ones left out.</summary>
    public static IReadOnlyList<string> Read(object? value) => value switch
    {
        string text => Formats.Names(text),
        IReadOnlyList<string> lines => [.. lines.Select(l => l.Trim()).Where(l => l.Length > 0)],
        _ => [],
    };
}

/// <summary>
/// Marks a string property as letters, each from a set and each as many times as it comes: a falling-block board's pieces,
/// "IJLOSTZ" (each once) or "IIO" (twice as many I's). The Inspector has a count for each letter, with its picture.
/// </summary>
/// <param name="letters">The letters there are, e.g. "IJLOSTZ".</param>
public sealed class LetterCountsAttribute(string letters) : FieldEditorAttribute
{
    /// <summary>The letters there are, in order.</summary>
    public string Letters { get; } = letters;

    /// <summary>What they are, e.g. "pieces", for the descriptions.</summary>
    public string Noun { get; init; } = "letters";

    /// <summary>
    /// A static method of the component taking a letter (a char) and giving the cells of its picture, (x, y) pairs, y up:
    /// a piece's shape. Null for none.
    /// </summary>
    public string? Cells { get; init; }

    public override string Describe() =>
        $"The {Noun}, by letter ({string.Join(", ", Letters.ToCharArray())}), each written as many times as it comes, e.g. \"{Letters}\" for one of each or \"{Letters[0]}{Letters[0]}{Letters[^1]}\" for twice as many {Letters[0]}'s. The Inspector has a count for each.";

    public override string? Problem(Component component, object? value)
    {
        var bad = (value as string ?? "").Where(c => !char.IsWhiteSpace(c) && c != ',' && !Letters.Contains(char.ToUpperInvariant(c))).Distinct().ToList();
        return bad.Count > 0 ? $"\"{string.Concat(bad)}\" isn't one of the {Noun}: they're {string.Join(", ", Letters.ToCharArray())}" : null;
    }

    /// <summary>How many times each letter comes in <paramref name="text"/>.</summary>
    public IReadOnlyList<int> Read(string? text) =>
        [.. Letters.Select(l => (text ?? "").Count(c => char.ToUpperInvariant(c) == l))];

    /// <summary>The text for <paramref name="counts"/>, a count for each letter in order: "IJLOSTZ", "IIO".</summary>
    public string Write(IReadOnlyList<int> counts) =>
        string.Concat(Letters.Select((l, i) => new string(l, Math.Max(0, i < counts.Count ? counts[i] : 0))));

    /// <summary>The cells of <paramref name="letter"/>'s picture on <paramref name="component"/>'s type, or none.</summary>
    public IReadOnlyList<(int X, int Y)> CellsOf(Component component, char letter)
    {
        var method = Cells is null ? null : ComponentModel.Of(component).Method(Cells, typeof(char)) is { IsStatic: true } cells ? cells : null;
        return method?.Invoke(null, [letter]) as IReadOnlyList<(int X, int Y)> ?? [];
    }
}

/// <summary>
/// Marks a list of strings as items and how many of each, one a line: "Key 2", or "Key" for one (an inventory's Items).
/// The Inspector lists them a row each, the item picked from those the game gives, with how many; the items listed count
/// as given, for the fields that name items.
/// </summary>
public sealed class ItemCountsAttribute : FieldEditorAttribute
{
    public override bool Fits(Type type) => type == typeof(IReadOnlyList<string>);

    public override string FitsText => "a list of strings";

    public override string Describe() =>
        "Items and how many of each, one a line: the item's name then how many, e.g. \"Key 2\" (just \"Key\" is one). The Inspector lists them a row each, the item picked from those the game gives.";

    /// <summary>A line read: the item and how many (see <see cref="Items.ReadOne"/>).</summary>
    public static (string Item, int Count) Read(string line) => Items.ReadOne(line);
}

/// <summary>
/// Marks a list of strings as what items do when they're used, one a line: the item, a colon, and what it does, several
/// separated by commas: "heal 3", "spawn Explosion" (a prefab), "send Teleport" (a message), e.g. "Bomb: spawn Explosion,
/// send Boom" (an inventory's Item Uses). The Inspector lists each item with what it does a row each, the item, prefab and
/// message picked from the project's.
/// </summary>
public sealed class ItemUsesAttribute : FieldEditorAttribute
{
    /// <summary>What an item can do: heal hit points, spawn a prefab, send a message.</summary>
    public static IReadOnlyList<string> Verbs => ItemEffects.Verbs;

    public override bool Fits(Type type) => type == typeof(IReadOnlyList<string>);

    public override string FitsText => "a list of strings";

    public override string Describe() =>
        "What items do when used, one item a line: its name, a colon, then what it does, separated by commas: \"heal 3\" (hit points), \"spawn Explosion\" (a prefab) or \"send Teleport\" (a message), e.g. \"Bomb: spawn Explosion, send Boom\". The Inspector lists them, picking the item, prefab and message.";

    public override string? Problem(Component component, object? value)
    {
        foreach (var line in (value as IReadOnlyList<string> ?? []).Where(l => l.Trim().Length > 0))
        {
            if (Read(line) is not { } use)
                return $"\"{line.Trim()}\" isn't an item's use: write the item, a colon, then what it does, like Potion: heal 3";
            if (use.Effects.FirstOrDefault(e => !Verbs.Contains(e.Verb) || e.Argument.Length == 0) is { Verb: not null } bad)
                return $"\"{bad.Verb} {bad.Argument}\" isn't something an item does: it can heal 3, spawn a prefab or send a message";
            if (use.Effects.FirstOrDefault(e => e.Verb == "heal" && !float.TryParse(e.Argument, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) is { Verb: not null } heal)
                return $"\"heal {heal.Argument}\" needs a number, like heal 3";
        }

        return null;
    }

    /// <summary>A line read, every effect kept as written (verbs in small letters); null when it has no colon.</summary>
    public static (string Item, IReadOnlyList<(string Verb, string Argument)> Effects)? Read(string line)
    {
        var colon = line.IndexOf(':');
        if (colon <= 0)
            return null;
        var effects = line[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.IndexOf(' ') is var space and > 0 ? (part[..space].ToLowerInvariant(), part[(space + 1)..].Trim()) : (part.ToLowerInvariant(), ""))
            .ToList();
        return (line[..colon].Trim(), effects);
    }
}

/// <summary>
/// Marks a string property as tiles and how many of each, as tile:count pairs separated by commas, e.g. "0:20, 3:15" (a
/// match board's gems to collect). The Inspector lists them a row each, the tile picked off the tileset, with how many.
/// </summary>
public sealed class TileCountsAttribute : FieldEditorAttribute
{
    public override string Describe() =>
        "Tiles and how many of each, as tile:count pairs separated by commas, e.g. \"0:20, 3:15\" for 20 of tile 0 and 15 of tile 3. The Inspector lists them a row each, the tile picked off the tileset.";

    public override string? Problem(Component component, object? value)
    {
        var bad = Parts(value).FirstOrDefault(p => ReadPair(p) is null);
        return bad is not null ? $"\"{bad}\" isn't a tile and how many: write it like 0:20" : null;
    }

    /// <summary>The pairs a string holds, those that read.</summary>
    public static IReadOnlyList<(int Tile, int Count)> Read(string? text) =>
        [.. Parts(text).Select(ReadPair).OfType<(int, int)>()];

    /// <summary>How many of each tile the string says, a tile named twice adding up: what the game counts against (see <see cref="Formats.KindCounts"/>).</summary>
    public static IReadOnlyDictionary<int, int> Totals(string? text) => Formats.KindCounts(text);

    private static (int, int)? ReadPair(string part) => Formats.KindCount(part);
}

/// <summary>
/// Marks a string property as the value an action is done with, the action named by another property (a trigger's With,
/// for its Do): the Inspector gives a number, a tick for true or false, or the names it takes (flags for Set Flag, scenes
/// for Load Scene), as the action wants, and flags a value that isn't one.
/// </summary>
/// <param name="action">The property naming the action, marked <see cref="ActionNameAttribute"/>, e.g. "Do".</param>
public sealed class ActionValueAttribute(string action) : FieldEditorAttribute
{
    /// <summary>The property of the component naming the action.</summary>
    public string Action { get; } = action;

    public override string Describe() =>
        $"The value the action {System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(Action)} names is done with, as the action takes it: a number (\"2\"), true or false, or a name (a flag for Set Flag, a scene for Load Scene). Empty uses the action's own default. The Inspector gives a box, a tick or a list as the action wants.";

    public override string? Problem(Component component, object? value)
    {
        if (value is not string { Length: > 0 } text || string.IsNullOrWhiteSpace(text) || Find(component) is not { ValueType: { } type } action)
            return null;
        var trimmed = text.Trim();
        if (type == typeof(bool))
            return trimmed.ToLowerInvariant() is "true" or "false" or "on" or "off" or "1" or "0" ? null : $"\"{trimmed}\" isn't true or false: {action.DisplayName} takes one";
        if (type == typeof(float) || type == typeof(int))
        {
            return float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && (type == typeof(float) || number == MathF.Round(number))
                ? null
                : $"\"{trimmed}\" isn't {(type == typeof(int) ? "a whole number" : "a number")}: {action.DisplayName} takes {action.ValueKind}{(action.ValueName is { } named ? $" ({named})" : "")}";
        }

        return null;
    }

    /// <summary>
    /// The action <paramref name="component"/>'s <see cref="Action"/> names, on the first of its targets (or its own entity)
    /// that has one; null when none does yet.
    /// </summary>
    public ComponentAction? Find(Component component)
    {
        var property = ComponentModel.Of(component).Property(Action);
        if (component.Entity is not { } owner || property?.GetValue(component) is not string { Length: > 0 } name || string.IsNullOrWhiteSpace(name))
            return null;
        var on = property.GetCustomAttribute<ActionNameAttribute>()?.On is { Length: > 0 } onName ? ComponentModel.Of(component).Property(onName) : null;
        IReadOnlyList<Entity> targets = on?.GetCustomAttribute<EntityNameAttribute>() is { } entityName && on.GetValue(component) is string { } list && !string.IsNullOrWhiteSpace(list)
            ? [.. EntityReferences.Names(list, entityName).SelectMany(n => EntityReferences.Named(owner, entityName, n)).Distinct()]
            : [owner];
        return targets.SelectMany(t => t.Components).Select(c => ComponentActions.Find(c, name)).FirstOrDefault(a => a is not null);
    }
}

/// <summary>
/// Marks a list of strings as a <see cref="Dialogue"/>'s lines (see <see cref="Dialogue.Lines"/>): the Inspector shows
/// them with the Dialogue Editor a click away, which builds each line's speaker, choices, where they go and its tags
/// from lists, and flags a go-to to a part that isn't there.
/// </summary>
public sealed class DialogueLinesAttribute : FieldEditorAttribute
{
    public override bool Fits(Type type) => type == typeof(IReadOnlyList<string>);

    public override string FitsText => "a list of strings";

    public override string Describe() =>
        "Lines of talk, one a row: \"Name: ...\" said by a speaker, \"> ...\" a choice (\"-> Part\" at its end goes there), \"# Part\" starts a part, \"-> Part\" or \"-> End\" goes there, and tags first: [if Flag], [if not Flag], [set Flag], [clear Flag], [send Message]. The Dialogue Editor (Entity > Edit Dialogue) builds each line from lists.";

    public override string? Problem(Component component, object? value) =>
        component is Dialogue dialogue && dialogue.GoesNowhere() is [var part, ..]
            ? $"\"-> {part}\" goes nowhere: there's no \"# {part}\" (or End)"
            : null;
}

/// <summary>
/// Marks a list of strings as which properties take which of a table's columns, one a line: <c>Health.Max = Health</c> (a
/// Table Values' Sets). The Inspector lists them a row each, the property picked from the entity's components and the
/// column from the table's.
/// </summary>
/// <param name="table">The property naming the table, e.g. "Table".</param>
public sealed class TableSetsAttribute(string table) : FieldEditorAttribute
{
    /// <summary>The property of the component naming the table.</summary>
    public string Table { get; } = table;

    public override bool Fits(Type type) => type == typeof(IReadOnlyList<string>);

    public override string FitsText => "a list of strings";

    public override string Describe() =>
        "Which property takes which column, one a line: \"Health.Max = Health\" (a component's property, then the column), or \"Speed\" for every Speed from the Speed column. Empty sets every property named like a column. The Inspector lists them a row each, picking the property from the entity's and the column from the table's.";

    public override string? Problem(Component component, object? value) =>
        component is TableValues values && values.Entity is not null && values.LineProblems() is { Count: > 0 } problems
            ? char.ToUpperInvariant(problems[0][0]) + problems[0][1..]
            : null;
}

/// <summary>
/// Marks a <see cref="System.Numerics.Vector2"/> as the size of a rectangle around the entity, in its own units before its
/// scale: a button's clickable area, a bar's or slider's size, a save slot's screenshot. With Resize handles the Scene view
/// puts handles on its edges and corners to drag.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ResizableAttribute : Attribute
{
    /// <summary>
    /// A <see cref="System.Numerics.Vector2"/> property of the component that says where the rectangle's middle is from the
    /// entity's (a save slot's Picture Offset): dragging an edge keeps the opposite one where it is by moving it. Without
    /// one it's centered on the entity, and grows around it.
    /// </summary>
    public string? Center { get; init; }

    /// <summary>It stays upright, the entity's rotation ignored (a button's clickable area).</summary>
    public bool Upright { get; init; }
}
