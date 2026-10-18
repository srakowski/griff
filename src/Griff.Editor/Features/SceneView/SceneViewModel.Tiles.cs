using System.Numerics;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Transform = Griff.Scenes.Transform;

namespace Griff.Editor.Features.SceneView;

/// <summary>What dragging on a selected tilemap in the Scene view does.</summary>
public enum TileTool
{
    /// <summary>
    /// Don't paint: clicks and drags select and move entities as they do with no tilemap selected, while the palette stays
    /// up to pick a painting tool again.
    /// </summary>
    Select,

    /// <summary>Paint each cell the cursor passes over.</summary>
    Brush,

    /// <summary>Drag out a box and fill every cell in it.</summary>
    Rectangle,

    /// <summary>Click to fill the area around a cell, like a paint bucket: the cells joined to it holding the same tile.</summary>
    Fill,

    /// <summary>
    /// Drag a line round a corner, each cell pointing the way the line runs: laid with the tilemap's
    /// <see cref="LineTiles"/> (a straight piece and a corner, turned to follow it), or without them the brush's tile.
    /// </summary>
    Line,
}

public sealed partial class SceneViewModel
{
    private TileTool _tileTool = TileTool.Brush;
    private int _brushTile;
    private TileTurn _brushTurn;

    /// <summary>
    /// What clicking and dragging on the selected tilemap in the Scene view does: paint cell by cell (Brush, the
    /// default), fill a box (Rectangle), fill an area (Fill), lay a line round a corner (Line), or select and move
    /// entities as usual (Select, the cursor). V, B, R, G and L pick it in the Scene view while a tilemap is selected. It stays as it's left while other things are
    /// selected, so a tilemap selected again with Select picked doesn't paint until a painting tool is picked.
    /// </summary>
    public TileTool TileTool
    {
        get => _tileTool;
        set
        {
            if (SetProperty(ref _tileTool, value))
                OnPropertyChanged(nameof(IsPainting));
        }
    }

    /// <summary>
    /// True while clicks and drags in the Scene view paint: there's a tilemap to paint (<see cref="PaintTarget"/>) and
    /// the <see cref="TileTool"/> isn't Select. Otherwise they select, move and work the handles as usual.
    /// </summary>
    public bool IsPainting => PaintTarget is not null && TileTool != TileTool.Select;

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

    /// <summary>
    /// How painting turns or flips the <see cref="BrushTile"/> as it lays it (see <see cref="Tilemap.Turns"/>): None,
    /// the default, lays tiles as they're drawn. Z in the Scene view turns it a quarter clockwise (Shift+Z back), X
    /// flips it left to right and Shift+X upside down, and Ctrl-clicking a painted cell picks its turn with its tile. The
    /// cell under the cursor shows the tile turned.
    /// </summary>
    public TileTurn BrushTurn
    {
        get => _brushTurn;
        set => SetProperty(ref _brushTurn, (TileTurn)((int)value & 7));
    }

    /// <summary>Turns the <see cref="BrushTurn"/> a quarter clockwise, or with a negative <paramref name="quarters"/> back: what Z (Shift+Z) does.</summary>
    public void TurnBrush(int quarters = 1) => BrushTurn = BrushTurn.Turned(quarters);

