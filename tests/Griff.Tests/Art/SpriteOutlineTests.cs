using System.Numerics;
using Griff.Art;
using Griff.Scenes;

namespace Griff.Tests.Art;

/// <summary>Tracing round a picture's solid part, for a polygon collider that fits a sprite.</summary>
public class SpriteOutlineTests
{
    // A picture `width` by `height` whose pixels are solid where `solid` says (x right, y down).
    private static byte[] Picture(int width, int height, Func<int, int, bool> solid)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                rgba[(y * width + x) * 4 + 3] = solid(x, y) ? (byte)255 : (byte)0;
        }

        return rgba;
    }

    private static PolygonCollider Fitted(IReadOnlyList<Vector2> points) => new() { Points = PolygonCollider.FormatPoints(points) };

    private static float Area(IReadOnlyList<Vector2> points)
    {
        var sum = 0f;
        for (var i = 0; i < points.Count; i++)
            sum += points[i].X * points[(i + 1) % points.Count].Y - points[i].Y * points[(i + 1) % points.Count].X;
        return sum / 2;
    }

    [Fact]
    public void A_solid_square_in_a_clear_border_is_its_four_corners_in_the_sprites_own_units()
    {
        var picture = Picture(16, 16, (x, y) => x is >= 4 and < 12 && y is >= 2 and < 10);

        var outline = SpriteOutline.Trace(picture, 16, 16, (0, 0, 16, 16), new Vector2(2, 2))!;

        Assert.Equal(4, outline.Count);
        Assert.Equal(1, Area(outline), 4);
        Assert.Contains(new Vector2(-0.5f, 0.75f), outline);
        Assert.Contains(new Vector2(0.5f, -0.25f), outline);
    }

    [Fact]
    public void A_triangle_is_traced_as_a_few_points_hugging_its_slant()
    {
        // Solid below the line from the bottom left to the top right.
        var picture = Picture(32, 32, (x, y) => y >= 31 - x);

        var outline = SpriteOutline.Trace(picture, 32, 32, (0, 0, 32, 32), new Vector2(1, 1))!;

        Assert.InRange(outline.Count, 3, 12);
        var polygon = Fitted(outline);
        Assert.Null(polygon.Problem);
        Assert.Equal(0.5f, Area(polygon.Outline), 1);
        Assert.True(Area(outline) > 0, "Not anticlockwise.");
    }

    [Fact]
    public void An_l_shape_stays_concave_and_a_ring_is_traced_round_its_outside()
    {
        var el = SpriteOutline.Trace(Picture(16, 16, (x, y) => x < 6 || y >= 10), 16, 16, (0, 0, 16, 16), new Vector2(16, 16))!;
        var polygon = Fitted(el);
        Assert.Null(polygon.Problem);
        Assert.Equal(6, el.Count);
        Assert.True(polygon.PieceCount > 1);

        var ring = SpriteOutline.Trace(Picture(16, 16, (x, y) => Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(8)) is >= 3 and < 7), 16, 16,
            (0, 0, 16, 16), new Vector2(16, 16))!;
        Assert.InRange(ring.Count, 6, 12);
        Assert.Null(Fitted(ring).Problem);
        // The hole is filled in: the middle is inside.
        var entity = new Entity("Ring");
        var collider = entity.AddComponent<PolygonCollider>();
        collider.Points = PolygonCollider.FormatPoints(ring);
        new Scene().Add(entity);
        Assert.True(collider.Contains(Vector2.Zero));
    }

    [Fact]
    public void Only_the_biggest_blob_counts_and_a_clear_frame_has_no_outline()
    {
        var picture = Picture(16, 16, (x, y) => (x is >= 8 and < 15 && y is >= 1 and < 15) || (x is 1 or 2 && y is 1 or 2));

        var outline = SpriteOutline.Trace(picture, 16, 16, (0, 0, 16, 16), new Vector2(16, 16))!;

        Assert.Equal(4, outline.Count);
        Assert.All(outline, p => Assert.True(p.X >= 0, $"{p} is in the small blob."));
        Assert.Null(SpriteOutline.Trace(Picture(8, 8, (_, _) => false), 8, 8, (0, 0, 8, 8), Vector2.One));
    }

    [Fact]
    public void It_traces_the_frame_shown_mirrored_as_the_sprite_is()
    {
        // Two 8-pixel frames side by side: a block on the left of the first, on the right of the second.
        var picture = Picture(16, 8, (x, y) => x < 3 || x >= 13);

        var second = SpriteOutline.Trace(picture, 16, 8, (8, 0, 8, 8), new Vector2(8, 8))!;
        Assert.All(second, p => Assert.True(p.X >= 1 - 1e-4f, $"{p}"));

        var flipped = SpriteOutline.Trace(picture, 16, 8, (8, 0, 8, 8), new Vector2(8, 8), flipX: true)!;
        Assert.All(flipped, p => Assert.True(p.X <= -1 + 1e-4f, $"{p}"));
        Assert.True(Area(flipped) > 0, "Not anticlockwise once mirrored.");
    }

    [Fact]
    public void A_ragged_blob_is_cut_down_to_the_most_points_asked_for_without_crossing()
    {
        var random = new Random(3);
        for (var trial = 0; trial < 30; trial++)
        {
            var noise = Enumerable.Range(0, 24 * 24).Select(_ => random.NextDouble() < 0.25).ToArray();
            var picture = Picture(24, 24, (x, y) => Vector2.Distance(new Vector2(x, y), new Vector2(12)) < 9 + (noise[y * 24 + x] ? 2 : 0));

            var outline = SpriteOutline.Trace(picture, 24, 24, (0, 0, 24, 24), new Vector2(3, 3), maxPoints: 8)!;

            Assert.InRange(outline.Count, 3, 8);
            Assert.Null(Fitted(outline).Problem);
        }
    }
}
