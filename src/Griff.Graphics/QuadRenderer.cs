using Griff.Fonts;
using Griff.Rendering;
using Griff.Runtime;

namespace Griff.Graphics;

/// <summary>
/// What every renderer does the same, whatever it draws with: each frame is turned into a <see cref="DrawList"/>, the
/// images it draws are numbered and loaded once (and loaded again when their files change, so editing a sprite in a paint
/// program updates the running game), and the rows of the glyph atlas's pages that text added glyphs to are handed over
/// before the frame is drawn. A renderer for a graphics API says how to load an image, copy glyph rows to a page's
/// texture, run a draw list's passes and read the pixels back.
/// </summary>
/// <remarks>
/// Textures are numbered as the draw list numbers them: <see cref="DrawList.WhiteTexture"/> (0) is white, images are
/// 1, 2, 3... in the order they're first drawn, and glyph pages are <see cref="DrawList.GlyphTexture"/>'s -1, -2...
/// Images sample nearest (pixel art is the common jam style) and glyphs smoothly; an image that's missing or can't be
/// read draws as a magenta checker.
/// </remarks>
public abstract class QuadRenderer : IRenderer
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly string _assetRoot;
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _byPath = new(StringComparer.OrdinalIgnoreCase);

    // Each image's file and when it was last written, by texture (0 is white, so no file).
    private readonly List<string> _paths = [""];
    private readonly List<DateTime> _stamps = [DateTime.MinValue];
    private byte[] _glyphRows = [];
    private DateTime _lastRefresh = DateTime.UtcNow;

    /// <param name="assetRoot">The game's Assets folder, which images and font files are read from.</param>
    /// <param name="installedFonts">
    /// Whether text may be drawn in fonts installed on this computer (a font a scene names that isn't in the game's files,
    /// and characters no font of the game's has). Not in a browser, which has only the game's files.
    /// </param>
    protected QuadRenderer(string? assetRoot, bool installedFonts)
    {
        _assetRoot = assetRoot ?? "";
        List = new DrawList(ImageTexture, new GlyphAtlas(assetRoot, installedFonts));
    }

    /// <summary>The last frame, as it was drawn.</summary>
    public DrawList List { get; }

    /// <summary>The glyphs text is drawn with.</summary>
    public GlyphAtlas Glyphs => List.Glyphs;

    public int LastDrawCalls { get; protected set; }

    public void Render(RenderFrame frame, int width, int height)
    {
        Glyphs.RefreshChanged();
        RefreshImages();
        RefreshPictures(frame.Pictures);
        List.Build(frame, width, height);
        SendGlyphs();
        Draw(List);
        Glyphs.EndFrame();
    }

    public abstract byte[] Capture(int width, int height);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    /// <summary>
    /// Gives <paramref name="texture"/> the image in the file at <paramref name="path"/>: the first time it's drawn, and
    /// again whenever the file changes. A file that isn't there or can't be read is the missing image's checker.
    /// </summary>
    protected abstract void LoadImage(int texture, string path);

    /// <summary>
    /// Gives <paramref name="texture"/> the image encoded in <paramref name="encoded"/> (a PNG): one of the game's own
    /// pictures (see <see cref="GamePictures"/>), the first time it's drawn and again whenever it changes. Nothing (or what
    /// can't be read) is the missing image's checker. A renderer that draws none of them can leave it.
    /// </summary>
    protected virtual void LoadImage(int texture, byte[] encoded)
    {
    }

    /// <summary>
    /// Copies <paramref name="rows"/> rows from <paramref name="top"/> of the glyph page <paramref name="texture"/>
    /// (straight-alpha RGBA, <see cref="GlyphPage.Size"/> pixels wide) to its texture, made <see cref="GlyphPage.Size"/>
    /// pixels square the first time.
    /// </summary>
    protected abstract void LoadGlyphRows(int texture, int top, int rows, Span<byte> rgba);

    /// <summary>Runs <paramref name="list"/>'s passes, drawing it into the target bound now.</summary>
    protected abstract void Draw(DrawList list);

    // Each name's file is worked out once, so thousands of sprites a frame look theirs up without making strings; two
    // names for one file (another case, the other slash) share its texture.
    private int ImageTexture(string name)
    {
        if (_byName.TryGetValue(name, out var texture))
            return texture;
        if (GamePictures.IsPicture(name))
            return PictureTexture(name);
        var path = Path.Combine(_assetRoot, name.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));
        if (!_byPath.TryGetValue(path, out texture))
        {
            texture = _paths.Count;
            _paths.Add(path);
            _stamps.Add(Stamp(path));
            _byPath[path] = texture;
            LoadImage(texture, path);
        }

        _byName[name] = texture;
        return texture;
    }

    // Loads the images whose files changed again, looking at most once a second.
    private void RefreshImages()
    {
        var now = DateTime.UtcNow;
        if (now - _lastRefresh < RefreshInterval)
            return;
        _lastRefresh = now;
        for (var texture = 1; texture < _paths.Count; texture++)
        {
            var stamp = Stamp(_paths[texture]);
            if (stamp == _stamps[texture])
                continue;
            _stamps[texture] = stamp;
            LoadImage(texture, _paths[texture]);
        }
    }

    // The rows of the glyph pages that glyphs were drawn into since the last frame, for the textures to have them before it's drawn.
    private void SendGlyphs()
    {
        var pages = Glyphs.Pages;
        for (var i = 0; i < pages.Count; i++)
        {
            if (!pages[i].TakeChanges(out var top, out var rows))
                continue;
            var length = rows * GlyphPage.Size * 4;
            if (_glyphRows.Length < length)
                _glyphRows = new byte[GlyphPage.Size * GlyphPage.Size * 4];
            pages[i].CopyRgba(top, rows, _glyphRows);
            LoadGlyphRows(DrawList.GlyphTexture(i), top, rows, _glyphRows.AsSpan(0, length));
        }
    }

    // The game's own pictures drawn so far, by texture, with the version of each loaded (-1 for none yet), and the game's
    // pictures they came from.
    private readonly Dictionary<int, (string Name, int Version)> _pictureTextures = [];
    private GamePictures? _pictures;

    private int PictureTexture(string name)
    {
        var texture = _paths.Count;
        _paths.Add("");
        _stamps.Add(DateTime.MinValue);
        _byName[name] = texture;
        _pictureTextures[texture] = (name, -1);
        LoadPicture(texture, name);
        return texture;
    }

    // Loads the pictures that changed since they were last drawn (all of them, for another game's).
    private void RefreshPictures(GamePictures? pictures)
    {
        if (pictures != _pictures)
        {
            _pictures = pictures;
            foreach (var texture in _pictureTextures.Keys.ToList())
                _pictureTextures[texture] = (_pictureTextures[texture].Name, -1);
        }

        if (_pictureTextures.Count == 0)
            return;
        foreach (var (texture, (name, version)) in _pictureTextures.ToList())
        {
            if ((pictures?.Get(name).Version ?? 0) != version)
                LoadPicture(texture, name);
        }
    }

    private void LoadPicture(int texture, string name)
    {
        var (png, version) = _pictures?.Get(name) ?? (null, 0);
        _pictureTextures[texture] = (name, version);
        LoadImage(texture, png ?? []);
    }

    private static DateTime Stamp(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
}
