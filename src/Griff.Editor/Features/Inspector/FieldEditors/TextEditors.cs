using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Scenes;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// Text with holes the game fills in (<see cref="FormatAttribute"/>): a counter's "Score: {0}", a save slot's "{date}": a
/// button for each hole, saying what it is, and how the text reads now with what the component holds.
/// </summary>
public sealed class FormatEditor : FieldEditor<FormatAttribute>
{
    public FormatEditor(PropertyField field, FormatAttribute attribute)
        : base(field, attribute)
    {
        Holes = [.. attribute.Holes.Select(h => new HoleChoice(h.Hole, h.Meaning, this))];
    }

    /// <summary>The holes it can have, each a button that puts it in.</summary>
    public IReadOnlyList<HoleChoice> Holes { get; }

    /// <summary>"Reads now: Score: 12", with what the component holds now; empty when it can't say.</summary>
    public string Preview
    {
        get
        {
            if (Field.IsMixed)
                return "The selected entities' texts differ.";
            if (Text.Length == 0)
                return "";
            return Attribute.ReadsAs(Component, Text) is { } reads ? $"Reads now: {reads.ReplaceLineEndings(" / ")}" : "";
        }
    }

    /// <summary>
    /// Puts a hole on the end of the text, as one undo step: its token ("{0}", "{slot}") or just its name ("0", "slot").
    /// </summary>
    public void Insert(string hole)
    {
        var name = hole.Trim().TrimStart('{').TrimEnd('}').Trim();
        var found = Attribute.Holes.FirstOrDefault(h => h.Hole.Trim('{', '}').Equals(name, StringComparison.OrdinalIgnoreCase));
        if (found.Hole is null)
            throw new ArgumentException($"There's no hole {hole}; the holes are {string.Join(", ", Attribute.Holes.Select(h => h.Hole))}.", nameof(hole));
        SetText(Text + found.Hole);
    }
}

/// <summary>One of a <see cref="FormatEditor"/>'s holes: "{0}", what it is, and a button that puts it in.</summary>
public sealed class HoleChoice(string hole, string meaning, FormatEditor owner)
{
    public string Hole { get; } = hole;

    public string Meaning { get; } = meaning;

    /// <summary>"Put in {0}: the count", for its tooltip.</summary>
    public string Tip => $"Put in {Hole}: {Meaning}";

    public IRelayCommand InsertCommand => _insert ??= new RelayCommand(() => owner.Insert(Hole));

    private IRelayCommand? _insert;

    public override string ToString() => $"{Hole}: {Meaning}";
}

/// <summary>
/// How a number is written (<see cref="NumberFormatAttribute"/>), e.g. a display's "0.##": the common ways to pick from,
/// and a number written the way it says.
/// </summary>
public sealed class NumberFormatEditor(PropertyField field, NumberFormatAttribute attribute) : FieldEditor<NumberFormatAttribute>(field, attribute)
{
    /// <summary>The common formats, as the list shows them: "0.00: two decimals".</summary>
    public IReadOnlyList<string> Choices => [.. NumberFormatAttribute.Common.Select(c => $"{c.Format}: {c.Meaning}")];

    /// <summary>The common format it is, as listed; null for one of its own.</summary>
    public string? Choice
    {
        get => NumberFormatAttribute.Common.FirstOrDefault(c => c.Format == Text.Trim()) is { Format: not null } c ? $"{c.Format}: {c.Meaning}" : null;
        set
        {
            if (value is not null && value != Choice)
                Use(value.Split(':')[0]);
        }
    }

    /// <summary>Sets the format, e.g. "0.00", as one undo step.</summary>
    public void Use(string format) => SetText(format.Trim());

    /// <summary>"Shows 1234.57": a number written as it says (the display's own, when it has one).</summary>
    public string Preview => Field.IsMixed ? "The selected entities' formats differ." :
        $"Shows {NumberFormatAttribute.Write(Attribute.SampleOf(Component), Text)}";
}

/// <summary>
/// Names in order (<see cref="NameListAttribute"/>): a turn order's sides, a choice's options, a game's teams, a row each
/// to rename, take out and move up, and Add for another.
/// </summary>
public sealed class NameListEditor : FieldEditor<NameListAttribute>
{
    public NameListEditor(PropertyField field, NameListAttribute attribute)
        : base(field, attribute)
    {
        Refresh();
    }

    /// <summary>The names, a row each.</summary>
    public ObservableCollection<NameRow> Rows { get; } = [];

