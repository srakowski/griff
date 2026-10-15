using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Scenes;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// Rows of a few cells each (<see cref="RowsAttribute"/>): items and how many, a tile and what it drops, a recipe, a
/// stage's positions. Each row shows a box, picker or number for each cell (names picked from the project's and flagged when
/// it has none of them, tiles picked off the tileset, pictures and sounds from the project, colors from the palette), with up
/// and × buttons; Add puts a new row on the end. A row that doesn't read shows as typed, to put right. Agents set a cell with
/// <see cref="Set"/> (or ui_set on Rows[n].Cells[m].Value) and add, remove and move rows with <see cref="Add"/>,
/// <see cref="Remove"/> and <see cref="MoveUp"/>.
/// </summary>
public sealed class RowsEditor : FieldEditor<RowsAttribute>
{
    public RowsEditor(PropertyField field, RowsAttribute attribute)
        : base(field, attribute)
    {
        Refresh();
    }

    /// <summary>The rows, numbered from 1; one always, for a field of one.</summary>
    public ObservableCollection<RowsRow> Rows { get; } = [];

    /// <summary>The columns each row has.</summary>
    public IReadOnlyList<RowColumn> Columns => Attribute.Columns;

    /// <summary>The names of the columns that sit side by side in a row (the others take a line each under them).</summary>
    public IReadOnlyList<string> Headers => [.. Columns.Where(IsAcross).Select(c => c.Unit is { } unit ? $"{c.Name} ({unit})" : c.Name)];

    /// <summary>How wide each side-by-side column is, as a grid's column widths: "*,56".</summary>
    public string Layout => Widths(Columns.Where(IsAcross));

    /// <summary>How wide each of the cells on the line under the others is: as wide as each other.</summary>
    public string UnderLayout => string.Join(",", Columns.Where(c => c.Under).Select(_ => "*"));

    // A column whose cells sit side by side on the row's first line.
    internal static bool IsAcross(RowColumn column) => !column.Wide && !column.Under;

    private static string Widths(IEnumerable<RowColumn> columns)
    {
        var widths = columns.Select(c => c.Width > 0 ? c.Width.ToString(CultureInfo.InvariantCulture) : c.Kind switch
        {
            CellKind.Number or CellKind.Whole => "56",
            CellKind.Tile => "100",
            CellKind.Color => "112",
            _ => "*",
        }).ToList();
        if (widths.Count > 0 && !widths.Contains("*"))
            widths[0] = "*";
        return string.Join(",", widths);
    }

    /// <summary>True when the column names show over the rows: there are rows, and more than one column side by side.</summary>
    public bool ShowsHeaders => Rows.Count > 0 && Headers.Count > 1;

    /// <summary>Rows of several cells (or of one with a line of its own) take the section's whole width, under the field's name.</summary>
    public override bool Stacks => !Attribute.One && (Columns.Count > 1 || Columns.Any(c => c.Wide));

    /// <summary>True when rows can be added and taken out: not for a field of one.</summary>
    public bool IsList => !Attribute.One;

    /// <summary>"Add Item", "Add Recipe": the Add button's words, from what a row is.</summary>
    public string AddText
    {
        get
        {
            var noun = Attribute.Noun;
            foreach (var article in new[] { "a ", "an " })
            {
                if (noun.StartsWith(article, StringComparison.OrdinalIgnoreCase))
                    noun = noun[article.Length..];
            }

            var and = noun.IndexOf(" and ", StringComparison.Ordinal);
            if (and > 0)
                noun = noun[..and];
            return "Add " + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(noun);
        }
    }

    /// <summary>The rows as written, blank ones left out.</summary>
    internal List<string> Entries => Field.IsMixed ? [] : [.. Attribute.Rows(Field.Value)];

    /// <summary>Row <paramref name="number"/>'s cells, or null when it isn't there or doesn't read.</summary>
    internal IReadOnlyList<string>? CellsOf(int number) => Entries.ElementAtOrDefault(number - 1) is { } row ? Attribute.Read(row) : null;

