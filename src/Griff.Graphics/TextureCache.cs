using Griff.Fonts;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace Griff.Graphics;

public sealed class Texture(uint handle, int width, int height)
{
    public uint Handle { get; } = handle;

    public int Width { get; } = width;

    public int Height { get; } = height;
}

/// <summary>
/// Loads images from the project's Assets folder on first use and reloads them when the file changes,
/// so editing a sprite in a paint program updates the running game. Missing images draw as a magenta checker. Text's
/// glyphs are drawn from a texture of each of the <see cref="Glyphs"/> atlas's pages, kept up to date as glyphs are added.
/// </summary>
internal sealed class TextureCache : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly GL _gl;
    private readonly string _assetRoot;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);
    private readonly List<string> _changed = [];
    private readonly List<Texture> _glyphPages = [];
    private byte[] _glyphRows = [];
    private readonly Texture _white;
    private readonly Texture _missing;
    private DateTime _lastRefresh = DateTime.UtcNow;

    public TextureCache(GL gl, string assetRoot)
    {
        _gl = gl;
        _assetRoot = assetRoot;
        Glyphs = new GlyphAtlas(assetRoot);
        _white = Upload([255, 255, 255, 255], 1, 1);
        _missing = Upload(Checker(), 2, 2);
    }

    /// <summary>The texture for an Assets-relative path; white for none, a checker if it can't be loaded.</summary>
    public Texture Get(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return _white;

        // Each name's file is worked out once: thousands of sprites a frame look theirs up without making strings.
        if (!_paths.TryGetValue(relativePath, out var path))
            _paths[relativePath] = path = Path.Combine(_assetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!_entries.TryGetValue(path, out var entry))
        {
            entry = Load(path);
            _entries[path] = entry;
        }

        return entry.Texture ?? _missing;
    }

    /// <summary>The glyphs text is drawn with, from font files in the Assets folder, installed fonts or the built-in one.</summary>
    public GlyphAtlas Glyphs { get; }

    /// <summary>
    /// The texture of the atlas's page <paramref name="index"/>, with the glyphs added to the page since it was last asked
    /// for copied to it.
    /// </summary>
    public Texture GlyphPage(int index)
    {
        var page = Glyphs.Pages[index];
        while (_glyphPages.Count <= index)
            _glyphPages.Add(Upload(null, Fonts.GlyphPage.Size, Fonts.GlyphPage.Size, smooth: true));
        var texture = _glyphPages[index];
        if (page.TakeChanges(out var top, out var rows))
        {
            if (_glyphRows.Length < rows * Fonts.GlyphPage.Size * 4)
                _glyphRows = new byte[Fonts.GlyphPage.Size * Fonts.GlyphPage.Size * 4];
            page.CopyRgba(top, rows, _glyphRows);
            _gl.BindTexture(TextureTarget.Texture2D, texture.Handle);
            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            _gl.TexSubImage2D<byte>(TextureTarget.Texture2D, 0, 0, top, Fonts.GlyphPage.Size, (uint)rows, PixelFormat.Rgba, PixelType.UnsignedByte,
                _glyphRows.AsSpan(0, rows * Fonts.GlyphPage.Size * 4));
        }

        return texture;
    }

    /// <summary>Called once per frame, after drawing it.</summary>
    public void EndFrame() => Glyphs.EndFrame();

    /// <summary>Reloads images whose files changed. Checks at most once a second.</summary>
    public void RefreshChanged()
    {
        Glyphs.RefreshChanged();
        var now = DateTime.UtcNow;
        if (now - _lastRefresh < RefreshInterval)
            return;
        _lastRefresh = now;

        // Into a list kept for it, so a check that finds nothing changed makes no garbage.
        _changed.Clear();
        foreach (var (path, entry) in _entries)
        {
            if (Stamp(path) != entry.Stamp)
                _changed.Add(path);
        }

        foreach (var path in _changed)
        {
            if (_entries[path].Texture is { } texture)
                _gl.DeleteTexture(texture.Handle);
            _entries[path] = Load(path);
        }
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values.Where(e => e.Texture is not null))
            _gl.DeleteTexture(entry.Texture!.Handle);
        foreach (var page in _glyphPages)
            _gl.DeleteTexture(page.Handle);
        _gl.DeleteTexture(_white.Handle);
        _gl.DeleteTexture(_missing.Handle);
    }

    private Entry Load(string path)
    {
        var stamp = Stamp(path);
        if (stamp == DateTime.MinValue)
            return new Entry(null, stamp);

        try
        {
            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            return new Entry(Upload(image.Data, image.Width, image.Height), stamp);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Mid-save or unsupported format: show the checker and retry when the file changes again.
            return new Entry(null, stamp);
        }
    }

    // Pixel art is the common jam style, so images sample with nearest-neighbour filtering; text's glyphs are smoothed.
    private unsafe Texture Upload(byte[]? rgba, int width, int height, bool smooth = false)
    {
        var handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        if (rgba is null)
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        else
            _gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
        var filter = smooth ? (int)TextureMinFilter.Linear : (int)TextureMinFilter.Nearest;
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, filter);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, filter);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        return new Texture(handle, width, height);
    }

    private static DateTime Stamp(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

    private static byte[] Checker() =>
    [
        255, 0, 255, 255, 0, 0, 0, 255,
        0, 0, 0, 255, 255, 0, 255, 255,
    ];

    private sealed record Entry(Texture? Texture, DateTime Stamp);
}
