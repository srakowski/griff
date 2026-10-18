using System.Numerics;

namespace Griff.Scenes;

// A tilemap draws one quad per tile, and only the tiles in view when it can: a big world doesn't draw all of itself every
// frame. A component that draws the map itself (a match-3 board) takes its place. Tiles go back to front, row by row (by
// diagonal on an isometric map), so tall ones overlap those behind them (and a hex row's pictures overlap the points of
// the row above); a map that sorts by Y sorts each row among the sprites.
public sealed partial class Tilemap
{
    int? IDrawable.DrawOrder => Order;

    int IDrawable.DrawStep => DrawSteps.Tiles;

    void IDrawable.Draw(Drawing drawing)
    {
        foreach (var component in Entity!.ComponentList)
        {
            if (component is IDrawsTilemap)
                return;
        }

        AddTileDraws(drawing.Draws, drawing.CanCull ? drawing.View : null, drawing.Smooth(_tint), SortByY ? drawing : null);
    }

    /// <summary>
    /// One quad per tile (in the world), back to front, of those in <paramref name="cull"/> (and a cell around it) or all
    /// of them. Without an image, tiles are solid shapes of the tint (times their <see cref="PlaceholderColors"/>): squares,
    /// diamonds or hexagons (three quads each), each over a slightly bigger one of its grid line's color with
    /// <see cref="PlaceholderGrid"/>.
    /// </summary>
    public IEnumerable<SpriteDraw> TileDraws(Box? cull = null)
    {
        var draws = new List<SpriteDraw>();
        AddTileDraws(draws, cull, Tint, null);
        return draws;
    }

    /// <summary>With <see cref="SortByY"/>, each row of tiles (see <see cref="DepthOf"/>) and the height it sorts at.</summary>
    public IEnumerable<(int Part, float Y)> SortParts()
    {
        if (!SortByY)
            yield break;
        for (var depth = 0; depth < Depths; depth++)
            yield return (depth, DepthY(depth));
    }

    /// <summary>
    /// The world Y a row of tiles (see <see cref="DepthOf"/>) sorts by when the map sorts by Y: the middle of its cells,
    /// where a character standing among them has its feet.
    /// </summary>
    public float DepthY(int depth)
    {
        var world = Entity?.Transform.WorldMatrix ?? Matrix4x4.Identity;
        var middle = Shape == TileShape.Isometric ? PointAt(depth / 2f + 0.5f, depth / 2f + 0.5f) : CellCenter(0, depth);
        return Vector2.Transform(middle, world).Y;
    }

    // Adds the tiles' quads (see TileDraws) to `draws`, making no garbage once it has room for them: a big map is drawn
    // every frame. With `sorting`, each row of tiles sorts at its own height (see Drawing.SortAt).
    private void AddTileDraws(List<SpriteDraw> draws, Box? cull, Color tint, Drawing? sorting)
    {
        var entity = Entity!;
        var world = entity.Transform.WorldMatrix;
        // A hair bigger than a cell, so neighbors overlap: edges computed tile by tile can round apart and let a hairline of
        // the background show between them as the camera moves. The image is stretched, not extended, so nothing bleeds in.
        const float seamless = 1.004f;
        var (grid, placeholders) = (BaseMap.PlaceholderGrid, BaseMap._placeholders);
        var (fromX, toX, fromY, toY) = cull is { } view ? CellRangeIn(world, view) : (0, int.MaxValue, 0, int.MaxValue);
        (fromX, toX) = (Math.Max(0, fromX), Math.Min(Width - 1, toX));
        (fromY, toY) = (Math.Max(0, fromY), Math.Min(Height - 1, toY));
        var depth = -1;
        var turned = _turns.Length > 0;
        if (Shape != TileShape.Isometric)
        {
            for (var y = fromY; y <= toY; y++)
            {
                for (var x = fromX; x <= toX; x++)
                {
                    if (GetTile(x, y) is var tile and >= 0)
                        Add(x, y, tile);
                }
            }

            return;
        }

        for (var d = fromX + fromY; d <= toX + toY; d++)
        {
            for (var x = Math.Max(fromX, d - toY); x <= Math.Min(toX, d - fromY); x++)
            {
                if (GetTile(x, d - x) is var tile and >= 0)
                    Add(x, d - x, tile);
            }
        }

        void Add(int x, int y, int tile)
        {
            if (sorting is not null && DepthOf(x, y) != depth)
            {
                depth = DepthOf(x, y);
                sorting.SortAt(DepthY(depth));
            }

            var shown = ShownTile(x, y, tile);
            var tileset = TilesetOf(shown);
            if (tileset.Image is null)
            {
                var color = placeholders.Length == 0 ? tint : Times(tint, placeholders[shown % placeholders.Length]);
                if (grid)
                {
                    AddPlain(x, y, seamless, GridLineOf(color));
                    AddPlain(x, y, 1 - GridLine, color);
                }
                else
                {
                    AddPlain(x, y, seamless, color);
                }

                return;
            }

            var (center, size) = PictureOf(x, y);
            var matrix = Matrix4x4.CreateScale(size.X * seamless, size.Y * seamless, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0) * world;
            // A turned cell turns its picture round its middle first (see Turns).
            if (turned && TurnAt(x, y) is var turn and not TileTurn.None)
                matrix = turn.Matrix() * matrix;
            draws.Add(new SpriteDraw(matrix, tileset.Image, tint, entity) { Source = tileset.Rect(shown) });
        }

        // A tile with no picture: its cell's shape (see CellShape) at `scale` of it round its middle, in one color.
        void AddPlain(int x, int y, float scale, Color color)
        {
            var middle = Matrix4x4.CreateTranslation(new Vector3(CellCenter(x, y), 0)) * world;
            var size = TileSize * scale;
            switch (Shape)
            {
                case TileShape.Hex:
                    foreach (var part in HexParts)
                        draws.Add(new SpriteDraw(part * Matrix4x4.CreateScale(size, size, 1) * middle, null, color, entity) { Source = new Vector4(0, 0, 1, 1) });
                    break;
                case TileShape.Isometric:
                    draws.Add(new SpriteDraw(Diamond(size) * middle, null, color, entity) { Source = new Vector4(0, 0, 1, 1) });
                    break;
                default:
                    draws.Add(new SpriteDraw(Matrix4x4.CreateScale(size, size, 1) * middle, null, color, entity) { Source = new Vector4(0, 0, 1, 1) });
                    break;
            }
        }
    }

