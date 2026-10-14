using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.FallingBlocks;

// A falling-block board: its backdrop, the blocks that have landed (full rows shrinking away as they clear), an outline
// where the piece will land, the piece itself, and the pieces coming next in a box to the right. Without a tileset image
// each kind of piece is a square of its own color. A frame between two steps draws the board between how it was and is:
// the piece part way along a slide or a fall, full rows part shrunk, and rows that went in the last step shrinking the
// rest of the way as the rows above drop into their place.
public sealed partial class FallingBoard
{
    // A color for each shape on a board with no tileset image, in the order of FallingBoard.AllShapes (I J L O S T Z).
    private static readonly Color[] PieceColors =
    [
        Color.FromBytes(0x4C, 0xD4, 0xE8), Color.FromBytes(0x4A, 0x6C, 0xE8), Color.FromBytes(0xF0, 0x9A, 0x3A), Color.FromBytes(0xF2, 0xD3, 0x4B),
        Color.FromBytes(0x5C, 0xC0, 0x5A), Color.FromBytes(0xA0, 0x5C, 0xD8), Color.FromBytes(0xE0, 0x4A, 0x4A),
    ];

    private static readonly Color BlockGray = Color.FromBytes(0x8A, 0x90, 0x9C);

    // How much of its cell a colored block fills, leaving a gap between neighbors.
    private const float BlockFill = 0.92f;

    // Cells across the box of pieces coming next, and down for each one.
    private const int PreviewWidth = 5, PreviewSlot = 3;

    // Drawn in place of its tilemap's tiles, at the tilemap's order.
    int? IDrawable.DrawOrder => null;

    int IDrawable.DrawStep => DrawSteps.Tiles;

    void IDrawable.Draw(Drawing drawing)
    {
        if (Entity!.GetComponent<Tilemap>() is not { } map)
            return;
        foreach (var draw in FallingDraws(Entity, map, this, drawing.Blend))
            drawing.Add(draw);
    }

    private static IEnumerable<SpriteDraw> FallingDraws(Entity entity, Tilemap map, FallingBoard board, StepBlend blend)
    {
        var world = entity.Transform.WorldMatrix;
        var size = map.TileSize;
        var mapTint = map.TintDrawn(blend);
        var (columns, rows) = (Math.Max(0, board.Columns), Math.Max(0, board.Rows));
        if (board.Background.A > 0 && columns > 0 && rows > 0)
            yield return Square(new Vector2(columns * size / 2, -rows * size / 2), new Vector2(columns * size, rows * size), board.Background);

        // Rows that went in the last step shrink the rest of the way away, and the rows above drop into their place.
        var removed = blend.For(board.RemovedIn);
        if (removed.IsBetween)
        {
            var gone = Shrunk(removed.Between(board.RemovedClearing.From, board.RemovedClearing.To));
            foreach (var (x, y, tile) in board.Removed)
            {
                if (gone > 0)
                    yield return Block(new Vector2(x + 0.5f, y + 0.5f), tile, gone, 1);
            }
        }

        // What has landed, full rows shrinking away as they clear.
        var clearing = Shrunk(board.ClearingDrawn(blend));
        foreach (var (x, y, tile) in map.Cells())
        {
            var inside = x < columns && y < rows;
            var scale = inside && board.ClearingRows.Contains(y) ? clearing : 1;
            var row = removed.IsBetween && inside && y < board.DroppedBy.Length ? removed.Between(y - board.DroppedBy[y], y) : y;
            if (scale > 0)
                yield return Block(new Vector2(x + 0.5f, row + 0.5f), tile, scale, 1);
        }

        if (board.Falling is { } piece)
        {
            var tile = board.TileOf(piece.Shape);
            // Part way along a slide or a fall; the outline where it will land keeps under it as it slides.
            var shift = board.PieceDrawn(blend, size) - new Vector2(piece.X, piece.Y);
            if (board.Ghost.A > 0)
            {
                var edge = 0.08f;
                foreach (var (x, y) in board.LandingCells().Where(c => c.Y >= 0))
                {
                    var center = new Vector2(x + shift.X + 0.5f, y + 0.5f);
                    foreach (var (offset, extent) in new[]
                             {
                                 (new Vector2(0, (1 - edge) / 2), new Vector2(1, edge)), (new Vector2(0, -(1 - edge) / 2), new Vector2(1, edge)),
                                 (new Vector2((1 - edge) / 2, 0), new Vector2(edge, 1)), (new Vector2(-(1 - edge) / 2, 0), new Vector2(edge, 1)),
                             })
                        yield return Square(Cell(center + offset), extent * size, board.Ghost);
                }
            }

            foreach (var (x, y) in board.PieceCells().Where(c => c.Y >= 0))
                yield return Block(new Vector2(x + shift.X + 0.5f, y + shift.Y + 0.5f), tile, 1, 1);
        }

        // The pieces coming next, each centered in its slot of the box beside the board.
        var preview = Math.Min(board.Preview, board.Coming.Count);
        if (preview <= 0)
            yield break;
        var left = columns + 0.5f;
        if (board.Background.A > 0)
            yield return Square(Cell(new Vector2(left + PreviewWidth / 2f, (preview * PreviewSlot + 1) / 2f)),
                new Vector2(PreviewWidth, preview * PreviewSlot + 1) * size, board.Background);
        for (var i = 0; i < preview; i++)
        {
            var shape = board.Coming[i];
            var cells = FallingShapes.Cells(shape, 0, 0, 0);
            var (minX, maxX, minY, maxY) = (cells.Min(c => c.X), cells.Max(c => c.X), cells.Min(c => c.Y), cells.Max(c => c.Y));
            var middle = new Vector2(left + PreviewWidth / 2f, 0.5f + i * PreviewSlot + PreviewSlot / 2f);
            foreach (var (x, y) in cells)
                yield return Block(middle + new Vector2(x - (minX + maxX + 1) / 2f + 0.5f, y - (minY + maxY + 1) / 2f + 0.5f), board.TileOf(shape), 1, 1);
        }

        // From cells (x right, y down) to the tilemap's own space.
        Vector2 Cell(Vector2 at) => new(at.X * size, -at.Y * size);

        // How big a block is in rows that have been clearing this long.
        float Shrunk(float cleared) => board.ClearTime > 0 ? 1 - Math.Clamp(cleared / board.ClearTime, 0, 1) : 0;

        SpriteDraw Block(Vector2 at, int tile, float scale, float alpha)
        {
            var shown = map.ShownTile(tile);
            var tileset = map.TilesetOf(shown);
            var colored = tileset.Image is null;
            var extent = size * scale * (colored ? BlockFill : 1.004f);
            var tint = mapTint;
            if (colored)
            {
                var kind = tile - board.FirstTile;
                var color = kind >= 0 && kind < PieceColors.Length ? PieceColors[kind] : BlockGray;
                tint = new Color(color.R * tint.R, color.G * tint.G, color.B * tint.B, color.A * tint.A);
            }

            var center = Cell(at);
            var matrix = Matrix4x4.CreateScale(extent, extent, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0) * world;
            return new SpriteDraw(matrix, tileset.Image, tint with { A = tint.A * alpha }, entity)
            {
                Source = tileset.Image is null ? new Vector4(0, 0, 1, 1) : tileset.Rect(shown),
            };
        }

        SpriteDraw Square(Vector2 center, Vector2 extent, Color color) =>
            new(Matrix4x4.CreateScale(extent.X, extent.Y, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0) * world, null, color, entity);
    }
}
