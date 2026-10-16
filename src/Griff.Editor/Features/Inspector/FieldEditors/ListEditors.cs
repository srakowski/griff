using System.Collections.ObjectModel;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Editing;
using Griff.Scenes;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// Prefabs and how many of each (<see cref="PrefabCountsAttribute"/>), a row each: the prefab picked from the project's
/// (flagged when there's none of that name) and the fewest and most of it, a number picked between them each time. Add
/// puts a prefab of the project's on the end.
/// </summary>
public sealed class PrefabCountsEditor : FieldEditor<PrefabCountsAttribute>
{
    public PrefabCountsEditor(PropertyField field, PrefabCountsAttribute attribute)
        : base(field, attribute)
    {
        Refresh();
    }

    /// <summary>The prefabs, a row each.</summary>
    public ObservableCollection<PrefabCountRow> Rows { get; } = [];

    /// <summary>The project's prefabs, to pick from.</summary>
    public IReadOnlyList<string> Prefabs => [.. (Host.Names?.Prefabs ?? Host.PrefabNames).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The lines, blank ones left out.</summary>
    internal List<string> Entries => [.. Lines.Where(l => l.Trim().Length > 0)];

    /// <summary>Adds <paramref name="prefab"/>, <paramref name="least"/> to <paramref name="most"/> of it, on the end, as one undo step.</summary>
    public void Add(string prefab, int least = 1, int most = 1)
    {
        if (string.IsNullOrWhiteSpace(prefab))
            throw new ArgumentException("Name a prefab, like Enemy.", nameof(prefab));
        SetLines([.. Entries, PrefabCountsAttribute.Write(prefab, Math.Max(0, least), Math.Max(0, Math.Max(least, most)))]);
    }

    /// <summary>Sets row <paramref name="number"/> (from 1): its prefab and the fewest and most of it, as one undo step.</summary>
    public void Set(int number, string prefab, int least, int most) =>
        Change(number, _ => (prefab.Trim(), Math.Max(0, least), Math.Max(0, most)), seal: true);

    /// <summary>Takes row <paramref name="number"/> (from 1) out, as one undo step.</summary>
    public void Remove(int number)
    {
        var entries = Entries;
        if (number < 1 || number > entries.Count)
            throw new ArgumentOutOfRangeException(nameof(number), $"The rows are numbered 1 to {entries.Count}.");
        entries.RemoveAt(number - 1);
        SetLines(entries);
    }

    /// <summary>Adds the prefab picked from the list on the end, one of it.</summary>
    public string? AddPrefab
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
        Fit(Rows, Entries.Count, () => new PrefabCountRow(this));
        OnPropertyChanged(string.Empty);
    }

    internal (string Prefab, int Least, int Most) Entry(int number) =>
        Entries.ElementAtOrDefault(number - 1) is { } line ? PrefabCountsAttribute.Read(line) : ("", 1, 1);

    internal void Change(int number, Func<(string Prefab, int Least, int Most), (string Prefab, int Least, int Most)> change, bool seal)
    {
        var entries = Entries;
        if (number < 1 || number > entries.Count)
            return;
        var (prefab, least, most) = change(PrefabCountsAttribute.Read(entries[number - 1]));
        if (prefab.Length == 0)
            return;
        entries[number - 1] = PrefabCountsAttribute.Write(prefab, least, Math.Max(least, most));
        SetLines(entries, seal);
    }

    // True when the project has no prefab of that name (it can't be told without knowing the project's).
    internal bool IsMissing(string prefab) => Host.Names is { } names && prefab.Length > 0 && !names.Prefabs.Contains(prefab);
}

/// <summary>One row of a <see cref="PrefabCountsEditor"/>: a prefab and how many of it.</summary>
public sealed class PrefabCountRow(PrefabCountsEditor owner) : EditorRow(owner.Remove)
{
    /// <summary>The prefab, picked from the project's.</summary>
    public string Prefab
    {
        get => owner.Entry(Number).Prefab;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                owner.Change(Number, e => e with { Prefab = value.Trim() }, seal: true);
        }
    }

    /// <summary>The fewest of it.</summary>
    public double Least
    {
        get => owner.Entry(Number).Least;
        set => owner.Change(Number, e => (e.Prefab, Math.Max(0, (int)Math.Round(value)), Math.Max(e.Most, Math.Max(0, (int)Math.Round(value)))), seal: false);
    }

    /// <summary>The most of it; a number between the fewest and this is picked each time.</summary>
    public double Most
    {
        get => owner.Entry(Number).Most;
        set => owner.Change(Number, e => (e.Prefab, Math.Min(e.Least, Math.Max(0, (int)Math.Round(value))), Math.Max(0, (int)Math.Round(value))), seal: false);
    }

    /// <summary>True when the project has no prefab called that (flagged in yellow).</summary>
    public bool IsMissing => owner.IsMissing(Prefab);

    /// <summary>The project's prefabs (and this one's, if it isn't among them), to pick from.</summary>
    public IReadOnlyList<string> Choices => owner.Prefabs.Contains(Prefab, StringComparer.OrdinalIgnoreCase) || Prefab.Length == 0
        ? owner.Prefabs
        : [Prefab, .. owner.Prefabs];
}

