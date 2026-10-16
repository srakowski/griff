using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Scenes;
using Griff.Editing;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// Tile pictures for the tile fields' editors: each tile cut from its tileset's image, as the Scene view's palette shows
/// it; null for a tile with no image (it draws as a square of the map's tint).
/// </summary>
internal static class TilePictures
{
    // Cut pictures by their image and where in it: kept while the image is the same one.
    private static readonly ConditionalWeakTableCache Cache = new();

    /// <summary>The tilemap a tile field is about: the component itself, or the one on its entity.</summary>
    public static Tilemap? MapOf(Component component) => component as Tilemap ?? component.Entity?.GetComponent<Tilemap>();

    /// <summary>Tile <paramref name="tile"/> of <paramref name="map"/>'s tilesets, cut out of its image.</summary>
    public static ImageSource? Of(Art.EditorArt art, Tilemap map, int tile)
    {
        if (tile < 0 || map.ImageOf(tile) is not { } image || art.Thumbnail(image) is not { } picture)
            return null;
        var rect = map.TileRect(tile);
        var area = new Int32Rect(
            (int)Math.Floor(rect.X * picture.PixelWidth), (int)Math.Floor(rect.Y * picture.PixelHeight),
            Math.Max(1, (int)Math.Round(rect.Z * picture.PixelWidth)), Math.Max(1, (int)Math.Round(rect.W * picture.PixelHeight)));
        if (area.X + area.Width > picture.PixelWidth || area.Y + area.Height > picture.PixelHeight)
            return null;
        return Cache.Get(picture, area);
    }

    /// <summary>The fill a tile with no picture shows: the map's tint.</summary>
    public static Brush Tint(Tilemap map) => ColorPicker.BrushOf(map.Tint);

    // A cache of cut pictures for each image, dropped with the image.
    private sealed class ConditionalWeakTableCache
    {
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<BitmapSource, Dictionary<Int32Rect, ImageSource>> _cuts = new();

        public ImageSource Get(BitmapSource picture, Int32Rect area)
        {
            var cuts = _cuts.GetOrCreateValue(picture);
            if (!cuts.TryGetValue(area, out var cut))
            {
                var cropped = new CroppedBitmap(picture, area);
                cropped.Freeze();
                cuts[area] = cut = cropped;
            }

            return cut;
        }
    }
}

/// <summary>One tile shown in a tile field's editor: its number, its picture, and whether the field has it.</summary>
/// <param name="pick">What clicking it does (adds it or takes it out), or null for one only shown.</param>
public sealed class TileChoice(int number, ImageSource? picture, Brush fill, bool isPicked, Action<int>? pick = null) : ObservableObject
{
    private IRelayCommand? _pick;

    /// <summary>Clicks it: on a field of several tiles, adds it or takes it out; on one of one, picks it.</summary>
    public IRelayCommand PickCommand => _pick ??= new RelayCommand(() => pick?.Invoke(Number), () => pick is not null);

    public int Number { get; } = number;

    public ImageSource? Picture { get; } = picture;

    /// <summary>What shows without a picture: the map's tint.</summary>
    public Brush Fill { get; } = fill;

    private bool _isPicked = isPicked;

    /// <summary>True when the field has this tile.</summary>
    public bool IsPicked
    {
        get => _isPicked;
        internal set => SetProperty(ref _isPicked, value);
    }

    /// <summary>"Tile 12", and whether it's picked, for its tooltip.</summary>
    public string Tip => $"Tile {Number}";
}

/// <summary>One of a tilemap's tilesets in a tile field's editor: its name and its tiles.</summary>
public sealed record TilesetChoices(string Name, int First, IReadOnlyList<TileChoice> Tiles, int Columns);

/// <summary>
/// Tiles of a tilemap picked by clicking them (<see cref="TileNumbersAttribute"/>): the map's tilesets' tiles, each picked
/// one marked, under the numbers as typed. A string field takes any number of tiles, written as numbers and ranges
/// ("10-15, 20"); a whole number takes one.
/// </summary>
public sealed class TileNumbersEditor : FieldEditor<TileNumbersAttribute>
{
    /// <summary>The most tiles a tileset shows; the rest can be typed.</summary>
    public const int MostShown = 512;

    private bool _isOpen;

    public TileNumbersEditor(PropertyField field, TileNumbersAttribute attribute)
        : base(field, attribute)
    {
    }

    /// <summary>True for a field holding one tile (a whole number) rather than several.</summary>
    public bool IsOne => Field.Property.Type == typeof(int);

