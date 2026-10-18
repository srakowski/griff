using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Scenes;

namespace Griff.Editor.Features.SceneView;

/// <summary>
/// The Scene view's tile palette, at its top left while a tilemap is selected to paint: a tab for each of the map's
/// tilesets (<see cref="Tilesets"/>, the one <see cref="Shown"/> picked out), that tileset's tiles in rows of eight
/// (scrolled down <see cref="Scroll"/> rows when they don't all fit), the tools (<see cref="Tool"/>), and a button for each
/// of the map's layers (<see cref="Layers"/>). Picking a tile, tool, tileset or layer here is the same as clicking it.
/// With the Select tool picked it stays up, but clicks on the map select and move entities instead of painting.
/// Painting itself is <see cref="SceneViewModel"/>'s (PaintTile, PaintRectangle, FillTiles), and Art > Add Tileset to
/// Tilemap is the palette's + tab.
/// </summary>
public sealed class TilePalette : ObservableObject
{
    /// <summary>How many tiles each row of the palette holds.</summary>
    public const int Columns = 8;

    private readonly SceneViewModel _model;
    private int _scroll;
    private int? _scrollOf;
    private int _availableRows = 2;

    public TilePalette(SceneViewModel model)
    {
        _model = model;
        model.PropertyChanged += OnModelChanged;
    }

    /// <summary>True while a tilemap is selected to paint, so the palette shows.</summary>
    public bool IsShowing => _model.PaintTarget is not null;

    /// <summary>The names of the tilemap's tilesets, as its tabs show them ("Tiles", "props"); empty with nothing to paint.</summary>
    public IReadOnlyList<string> Tilesets => _model.PaintTarget?.Tilesets().Select(t => t.Name).ToList() ?? [];

    /// <summary>Which of <see cref="Tilesets"/> the palette shows (the one the brush's tile is from), or -1 with nothing to paint.</summary>
    public int Shown => _model.PaintTarget is { } tilemap && _model.PaletteTileset is { } shown ? tilemap.Tilesets().ToList().IndexOf(shown) : -1;

    /// <summary>The numbers of the tiles the palette shows, those of the <see cref="Shown"/> tileset; empty with nothing to paint.</summary>
    public IReadOnlyList<int> Tiles => ShownTileset is { } shown ? Enumerable.Range(shown.First, TileCount).ToList() : [];

    /// <summary>
    /// The colors <see cref="Tiles"/> show in when the shown tileset has no image, as hex: each tile's placeholder color
    /// (see <see cref="Tilemap.PlaceholderColors"/>) times the map's tint, as the map draws it; empty when it has one.
    /// </summary>
    public IReadOnlyList<string> TileColors => ShownTileset is { Image: null } ? [.. Tiles.Select(t => ColorOf(t).ToHex())] : [];

    /// <summary>The color tile <paramref name="tile"/> shows in without a picture: its placeholder color times the map's tint, opaque.</summary>
    internal Color ColorOf(int tile)
    {
        if (Target is not { } map)
            return Color.White;
        var (tint, color) = (map.Tint, map.PlaceholderColorOf(tile));
        return new Color(tint.R * color.R, tint.G * color.G, tint.B * color.B);
    }

    /// <summary>How many rows the shown tiles take, eight to a row.</summary>
    public int Rows => (TileCount + Columns - 1) / Columns;

    /// <summary>
    /// How many rows of tiles the Scene view has room for above the tools, layers and hint (at least two); the rest
    /// scroll. The Scene view sets it as its height changes.
    /// </summary>
    public int AvailableRows
    {
        get => _availableRows;
        internal set
        {
            if (SetProperty(ref _availableRows, Math.Max(2, value)))
                OnPropertyChanged(nameof(VisibleRows));
        }
    }

    /// <summary>How many rows of tiles are in sight at once.</summary>
    public int VisibleRows => Math.Min(Rows, AvailableRows);

    /// <summary>
    /// How many rows the tiles are scrolled down (the mouse wheel over the palette scrolls them). Showing another tileset
    /// scrolls to the brush's tile in it.
    /// </summary>
    public int Scroll
    {
        get
        {
            Sync();
            return _scroll;
        }
        set
        {
            Sync();
            if (SetProperty(ref _scroll, Math.Clamp(value, 0, Math.Max(0, Rows - VisibleRows))))
                OnPropertyChanged(nameof(VisibleTiles));
        }
    }

    /// <summary>The tiles in sight, given <see cref="Scroll"/>.</summary>
    public IReadOnlyList<int> VisibleTiles => Tiles.Skip(Scroll * Columns).Take(VisibleRows * Columns).ToList();

    /// <summary>The tile painting puts down (see <see cref="SceneViewModel.BrushTile"/>).</summary>
    public int BrushTile
    {
        get => _model.BrushTile;
        set => _model.BrushTile = value;
    }

    /// <summary>
    /// How the brush turns or flips its tile as it paints (see <see cref="SceneViewModel.BrushTurn"/>): Z turns it, X
    /// flips it. The palette draws the brush's tile turned, and the cell under the cursor shows it.
    /// </summary>
    public TileTurn Turn
    {
        get => _model.BrushTurn;
        set => _model.BrushTurn = value;
    }

    /// <summary>The brush's turn in words for the hint, "turned 90°", "flipped" or "flipped, turned 90°"; empty for none.</summary>
    public string TurnText => Turn == TileTurn.None ? ""
        : Turn == TileTurn.FlipY ? "flipped upside down"
        : string.Join(", ", new[] { Turn.IsFlipped() ? "flipped" : "", Turn.Quarters() > 0 ? $"turned {Turn.Quarters() * 90}°" : "" }.Where(t => t.Length > 0));

