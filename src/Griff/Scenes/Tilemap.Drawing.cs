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

        var depth = -1;
        foreach (var (draw, x, y) in TileDraws(drawing.CanCull ? drawing.View : null, drawing.Smooth(_tint)))
        {
            if (SortByY && DepthOf(x, y) != depth)
            {
                depth = DepthOf(x, y);
                drawing.SortAt(DepthY(depth));
            }

            drawing.Add(draw);
        }
    }

    /// <summary>
    /// One quad per tile (in the world), back to front, of those in <paramref name="cull"/> (and a cell around it) or all
    /// of them. Without an image, tiles are solid shapes of the tint: squares, diamonds or hexagons (three quads each).
    /// </summary>
    public IEnumerable<SpriteDraw> TileDraws(Box? cull = null) => TileDraws(cull, Tint).Select(d => d.Draw);

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

    private IEnumerable<(SpriteDraw Draw, int X, int Y)> TileDraws(Box? cull, Color tint)
    {
        var entity = Entity!;
        var world = entity.Transform.WorldMatrix;
        // A hair bigger than a cell, so neighbors overlap: edges computed tile by tile can round apart and let a hairline of
        // the background show between them as the camera moves. The image is stretched, not extended, so nothing bleeds in.
        const float seamless = 1.004f;
        var diamond = Diamond(TileSize * seamless);
        foreach (var (x, y, tile) in cull is { } view ? CellsIn(world, view) : BackToFront())
        {
            var shown = ShownTile(x, y, tile);
            var tileset = TilesetOf(shown);
            var (center, size) = PictureOf(x, y);
            if (tileset.Image is null && Shape == TileShape.Hex)
            {
                foreach (var part in HexParts)
                    yield return (new SpriteDraw(part * Matrix4x4.CreateScale(TileSize * seamless, TileSize * seamless, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0) * world, null, tint, entity) { Source = new Vector4(0, 0, 1, 1) }, x, y);
                continue;
            }

            var matrix = tileset.Image is null && Shape == TileShape.Isometric
                ? diamond * Matrix4x4.CreateTranslation(new Vector3(CellCenter(x, y), 0)) * world
                : Matrix4x4.CreateScale(size.X * seamless, size.Y * seamless, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0) * world;
            yield return (new SpriteDraw(matrix, tileset.Image, tint, entity) { Source = tileset.Image is null ? new Vector4(0, 0, 1, 1) : tileset.Rect(shown) }, x, y);
        }
    }

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

    // The cells of the map inside a view (and a cell around it), back to front.
    private IEnumerable<(int X, int Y, int Tile)> CellsIn(Matrix4x4 world, Box view)
    {
        if (!Matrix4x4.Invert(world, out var toLocal) || !(TileSize > 0))
            return BackToFront();

        var (min, max) = (new Vector2(float.PositiveInfinity), new Vector2(float.NegativeInfinity));
        foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
        {
            var local = Vector2.Transform(view.Center + corner * view.HalfSize, toLocal);
            (min, max) = (Vector2.Min(min, local), Vector2.Max(max, local));
        }

        if (Shape == TileShape.Hex)
        {
            // Rows are three quarters of a tile apart, and a hexagon reaches a quarter tile past its row on each side.
            var (size, row) = (TileSize, TileSize * 0.75f);
            return CellsBetween((int)MathF.Floor(min.X / size) - 1, (int)MathF.Floor(max.X / size) + 1,
                (int)MathF.Floor(-max.Y / row) - 1, (int)MathF.Floor(-min.Y / row) + 1);
        }

        if (Shape != TileShape.Isometric)
        {
            var size = TileSize;
            return CellsBetween((int)MathF.Floor(min.X / size) - 1, (int)MathF.Floor(max.X / size) + 1,
                (int)MathF.Floor(-max.Y / size) - 1, (int)MathF.Floor(-min.Y / size) + 1);
        }

        // A tile standing up from a cell below the view can reach up into it.
        min.Y -= Math.Max(0, TileHeight - 0.5f) * TileSize;
        var (fromX, toX, fromY, toY) = (int.MaxValue, int.MinValue, int.MaxValue, int.MinValue);
        foreach (var corner in new[] { min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y) })
        {
            var (x, y) = CellAt(corner);
            (fromX, toX, fromY, toY) = (Math.Min(fromX, x), Math.Max(toX, x), Math.Min(fromY, y), Math.Max(toY, y));
        }

        return CellsBetween(fromX - 1, toX + 1, fromY - 1, toY + 1);
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