    /// <summary>The tilemap whose tiles it picks, or null when there's none (they can still be typed).</summary>
    public Tilemap? Map => TilePictures.MapOf(Component);

    /// <summary>True when there's a tilemap to pick from.</summary>
    public bool HasMap => Map is not null;

    /// <summary>Whether the tiles show, to click; shut, just the numbers do.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (!SetProperty(ref _isOpen, value))
                return;
            _tilesets = value ? Build() : [];
            OnPropertyChanged(nameof(Tilesets));
        }
    }

    private IReadOnlyList<TilesetChoices> _tilesets = [];
    private IReadOnlyList<Tileset>? _builtFrom;

    public IRelayCommand OpenCommand => _open ??= new RelayCommand(() => IsOpen = !IsOpen);

    private IRelayCommand? _open;

    /// <summary>The tiles it has now, in order.</summary>
    public IReadOnlyList<int> Picked => IsOne
        ? Field.IsMixed ? [] : Field.Value is int tile && tile >= 0 ? [tile] : []
        : TileNumbersAttribute.Read(Text);

    /// <summary>"3 tiles: 10-11, 20", "Tile 4" or "None", for the closed editor.</summary>
    public string Summary => Field.IsMixed ? "Mixed" : Picked.Count switch
    {
        0 => IsOne ? "None" : "No tiles",
        1 => $"Tile {Picked[0]}",
        var n => $"{n} tiles",
    };

    /// <summary>The map's tilesets, each with its tiles to click (while it's open).</summary>
    public IReadOnlyList<TilesetChoices> Tilesets => _tilesets;

    private IReadOnlyList<TilesetChoices> Build()
    {
        if (Map is not { } map)
            return [];
        var picked = Picked.ToHashSet();
        var fill = TilePictures.Tint(map);
        _builtFrom = map.Tilesets();
        return [.. _builtFrom.Select(t => new TilesetChoices(t.Name, t.First,
            [.. Enumerable.Range(t.First, Math.Min(t.Count, MostShown)).Select(n => new TileChoice(n, TilePictures.Of(Host.Art, map, n), fill, picked.Contains(n), Toggle))],
            Math.Clamp(t.Columns, 1, 16)))];
    }

    /// <summary>
    /// Clicks a tile: on a field of several, adds it or takes it out; on one of one, picks it (and clicking it again
    /// leaves it). One undo step.
    /// </summary>
    public void Toggle(int tile)
    {
        if (tile < 0 || tile >= Tilemap.MaxTiles)
            throw new ArgumentOutOfRangeException(nameof(tile), $"Tiles are numbered 0 to {Tilemap.MaxTiles - 1}.");
        if (IsOne)
        {
            Field.NumberValue = tile;
            Host.Seal();
            return;
        }

        var picked = Picked.ToList();
        if (!picked.Remove(tile))
            picked.Add(tile);
        SetText(FieldValues.WriteTiles(picked));
    }

    public override void Refresh()
    {
        // The map's tilesets changed (one added, an image swapped): show them afresh.
        if (IsOpen && !ReferenceEquals(Map?.Tilesets(), _builtFrom))
            _tilesets = Build();
        var picked = Picked.ToHashSet();
        foreach (var tile in _tilesets.SelectMany(t => t.Tiles))
            tile.IsPicked = picked.Contains(tile.Number);
        base.Refresh();
    }
}

/// <summary>
/// Runs of animated tiles (<see cref="TileAnimationsAttribute"/>), a row each: from which tile to which and how fast, with
/// its frames shown in turn; Add puts a new run after the last.
/// </summary>
public sealed class TileAnimationsEditor : FieldEditor<TileAnimationsAttribute>
{
    public TileAnimationsEditor(PropertyField field, TileAnimationsAttribute attribute)
        : base(field, attribute)
    {
        Refresh();
    }

    /// <summary>The runs, a row each.</summary>
    public ObservableCollection<TileRunRow> Rows { get; } = [];

    /// <summary>The runs as they are.</summary>
    public IReadOnlyList<TileAnimation> Runs => Tilemap.ParseAnimations(Text);

    /// <summary>The tilemap the runs are tiles of, for their pictures.</summary>
    internal Tilemap? Map => TilePictures.MapOf(Component);

    internal Art.EditorArt Art => Host.Art;

    /// <summary>Adds a run of four tiles after the last run's, at 4 a second, as one undo step.</summary>
    public void Add()
    {
        var runs = Runs.ToList();
        var first = runs.Count > 0 ? runs.Max(r => r.Last) + 1 : 0;
        runs.Add(new TileAnimation(first, first + 3, 4));
        SetText(FieldValues.WriteTileAnimations(runs));
    }

