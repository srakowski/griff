using System.Numerics;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Rendering;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Transform = Griff.Scenes.Transform;

namespace Griff.Editor.Features.SceneView;

/// <summary>What dragging on a selected tilemap in the Scene view does.</summary>
public enum TileTool
{
    /// <summary>Paint each cell the cursor passes over.</summary>
    Brush,

    /// <summary>Drag out a box and fill every cell in it.</summary>
    Rectangle,

    /// <summary>Click to fill the area around a cell, like a paint bucket: the cells joined to it holding the same tile.</summary>
    Fill,
}

public sealed partial class SceneViewModel
{
    private TileTool _tileTool;
    private int _brushTile;

    /// <summary>
    /// What painting on the selected tilemap does: paint cell by cell (Brush, the default), fill a box (Rectangle) or
    /// fill an area (Fill). B, R and G pick it in the Scene view while a tilemap is selected.
    /// </summary>
    public TileTool TileTool
    {
        get => _tileTool;
        set => SetProperty(ref _tileTool, value);
    }

    /// <summary>
    /// The tile painting puts down (picked from the Scene view's palette): numbered from 0 in the first tileset, and from
    /// 100, 200 and so on in the tilesets after it (see <see cref="Tilemap.MoreTilesets"/>).
    /// </summary>
    public int BrushTile
    {
        get => _brushTile;
        set
        {
            if (value is < 0 or >= Tilemap.MaxTiles)
                throw new ArgumentOutOfRangeException(nameof(value), $"Tiles are numbered 0 to {Tilemap.MaxTiles - 1}.");
            if (SetProperty(ref _brushTile, value))
            {
                if (PaintTarget?.TilesetOf(value) is { } tileset)
                    _lastPicked[tileset.First] = value;
                OnPropertyChanged(nameof(PaletteTileset));
            }
        }
    }

    // The tile last picked from each tileset (by its first tile), so switching back to one picks it again.
    private readonly Dictionary<int, int> _lastPicked = [];

    /// <summary>
    /// The selected tilemap's tileset the Scene view's palette shows: the one the <see cref="BrushTile"/> comes from.
    /// Null with nothing to paint.
    /// </summary>
    public Tileset? PaletteTileset => PaintTarget?.TilesetOf(BrushTile);

    /// <summary>
    /// Shows one of the selected tilemap's tilesets in the palette, by its place in <see cref="Tilemap.Tilesets"/> (0 is
    /// the first): the <see cref="BrushTile"/> becomes the tile last picked from it, or its first tile.
    /// </summary>
    public void ShowTileset(int index)
    {
        var tilesets = (PaintTarget ?? throw NothingToPaint()).Tilesets();
        if (index < 0 || index >= tilesets.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"The tilemap has {tilesets.Count} tileset{(tilesets.Count == 1 ? "" : "s")}, numbered from 0.");
        var tileset = tilesets[index];
        BrushTile = _lastPicked.TryGetValue(tileset.First, out var tile) && tileset.Contains(tile) ? tile : tileset.First;
    }

    /// <summary>
    /// The layers of the tilemap being painted (see <see cref="Tilemap.Layers"/>), by their entities' names: its base map
    /// first, then its layers in the order of the Hierarchy. Empty with nothing to paint.
    /// </summary>
    public IReadOnlyList<string> PaintLayers => PaintTarget?.Layers().Select(l => l.Entity!.Name).ToList() ?? [];

    /// <summary>Which of <see cref="PaintLayers"/> is being painted, or -1 with nothing to paint.</summary>
    public int PaintLayer => PaintTarget is { } tilemap ? IndexOf(tilemap.Layers(), tilemap) : -1;

