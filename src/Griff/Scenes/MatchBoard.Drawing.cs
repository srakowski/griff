using System.Numerics;

namespace Griff.Scenes;

// A match-3 board draws its tilemap itself: its backdrop, the picked-up gem's cell and the cursors, then its gems, each
// where it's sliding or falling to and shrinking as it clears. Without a tileset image each kind of gem is a square of its
// own color.
public sealed partial class MatchBoard
{
    // A color for each kind of gem on a board with no tileset image, in the order of its Gems.
    private static readonly Color[] GemColors =
    [
        Color.FromBytes(0xE0, 0x4A, 0x4A), Color.FromBytes(0xF0, 0x9A, 0x3A), Color.FromBytes(0xF2, 0xD3, 0x4B),
        Color.FromBytes(0x5C, 0xC0, 0x5A), Color.FromBytes(0x4A, 0x8C, 0xE8), Color.FromBytes(0xA0, 0x5C, 0xD8),
        Color.FromBytes(0xEC, 0xEC, 0xF2), Color.FromBytes(0x4C, 0xD4, 0xC8), Color.FromBytes(0xE8, 0x6A, 0xB8),
    ];

    // How much of its cell a colored gem fills, leaving a gap between neighbors.
    private const float GemFill = 0.82f;

    // Drawn in place of its tilemap's tiles, at the tilemap's order.
    int? IDrawable.DrawOrder => null;

    int IDrawable.DrawStep => DrawSteps.Tiles;

    void IDrawable.Draw(Drawing drawing)
    {
        if (Entity!.GetComponent<Tilemap>() is not { } map)
            return;
        foreach (var draw in BoardDraws(Entity, map, this))
            drawing.Add(draw);
    }

    private static IEnumerable<SpriteDraw> BoardDraws(Entity entity, Tilemap map, MatchBoard board)
    {
        var world = entity.Transform.WorldMatrix;
        var size = map.TileSize;
        var (columns, rows) = (Math.Max(0, board.Columns), Math.Max(0, board.Rows));
        if (board.Background.A > 0 && columns > 0 && rows > 0)
            yield return Square(new Vector2(columns * size / 2, -rows * size / 2), new Vector2(columns * size, rows * size), board.Background);

        if (board.Highlight.A > 0)
        {
            var hands = board.Hands.Count > 0
                ? board.Hands.Values.Select(h => (h.Picked, h.Cursor))
                : board.Remote.Values.Select(r => (r.Picked, r.Cursor));
            foreach (var (picked, cursor) in hands)
            {
                if (picked is { } p)
                    yield return Square(map.CellCenter(p.X, p.Y), new Vector2(size), board.Highlight);
                if (cursor is { } c)
                {
                    // An outline, so it shows over a picked-up gem's cell too.
                    var center = map.CellCenter(c.X, c.Y);
                    var edge = size * 0.08f;
                    foreach (var (offset, extent) in new[]
                             {
                                 (new Vector2(0, (size - edge) / 2), new Vector2(size, edge)), (new Vector2(0, -(size - edge) / 2), new Vector2(size, edge)),
                                 (new Vector2((size - edge) / 2, 0), new Vector2(edge, size)), (new Vector2(-(size - edge) / 2, 0), new Vector2(edge, size)),
                             })
                        yield return Square(center + offset, extent, board.Highlight with { A = Math.Min(1, board.Highlight.A * 2.5f) });
                }
            }
        }

        var kinds = board.Kinds().ToList();
        const float seamless = 1.004f;
        foreach (var (x, y, tile) in map.Cells())
        {
            var inside = x < columns && y < rows;
            var motion = inside ? board.Motions.GetValueOrDefault((x, y)) : null;
            var offset = motion?.Offset ?? Vector2.Zero;
            var scale = 1f;
            if (motion is { Grows: true })
                scale = motion.Progress;
            if (inside && board.Clearing.TryGetValue((x, y), out var cleared))
                scale = board.ClearTime > 0 ? 1 - Math.Clamp(cleared / board.ClearTime, 0, 1) : 0;

            // A gem dropping in fades in as it comes onto the board.
            var row = y + offset.Y;
            var alpha = row < 0 ? Math.Clamp(1 + row, 0, 1) : 1;
            if (scale <= 0 || alpha <= 0)
                continue;

            var center = new Vector2((x + offset.X + 0.5f) * size, -(row + 0.5f) * size);
            var shown = map.ShownTile(x, y, tile);
            var tileset = map.TilesetOf(shown);
            var colored = tileset.Image is null && board.IsGem(tile);
            var extent = size * scale * (colored ? GemFill : seamless);
            var tint = map.Tint;
            if (colored)
            {
                var gem = GemColors[Math.Max(0, kinds.IndexOf(tile)) % GemColors.Length];
                tint = new Color(gem.R * tint.R, gem.G * tint.G, gem.B * tint.B, gem.A * tint.A);
            }

            var matrix = Matrix4x4.CreateScale(extent, extent, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0) * world;
            yield return new SpriteDraw(matrix, tileset.Image, tint with { A = tint.A * alpha }, entity)
            {
                Source = tileset.Image is null ? new Vector4(0, 0, 1, 1) : tileset.Rect(shown),
            };
        }

        SpriteDraw Square(Vector2 center, Vector2 extent, Color color) =>
            new(Matrix4x4.CreateScale(extent.X, extent.Y, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0) * world, null, color, entity);
    }
}