    /// <summary>
    /// Puts a new row on the end, as one undo step, its cells what a new row has (a name the project has where it takes one),
    /// and returns its number.
    /// </summary>
    public int Add()
    {
        var entries = Entries;
        if (Attribute.One && entries.Count > 0)
            throw new InvalidOperationException("It holds one: set its cells instead.");
        entries.Add(Attribute.Write(Fill(Attribute.NewRow())));
        Store(entries, seal: true);
        return entries.Count;
    }

    /// <summary>
    /// Sets the cell of row <paramref name="number"/> (from 1) in the column called <paramref name="column"/> ("Item",
    /// "Count") to <paramref name="value"/> as the row writes it ("Key", "3", "#8B5A2B", "Wood + Coal 2"), as one undo step.
    /// </summary>
    public void Set(int number, string column, string value)
    {
        var index = Columns.ToList().FindIndex(c => c.Name.Equals(column.Trim(), StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new ArgumentException($"There's no column called {column}; they're {string.Join(", ", Columns.Select(c => c.Name))}.", nameof(column));
        Check(number);
        if (!Attribute.One && CellsOf(number) is null)
            throw new InvalidOperationException($"Row {number} doesn't read (\"{Entries[number - 1]}\"): set it whole with SetRow.");
        ChangeCell(number, index, value ?? "", seal: true);
    }

    /// <summary>Sets row <paramref name="number"/> (from 1) as it's written, e.g. "Coin 3", as one undo step: for a row that doesn't read.</summary>
    public void SetRow(int number, string row)
    {
        var entries = Entries;
        Check(number);
        if (number > entries.Count)
            entries.Add(row);
        else
            entries[number - 1] = row;
        Store(entries, seal: true);
    }

    /// <summary>Takes row <paramref name="number"/> (from 1) out, as one undo step.</summary>
    public void Remove(int number)
    {
        var entries = Entries;
        if (number < 1 || number > entries.Count)
            throw new ArgumentOutOfRangeException(nameof(number), entries.Count == 0 ? "There are no rows yet: Add makes one." : $"The rows are numbered 1 to {entries.Count}.");
        entries.RemoveAt(number - 1);
        Store(entries, seal: true);
    }

    /// <summary>Moves row <paramref name="number"/> (from 2) up one, ahead of the one before it, as one undo step.</summary>
    public void MoveUp(int number)
    {
        var entries = Entries;
        if (number < 2 || number > entries.Count)
            throw new ArgumentOutOfRangeException(nameof(number), $"Rows 2 to {entries.Count} can move up.");
        (entries[number - 2], entries[number - 1]) = (entries[number - 1], entries[number - 2]);
        Store(entries, seal: true);
    }

    public IRelayCommand AddCommand => _add ??= new RelayCommand(() => Add());

    private IRelayCommand? _add;

    public override void Refresh()
    {
        Fit(Rows, Attribute.One ? 1 : Entries.Count, () => new RowsRow(this));
        foreach (var row in Rows)
            row.RefreshCells();
        OnPropertyChanged(string.Empty);
    }

    private void Check(int number)
    {
        var count = Attribute.One ? 1 : Entries.Count;
        if (number < 1 || number > count)
            throw new ArgumentOutOfRangeException(nameof(number), count == 0 ? "There are no rows yet: Add makes one." : $"The rows are numbered 1 to {count}.");
    }

    // ----- For the rows and cells

    /// <summary>Cell <paramref name="column"/> of row <paramref name="number"/> as it's written; empty when it isn't there.</summary>
    internal string CellOf(int number, int column) => CellsOf(number)?.ElementAtOrDefault(column) ?? "";

    /// <summary>Sets a cell; <paramref name="seal"/> ends the undo step (not while a number's dragged).</summary>
    internal void ChangeCell(int number, int column, string value, bool seal)
    {
        var entries = Entries;
        // A field of one with nothing in it yet gets its row as its cell is set.
        if (Attribute.One && entries.Count == 0 && number == 1)
            entries.Add(Attribute.Write(Fill(Attribute.NewRow())));
        if (number < 1 || number > entries.Count || Attribute.Read(entries[number - 1]) is not { } read)
            return;
        var cells = read.ToList();
        if (cells[column] == value)
            return;
        cells[column] = value;
        var row = Attribute.Write(cells);
        // A change that wouldn't read back (a name taken away that it needs) isn't made.
        if (Attribute.Read(row) is null)
            return;
        entries[number - 1] = row;
        Store(entries, seal);
    }

    // Writes the rows to the field, as it holds them.
    private void Store(List<string> entries, bool seal)
    {
        var value = Attribute.Store(entries, Field.Property.Type);
        if (value is IReadOnlyList<string> lines)
            SetLines(lines, seal);
        else if (seal)
            SetText(value as string);
        else
            SetTextLive(value as string);
    }

    // A new row's empty cells filled with what makes it read: the first name the project has, or the column's name.
    private List<string> Fill(IReadOnlyList<string> cells)
    {
        var filled = cells.ToList();
        for (var i = 0; i < Columns.Count && i < filled.Count; i++)
        {
            var column = Columns[i];
            if (filled[i].Length > 0 || column.Optional)
                continue;
            filled[i] = column.Kind switch
            {
                CellKind.Number or CellKind.Whole => "0",
                CellKind.Color => "#FFFFFF",
                CellKind.Tile => "0",
                _ => ChoicesFor(i).FirstOrDefault(c => c.Length > 0) ?? column.Name,
            };
        }

        return filled;
    }

    /// <summary>What column <paramref name="column"/>'s cells offer: the project's names (or pictures, or sounds), the column's own words, and what the field adds.</summary>
    internal IReadOnlyList<string> ChoicesFor(int column)
    {
        var info = Columns[column];
        IEnumerable<string> found = info.Kind switch
        {
            CellKind.Image => Images,
            CellKind.Sound => Sounds,
            CellKind.Name or CellKind.Amounts when info.Names is { } kind && Host.Names is { } names => names.Choices(kind, Component).Select(c => c.Name),
            _ => [],
        };
        return [.. (info.Choices ?? []).Concat(found).Concat(Attribute.MoreChoices(column, Component, Host.Names))
            .Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>True when a name cell's <paramref name="value"/> is one the project has none of (flagged in yellow).</summary>
    internal bool IsMissing(int column, string value)
    {
        var info = Columns[column];
        if (value.Trim().Length == 0 || info.Loose || (info.Choices ?? []).Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
            return false;
        return info.Kind switch
        {
            CellKind.Name or CellKind.Amounts when info.Names is { } kind && Host.Names is { } names =>
                (info.Kind == CellKind.Amounts ? Amounts.Read(value).Select(a => a.Name) : [value.Trim()]).Any(n => names.Unmatched(kind, n).Count > 0),
            CellKind.Image => Host.Art is { AssetsPath: not null } art && art.Resolve(value) is null &&
                !Attribute.MoreChoices(column, Component, Host.Names).Contains(value.Trim(), StringComparer.OrdinalIgnoreCase),
            CellKind.Sound => Host.Audio is { AssetsPath: not null } audio && audio.Resolve(value) is null,
            _ => false,
        };
    }

    /// <summary>The project's images (looked for again every few seconds at most).</summary>
    internal IReadOnlyList<string> Images
    {
        get
        {
            if (_images is null || Environment.TickCount64 - _imagesAt > 2000)
                (_images, _imagesAt) = (Host.Art.Images(), Environment.TickCount64);
            return _images;
        }
    }

    /// <summary>The project's sounds and music (looked for again every few seconds at most).</summary>
    internal IReadOnlyList<string> Sounds
    {
        get
        {
            if (_sounds is null || Environment.TickCount64 - _soundsAt > 2000)
                (_sounds, _soundsAt) = (Host.Audio.Sounds(), Environment.TickCount64);
            return _sounds;
        }
    }

    private IReadOnlyList<string>? _images, _sounds;
    private long _imagesAt, _soundsAt;

    /// <summary>The tilemap whose tiles tile cells pick, or null.</summary>
    internal Tilemap? Map => TilePictures.MapOf(Component);

    internal Art.EditorArt Art => Host.Art;

    internal Audio.EditorAudio Audio => Host.Audio;

    internal Griff.Art.Palette Palette => Host.Palette;

    /// <summary>The note under row <paramref name="number"/>, from the field (a song's chart), or null.</summary>
    internal string? NoteOf(int number) => CellsOf(number) is { } cells ? Attribute.Note(Component, cells, Host.Names) : null;
}

/// <summary>One row of a <see cref="RowsEditor"/>: its cells, side by side and a line each, or the row as typed when it doesn't read.</summary>
public sealed class RowsRow : EditorRow
{
    private readonly RowsEditor _owner;

    public RowsRow(RowsEditor owner)
        : base(owner.Remove)
    {
        _owner = owner;
        Cells = [.. owner.Columns.Select((c, i) => RowCell.Make(owner, this, i))];
        var (across, under) = (0, 0);
        foreach (var cell in Cells)
        {
            if (RowsEditor.IsAcross(cell.Column))
                cell.Slot = across++;
            else if (cell.Column.Under)
                cell.Slot = under++;
        }
    }

    /// <summary>Its cells, a column each.</summary>
    public IReadOnlyList<RowCell> Cells { get; }

    /// <summary>The cells side by side.</summary>
    public IReadOnlyList<RowCell> Narrow => [.. Cells.Where(c => RowsEditor.IsAcross(c.Column))];

    /// <summary>The cells a line each, under them.</summary>
    public IReadOnlyList<RowCell> Wide => [.. Cells.Where(c => c.Column.Wide)];

    /// <summary>The cells side by side on a line under the rest, each with its name.</summary>
    public IReadOnlyList<RowCell> Under => [.. Cells.Where(c => c.Column.Under)];

    /// <summary>True when it has cells on a line under the rest.</summary>
    public bool HasUnder => Cells.Any(c => c.Column.Under);

    /// <summary>The grid's column widths for <see cref="Narrow"/>.</summary>
    public string Layout => _owner.Layout;

    /// <summary>The grid's column widths for <see cref="Under"/>.</summary>
    public string UnderLayout => _owner.UnderLayout;

    /// <summary>True when it reads, so its cells show; false shows it as typed.</summary>
    public bool Reads => _owner.CellsOf(Number) is not null || (_owner.Attribute.One && _owner.Entries.Count == 0);

    /// <summary>The row as typed: setting it sets the whole row, as one undo step.</summary>
    public string Text
    {
        get => _owner.Entries.ElementAtOrDefault(Number - 1) ?? "";
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value != Text)
                _owner.SetRow(Number, value.Trim());
        }
    }

    /// <summary>Why it doesn't read, under it.</summary>
    public string Problem => Reads ? "" : $"This doesn't read as {_owner.Attribute.Noun}: write it like {_owner.Attribute.Example}";

    /// <summary>A word on what it does now (a song's chart), or null.</summary>
    public string? Note => Reads ? _owner.NoteOf(Number) : null;

    public bool HasNote => !string.IsNullOrEmpty(Note);

    /// <summary>True when it can be taken out and moved: a list's rows.</summary>
    public bool IsListed => _owner.IsList;

    public bool CanMoveUp => _owner.IsList && Number > 1;

    /// <summary>Moves it up a place, as one undo step (its ↑ button).</summary>
    public IRelayCommand MoveUpCommand => _up ??= new RelayCommand(() => _owner.MoveUp(Number));

    private IRelayCommand? _up;

    internal void RefreshCells()
    {
        foreach (var cell in Cells)
            cell.Refresh();
    }
}

/// <summary>A cell of a <see cref="RowsRow"/>: the value it holds as written, edited as its column says.</summary>
public abstract class RowCell : ObservableObject
{
    private protected RowCell(RowsEditor owner, RowsRow row, int column)
    {
        Owner = owner;
        Row = row;
        Index = column;
    }

    private protected RowsEditor Owner { get; }

    private protected RowsRow Row { get; }

    /// <summary>Which column it is, from 0.</summary>
    public int Index { get; }

    /// <summary>Where it sits among the row's side-by-side cells.</summary>
    public int Slot { get; internal set; }

    /// <summary>Its column.</summary>
    public RowColumn Column => Owner.Columns[Index];

    /// <summary>Its column's name, for a cell on a line of its own.</summary>
    public string Label => Column.Name;

    /// <summary>What it is, as a tip.</summary>
    public string Tip => Column.Tip is { } tip ? $"{Column.Name}: {tip}" : Column.Name;

    /// <summary>"editor:item:2": what demos find it by.</summary>
    public string Tag => $"editor:{Column.Name.ToLowerInvariant()}:{Row.Number}";

    /// <summary>What it holds, as the row writes it; setting it is one undo step.</summary>
    public string Value
    {
        get => Owner.CellOf(Row.Number, Index);
        set => Owner.ChangeCell(Row.Number, Index, value?.Trim() ?? "", seal: true);
    }

    /// <summary>True when it names something the project has none of.</summary>
    public bool IsMissing => Owner.IsMissing(Index, Value);

    /// <summary>What it offers to pick.</summary>
    public IReadOnlyList<string> Choices => Owner.ChoicesFor(Index);

    /// <summary>Picks from <see cref="Choices"/> (a picker's list): sets <see cref="Value"/>.</summary>
    public string? Pick
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                Value = value;
            OnPropertyChanged();
        }
    }

    public virtual void Refresh() => OnPropertyChanged(string.Empty);

    internal static RowCell Make(RowsEditor owner, RowsRow row, int column) => owner.Columns[column].Kind switch
    {
        CellKind.Number or CellKind.Whole => new NumberCell(owner, row, column),
        CellKind.Name => new NameCell(owner, row, column),
        CellKind.Choice => new ChoiceCell(owner, row, column),
        CellKind.Tile => new TileCell(owner, row, column),
        CellKind.Image => new ImageCell(owner, row, column),
        CellKind.Sound => new SoundCell(owner, row, column),
        CellKind.Color => new ColorCell(owner, row, column),
        CellKind.Amounts => new AmountsCell(owner, row, column),
        _ => new TextCell(owner, row, column),
    };

    // Sets the cell without ending the undo step, for a number dragged.
    private protected void SetLive(string value) => Owner.ChangeCell(Row.Number, Index, value, seal: false);
}

/// <summary>Words of its own: a line said, a name given here.</summary>
public sealed class TextCell(RowsEditor owner, RowsRow row, int column) : RowCell(owner, row, column);

/// <summary>A name picked from what the project has (or typed), flagged when it has none of it.</summary>
public sealed class NameCell(RowsEditor owner, RowsRow row, int column) : RowCell(owner, row, column);

/// <summary>One of a fixed few, picked.</summary>
public sealed class ChoiceCell(RowsEditor owner, RowsRow row, int column) : RowCell(owner, row, column);

/// <summary>A number or whole number, dragged or typed.</summary>
public sealed class NumberCell(RowsEditor owner, RowsRow row, int column) : RowCell(owner, row, column)
{
    /// <summary>True for a whole number.</summary>
    public bool IsInteger => Column.Kind == CellKind.Whole;