    // How much of a tile's width the placeholder grid's line takes, half on each side of it.
    private const float GridLine = 0.06f;

    // The color of the placeholder grid's line round a tile of `color`: a little darker, or on a dark tile a little lighter,
    // so it shows either way.
    private static Color GridLineOf(Color color)
    {
        var light = 0.299f * color.R + 0.587f * color.G + 0.114f * color.B;
        var line = Color.Lerp(color, light > 0.2f ? Color.Black : Color.White, 0.3f);
        return line with { A = color.A };
    }

    private static Color Times(Color a, Color b) => new(a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A);

    // Without a picture, an isometric tile is its diamond, this wide: a square turned an eighth and squashed to half its height.
    private static Matrix4x4 Diamond(float width) =>
        Matrix4x4.CreateScale(width / MathF.Sqrt(2), width / MathF.Sqrt(2), 1) * Matrix4x4.CreateRotationZ(MathF.PI / 4) * Matrix4x4.CreateScale(1, 0.5f, 1);

    /// <summary>
    /// What covers cell (<paramref name="x"/>, <paramref name="y"/>)'s shape in the entity's own space, shrunk to
    /// <paramref name="scale"/> of it round its middle, as transforms of the unit quad (-0.5 to 0.5 each way): a square, an
    /// isometric map's diamond, or a hex map's hexagon in three parts that don't overlap. For lighting cells up, as
    /// Tactics shows where a unit can go.
    /// </summary>
    public IEnumerable<Matrix4x4> CellShape(int x, int y, float scale = 1)
    {
        var middle = Matrix4x4.CreateTranslation(new Vector3(CellCenter(x, y), 0));
        var size = TileSize * scale;
        switch (Shape)
        {
            case TileShape.Hex:
                foreach (var part in HexParts)
                    yield return part * Matrix4x4.CreateScale(size, size, 1) * middle;
                break;
            case TileShape.Isometric:
                yield return Diamond(size) * middle;
                break;
            default:
                yield return Matrix4x4.CreateScale(size, size, 1) * middle;
                break;
        }
    }

    // Without a picture, a hex tile is its hexagon (a tile wide and tall, round its middle): three quads that don't overlap,
    // each a parallelogram from the middle to three corners in a row, so a see-through tint is even all over.
    private static readonly Matrix4x4[] HexParts =
    [
        Parallelogram(new Vector2(0, 0.5f), new Vector2(0.5f, -0.25f)),
        Parallelogram(new Vector2(0.5f, -0.25f), new Vector2(-0.5f, -0.25f)),
        Parallelogram(new Vector2(-0.5f, -0.25f), new Vector2(0, 0.5f)),
    ];

