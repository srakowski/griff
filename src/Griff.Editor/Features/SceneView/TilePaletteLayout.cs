using System.Windows;
using Griff.Scenes;

namespace Griff.Editor.Features.SceneView;

/// <summary>
/// Where the <see cref="TilePalette"/>'s parts go in the Scene view: a tab for each of the map's tilesets (and a + to add
/// one) along the top, the tiles of the one <see cref="Shown"/> under them in rows (scrolled down <see cref="Scroll"/>
/// rows when they don't all fit), the tools below, and a button for each of the map's layers (and + Layer) under those.
/// </summary>
internal sealed record TilePaletteLayout(Tileset Shown, int Count, int Scroll, int VisibleRows, Rect Panel, double CellsTop,
    IReadOnlyList<(Rect Box, Tileset Tileset)> Tabs, Rect AddTab, IReadOnlyList<(Rect Box, Tilemap Layer)> LayerButtons, Rect AddLayer)
{
    public const double Cell = 28;
    public const string AddLayerLabel = "+ Layer";
    private const double TabHeight = 20;
    private const double ToolButtonWidth = 84;

    /// <summary>Where the tiles start, from the top of the view.</summary>
    public const double TilesTop = 12 + TabHeight + 4;

    /// <summary>The room below the tiles for the tools, the layers and the hint.</summary>
    public const double RoomBelow = 90;

    public int Rows => (Count + TilePalette.Columns - 1) / TilePalette.Columns;

    /// <summary>The palette laid out for the tilemap being painted, with text measured by <paramref name="measure"/>.</summary>
    public static TilePaletteLayout Of(TilePalette palette, Tilemap tilemap, Func<string, Size> measure)
    {
        var shown = palette.ShownTileset ?? tilemap.TilesetOf(palette.BrushTile);
        var count = palette.TileCount;

        var tabs = new List<(Rect, Tileset)>();
        var x = 12.0;
        foreach (var tileset in tilemap.Tilesets())
        {
            var width = measure(tileset.Name).Width + 14;
            tabs.Add((new Rect(x, 12, width, TabHeight), tileset));
            x += width + 2;
        }

        var add = new Rect(x, 12, TabHeight + 4, TabHeight);
        var visible = palette.VisibleRows;
        var panelWidth = Math.Max(Math.Min(count, TilePalette.Columns) * Cell, add.Right - 12) + 8;
        var panel = new Rect(8, 8, panelWidth, TilesTop - 8 + visible * Cell + 4);

        var layerButtons = new List<(Rect, Tilemap)>();
        var (layerX, layerY) = (8.0, panel.Bottom + 4 + 22 + 4);
        foreach (var layer in tilemap.Layers())
        {
            var width = measure(layer.Entity!.Name).Width + 14;
            layerButtons.Add((new Rect(layerX, layerY, width, 22), layer));
            layerX += width + 4;
        }

        var addLayer = new Rect(layerX, layerY, measure(AddLayerLabel).Width + 14, 22);
        return new TilePaletteLayout(shown, count, palette.Scroll, visible, panel, TilesTop, tabs, add, layerButtons, addLayer);
    }

    /// <summary>The box of the shown tileset's tile <paramref name="tile"/>, or null when it isn't one of them or is scrolled out of sight.</summary>
    public Rect? CellBounds(int tile)
    {
        var index = tile - Shown.First;
        var row = index / TilePalette.Columns - Scroll;
        if (index < 0 || index >= Count || row < 0 || row >= VisibleRows)
            return null;
        return new Rect(12 + index % TilePalette.Columns * Cell, CellsTop + row * Cell, Cell, Cell);
    }

    public Rect ToolBounds(TileTool tool) => new(8 + (int)tool * (ToolButtonWidth + 4), Panel.Bottom + 4, ToolButtonWidth, 22);

    /// <summary>The tile at a point, the brush's tile elsewhere on the palette's panel, or null off it.</summary>
    public int? TileAt(Point screen, int brushTile)
    {
        for (var tile = Shown.First; tile < Shown.First + Count; tile++)
        {
            if (CellBounds(tile) is { } box && box.Contains(screen))
                return tile;
        }

        return Panel.Contains(screen) ? brushTile : null;
    }

    /// <summary>The tool whose button is at a point, if any.</summary>
    public TileTool? ToolAt(Point screen) =>
        Enum.GetValues<TileTool>().Cast<TileTool?>().FirstOrDefault(t => ToolBounds(t!.Value).Contains(screen));

    /// <summary>Which layer button is at a point: its place in the map's layers, the count of them for + Layer, or null.</summary>
    public int? LayerAt(Point screen)
    {
        for (var i = 0; i < LayerButtons.Count; i++)
        {
            if (LayerButtons[i].Box.Contains(screen))
                return i;
        }

        return AddLayer.Contains(screen) ? LayerButtons.Count : null;
    }

    /// <summary>Which tileset's tab is at a point, by its place in the map's tilesets, if any.</summary>
    public int? TabAt(Point screen)
    {
        for (var i = 0; i < Tabs.Count; i++)
        {
            if (Tabs[i].Box.Contains(screen))
                return i;
        }

        return null;
    }

    /// <summary>
    /// The screen box of a part, for demos to point at: a tool's button ("Rectangle"), a tile of the shown tileset by its
    /// number ("3", "104"), a tileset's tab by its name ("props"), the + tab that adds one ("+"), a layer's button by its
    /// entity's name ("layer:Level Layer 2"), or the button that adds one ("layer:+"). Null for a tile scrolled out of sight.
    /// </summary>
    public Rect? Bounds(string part)
    {
        // By name only: Enum.TryParse would read "1" as the second tool.
        if (Enum.GetValues<TileTool>().Where(t => t.ToString().Equals(part, StringComparison.OrdinalIgnoreCase)).Cast<TileTool?>().FirstOrDefault() is { } tool)
            return ToolBounds(tool);
        if (part.Trim() == "+")
            return AddTab;
        if (part.Trim().StartsWith("layer:", StringComparison.OrdinalIgnoreCase))
        {
            var name = part.Trim()["layer:".Length..].Trim();
            return name == "+"
                ? AddLayer
                : LayerButtons.Where(l => l.Layer.Entity!.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(l => (Rect?)l.Box).FirstOrDefault();
        }

        if (int.TryParse(part, out var tile))
            return CellBounds(tile);
        return Tabs.Where(t => t.Tileset.Name.Equals(part.Trim(), StringComparison.OrdinalIgnoreCase)).Select(t => (Rect?)t.Box).FirstOrDefault();
    }
}