    /// <summary>The number: dragging it is one undo step.</summary>
    public double Number
    {
        get => double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;
        set => SetLive(IsInteger ? ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture) : RowsAttribute.Number(value));
    }
}

/// <summary>A tile, by number (or a range), with its picture, picked off the tilemap's tilesets.</summary>
public sealed class TileCell(RowsEditor owner, RowsRow row, int column) : RowCell(owner, row, column)
{
    private bool _isOpen;
    private IReadOnlyList<TilesetChoices> _tilesets = [];

    /// <summary>The first tile it names, or -1.</summary>
    public int Tile => TileNumbersAttribute.Read(Value) is [var first, ..] ? first : -1;

    /// <summary>Its tile's picture, or null when there's no tilemap.</summary>
    public ImageSource? Picture => Owner.Map is { } map && Tile >= 0 ? TilePictures.Of(Owner.Art, map, Tile) : null;

    /// <summary>The tilemap's tint, behind a tile.</summary>
    public Brush? Fill => Owner.Map is { } map ? TilePictures.Tint(map) : null;

    /// <summary>True when there's a tilemap to pick from.</summary>
    public bool HasMap => Owner.Map is not null;

    /// <summary>Whether the tilesets' tiles show, to click one.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (!SetProperty(ref _isOpen, value))
                return;
            _tilesets = value && Owner.Map is { } map ? Build(map) : [];
            OnPropertyChanged(nameof(Tilesets));
        }
    }

    /// <summary>The tilesets' tiles while it's open.</summary>
    public IReadOnlyList<TilesetChoices> Tilesets => _tilesets;

    /// <summary>Picks tile <paramref name="tile"/>, as one undo step, and shuts the tiles.</summary>
    public void PickTile(int tile)
    {
        Value = tile.ToString(CultureInfo.InvariantCulture);
        IsOpen = false;
    }

    private IReadOnlyList<TilesetChoices> Build(Tilemap map)
    {
        var fill = TilePictures.Tint(map);
        var picked = Tile;
        return [.. map.Tilesets().Select(t => new TilesetChoices(t.Name, t.First,
            [.. Enumerable.Range(t.First, Math.Min(t.Count, TileNumbersEditor.MostShown)).Select(n => new TileChoice(n, TilePictures.Of(Owner.Art, map, n), fill, n == picked, PickTile))],
            Math.Clamp(t.Columns, 1, 16)))];
    }
}

