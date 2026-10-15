using System.Globalization;
using System.Reflection;
using Griff.Data;

namespace Griff.Scenes;

/// <summary>
/// Gives its entity's other components their values from a row of a table, with no code: an enemy's Health and Damage
/// from the Enemies table's Slime row. <see cref="Sets"/> says which properties take which columns, one a line
/// (<c>Health.Max = Health</c>); left empty, every property named like a column takes it (a column called "Speed" sets
/// every Speed, one called "Health.Max" sets that one). The values are set as the entity starts in the game (spawned
/// ones straight away), and again whenever the row changes while the game runs, so balancing a table while playing shows
/// at once. A row renamed in the Tables panel is still found by its old name; a table or row that isn't there is flagged
/// in the Inspector, a line of Sets naming a property the entity hasn't got is noted above its fields, and anything missing
/// (a column too) is said in Output as the game starts.
/// </summary>
[Category("Data")]
[ComponentName("Griff.TableValues")]
public sealed class TableValues : Component
{
    /// <summary>The table the values come from, e.g. "Enemies" (Assets/Tables/Enemies.table).</summary>
    [Names(NameKind.Table)]
    public string? Table { get; set; }

    /// <summary>The row they come from, e.g. "Slime". Empty takes the row named like the entity, so a Slime prefab finds the Slime row.</summary>
    [Names(NameKind.Row, Table = nameof(Table))]
    public string? Row { get; set; }

    /// <summary>
    /// Which property takes which column, one a line: <c>Health.Max = Health</c> (a component's property, then the column),
    /// <c>Speed</c> (every Speed on the entity, from the Speed column) or <c>Patrol.Speed = Walk Speed</c>. A number
    /// property given dice rolls them once, as the entity starts. Empty sets every property named like a column.
    /// </summary>
    public IReadOnlyList<string> Sets { get; set; } = [];

    /// <summary>
    /// Set by the game: which values it set last, so a rewind (which brings back what they were) doesn't set them again,
    /// rolling new dice, unless the row has changed since. Leave it be.
    /// </summary>
    public string? Applied { get; set; }

    public override bool Uses(string property) => property != nameof(Applied);

    /// <summary>What <see cref="Sets"/> does, or which of its lines name a component or property the entity hasn't got.</summary>
    public override string? SetupNote()
    {
        if (Entity is null)
            return null;
        if (Lines().Count == 0)
            return "Sets every property named like one of the table's columns.";
        var problems = new List<string>();
        Targets(null, problems);
        return problems.Count == 0 ? null : "Sets: " + string.Join("; ", problems) + ".";
    }

    /// <summary>The row it reads: <see cref="Row"/>, or the entity's name when that's empty.</summary>
    public string? RowName => string.IsNullOrWhiteSpace(Row) ? Entity?.Name : Row.Trim();

    /// <summary>A property of a component on the entity and the column it takes, as <see cref="Sets"/> says.</summary>
    public sealed record Target(Component Component, PropertyInfo Property, string Column)
    {
        /// <summary>"Health.Max".</summary>
        public string Label => $"{Component.GetType().Name}.{Property.Name}";
    }

    /// <summary>One of <see cref="Sets"/>' lines read: a component's name (or null for any), a property's, and the column's.</summary>
    public sealed record Line(string? Component, string Property, string Column);

    /// <summary><see cref="Sets"/>, each line read; blank lines and those starting with # are left out.</summary>
    public IReadOnlyList<Line> Lines() => [.. Sets.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(ReadLine)];

    private static Line ReadLine(string line)
    {
        var equals = line.IndexOf('=');
        var target = (equals < 0 ? line : line[..equals]).Trim();
        var column = equals < 0 ? null : line[(equals + 1)..].Trim();
        var dot = target.LastIndexOf('.');
        var (component, property) = dot < 0 ? (null, target) : (target[..dot].Trim(), target[(dot + 1)..].Trim());
        return new Line(component, property, string.IsNullOrEmpty(column) ? property : column);
    }

    /// <summary>
    /// The properties it sets and the columns they take: as <see cref="Sets"/> says, or with it empty, every property of
    /// the entity's other components named like one of <paramref name="table"/>'s columns. Lines that match nothing are
    /// in <paramref name="problems"/>.
    /// </summary>
    public IReadOnlyList<Target> Targets(Table? table, ICollection<string>? problems = null) =>
        Targets(table?.Columns.Select(c => c.Name).ToList(), table, problems);