    /// <summary>Sets run <paramref name="number"/> (from 1): its first and last tile and frames a second, as one undo step.</summary>
    public void Set(int number, int first, int last, double speed)
    {
        Change(number, _ => new TileAnimation(Math.Min(first, last), Math.Max(first, last), (float)speed), seal: true);
    }

    /// <summary>Takes run <paramref name="number"/> (from 1) out, as one undo step.</summary>
    public void Remove(int number)
    {
        var runs = Runs.ToList();
        Check(number, runs.Count);
        runs.RemoveAt(number - 1);
        SetText(FieldValues.WriteTileAnimations(runs));
    }

    public IRelayCommand AddCommand => _add ??= new RelayCommand(Add);

    private IRelayCommand? _add;

    public override void Refresh()
    {
        Fit(Rows, Runs.Count, () => new TileRunRow(this));
        OnPropertyChanged(string.Empty);
    }

    internal void Change(int number, Func<TileAnimation, TileAnimation> change, bool seal = false)
    {
        var runs = Runs.ToList();
        Check(number, runs.Count);
        var run = change(runs[number - 1]);
        // A run needs two tiles at least, and a speed.
        run = run with { First = Math.Clamp(run.First, 0, Tilemap.MaxTiles - 2), FramesPerSecond = Math.Max(0.1f, run.FramesPerSecond) };
        run = run with { Last = Math.Clamp(run.Last, run.First + 1, Tilemap.MaxTiles - 1) };
        runs[number - 1] = run;
        var text = FieldValues.WriteTileAnimations(runs);
        if (seal)
            SetText(text);
        else
            SetTextLive(text);
    }

    private static void Check(int number, int count)
    {
        if (number < 1 || number > count)
            throw new ArgumentOutOfRangeException(nameof(number), count == 0 ? "There are no runs yet: Add makes one." : $"The runs are numbered 1 to {count}.");
    }
}

/// <summary>One run of a <see cref="TileAnimationsEditor"/>: its first and last tile, its speed and its frames.</summary>
public sealed class TileRunRow(TileAnimationsEditor owner) : EditorRow(owner.Remove)
{
    private TileAnimation Run => owner.Runs.ElementAtOrDefault(Number - 1);

    /// <summary>The run's first tile.</summary>
    public double First
    {
        get => Run.First;
        set => owner.Change(Number, r => r with { First = (int)Math.Round(value), Last = Math.Max(r.Last, (int)Math.Round(value) + 1) });
    }

    /// <summary>The run's last tile.</summary>
    public double Last
    {
        get => Run.Last;
        set => owner.Change(Number, r => r with { Last = (int)Math.Round(value) });
    }

    /// <summary>Frames a second.</summary>
    public double Speed
    {
        get => Run.FramesPerSecond;
        set => owner.Change(Number, r => r with { FramesPerSecond = (float)value });
    }

    /// <summary>Its frames' pictures, first to last (up to 16).</summary>
    public IReadOnlyList<TileChoice> Frames
    {
        get
        {
            var key = (Run.First, Run.Last, owner.Map?.Tilesets());
            if (_frames is null || key != _framesFor)
            {
                _framesFor = key;
                _frames = owner.Map is { } map
                    ? [.. Enumerable.Range(Run.First, Math.Clamp(Run.Last - Run.First + 1, 0, 16)).Select(n => new TileChoice(n, TilePictures.Of(owner.Art, map, n), TilePictures.Tint(map), false))]
                    : [];
            }

            return _frames;
        }
    }

    private IReadOnlyList<TileChoice>? _frames;
    private (int, int, IReadOnlyList<Tileset>?) _framesFor;
}

/// <summary>
/// Runs of 16 tiles that join up with their neighbors (<see cref="AutoTilesAttribute"/>), a row each: its first tile and
/// its 16 pieces shown; Add puts a new run after the last.
/// </summary>
public sealed class AutoTilesEditor : FieldEditor<AutoTilesAttribute>
{
    public AutoTilesEditor(PropertyField field, AutoTilesAttribute attribute)
        : base(field, attribute)
    {
        Refresh();
    }

    /// <summary>The runs, a row each.</summary>
    public ObservableCollection<AutoRunRow> Rows { get; } = [];

    /// <summary>Each run's first tile.</summary>
    public IReadOnlyList<int> Firsts => Tilemap.ParseAutoTiles(Text);