    /// <summary>The names as they are, in order.</summary>
    public IReadOnlyList<string> Names => Field.IsMixed ? [] : NameListAttribute.Read(Field.Value);

    /// <summary>True for a list of strings (a name a line), rather than text with commas between.</summary>
    public bool IsList => Field.Property.Type != typeof(string);

    /// <summary>The names as typed: separated by commas, or a line each for a list. Setting it sets them, as one undo step.</summary>
    public string Typed
    {
        get => IsList ? Field.LinesValue : Field.TextValue ?? "";
        set
        {
            if (IsList)
                Field.LinesValue = value;
            else
                Field.TextValue = value;
        }
    }

    /// <summary>"Add Side", for its button.</summary>
    public string AddText => $"Add {char.ToUpperInvariant(Attribute.Noun[0])}{Attribute.Noun[1..]}";

    /// <summary>True when one can be taken out: there are more than the fewest it needs.</summary>
    public bool CanRemove => Rows.Count > Attribute.Least;

    /// <summary>Adds <paramref name="name"/> on the end (or a new one, "Side 3", for none), as one undo step.</summary>
    public void Add(string? name = null)
    {
        var names = Names.ToList();
        var noun = $"{char.ToUpperInvariant(Attribute.Noun[0])}{Attribute.Noun[1..]}";
        var added = string.IsNullOrWhiteSpace(name)
            ? Enumerable.Range(names.Count + 1, 100).Select(i => $"{noun} {i}").First(n => !names.Contains(n, StringComparer.OrdinalIgnoreCase))
            : Clean(name);
        names.Add(added);
        Save(names);
    }

    /// <summary>Renames row <paramref name="number"/> (from 1), as one undo step.</summary>
    public void Set(int number, string name)
    {
        var names = Names.ToList();
        Check(number, names.Count);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException($"A {Attribute.Noun} needs a name: take it out instead.", nameof(name));
        names[number - 1] = Clean(name);
        Save(names);
    }

    /// <summary>Takes row <paramref name="number"/> (from 1) out, unless it has only the fewest it needs.</summary>
    public void Remove(int number)
    {
        var names = Names.ToList();
        Check(number, names.Count);
        if (names.Count <= Attribute.Least)
            throw new InvalidOperationException($"It needs at least {Attribute.Least} {Attribute.Noun}{(Attribute.Least == 1 ? "" : "s")}: rename this one instead.");
        names.RemoveAt(number - 1);
        Save(names);
    }

    /// <summary>Moves row <paramref name="number"/> (from 2) one place up the list.</summary>
    public void MoveUp(int number)
    {
        var names = Names.ToList();
        Check(number, names.Count);
        if (number == 1)
            throw new InvalidOperationException("It's first already.");
        (names[number - 2], names[number - 1]) = (names[number - 1], names[number - 2]);
        Save(names);
    }

    public IRelayCommand AddCommand => _add ??= new RelayCommand(() => Add());

    private IRelayCommand? _add;

    public override void Refresh()
    {
        Fit(Rows, Names.Count, () => new NameRow(this));
        OnPropertyChanged(string.Empty);
    }

    // A name in a list separated by commas can't have one in it.
    private string Clean(string name) =>
        Field.Property.Type == typeof(string) ? string.Join(' ', name.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) : name.Trim();

    private void Save(List<string> names)
    {
        if (Field.Property.Type == typeof(string))
            SetText(string.Join(", ", names));
        else
            SetLines(names);
    }

    private void Check(int number, int count)
    {
        if (number < 1 || number > count)
            throw new ArgumentOutOfRangeException(nameof(number), count == 0 ? $"There are no {Attribute.Noun}s yet: Add makes one." : $"The {Attribute.Noun}s are numbered 1 to {count}.");
    }
}

/// <summary>One name of a <see cref="NameListEditor"/>.</summary>
public sealed class NameRow(NameListEditor owner) : EditorRow(owner.Remove)
{
    /// <summary>The name; setting it renames it, as one undo step.</summary>
    public string Name
    {
        get => owner.Names.ElementAtOrDefault(Number - 1) ?? "";
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value.Trim() != Name)
                owner.Set(Number, value);
        }
    }

    /// <summary>True when it can be taken out.</summary>
    public bool CanRemove => owner.CanRemove;

    /// <summary>True when it can move up: it isn't first.</summary>
    public bool CanMoveUp => Number > 1;

    public IRelayCommand MoveUpCommand => _up ??= new RelayCommand(() => owner.MoveUp(Number));

    private IRelayCommand? _up;

    public override string ToString() => $"{Number}: {Name}";
}