    /// <summary>As <see cref="Targets(Table?, ICollection{string}?)"/>, knowing only the names of the table's columns.</summary>
    public IReadOnlyList<Target> Targets(IReadOnlyList<string>? columns) => Targets(columns, null, null);

    private List<Target> Targets(IReadOnlyList<string>? columns, Table? table, ICollection<string>? problems)
    {
        var entity = Entity;
        if (entity is null)
            return [];
        var targets = new List<Target>();
        var others = entity.Components.Where(c => c != this && c is not MissingComponent).ToList();
        var lines = Lines();
        if (lines.Count == 0)
        {
            foreach (var column in columns ?? [])
            {
                var line = ReadLine(column);
                targets.AddRange(Find(others, line.Component, line.Property).Select(m => new Target(m.Component, m.Property, column)));
            }

            return targets;
        }

        foreach (var line in lines)
        {
            var found = Find(others, line.Component, line.Property);
            if (found.Count == 0)
            {
                problems?.Add(line.Component is null
                    ? $"no component on {entity.Name} has a property called {line.Property}"
                    : others.Any(c => Matches(c, line.Component))
                        ? $"{line.Component} has no property called {line.Property}"
                        : $"{entity.Name} has no {line.Component} component");
                continue;
            }

            if (table is not null ? table.ColumnIndex(line.Column) < 0 : columns is not null && !columns.Contains(line.Column, StringComparer.OrdinalIgnoreCase))
            {
                problems?.Add(table?.NoColumn(line.Column) ?? $"the table has no column \"{line.Column}\"");
                continue;
            }

            targets.AddRange(found.Select(m => new Target(m.Component, m.Property, line.Column)));
        }

        return targets;
    }

    private static List<(Component Component, PropertyInfo Property)> Find(List<Component> components, string? component, string property)
    {
        var key = Key(property);
        var found = new List<(Component, PropertyInfo)>();
        foreach (var candidate in components)
        {
            if (component is not null && !Matches(candidate, component))
                continue;
            var match = candidate.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => p.CanWrite && p.SetMethod!.IsPublic && p.GetIndexParameters().Length == 0
                    && p.GetCustomAttribute<DoNotSerializeAttribute>() is null && Key(p.Name) == key && CanTake(p.PropertyType));
            if (match is not null)
                found.Add((candidate, match));
        }