    internal Tilemap? Map => TilePictures.MapOf(Component);

    internal Art.EditorArt Art => Host.Art;

    /// <summary>Adds a run starting at <paramref name="first"/> (or after the last run), as one undo step.</summary>
    public void Add(int? first = null)
    {
        var firsts = Firsts.ToList();
        var start = first ?? (firsts.Count > 0 ? firsts.Max() + 16 : 0);
        if (start < 0 || start + 15 >= Tilemap.MaxTiles)
            throw new ArgumentOutOfRangeException(nameof(first), $"A run is 16 tiles, from 0 to {Tilemap.MaxTiles - 16}.");
        if (!firsts.Contains(start))
            firsts.Add(start);
        SetText(FieldValues.WriteAutoTiles(firsts));
    }

    /// <summary>Starts run <paramref name="number"/> (from 1) at <paramref name="first"/>, as one undo step.</summary>
    public void Set(int number, int first)
    {
        SetFirst(number, first);
        Host.Seal();
    }

    /// <summary>Takes run <paramref name="number"/> (from 1) out, as one undo step.</summary>
    public void Remove(int number)
    {
        var firsts = Firsts.ToList();
        if (number < 1 || number > firsts.Count)
            throw new ArgumentOutOfRangeException(nameof(number), $"The runs are numbered 1 to {firsts.Count}.");
        firsts.RemoveAt(number - 1);
        SetText(FieldValues.WriteAutoTiles(firsts));
    }

    public IRelayCommand AddCommand => _add ??= new RelayCommand(() => Add());

    private IRelayCommand? _add;

    public override void Refresh()
    {
        Fit(Rows, Firsts.Count, () => new AutoRunRow(this));
        OnPropertyChanged(string.Empty);
    }

    internal void SetFirst(int number, int first)
    {
        var firsts = Firsts.ToList();
        if (number < 1 || number > firsts.Count)
            return;
        firsts[number - 1] = Math.Clamp(first, 0, Tilemap.MaxTiles - 16);
        SetTextLive(FieldValues.WriteAutoTiles(firsts));
    }
}

/// <summary>One run of an <see cref="AutoTilesEditor"/>: its first tile and its pieces.</summary>
public sealed class AutoRunRow(AutoTilesEditor owner) : EditorRow(owner.Remove)
{
    /// <summary>The run's first tile; its pieces are it and the 15 after it.</summary>
    public double First
    {
        get => owner.Firsts.ElementAtOrDefault(Number - 1);
        set => owner.SetFirst(Number, (int)Math.Round(value));
    }

    /// <summary>"16-31".</summary>
    public string Range => $"{(int)First}-{(int)First + 15}";

    /// <summary>Its 16 pieces' pictures.</summary>
    public IReadOnlyList<TileChoice> Pieces
    {
        get
        {
            var first = owner.Firsts.ElementAtOrDefault(Number - 1);
            var key = (first, owner.Map?.Tilesets());
            if (_pieces is null || key != _piecesFor)
            {
                _piecesFor = key;
                _pieces = owner.Map is { } map
                    ? [.. Enumerable.Range(first, 16).Select(n => new TileChoice(n, TilePictures.Of(owner.Art, map, n), TilePictures.Tint(map), false))]
                    : [];
            }

            return _pieces;
        }
    }

    private IReadOnlyList<TileChoice>? _pieces;
    private (int, IReadOnlyList<Tileset>?) _piecesFor;
}

/// <summary>
/// A tilemap's tilesets after the first (<see cref="TilesetsAttribute"/>), a row each: its image (picked from the
/// project's), its tiles across and down, and the numbers its tiles get; Add puts an image of the project's on the end,
/// cut up as it was painted (or into tiles the size of the first tileset's).
/// </summary>
public sealed class TilesetsEditor : FieldEditor<TilesetsAttribute>
{
    public TilesetsEditor(PropertyField field, TilesetsAttribute attribute)
        : base(field, attribute)
    {
        Refresh();
    }

    /// <summary>The tilesets, a row each.</summary>
    public ObservableCollection<TilesetRow> Rows { get; } = [];

    /// <summary>The project's images, to pick a tileset from (looked for again every few seconds at most).</summary>
    public IReadOnlyList<string> Images
    {
        get
        {
            if (_images is null || Environment.TickCount64 - _imagesAt > 2000)
                (_images, _imagesAt) = (Host.Art.Images(), Environment.TickCount64);
            return _images;
        }
    }

    private IReadOnlyList<string>? _images;
    private long _imagesAt;