    private static int IndexOf(IReadOnlyList<Tilemap> layers, Tilemap layer)
    {
        for (var i = 0; i < layers.Count; i++)
        {
            if (layers[i] == layer)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Paints another layer of the tilemap being painted, by its place in <see cref="PaintLayers"/> (0 is the base map):
    /// selects its entity, so painting goes there and the Inspector shows its settings.
    /// </summary>
    public void ShowLayer(int index)
    {
        var layers = (PaintTarget ?? throw NothingToPaint()).Layers();
        if (index < 0 || index >= layers.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"The tilemap has {layers.Count} layer{(layers.Count == 1 ? "" : "s")}, numbered from 0.");
        selection.Select(layers[index].Entity);
    }

    /// <summary>
    /// Adds a layer to the tilemap being painted, as one undo step: an entity inside the base map's, named after it
    /// ("Level Layer 2"), holding a tilemap with no image of its own, so it paints with the base map's tilesets (see
    /// <see cref="Tilemap.LayerOf"/>). It draws over the other layers and doesn't collide (turn Solid on for walls).
    /// The new layer is selected, so painting goes there. Returns its name.
    /// </summary>
    public string AddLayer()
    {
        var target = PaintTarget ?? throw NothingToPaint();
        if (workspace.Scene is not { } document)
            throw NothingToPaint();
        var baseMap = target.BaseMap;
        var layers = target.Layers();
        var owner = baseMap.Entity!;
        var names = document.Scene.Entities.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var number = layers.Count + 1;
        while (names.Contains($"{owner.Name} Layer {number}"))
            number++;

        var entity = new Entity($"{owner.Name} Layer {number}");
        var layer = entity.AddComponent<Tilemap>();
        (layer.Order, layer.Solid, layer.Tint) = (layers.Max(l => l.Order) + 1, false, baseMap.Tint);
        document.History.Seal();
        document.Execute(new AddEntityEdit(document.Scene, entity, owner));
        document.History.Seal();
        selection.Select(entity);
        return entity.Name;
    }

    /// <summary>
    /// Adds a tileset to the selected tilemap, as one undo step: <paramref name="image"/> (relative to Assets) cut into
    /// <paramref name="columns"/> by <paramref name="rows"/> tiles. A map with no image yet takes it as its first
    /// (<see cref="Tilemap.Image"/>); otherwise it goes on the end of <see cref="Tilemap.MoreTilesets"/>, numbered from
    /// the next hundred. An image the map already uses isn't added again. Either way the palette shows it after. On a
    /// layer it goes on the map the layer is part of (<see cref="Tilemap.BaseMap"/>), so every layer paints with it.
    /// Returns its first tile.
    /// </summary>
    public int AddTileset(string image, int columns, int rows)
    {
        var tilemap = (PaintTarget ?? throw NothingToPaint()).BaseMap;
        if (string.IsNullOrWhiteSpace(image))
            throw new ArgumentException("Name an image in the project's Assets, like Tiles/props.png.", nameof(image));
        if (workspace.Scene is not { } document)
            throw NothingToPaint();
        image = image.Trim().Replace('\\', '/');
        (columns, rows) = (Math.Clamp(columns, 1, 1024), Math.Clamp(rows, 1, 1024));

        var index = IndexOfTileset(tilemap, image);
        if (index < 0)
        {
            var edits = new List<IEdit>();
            if (string.IsNullOrWhiteSpace(tilemap.Image))
            {
                edits.Add(new SetPropertyEdit(tilemap, Property(tilemap, nameof(Tilemap.Image)), image));
                edits.Add(new SetPropertyEdit(tilemap, Property(tilemap, nameof(Tilemap.Columns)), columns));
                edits.Add(new SetPropertyEdit(tilemap, Property(tilemap, nameof(Tilemap.Rows)), rows));
                // Colored squares standing in for art: the picture shows in its own colors.
                if (tilemap.Tint != Griff.Color.White)
                    edits.Add(new SetPropertyEdit(tilemap, Property(tilemap, nameof(Tilemap.Tint)), Griff.Color.White));
            }
            else
            {
                IReadOnlyList<string> lines = [.. tilemap.MoreTilesets.Where(l => l.Trim().Length > 0), new Tileset(image, columns, rows, 0).ToLine()];
                edits.Add(new SetPropertyEdit(tilemap, Property(tilemap, nameof(Tilemap.MoreTilesets)), lines));
            }

            document.History.Seal();
            document.Execute(new CompositeEdit($"Add Tileset {image}", edits));
            document.History.Seal();
            index = IndexOfTileset(tilemap, image);
        }

        if (index < 0)
            throw new InvalidOperationException($"The tilemap has no room for another tileset: tiles are numbered up to {Tilemap.MaxTiles - 1}.");
        ShowTileset(index);
        return tilemap.Tilesets()[index].First;
    }

    private static int IndexOfTileset(Tilemap tilemap, string image) =>
        tilemap.Tilesets().ToList().FindIndex(t => string.Equals(t.Image, image, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The tilemap the Scene view paints: the selected entity's, when it's the only one selected, in the shown scene and
    /// enabled. Null otherwise (then the Scene view selects and moves as usual).
    /// </summary>
    public Tilemap? PaintTarget =>
        selection.Entities is [var entity] && entity.Scene == workspace.Scene?.Scene && SceneRenderer.IsActive(entity)
            ? entity.GetComponent<Tilemap>()
            : null;

    /// <summary>The cell of the <see cref="PaintTarget"/> at a point in the world (0, 0 is its top-left cell), or null with nothing to paint.</summary>
    public (int X, int Y)? CellAt(Vector2 world)
    {
        if (PaintTarget is not { } tilemap || !Matrix4x4.Invert(tilemap.Entity!.Transform.WorldMatrix, out var inverse))
            return null;
        var local = Vector3.Transform(new Vector3(world, 0), inverse);
        return tilemap.CellAt(new Vector2(local.X, local.Y));
    }

    /// <summary>
    /// Paints the <see cref="BrushTile"/> (or clears, with <paramref name="erase"/>) into one cell of the selected
    /// tilemap, as one undo step. Cells left of or above the grid grow it, moving the entity so its tiles stay put.
    /// </summary>
    public void PaintTile(int x, int y, bool erase = false)
    {
        var stroke = BeginTileStroke(erase) ?? throw NothingToPaint();
        stroke.To(x, y);
        stroke.End();
    }

    /// <summary>
    /// Fills the rectangle between two corner cells of the selected tilemap (either way round) with the
    /// <see cref="BrushTile"/>, or clears it with <paramref name="erase"/>, as one undo step. Like painting, a rectangle
    /// reaching past the left or top edge grows the grid that way.
    /// </summary>
    public void PaintRectangle(int fromX, int fromY, int toX, int toY, bool erase = false)
    {
        var tilemap = PaintTarget ?? throw NothingToPaint();
        var (tiles, shiftX, shiftY) = tilemap.PaintedRect(fromX, fromY, toX, toY, erase ? -1 : BrushTile);
        Apply(tilemap, tiles, shiftX, shiftY, erase ? "Erase Tiles" : "Fill Rectangle");
    }

    /// <summary>
    /// Fills the area around a cell of the selected tilemap with the <see cref="BrushTile"/>, or clears it with
    /// <paramref name="erase"/>, as one undo step: the cell and every cell joined to it side by side holding the same
    /// tile (or all empty), within the grid. Returns how many cells changed; nothing does outside the grid.
    /// </summary>
    public int FillTiles(int x, int y, bool erase = false)
    {
        var tilemap = PaintTarget ?? throw NothingToPaint();
        var tile = erase ? -1 : BrushTile;
        if (tilemap.GetTile(x, y) == tile)
            return 0;
        var count = tilemap.FillRegion(x, y).Count;
        Apply(tilemap, tilemap.Filled(x, y, tile), 0, 0, erase ? "Erase Tiles" : "Fill Tiles");
        return count;
    }

    /// <summary>Makes the tile in a cell of the selected tilemap (the layer being painted) the <see cref="BrushTile"/>; false (and no change) for an empty cell.</summary>
    public bool PickTile(int x, int y)
    {
        var tilemap = PaintTarget ?? throw NothingToPaint();
        if (tilemap.GetTile(x, y) is not (var tile and >= 0))
            return false;
        BrushTile = tile;
        return true;
    }

    /// <summary>
    /// Starts a brush stroke on the selected tilemap, for a drag: <see cref="TileStroke.To"/> paints every cell on the
    /// way to a cell, and the whole stroke is one undo step (until <see cref="TileStroke.End"/>, undo is held up, so this
    /// is for the Scene view's drags only; agents paint with <see cref="PaintTile"/>). Null with no tilemap to paint.
    /// </summary>
    internal TileStroke? BeginTileStroke(bool erase = false) =>
        workspace.Scene is { } document && PaintTarget is { } tilemap
            ? new TileStroke(document, tilemap, erase ? -1 : BrushTile, Property(tilemap, nameof(Tilemap.Tiles)), Property(tilemap.Entity!.Transform, nameof(Transform.Position)))
            : null;

    // Puts new tiles on the map, moving the entity back by the cells added left of and above the old grid, as one step.
    private void Apply(Tilemap tilemap, IReadOnlyList<string> tiles, int shiftX, int shiftY, string description)
    {
        if (workspace.Scene is not { } document || tiles.SequenceEqual(tilemap.Tiles))
            return;
        document.History.Seal();
        using (document.History.BeginGroup(description))
            TileStroke.Put(document, tilemap, tiles, shiftX, shiftY, Property(tilemap, nameof(Tilemap.Tiles)), Property(tilemap.Entity!.Transform, nameof(Transform.Position)));
        document.History.Seal();
    }

    private static InvalidOperationException NothingToPaint() =>
        new("Select one tilemap (and nothing else) in the open scene to paint on it.");

    /// <summary>One brush stroke on a tilemap, from <see cref="BeginTileStroke"/>.</summary>
    public sealed class TileStroke
    {
        private readonly SceneDocument _document;
        private readonly Tilemap _tilemap;
        private readonly int _tile;
        private readonly PropertyDescriptor _tiles;
        private readonly PropertyDescriptor _position;
        private readonly IDisposable _group;
        private (int X, int Y)? _last;

        internal TileStroke(SceneDocument document, Tilemap tilemap, int tile, PropertyDescriptor tiles, PropertyDescriptor position)
        {
            _document = document;
            _tilemap = tilemap;
            _tile = tile;
            _tiles = tiles;
            _position = position;
            document.History.Seal();
            _group = document.History.BeginGroup(tile < 0 ? "Erase Tiles" : "Paint Tiles");
        }

        /// <summary>True if this stroke clears cells rather than painting them.</summary>
        public bool Erasing => _tile < 0;

        /// <summary>
        /// The last cell painted, in the grid as it is now (painting left of or above the grid grows it, which moves
        /// the cells along), or null before the first.
        /// </summary>
        public (int X, int Y)? Last => _last;

        /// <summary>
        /// Paints every cell on the line from the last cell painted to (<paramref name="x"/>, <paramref name="y"/>), so
        /// fast drags don't leave gaps. Cells are counted in the grid as it was when this cell was reached: if earlier
        /// cells grew the grid, the caller's cell (found from the cursor) already counts from the new edge.
        /// </summary>
        public void To(int x, int y)
        {
            var (fromX, fromY) = _last ?? (x, y);
            var steps = Math.Max(Math.Abs(x - fromX), Math.Abs(y - fromY));
            var shiftX = 0;
            var shiftY = 0;
            for (var i = 0; i <= steps; i++)
            {
                var t = steps == 0 ? 1 : (double)i / steps;
                var cx = (int)Math.Round(fromX + (x - fromX) * t) + shiftX;
                var cy = (int)Math.Round(fromY + (y - fromY) * t) + shiftY;
                if (_tilemap.GetTile(cx, cy) == _tile)
                    continue;
                var (tiles, sx, sy) = _tilemap.Painted(cx, cy, _tile);
                Put(_document, _tilemap, tiles, sx, sy, _tiles, _position);
                shiftX += sx;
                shiftY += sy;
            }

            _last = (x + shiftX, y + shiftY);
        }

        /// <summary>Ends the stroke, making it one undo step.</summary>
        public void End()
        {
            _group.Dispose();
            _document.History.Seal();
        }

        internal static void Put(SceneDocument document, Tilemap tilemap, IReadOnlyList<string> tiles, int shiftX, int shiftY,
            PropertyDescriptor tilesProperty, PropertyDescriptor positionProperty)
        {
            document.Execute(new SetPropertyEdit(tilemap, tilesProperty, tiles));
            if (shiftX == 0 && shiftY == 0)
                return;
            var transform = tilemap.Entity!.Transform;
            var shift = new Vector3(-shiftX * tilemap.TileSize, shiftY * tilemap.TileSize, 0);
            document.Execute(new SetPropertyEdit(transform, positionProperty, transform.Position + Vector3.TransformNormal(shift, transform.LocalMatrix)));

            // What's inside it (its layers, and anything else) stays where it was, so the layers stay lined up.
            foreach (var child in tilemap.Entity.Children)
                document.Execute(new SetPropertyEdit(child.Transform, positionProperty, child.Transform.Position - shift));
        }
    }
}