/// <summary>A picture in Assets (or what else the field offers, a clip), picked from the project's, shown small.</summary>
public sealed class ImageCell(RowsEditor owner, RowsRow row, int column) : RowCell(owner, row, column)
{
    /// <summary>Its picture, small; null when it isn't one.</summary>
    public ImageSource? Preview => Owner.Art.Thumbnail(Value);
}

/// <summary>A sound or music file in Assets, picked from the project's, with a button to hear it.</summary>
public sealed class SoundCell(RowsEditor owner, RowsRow row, int column) : RowCell(owner, row, column)
{
    /// <summary>Plays it, or stops it playing.</summary>
    public IRelayCommand PlayCommand => _play ??= new RelayCommand(() => Owner.Audio.TogglePreview(Value));

    private IRelayCommand? _play;
}

/// <summary>A color, as hex, shown, and picked from the painter's palette.</summary>
public sealed class ColorCell(RowsEditor owner, RowsRow row, int column) : RowCell(owner, row, column)
{
    /// <summary>The color, to show; clear when it doesn't read.</summary>
    public Brush Swatch => Color.TryParse(Value, out var color) ? ColorPicker.BrushOf(color) : Brushes.Transparent;

    /// <summary>The palette's colors, to pick from.</summary>
    public IReadOnlyList<PaletteColor> Palette => [.. Owner.Palette.Colors.Select(c =>
        new PaletteColor(Color.FromBytes((byte)(c >> 16), (byte)(c >> 8), (byte)c).ToHex()[..7]))];