    internal Tilemap? Map => TilePictures.MapOf(Component);

    internal Art.EditorArt Art => Host.Art;

    /// <summary>The lines, blank ones left out.</summary>
    internal List<string> Entries => [.. Lines.Where(l => l.Trim().Length > 0)];

    /// <summary>
    /// Adds <paramref name="image"/> (in Assets) as a tileset on the end, as one undo step: cut into
    /// <paramref name="columns"/> by <paramref name="rows"/> tiles, or without them as it was painted (or into tiles the size
    /// of the first tileset's).
    /// </summary>
    public void Add(string image, int columns = 0, int rows = 0)
    {
        if (string.IsNullOrWhiteSpace(image))
            throw new ArgumentException("Name an image in the project's Assets, like Tiles/props.png.", nameof(image));
        image = image.Trim().Replace('\\', '/');
        if (columns < 1 || rows < 1)
            (columns, rows) = Map is { } map ? Host.Art.TileGrid(image, map) : (1, 1);
        SetLines([.. Entries, new Tileset(image, columns, rows, 0).ToLine()]);
    }

    /// <summary>Takes tileset <paramref name="number"/> (from 1, the first after the map's own) out, as one undo step.</summary>
    public void Remove(int number)
    {
        var entries = Entries;
        if (number < 1 || number > entries.Count)
            throw new ArgumentOutOfRangeException(nameof(number), $"The tilesets are numbered 1 to {entries.Count}.");
        entries.RemoveAt(number - 1);
        SetLines(entries);
    }

    /// <summary>Sets tileset <paramref name="number"/> (from 1): its image and its tiles across and down, as one undo step.</summary>
    public void Set(int number, string image, int columns, int rows)
    {
        Change(number, _ => new Tileset(image.Trim().Replace('\\', '/'), Math.Clamp(columns, 1, 1024), Math.Clamp(rows, 1, 1024), 0), seal: true);
    }

    /// <summary>Adds the image picked from the list on the end (see <see cref="Add"/>).</summary>
    public string? AddImage
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
        Fit(Rows, Entries.Count, () => new TilesetRow(this));
        OnPropertyChanged(string.Empty);
    }

    internal Tileset? TilesetAt(int number) => Entries.ElementAtOrDefault(number - 1) is { } line ? Tileset.Parse(line, 0) : null;

    // Where its tiles start: as the map numbers them (its tilesets after the first, in order).
    internal int FirstTileOf(int number) => Map?.Tilesets().ElementAtOrDefault(number) is { } tileset ? tileset.First : number * Tilemap.TilesetStep;

    internal void Change(int number, Func<Tileset, Tileset> change, bool seal)
    {
        var entries = Entries;
        if (number < 1 || number > entries.Count || Tileset.Parse(entries[number - 1], 0) is not { } tileset)
            return;
        entries[number - 1] = change(tileset).ToLine();
        SetLines(entries, seal);
    }
}

/// <summary>One tileset of a <see cref="TilesetsEditor"/>: its image, its tiles across and down, and their numbers.</summary>
public sealed class TilesetRow(TilesetsEditor owner) : EditorRow(owner.Remove)
{
    private Tileset Tileset => owner.TilesetAt(Number) ?? new Tileset(null, 1, 1, 0);

    /// <summary>Its image in Assets, picked from the project's.</summary>
    public string? Image
    {
        get => Tileset.Image;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
                owner.Change(Number, t => t with { Image = value.Trim().Replace('\\', '/') }, seal: true);
        }
    }

    /// <summary>Tiles across its image.</summary>
    public double Columns
    {
        get => Tileset.Columns;
        set => owner.Change(Number, t => t with { Columns = Math.Clamp((int)Math.Round(value), 1, 1024) }, seal: false);
    }

    /// <summary>Tiles down its image.</summary>
    public double Rows
    {
        get => Tileset.Rows;
        set => owner.Change(Number, t => t with { Rows = Math.Clamp((int)Math.Round(value), 1, 1024) }, seal: false);
    }

    /// <summary>"Tiles 100-115": the numbers its tiles get.</summary>
    public string Numbers
    {
        get
        {
            var first = owner.FirstTileOf(Number);
            return string.Create(CultureInfo.InvariantCulture, $"Tiles {first}-{first + Tileset.Count - 1}");
        }
    }

    /// <summary>Its image, small.</summary>
    public ImageSource? Preview => owner.Art.Thumbnail(Tileset.Image);

    /// <summary>The project's images, to pick from.</summary>
    public IReadOnlyList<string> Images => owner.Images;
}