    /// <summary>Flips the <see cref="BrushTurn"/> left to right, or upside down with <paramref name="upsideDown"/>, as it looks: what X (Shift+X) does.</summary>
    public void FlipBrush(bool upsideDown = false) => BrushTurn = upsideDown ? BrushTurn.FlippedY() : BrushTurn.FlippedX();

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
    /// selects its entity, so painting goes there and the Inspector shows its settings. A layer hidden in the Scene view
    /// is shown again (one undo step), since it can't be painted unseen.
    /// </summary>
    public void ShowLayer(int index)
    {
        var layers = (PaintTarget ?? throw NothingToPaint()).Layers();
        if (index < 0 || index >= layers.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"The tilemap has {layers.Count} layer{(layers.Count == 1 ? "" : "s")}, numbered from 0.");
        var entity = layers[index].Entity!;
        if (entity.IsHidden)
            sceneDocuments.Scene!.Execute(new SetEntitiesHiddenEdit([entity], false));
        selection.Select(entity);
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
        if (sceneDocuments.Scene is not { } document)
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
        if (sceneDocuments.Scene is not { } document)
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
    /// The tilemap the Scene view paints: the selected entity's, when it's the only one selected, in the shown scene,
    /// enabled and not hidden. Null otherwise (then the Scene view selects and moves as usual). The palette shows while
    /// there is one; whether clicks paint it is <see cref="IsPainting"/> (not with the Select tool).
    /// </summary>
    public Tilemap? PaintTarget =>
        selection.Entities is [var entity] && entity.Scene == sceneDocuments.Scene?.Scene && IsShown(entity)
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
    /// Paints the <see cref="BrushTile"/>, turned by the <see cref="BrushTurn"/> (or clears, with <paramref name="erase"/>),
    /// into one cell of the selected tilemap, as one undo step. Cells left of or above the grid grow it, moving the entity so its tiles stay put.
    /// </summary>
    public void PaintTile(int x, int y, bool erase = false)
    {
        var stroke = BeginTileStroke(erase) ?? throw NothingToPaint();
        stroke.To(x, y);
        stroke.End();
    }

    /// <summary>
    /// Fills the rectangle between two corner cells of the selected tilemap (either way round) with the
    /// <see cref="BrushTile"/> turned by the <see cref="BrushTurn"/>, or clears it with <paramref name="erase"/>, as one undo step. Like painting, a rectangle
    /// reaching past the left or top edge grows the grid that way.
    /// </summary>
    public void PaintRectangle(int fromX, int fromY, int toX, int toY, bool erase = false)
    {
        var tilemap = PaintTarget ?? throw NothingToPaint();
        var (tiles, turns, shiftX, shiftY) = tilemap.PaintedRect(fromX, fromY, toX, toY, erase ? -1 : BrushTile, BrushTurn);
        Apply(tilemap, tiles, turns, shiftX, shiftY, erase ? "Erase Tiles" : "Fill Rectangle");
    }

    /// <summary>
    /// Fills the area around a cell of the selected tilemap with the <see cref="BrushTile"/> turned by the
    /// <see cref="BrushTurn"/>, or clears it with
    /// <paramref name="erase"/>, as one undo step: the cell and every cell joined to it side by side holding the same
    /// tile (or all empty), within the grid. Returns how many cells changed; nothing does outside the grid.
    /// </summary>
    public int FillTiles(int x, int y, bool erase = false)
    {
        var tilemap = PaintTarget ?? throw NothingToPaint();
        var (tile, turn) = erase ? (-1, TileTurn.None) : (BrushTile, BrushTurn);
        if (tilemap.GetTile(x, y) == tile && tilemap.GetTurn(x, y) == turn)
            return 0;
        var count = tilemap.FillRegion(x, y).Count;
        var (tiles, turns) = tilemap.Filled(x, y, tile, turn);
        Apply(tilemap, tiles, turns, 0, 0, erase ? "Erase Tiles" : "Fill Tiles");
        return count;
    }

    /// <summary>
    /// Lays a line on the selected tilemap from one cell to another, as the Line tool's drag does, as one undo step: along
    /// the row first, then up or down the column (or the column first, with <paramref name="acrossFirst"/> false), each
    /// cell's tile turned to point the way the line runs. With <see cref="LineTiles"/> on the tilemap (or the map it's a
    /// layer of) it lays their straight piece, corners at the corner and their start and end pieces at the ends; without,
    /// the <see cref="BrushTile"/> pointing along the line. A single cell points the way the <see cref="BrushTurn"/> turns
    /// a tile drawn pointing right (Z turns it). <paramref name="erase"/> clears the line's cells instead. Like painting,
    /// a line reaching past the left or top edge grows the grid that way. Returns the cells it laid, in order.
    /// </summary>
    public IReadOnlyList<LineCell> PaintLine(int fromX, int fromY, int toX, int toY, bool acrossFirst = true, bool erase = false)
    {
        var line = LineDrag.Path((fromX, fromY), (toX, toY), acrossFirst, (fromX, fromY) == (toX, toY) ? SingleWay : null);
        PaintLine(line, erase);
        return line;
    }

    // The way a single cell of a line points: where the brush's turn takes a tile drawn pointing right.
    internal (int X, int Y) SingleWay => BrushTurn.Apply(Vector2.UnitX) is var way ? ((int)MathF.Round(way.X), (int)MathF.Round(way.Y)) : (1, 0);

    // Lays the cells of a line (from PaintLine, or the Line tool's drag), as one undo step.
    internal void PaintLine(IReadOnlyList<LineCell> line, bool erase)
    {
        var tilemap = PaintTarget ?? throw NothingToPaint();
        if (sceneDocuments.Scene is not { } document)
            throw NothingToPaint();
        document.History.Seal();
        using (document.History.BeginGroup(erase ? "Erase Line" : "Paint Line"))
        {
            // Cells painted left of or above the grid grow it, moving the cells after them along.
            var (shiftX, shiftY) = (0, 0);
            for (var i = 0; i < line.Count; i++)
            {
                var (tile, turn) = erase ? (-1, TileTurn.None) : LineTile(tilemap, line, i);
                var (x, y) = (line[i].X + shiftX, line[i].Y + shiftY);
                if (tilemap.GetTile(x, y) == tile && tilemap.GetTurn(x, y) == turn)
                    continue;
                var (tiles, turns, sx, sy) = tilemap.PaintedRect(x, y, x, y, tile, turn);
                TileStroke.Put(document, projectSession.Components, tilemap, tiles, turns, sx, sy);
                (shiftX, shiftY) = (shiftX + sx, shiftY + sy);
            }
        }

        document.History.Seal();
    }

    /// <summary>
    /// The tile, turned, that the Line tool lays in cell <paramref name="index"/> of <paramref name="line"/> on
    /// <paramref name="tilemap"/>: its <see cref="LineTiles"/>' piece for it, or the <see cref="BrushTile"/> pointing the
    /// way the line runs.
    /// </summary>
    public (int Tile, TileTurn Turn) LineTile(Tilemap tilemap, IReadOnlyList<LineCell> line, int index)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (LineTiles.For(tilemap) is { } pieces && pieces.TileFor(line, index) is (>= 0, _) piece)
            return piece;
        return (BrushTile, TileTurns.Pointing(line[index].WayX, line[index].WayY));
    }

