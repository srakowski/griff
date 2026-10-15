using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Web;

/// <summary>
/// A <see cref="RenderFrame"/> as the browser draws it (see <c>wwwroot/griff.js</c>): every quad in one array, in draw
/// order, and the batches that split it by image. A quad is <see cref="FloatsPerQuad"/> floats, laid out as the desktop
/// renderer's instances are (Griff.Graphics' SpriteRenderer): its corners in clip space (top left, top right, bottom
/// right, bottom left), the part of the image on it (u, v, width, height), its tint and its flash. A batch is two ints:
/// the image's id (see <see cref="WhiteImage"/> and <see cref="HoleShape"/>) and how many quads it draws.
/// </summary>
/// <remarks>
/// Sprites (with their part of the image, tint and flash) and holes (a scene change's iris) are drawn. Text, the dark and
/// its lights, and screen effects aren't yet: text needs a rasterizer that isn't WPF's, and the lighting and effects
/// passes come with the renderer interface.
/// </remarks>
public sealed class WebFrame
{
    /// <summary>The floats in one quad.</summary>
    public const int FloatsPerQuad = 20;

    /// <summary>The image id of a sprite with no image: a white texture its tint colors.</summary>
    public const int WhiteImage = 0;

    /// <summary>The image id of a batch of holes, drawn with the shape shader rather than an image.</summary>
    public const int HoleShape = -1;

    private readonly Func<string, int> _imageId;
    private float[] _quads = new float[FloatsPerQuad * 1024];
    private int[] _batches = new int[2 * 64];

    /// <param name="imageId">The id the page knows an image (a path in Assets) by, more than 0; called each time one is drawn.</param>
    public WebFrame(Func<string, int> imageId) => _imageId = imageId;

    /// <summary>The quads of the last frame built, <see cref="FloatsPerQuad"/> floats each.</summary>
    public Span<float> Quads => _quads.AsSpan(0, QuadCount * FloatsPerQuad);

    /// <summary>The batches of the last frame built: an image id and a quad count each.</summary>
    public Span<int> Batches => _batches.AsSpan(0, BatchCount * 2);

    public int QuadCount { get; private set; }

    public int BatchCount { get; private set; }

    /// <summary>Sprites the last frame had that the browser can't draw yet (text).</summary>
    public int Skipped { get; private set; }

    public Color Background { get; private set; }

    /// <summary>Lays out <paramref name="frame"/>'s quads and batches, replacing the last frame's.</summary>
    public void Build(RenderFrame frame)
    {
        QuadCount = BatchCount = Skipped = 0;
        Background = frame.Background;
        var viewProjection = frame.ViewProjection;
        var sprites = frame.Sprites;
        for (var i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            if (sprite.HoleRadius > 0)
            {
                Write(HoleShape, sprite.World * viewProjection, new Vector4(0, 0, 1, 1), sprite.Tint, new Color(sprite.HoleRadius, 0, 0, 0));
                continue;
            }

            if (sprite.Text is not null)
            {
                Skipped++;
                continue;
            }

            var image = string.IsNullOrWhiteSpace(sprite.Image) ? WhiteImage : _imageId(sprite.Image);
            Write(image, sprite.World * viewProjection, sprite.Source, sprite.Tint, sprite.Flash);
        }
    }

    // One quad: the unit quad's corners (-0.5..0.5) taken to clip space, added to the batch of its image (a new batch when
    // the image changes from the quad before).
    private void Write(int image, in Matrix4x4 toClip, Vector4 source, Color tint, Color flash)
    {
        if (BatchCount == 0 || _batches[(BatchCount - 1) * 2] != image)
        {
            if (BatchCount * 2 == _batches.Length)
                Array.Resize(ref _batches, _batches.Length * 2);
            _batches[BatchCount * 2] = image;
            _batches[BatchCount * 2 + 1] = 0;
            BatchCount++;
        }

        _batches[BatchCount * 2 - 1]++;
        if ((QuadCount + 1) * FloatsPerQuad > _quads.Length)
            Array.Resize(ref _quads, _quads.Length * 2);
        var quad = _quads.AsSpan(QuadCount++ * FloatsPerQuad, FloatsPerQuad);
        Put(quad, 0, ToClip(-0.5f, 0.5f, toClip));
        Put(quad, 2, ToClip(0.5f, 0.5f, toClip));
        Put(quad, 4, ToClip(0.5f, -0.5f, toClip));
        Put(quad, 6, ToClip(-0.5f, -0.5f, toClip));
        (quad[8], quad[9], quad[10], quad[11]) = (source.X, source.Y, source.Z, source.W);
        (quad[12], quad[13], quad[14], quad[15]) = (tint.R, tint.G, tint.B, tint.A);
        (quad[16], quad[17], quad[18], quad[19]) = (flash.R, flash.G, flash.B, flash.A);
    }

    private static void Put(Span<float> quad, int at, Vector2 point) => (quad[at], quad[at + 1]) = (point.X, point.Y);

    private static Vector2 ToClip(float x, float y, in Matrix4x4 toClip)
    {
        var clip = Vector4.Transform(new Vector4(x, y, 0, 1), toClip);
        return new Vector2(clip.X / clip.W, clip.Y / clip.W);
    }
}