/// <summary>
/// Letters counted (<see cref="LetterCountsAttribute"/>), e.g. a falling-block board's pieces: a count for each letter,
/// with its picture, so "two I's for every O" is two clicks.
/// </summary>
public sealed class LetterCountsEditor : FieldEditor<LetterCountsAttribute>
{
    public LetterCountsEditor(PropertyField field, LetterCountsAttribute attribute)
        : base(field, attribute)
    {
        Letters = [.. attribute.Letters.Select(l => new LetterRow(l, this, Picture(attribute.CellsOf(Component, l))))];
    }

    /// <summary>The letters, a row each with its count.</summary>
    public IReadOnlyList<LetterRow> Letters { get; }

    /// <summary>How many of each, in the letters' order.</summary>
    public IReadOnlyList<int> Counts => Field.IsMixed ? [.. Attribute.Letters.Select(_ => 0)] : Attribute.Read(Text);

    /// <summary>"7 pieces a round" or, with none, that every one comes once.</summary>
    public string Summary => Field.IsMixed ? "The selected entities' differ." : Counts.Sum() switch
    {
        0 => $"None picked: every one of the {Attribute.Noun} comes once a round.",
        var n => $"{n} {Attribute.Noun} a round, shuffled.",
    };

    /// <summary>Sets how many times <paramref name="letter"/> comes, as one undo step (a drag of its count is one).</summary>
    public void Set(string letter, int count) => Set(letter, count, seal: true);

    internal void Set(string letter, int count, bool seal)
    {
        var index = Attribute.Letters.IndexOf(char.ToUpperInvariant(letter.Trim().FirstOrDefault()), StringComparison.Ordinal);
        if (letter.Trim().Length != 1 || index < 0)
            throw new ArgumentException($"There's no {letter}; the {Attribute.Noun} are {string.Join(", ", Attribute.Letters.ToCharArray())}.", nameof(letter));
        var counts = Counts.ToList();
        counts[index] = Math.Clamp(count, 0, 9);
        var text = Attribute.Write(counts);
        if (seal)
            SetText(text);
        else
            SetTextLive(text);
    }

    public override void Refresh()
    {
        base.Refresh();
        foreach (var letter in Letters)
            letter.Refresh();
    }

    // The cells as a picture: a square each, y up.
    private static System.Windows.Media.GeometryGroup? Picture(IReadOnlyList<(int X, int Y)> cells)
    {
        if (cells.Count == 0)
            return null;
        var group = new System.Windows.Media.GeometryGroup();
        var top = cells.Max(c => c.Y);
        foreach (var (x, y) in cells)
            group.Children.Add(new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(x + 0.06, top - y + 0.06, 0.88, 0.88)));
        group.Freeze();
        return group;
    }
}

/// <summary>One letter of a <see cref="LetterCountsEditor"/>: the letter, its picture and how many times it comes.</summary>
public sealed class LetterRow(char letter, LetterCountsEditor owner, System.Windows.Media.Geometry? picture) : ObservableObject
{
    public string Letter { get; } = letter.ToString();

    /// <summary>Its cells (a piece's shape), or null for none.</summary>
    public System.Windows.Media.Geometry? Picture { get; } = picture;

    /// <summary>How many times it comes in a round.</summary>
    public double Count
    {
        get => owner.Counts[owner.Attribute.Letters.IndexOf(letter, StringComparison.Ordinal)];
        set => owner.Set(Letter, (int)Math.Round(value), seal: false);
    }

    public void Refresh() => OnPropertyChanged(nameof(Count));

    public override string ToString() => $"{Letter} × {Count}";
}

/// <summary>
/// The value an action is done with (<see cref="ActionValueAttribute"/>), e.g. a trigger's With: a number box for an
/// amount, a tick for true or false, or a list of what a name names (flags for Set Flag, scenes for Load Scene), as the
/// action named wants, with what it takes said under it.
/// </summary>
public sealed class ActionValueEditor(PropertyField field, ActionValueAttribute attribute) : FieldEditor<ActionValueAttribute>(field, attribute)
{
    private ComponentAction? Action => Attribute.Find(Component);

