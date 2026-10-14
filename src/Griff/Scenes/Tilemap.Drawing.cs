using System.Numerics;

namespace Griff.Scenes;

// A tilemap draws one quad per tile, and only the tiles in view when it can: a big world doesn't draw all of itself every
// frame. A component that draws the map itself (a match-3 board) takes its place.
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

        foreach (var draw in TileDraws(drawing.CanCull ? drawing.View : null, drawing.Smooth(_tint)))
            drawing.Add(draw);
    }

    /// <summary>
    /// One quad per tile (in the world), of those in <paramref name="cull"/> (and a cell around it) or all of them.
    /// Without an image, tiles are solid squares of the tint.
    /// </summary>
    public IEnumerable<SpriteDraw> TileDraws(Box? cull = null) => TileDraws(cull, Tint);

    private IEnumerable<SpriteDraw> TileDraws(Box? cull, Color tint)
    {
        var entity = Entity!;
        var world = entity.Transform.WorldMatrix;
        // A hair bigger than a cell, so neighbors overlap: edges computed tile by tile can round apart and let a hairline of
        // the background show between them as the camera moves. The image is stretched, not extended, so nothing bleeds in.
        const float seamless = 1.004f;
        var size = Matrix4x4.CreateScale(TileSize * seamless, TileSize * seamless, 1);
        foreach (var (x, y, tile) in cull is { } view ? CellsIn(world, view) : Cells())
        {
            var center = CellCenter(x, y);
            var matrix = size * Matrix4x4.CreateTranslation(center.X, center.Y, 0) * world;
            var shown = ShownTile(x, y, tile);
            var tileset = TilesetOf(shown);
            yield return new SpriteDraw(matrix, tileset.Image, tint, entity) { Source = tileset.Image is null ? new Vector4(0, 0, 1, 1) : tileset.Rect(shown) };
        }
    }

    // The cells of the map inside a view (and a cell around it), row by row.
    private IEnumerable<(int X, int Y, int Tile)> CellsIn(Matrix4x4 world, Box view)
    {
        if (!Matrix4x4.Invert(world, out var toLocal) || !(TileSize > 0))
        {
            foreach (var cell in Cells())
                yield return cell;
            yield break;
        }

        var (min, max) = (new Vector2(float.PositiveInfinity), new Vector2(float.NegativeInfinity));
        foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
        {
            var local = Vector2.Transform(view.Center + corner * view.HalfSize, toLocal);
            (min, max) = (Vector2.Min(min, local), Vector2.Max(max, local));
        }

        var size = TileSize;
        var (fromX, toX) = (Math.Max(0, (int)MathF.Floor(min.X / size) - 1), Math.Min(Width - 1, (int)MathF.Floor(max.X / size) + 1));
        var (fromY, toY) = (Math.Max(0, (int)MathF.Floor(-max.Y / size) - 1), Math.Min(Height - 1, (int)MathF.Floor(-min.Y / size) + 1));
        for (var y = fromY; y <= toY; y++)
        {
            for (var x = fromX; x <= toX; x++)
            {
                if (GetTile(x, y) is var tile and >= 0)
                    yield return (x, y, tile);
            }
        }
    }
}