    /// <summary>
    /// Makes the tile in a cell of the selected tilemap (the layer being painted) the <see cref="BrushTile"/>, and its
    /// turn the <see cref="BrushTurn"/>; false (and no change) for an empty cell.
    /// </summary>
    public bool PickTile(int x, int y)
    {
        var tilemap = PaintTarget ?? throw NothingToPaint();
        if (tilemap.GetTile(x, y) is not (var tile and >= 0))
            return false;
        BrushTile = tile;
        BrushTurn = tilemap.GetTurn(x, y);
        return true;
    }

    /// <summary>
    /// Starts a brush stroke on the selected tilemap, for a drag: <see cref="TileStroke.To"/> paints every cell on the
    /// way to a cell, and the whole stroke is one undo step (until <see cref="TileStroke.End"/>, undo is held up, so this
    /// is for the Scene view's drags only; agents paint with <see cref="PaintTile"/>). Null with no tilemap to paint.
    /// </summary>
    internal TileStroke? BeginTileStroke(bool erase = false) =>
        sceneDocuments.Scene is { } document && PaintTarget is { } tilemap
            ? new TileStroke(document, projectSession.Components, tilemap, erase ? -1 : BrushTile, erase ? TileTurn.None : BrushTurn)
            : null;

    // Puts new tiles (and their turns) on the map, moving the entity back by the cells added left of and above the old
    // grid, as one step.
    private void Apply(Tilemap tilemap, IReadOnlyList<string> tiles, IReadOnlyList<string> turns, int shiftX, int shiftY, string description)
    {
        if (sceneDocuments.Scene is not { } document || (tiles.SequenceEqual(tilemap.Tiles) && turns.SequenceEqual(tilemap.Turns)))
            return;
        document.History.Seal();
        using (document.History.BeginGroup(description))
            TileStroke.Put(document, projectSession.Components, tilemap, tiles, turns, shiftX, shiftY);
        document.History.Seal();
    }

    private static InvalidOperationException NothingToPaint() =>
        new("Select one tilemap (and nothing else) in the open scene to paint on it.");

    /// <summary>One brush stroke on a tilemap, from <see cref="BeginTileStroke"/>.</summary>
    public sealed class TileStroke
    {
        private readonly SceneDocument _document;
        private readonly ComponentRegistry _components;
        private readonly Tilemap _tilemap;
        private readonly int _tile;
        private readonly TileTurn _turn;
        private readonly EditHistory.EditGroup _group;
        private (int X, int Y)? _last;

        internal TileStroke(SceneDocument document, ComponentRegistry components, Tilemap tilemap, int tile, TileTurn turn)
        {
            _document = document;
            _components = components;
            _tilemap = tilemap;
            _tile = tile;
            _turn = turn;
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
                if (_tilemap.GetTile(cx, cy) == _tile && _tilemap.GetTurn(cx, cy) == _turn)
                    continue;
                var (tiles, turns, sx, sy) = _tilemap.PaintedRect(cx, cy, cx, cy, _tile, _turn);
                Put(_document, _components, _tilemap, tiles, turns, sx, sy);
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

        internal static void Put(SceneDocument document, ComponentRegistry components, Tilemap tilemap, IReadOnlyList<string> tiles,
            IReadOnlyList<string> turns, int shiftX, int shiftY)
        {
            PropertyDescriptor Property(Component component, string name) => components.Get(component.GetType()).Properties.Single(p => p.Name == name);
            var positionProperty = Property(tilemap.Entity!.Transform, nameof(Transform.Position));
            if (!tiles.SequenceEqual(tilemap.Tiles))
                document.Execute(new SetPropertyEdit(tilemap, Property(tilemap, nameof(Tilemap.Tiles)), tiles));
            // A map that turns nothing gets no edit of its turns, so its undo steps are as they were.
            if (!turns.SequenceEqual(tilemap.Turns))
                document.Execute(new SetPropertyEdit(tilemap, Property(tilemap, nameof(Tilemap.Turns)), turns));
            if (shiftX == 0 && shiftY == 0)
                return;
            var transform = tilemap.Entity!.Transform;
            // Back by the columns and rows added before the old ones (on an isometric map, up the diagonals).
            var shift = new Vector3(tilemap.PointAt(-shiftX, -shiftY), 0);
            document.Execute(new SetPropertyEdit(transform, positionProperty, transform.Position + Vector3.TransformNormal(shift, transform.LocalMatrix)));

            // What's inside it (its layers, and anything else) stays where it was, so the layers stay lined up.
            foreach (var child in tilemap.Entity.Children)
                document.Execute(new SetPropertyEdit(child.Transform, positionProperty, child.Transform.Position - shift));
        }
    }
}