    /// <summary>Picks a palette color: sets <see cref="RowCell.Value"/>.</summary>
    public PaletteColor? PickColor
    {
        get => null;
        set
        {
            if (value is not null)
                Value = value.Hex;
            OnPropertyChanged();
        }
    }
}

/// <summary>A color of the palette a <see cref="ColorCell"/> offers.</summary>
public sealed record PaletteColor(string Hex)
{
    public Brush Brush => Color.TryParse(Hex, out var color) ? ColorPicker.BrushOf(color) : Brushes.Transparent;
}

/// <summary>Names and how many of each ("Wood + Coal 2"): a part each, its name and count to set, and one to add.</summary>
public sealed class AmountsCell(RowsEditor owner, RowsRow row, int column) : RowCell(owner, row, column)
{
    /// <summary>Its names, each with how many.</summary>
    public IReadOnlyList<AmountPart> Parts => [.. Amounts.Read(Value).Select((a, i) => new AmountPart(this, i, a.Name, a.Count))];

    /// <summary>Adds the name picked on the end, one of it.</summary>
    public string? AddPick
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                Value = Amounts.Write([.. Amounts.Read(Value), (value.Trim(), 1)]);
            OnPropertyChanged();
        }
    }

    internal void Change(int part, string? name, int? count, bool seal)
    {
        var amounts = Amounts.Read(Value).ToList();
        if (part < 0 || part >= amounts.Count)
            return;
        if (name is null && count is null)
            amounts.RemoveAt(part);
        else
            amounts[part] = (name ?? amounts[part].Name, Math.Max(1, count ?? amounts[part].Count));
        var text = Amounts.Write(amounts);
        if (seal)
            Value = text;
        else
            SetLive(text);
    }

    internal bool IsPartMissing(string name) => Owner.IsMissing(Index, name);
}