    // Turns the unit quad (-0.5 to 0.5 both ways) into the parallelogram with corners at the origin, a, a + b and b.
    private static Matrix4x4 Parallelogram(Vector2 a, Vector2 b) =>
        new(a.X, a.Y, 0, 0, b.X, b.Y, 0, 0, 0, 0, 1, 0, (a.X + b.X) / 2, (a.Y + b.Y) / 2, 0, 1);

    /// <summary>
    /// Every non-empty cell and its tile in the order they're drawn: row by row, or on an isometric map diagonal by
    /// diagonal from the top corner (see <see cref="DepthOf"/>), so what's in front comes after what's behind.
    /// </summary>
    public IEnumerable<(int X, int Y, int Tile)> BackToFront() => CellsBetween(0, Width - 1, 0, Height - 1);

    /// <summary>The non-empty cells of one row of <see cref="BackToFront"/> (see <see cref="DepthOf"/>), in the order they're drawn.</summary>
    public IEnumerable<(int X, int Y, int Tile)> CellsAtDepth(int depth)
    {
        if (Shape != TileShape.Isometric)
        {
            foreach (var cell in CellsBetween(0, Width - 1, depth, depth))
                yield return cell;
            yield break;
        }

        for (var x = Math.Max(0, depth - Height + 1); x <= Math.Min(depth, Width - 1); x++)
        {
            if (GetTile(x, depth - x) is var tile and >= 0)
                yield return (x, depth - x, tile);
        }
    }

    // The columns and rows of the map's cells inside a view (and a cell around it), not yet kept within the grid.
    private (int FromX, int ToX, int FromY, int ToY) CellRangeIn(Matrix4x4 world, Box view)
    {
        if (!Matrix4x4.Invert(world, out var toLocal) || !(TileSize > 0))
            return (0, int.MaxValue, 0, int.MaxValue);

        var (min, max) = (new Vector2(float.PositiveInfinity), new Vector2(float.NegativeInfinity));
        for (var i = 0; i < 4; i++)
        {
            var corner = new Vector2(i is 1 or 2 ? 1 : -1, i < 2 ? -1 : 1);
            var local = Vector2.Transform(view.Center + corner * view.HalfSize, toLocal);
            (min, max) = (Vector2.Min(min, local), Vector2.Max(max, local));
        }

        if (Shape == TileShape.Hex)
        {
            // Rows are three quarters of a tile apart, and a hexagon reaches a quarter tile past its row on each side.
            var (size, row) = (TileSize, TileSize * 0.75f);
            return ((int)MathF.Floor(min.X / size) - 1, (int)MathF.Floor(max.X / size) + 1,
                (int)MathF.Floor(-max.Y / row) - 1, (int)MathF.Floor(-min.Y / row) + 1);
        }

        if (Shape != TileShape.Isometric)
        {
            var size = TileSize;
            return ((int)MathF.Floor(min.X / size) - 1, (int)MathF.Floor(max.X / size) + 1,
                (int)MathF.Floor(-max.Y / size) - 1, (int)MathF.Floor(-min.Y / size) + 1);
        }

        // A tile standing up from a cell below the view can reach up into it.
        min.Y -= Math.Max(0, TileHeight - 0.5f) * TileSize;
        var (fromX, toX, fromY, toY) = (int.MaxValue, int.MinValue, int.MaxValue, int.MinValue);
        for (var i = 0; i < 4; i++)
        {
            var (x, y) = CellAt(new Vector2(i is 1 or 2 ? max.X : min.X, i < 2 ? min.Y : max.Y));
            (fromX, toX, fromY, toY) = (Math.Min(fromX, x), Math.Max(toX, x), Math.Min(fromY, y), Math.Max(toY, y));
        }

        return (fromX - 1, toX + 1, fromY - 1, toY + 1);
    }

    // The non-empty cells from columns fromX to toX and rows fromY to toY (kept within the grid), back to front.
    private IEnumerable<(int X, int Y, int Tile)> CellsBetween(int fromX, int toX, int fromY, int toY)
    {
        (fromX, toX) = (Math.Max(0, fromX), Math.Min(Width - 1, toX));
        (fromY, toY) = (Math.Max(0, fromY), Math.Min(Height - 1, toY));
        if (Shape != TileShape.Isometric)
        {
            for (var y = fromY; y <= toY; y++)
            {
                for (var x = fromX; x <= toX; x++)
                {
                    if (GetTile(x, y) is var tile and >= 0)
                        yield return (x, y, tile);
                }
            }

            yield break;
        }

        for (var depth = fromX + fromY; depth <= toX + toY; depth++)
        {
            for (var x = Math.Max(fromX, depth - toY); x <= Math.Min(toX, depth - fromY); x++)
            {
                if (GetTile(x, depth - x) is var tile and >= 0)
                    yield return (x, depth - x, tile);
            }
        }
    }
}