/// <summary>
/// A direction (<see cref="DirectionAttribute"/>): its X and Y, and a compass of the eight ways to point it, the one it
/// points marked. With Distance Handles out, the Scene view draws it as an arrow to drag round.
/// </summary>
public sealed class DirectionEditor : FieldEditor<DirectionAttribute>
{
    public DirectionEditor(PropertyField field, DirectionAttribute attribute)
        : base(field, attribute)
    {
        Ways = [.. DirectionAttribute.Ways.Select(w => new WayChoice(w.Name, w.Way, this)),
            .. attribute.None is null ? Array.Empty<WayChoice>() : [new WayChoice(NoneName, Vector2.Zero, this)]];
    }

    /// <summary>The eight ways, the compass's buttons, from up and to the left round to down and to the right.</summary>
    public IReadOnlyList<WayChoice> Ways { get; }

    /// <summary>What the compass's middle is called, for a direction that may be none: (0, 0).</summary>
    public const string NoneName = "None";

    /// <summary>The way it points, when it's one of the eight ("Up Right"), or None for (0, 0) where that means something; otherwise null.</summary>
    public string? Way => Field.IsMixed ? null : Field.Value is Vector2 v ? v == Vector2.Zero && Attribute.None is not null ? NoneName : DirectionAttribute.NameOf(v) : null;

    /// <summary>Points it one of the eight ways, by name ("Up", "Down Left"), as one undo step.</summary>
    public void Point(string way)
    {
        if (Attribute.None is not null && way.Trim().Equals(NoneName, StringComparison.OrdinalIgnoreCase))
        {
            Field.Value = Vector2.Zero;
            Host.Seal();
            return;
        }

        var found = DirectionAttribute.Ways.FirstOrDefault(w => w.Name.Equals(way.Trim(), StringComparison.OrdinalIgnoreCase) ||
            w.Name.Replace(" ", "", StringComparison.Ordinal).Equals(way.Trim(), StringComparison.OrdinalIgnoreCase));
        if (found.Name is null)
            throw new ArgumentException($"There's no way called {way}; the ways are {string.Join(", ", DirectionAttribute.Ways.Select(w => w.Name))}.");
        Field.Value = found.Way;
        Host.Seal();
    }

    public override void Refresh()
    {
        base.Refresh();
        foreach (var way in Ways)
            way.Refresh();
    }
}

/// <summary>One of a <see cref="DirectionEditor"/>'s compass's ways: its name, the arrow showing it, and whether it points that way.</summary>
public sealed class WayChoice(string name, Vector2 way, DirectionEditor owner) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>Its arrow, ↖ to ↘.</summary>
    public string Arrow { get; } = (way.X, way.Y) switch
    {
        (< 0, > 0) => "↖",
        (0, > 0) => "↑",
        (> 0, > 0) => "↗",
        (< 0, 0) => "←",
        (> 0, 0) => "→",
        (< 0, < 0) => "↙",
        (0, < 0) => "↓",
        (0, 0) => "•",
        _ => "↘",
    };

    /// <summary>Where it goes in the compass's three by three: its row and column.</summary>
    public int Row { get; } = 1 - Math.Sign((int)way.Y);

    public int Column { get; } = Math.Sign((int)way.X) + 1;

    /// <summary>True when the direction points this way.</summary>
    public bool IsPicked => owner.Way == Name;

    /// <summary>Points the direction this way, as one undo step (its button on the compass).</summary>
    public IRelayCommand PointCommand => _point ??= new RelayCommand(() => owner.Point(Name));

    private IRelayCommand? _point;

    public void Refresh() => OnPropertyChanged(nameof(IsPicked));
}

/// <summary>
/// Text that shows an output (<see cref="OutputFormatAttribute"/>), e.g. a Text's Value: while its output is set, buttons put
/// in the output ({0}) and what it's out of ({1}), and it says how it reads now with what the output gives.
/// </summary>
public sealed class OutputFormatEditor(PropertyField field, OutputFormatAttribute attribute) : FieldEditor<OutputFormatAttribute>(field, attribute)
{
    /// <summary>True while the component names an output to show, so the text is how it reads.</summary>
    public bool ShowsOutput => ComponentModel.Of(Component).Property(Attribute.Output)?.GetValue(Component) is string { Length: > 0 } output && output.Trim().Length > 0;

    /// <summary>"Reads now: Gold: 12", with what the output gives now; or why it can't be read yet.</summary>
    public string Preview
    {
        get
        {
            if (!ShowsOutput)
                return "";
            if (Field.IsMixed)
                return "The selected entities' texts differ.";
            return Attribute.Preview(Component, Text) is { } reads ? $"Reads now: {reads}" : "Reads as the output, once what it reads is there.";
        }
    }

    /// <summary>
    /// Puts something on the end of the text, as one undo step: "output" ({0}), "outof" ({1}), "decimal" (the output with
    /// one decimal, {0:0.0}) or "padded" (the output with three digits, {0:000}).
    /// </summary>
    public void Insert(string what)
    {
        var token = what.Trim().ToLowerInvariant() switch
        {
            "output" or "{0}" => "{0}",
            "outof" or "out of" or "{1}" => "{1}",
            "decimal" => "{0:0.0}",
            "padded" => "{0:000}",
            _ => throw new ArgumentException("Put in output, outof, decimal or padded."),
        };
        SetText(Text + token);
    }

    public IRelayCommand<string> InsertCommand => _insert ??= new RelayCommand<string>(w => Insert(w!));

    private IRelayCommand<string>? _insert;
}