    /// <summary>The tools under the tiles, in order: Select (V), Brush (B), Rectangle (R), Fill (G) and Line (L).</summary>
    public IReadOnlyList<TileTool> Tools => Enum.GetValues<TileTool>();

    /// <summary>The tool picked (see <see cref="SceneViewModel.TileTool"/>).</summary>
    public TileTool Tool
    {
        get => _model.TileTool;
        set => _model.TileTool = value;
    }

    /// <summary>The layers of the tilemap being painted, by their entities' names, as the buttons under the tools show them.</summary>
    public IReadOnlyList<string> Layers => _model.PaintLayers;

    /// <summary>Which of <see cref="Layers"/> is being painted, or -1 with nothing to paint.</summary>
    public int Layer => _model.PaintLayer;

    /// <summary>The line under the palette saying what the tool does and the keys that help.</summary>
    public string Hint => Tool switch
    {
        TileTool.Rectangle => $"Rectangle{Turned}: drag a box to fill · Shift erase · Ctrl pick · [ ] tile · Z X turn · Esc done",
        TileTool.Fill => $"Fill{Turned}: click an area to fill it · Shift erase · Ctrl pick · [ ] tile · Z X turn · Esc done",
        TileTool.Line => $"Line{Turned}: drag a line round a corner · Z turns a cell, or the corner while dragging · Shift erase · Ctrl pick · Esc done",
        TileTool.Select => "Select: click or drag to select and move · B R G L paint again · Esc done",
        _ => $"Brush{Turned}: drag to paint · Shift erase · Ctrl pick · [ ] tile · Z X turn · Esc done",
    };

    // The brush's turn after the tool's name in the hint: " (turned 90°)".
    private string Turned => Turn == TileTurn.None ? "" : $" ({TurnText})";

    /// <summary>The tilemap being painted (see <see cref="SceneViewModel.PaintTarget"/>).</summary>
    internal Tilemap? Target => _model.PaintTarget;

    /// <summary>The shown tileset, or null with nothing to paint.</summary>
    internal Tileset? ShownTileset => _model.PaletteTileset;

    /// <summary>
    /// How many tiles the shown tileset has (without an image, one, or one for each of the map's
    /// <see cref="Tilemap.PlaceholderColors"/>), or 0 with nothing to paint.
    /// </summary>
    internal int TileCount => ShownTileset is not { } shown ? 0
        : shown.Image is null && Target?.BaseMap.PlaceholderColors.Count is not > 0 ? 1 : Math.Clamp(shown.Count, 1, Tilemap.MaxTiles - shown.First);

    /// <summary>The key that picks a tool in the Scene view.</summary>
    public static char KeyOf(TileTool tool) => tool switch
    {
        TileTool.Select => 'V',
        TileTool.Rectangle => 'R',
        TileTool.Fill => 'G',
        TileTool.Line => 'L',
        _ => 'B',
    };

    /// <summary>Picks a tile to paint with, as clicking it does.</summary>
    public void Pick(int tile) => BrushTile = tile;

    /// <summary>
    /// Picks the next tile of the shown tileset (or with a negative <paramref name="by"/>, an earlier one), going round
    /// from its last to its first, and scrolls to it: what ] and [ do.
    /// </summary>
    public void Step(int by)
    {
        if (ShownTileset is not { } shown)
            throw new InvalidOperationException("Select a tilemap (and nothing else) to pick its tiles.");
        var count = TileCount;
        BrushTile = shown.First + ((BrushTile - shown.First + by) % count + count) % count;
        ScrollTo(BrushTile);
    }

    /// <summary>Scrolls just enough to bring a tile of the shown tileset into sight.</summary>
    public void ScrollTo(int tile)
    {
        if (ShownTileset is not { } shown)
            return;
        var row = (tile - shown.First) / Columns;
        if (row < Scroll)
            Scroll = row;
        else if (row >= Scroll + VisibleRows)
            Scroll = row - VisibleRows + 1;
    }

    /// <summary>Shows one of the tilemap's tilesets, by its place in <see cref="Tilesets"/>, as clicking its tab does.</summary>
    public void ShowTileset(int index) => _model.ShowTileset(index);

    /// <summary>Paints another of the map's layers, by its place in <see cref="Layers"/>, as clicking its button does.</summary>
    public void ShowLayer(int index) => _model.ShowLayer(index);

    /// <summary>Adds a layer to the map and paints it, as + Layer does (one undo step). Returns its entity's name.</summary>
    public string AddLayer() => _model.AddLayer();

    // A tileset shown for the first time (or again after another) opens scrolled to the brush's tile, roughly in the
    // middle; any scroll stays within the rows there are.
    private void Sync()
    {
        if (ShownTileset is not { } shown)
            return;
        if (_scrollOf != shown.First)
            (_scroll, _scrollOf) = ((BrushTile - shown.First) / Columns - VisibleRows / 2, shown.First);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, Rows - VisibleRows));
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SceneViewModel.TileTool):
                OnPropertyChanged(nameof(Tool));
                OnPropertyChanged(nameof(Hint));
                break;
            case nameof(SceneViewModel.BrushTile):
                OnPropertyChanged(nameof(BrushTile));
                break;
            case nameof(SceneViewModel.BrushTurn):
                OnPropertyChanged(nameof(Turn));
                OnPropertyChanged(nameof(TurnText));
                OnPropertyChanged(nameof(Hint));
                break;
            case nameof(SceneViewModel.PaletteTileset):
                OnPropertyChanged(nameof(Shown));
                OnPropertyChanged(nameof(Tiles));
                OnPropertyChanged(nameof(Scroll));
                break;
        }
    }
}