        return found;
    }

    private static bool Matches(Component component, string name) => Key(component.GetType().Name) == Key(name);

    private static string Key(string name) => new([.. name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    private static bool CanTake(Type type) =>
        type == typeof(float) || type == typeof(double) || type == typeof(int) || type == typeof(bool) || type == typeof(string) || type.IsEnum || type == typeof(Color);

    /// <summary>
    /// Sets the properties from the row, unless they were set from it as it is already (<see cref="Applied"/>, with
    /// <paramref name="session"/> telling one game from another). Returns what went wrong, if anything: a table, row or
    /// column that isn't there, or a value that doesn't fit its property.
    /// </summary>
    public IReadOnlyList<string> Apply(TableSet tables, string session)
    {
        var problems = new List<string>();
        if (Entity is not { } entity)
            return problems;
        var where = $"Table Values on {entity.Name}";
        if (string.IsNullOrWhiteSpace(Table))
        {
            problems.Add($"{where} names no Table.");
            return problems;
        }

        Table? table;
        try
        {
            table = tables.Find(Table);
        }
        catch (InvalidDataException ex)
        {
            problems.Add($"{where}: {ex.Message}");
            return problems;
        }

        if (table is null)
        {
            problems.Add($"{where}: {tables.NoTable(Table)}");
            return problems;
        }

        if (table.Find(RowName) is not { } row)
        {
            problems.Add($"{where}: {table.NoRow(RowName)}{(string.IsNullOrWhiteSpace(Row) ? " (With Row empty it takes the row named like its entity.)" : "")}");
            return problems;
        }

        var lineProblems = new List<string>();
        var targets = Targets(table, lineProblems);
        problems.AddRange(lineProblems.Select(p => $"{where}: {p}."));

        var fingerprint = Fingerprint(session, table.Name, row.Name, targets.Select(t => (t.Label, row[t.Column].ToString())));
        if (fingerprint == Applied)
            return problems;

        foreach (var target in targets)
        {
            var value = row[target.Column];
            try
            {
                // A number with dice of its own to roll each time it's used (a hit's damage): dice go there, rolled each hit,
                // and the number is their average; a plain number empties them.
                if (RollFor(target) is { } roll)
                {
                    var dice = value.Kind == TableValueKind.Dice ? value.AsDice : (Dice?)null;
                    roll.SetValue(target.Component, dice?.ToString());
                    if (dice is { } rolled)
                    {
                        target.Property.SetValue(target.Component, Convert(TableValue.Of(rolled.Average), target.Property.PropertyType));
                        continue;
                    }
                }

                target.Property.SetValue(target.Component, Convert(value, target.Property.PropertyType));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or TargetInvocationException)
            {
                problems.Add($"{where}: {table.Name}'s {row.Name}.{target.Column} ({value}) doesn't fit {target.Label}: {(ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message}");
            }
        }

        Applied = fingerprint;
        return problems;
    }

    /// <summary>
    /// <paramref name="value"/> as a property of <paramref name="type"/> takes it: a number, whole number (rounded) or yes/no
    /// as they are; dice rolled for a number, written out for text; a choice for an enum by its name; text that reads as a
    /// color ("#FF004D") for a color.
    /// </summary>
    public static object? Convert(TableValue value, Type type)
    {
        if (value.Kind == TableValueKind.Dice && (type == typeof(float) || type == typeof(double) || type == typeof(int)))
        {
            var rolled = value.AsDice.Roll();
            return type == typeof(float) ? (object)(float)rolled : type == typeof(double) ? (object)(double)rolled : rolled;
        }

        if (type == typeof(Color))
            return Color.TryParse(value.ToString(), out var color) ? color : throw new FormatException($"\"{value}\" isn't a color, like #FF004D");
        if (type.IsEnum && value.Kind == TableValueKind.WholeNumber)
            return Enum.ToObject(type, (int)value.AsWholeNumber);
        if (type == typeof(int))
            return (int)value.AsWholeNumber;
        return TableMapping.Convert(value, type);
    }

    // The text property marked [RollsFor] the target's property, if its component has one.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type, string), PropertyInfo?> s_rolls = new();

    private static PropertyInfo? RollFor(Target target) =>
        target.Property.PropertyType == typeof(string) ? null : s_rolls.GetOrAdd((target.Component.GetType(), target.Property.Name), key =>
            key.Item1.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(p =>
                p.PropertyType == typeof(string) && p.CanWrite && p.GetCustomAttribute<RollsForAttribute>()?.Property == key.Item2));

    // FNV-1a, so the same values give the same text in every game and on every machine.
    private static string Fingerprint(string session, string table, string row, IEnumerable<(string Target, string Value)> values)
    {
        var hash = 14695981039346656037UL;
        void Add(string text)
        {
            foreach (var c in text)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }

            hash ^= 0xFF;
            hash *= 1099511628211UL;
        }

        Add(table);
        Add(row);
        foreach (var (target, value) in values)
        {
            Add(target);
            Add(value);
        }

        return $"{session}:{hash.ToString("x16", CultureInfo.InvariantCulture)}";
    }
}

/// <summary>
/// Marks a text property holding dice (<c>"1d8+2"</c>) that are rolled each time the number property it names is used: a
/// hit's damage rolled for each hit rather than once. Table Values sets it from a dice column given for that number
/// property (and the number to the dice's average, which is what the Inspector shows).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RollsForAttribute(string property) : Attribute
{
    /// <summary>The number property the dice stand in for, e.g. "Damage".</summary>
    public string Property { get; } = property;
}

// Dice written as text, read once when the text changes: rolled each hit with Rand, or the plain number when it's empty
// (or doesn't read as dice).
internal struct DiceRoll
{
    private string? _text;
    private Dice? _dice;

    public string? Text
    {
        readonly get => _text;
        set
        {
            if (value == _text)
                return;
            _text = value;
            _dice = Dice.TryParse(value, out var dice) ? dice : null;
        }
    }

    public readonly float Roll(float otherwise) => _dice is { } dice ? dice.Roll() : otherwise;
}