/// <summary>One name of an <see cref="AmountsCell"/> and how many of it.</summary>
public sealed class AmountPart(AmountsCell owner, int index, string name, int count) : ObservableObject
{
    /// <summary>The name, typed or picked.</summary>
    public string Name
    {
        get => name;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                owner.Change(index, value.Trim(), null, seal: true);
        }
    }

    /// <summary>How many of it.</summary>
    public double Count
    {
        get => count;
        set => owner.Change(index, null, (int)Math.Round(value), seal: false);
    }

    /// <summary>True when the project has none of it.</summary>
    public bool IsMissing => owner.IsPartMissing(Name);

    /// <summary>Takes it out, as one undo step.</summary>
    public IRelayCommand RemoveCommand => _remove ??= new RelayCommand(() => owner.Change(index, null, null, seal: true));

    private IRelayCommand? _remove;

    /// <summary>"editor:part:2": what demos find it by, with its place from 1.</summary>
    public string Tag => $"editor:part:{index + 1}";
}

/// <summary>
/// Lays a grid's columns out from a list of widths ("*,64,104"), for a row's cells side by side: set on a Grid used as an
/// items panel, each cell put in its column by its slot.
/// </summary>
public static class GridColumns
{
    public static readonly DependencyProperty WidthsProperty = DependencyProperty.RegisterAttached(
        "Widths", typeof(string), typeof(GridColumns), new PropertyMetadata(null, OnWidthsChanged));

    public static string? GetWidths(DependencyObject element) => (string?)element.GetValue(WidthsProperty);

    public static void SetWidths(DependencyObject element, string? value) => element.SetValue(WidthsProperty, value);

    private static void OnWidthsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid grid)
            return;
        grid.ColumnDefinitions.Clear();
        foreach (var width in (e.NewValue as string ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = width == "*" ? new GridLength(1, GridUnitType.Star)
                    : new GridLength(double.Parse(width, CultureInfo.InvariantCulture)),
            });
        }
    }
}

/// <summary>
/// Tags a part of a cell for demos to find it: the cell's tag with the parameter after it ("editor:item:2" and ":pick" make
/// "editor:item:2:pick").
/// </summary>
public sealed class TagSuffixConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => $"{value}{parameter}";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