    /// <summary>"Damage takes an amount (empty: 1).", or that the action isn't found yet.</summary>
    public string Note => Action switch
    {
        null => "Pick what to do first: it says what it takes.",
        { ValueType: null } found => $"{found.DisplayName} takes nothing.",
        var found => $"{found.DisplayName} takes {found.ValueKind}{(found.ValueName is { } name ? $" ({InspectorPanel.Humanize(name).ToLowerInvariant()})" : "")}" +
                     (found.DefaultValue is { } fallback ? $"; empty uses {Convert.ToString(fallback, CultureInfo.InvariantCulture)?.ToLowerInvariant()}." : "."),
    };

    /// <summary>True when the action takes a number or whole number: a number box.</summary>
    public bool IsNumber => Action?.ValueType is { } type && (type == typeof(float) || type == typeof(int));

    /// <summary>True when it takes true or false: a tick.</summary>
    public bool IsBool => Action?.ValueType == typeof(bool);

    /// <summary>True when it takes a name that names something the project has: a list to pick from (or type).</summary>
    public bool IsName => Action?.ValueNames is not null;

    /// <summary>True when it's typed: a name (with what the project has to pick beside it), or an action not found yet.</summary>
    public bool IsTyped => !IsNumber && !IsBool;

    /// <summary>Picks a name from <see cref="Choices"/>, as one undo step (the list beside the box).</summary>
    public string? Pick
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                SetText(value);
            OnPropertyChanged();
        }
    }

    /// <summary>True for a whole number.</summary>
    public bool IsInteger => Action?.ValueType == typeof(int);

    /// <summary>The number it's done with (the action's default while it's empty).</summary>
    public double Number
    {
        get => double.TryParse(Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n
            : Action?.DefaultValue is IConvertible fallback ? fallback.ToDouble(CultureInfo.InvariantCulture) : 0;
        set => SetTextLive((IsInteger ? Math.Round(value) : value).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>True or false, for an action taking one (the action's default while it's empty).</summary>
    public bool Flag
    {
        get => Text.Trim().ToLowerInvariant() switch
        {
            "true" or "on" or "1" => true,
            "false" or "off" or "0" => false,
            _ => Action?.DefaultValue is true,
        };
        set => SetText(value ? "true" : "false");
    }

    /// <summary>What the name can be: the project's flags, scenes... (for a condition flag, each with "not" too).</summary>
    public IReadOnlyList<string> Choices => Action?.ValueNames is { } names && Host.Names is { } known
        ? [.. known.Choices(names, Component).Select(c => c.Name)]
        : [];

    /// <summary>The value as typed; setting it sets it, as one undo step.</summary>
    public string Value
    {
        get => Field.IsMixed ? "" : Text;
        set
        {
            if ((value ?? "") != Text)
                SetText(value);
        }
    }

    /// <summary>Sets the value, as one undo step: a number, true or false, or a name.</summary>
    public void Set(string value) => SetText(value);
}

/// <summary>
/// A dialogue's lines (<see cref="DialogueLinesAttribute"/>): as typed, a line a row, what they hold in a few words, and
/// the Dialogue Editor a click away to build them from lists.
/// </summary>
public sealed class DialogueLinesEditor(PropertyField field, DialogueLinesAttribute attribute) : FieldEditor<DialogueLinesAttribute>(field, attribute)
{
    /// <summary>"8 lines: 2 choices, 1 part", or that the entities' lines differ.</summary>
    public string Summary
    {
        get
        {
            if (Field.IsMixed || Component is not Dialogue dialogue)
                return "The selected entities' lines differ.";
            var lines = Lines.Count(l => l.Trim().Length > 0);
            var choices = Lines.Count(l => l.TrimStart().StartsWith('>'));
            var parts = dialogue.PartNames().Count;
            var bits = new List<string>();
            if (choices > 0)
                bits.Add($"{choices} choice{(choices == 1 ? "" : "s")}");
            if (parts > 0)
                bits.Add($"{parts} part{(parts == 1 ? "" : "s")}");
            return $"{lines} line{(lines == 1 ? "" : "s")}{(bits.Count > 0 ? ": " + string.Join(", ", bits) : "")}";
        }
    }

    /// <summary>Opens the Dialogue Editor on the selected entity's dialogue (Entity > Edit Dialogue, the Visual Novels genre's).</summary>
    public void OpenDialogueEditor() => Host.EditFully(Attribute);

    public IRelayCommand OpenDialogueEditorCommand => _open ??= new RelayCommand(OpenDialogueEditor);

    private IRelayCommand? _open;
}
