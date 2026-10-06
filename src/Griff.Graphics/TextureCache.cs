using Griff.Scenes;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace Griff.Graphics;

/// <summary>
/// Renders a string to straight-alpha RGBA pixels (white glyphs; the sprite tint colors them), in a font (see
/// <see cref="Text.Font"/>; null for the built-in one), lining up several lines by <paramref name="align"/>.
/// </summary>
public delegate (byte[] Rgba, int Width, int Height) TextRasterizer(string text, string? font, TextAlign align);

public sealed class Texture(uint handle, int width, int height)
{
    public uint Handle { get; } = handle;

    public int Width { get; } = width;

    public int Height { get; } = height;
}

/// <summary>
/// Loads images from the project's Assets folder on first use and reloads them when the file changes,
/// so editing a sprite in a paint program updates the running game. Missing images draw as a magenta checker.
/// </summary>
internal sealed class TextureCache : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly GL _gl;
    private readonly string _assetRoot;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Text, string? Font, TextAlign Align), (Texture Texture, long LastUsed)> _text = [];
    private readonly TextRasterizer? _rasterizer;
    private long _frame;
    private readonly Texture _white;
    private readonly Texture _missing;
    private DateTime _lastRefresh = DateTime.UtcNow;

    public TextureCache(GL gl, string assetRoot, TextRasterizer? rasterizer)
    {
        _gl = gl;
        _assetRoot = assetRoot;
        _rasterizer = rasterizer;
        _white = Upload([255, 255, 255, 255], 1, 1);
        _missing = Upload(Checker(), 2, 2);
    }

    /// <summary>The texture for an Assets-relative path; white for none, a checker if it can't be loaded.</summary>
    public Texture Get(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return _white;

        var path = Path.Combine(_assetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!_entries.TryGetValue(path, out var entry))
        {
            entry = Load(path);
            _entries[path] = entry;
        }

        return entry.Texture ?? _missing;
    }

    /// <summary>A texture of <paramref name="text"/>, or null if no rasterizer is available.</summary>
    public Texture? GetText(string text, string? font, TextAlign align)
    {
        if (_rasterizer is null)
            return null;

        var key = (text, font, align);
        if (!_text.TryGetValue(key, out var entry))
        {
            var (rgba, width, height) = _rasterizer(text, font, align);
            entry = (Upload(rgba, width, height, smooth: true), _frame);
        }

        _text[key] = entry with { LastUsed = _frame };
        return entry.Texture;
    }

    /// <summary>
    /// Called once per frame. Frees text textures unused for a few seconds, so text that changes every
    /// frame (scores, timers) doesn't pile up.
    /// </summary>
    public void EndFrame()
    {
        _frame++;
        if (_frame % 60 != 0)
            return;

        foreach (var (key, entry) in _text.Where(t => _frame - t.Value.LastUsed > 120).ToList())
        {
            _gl.DeleteTexture(entry.Texture.Handle);
            _text.Remove(key);
        }
    }

    /// <summary>Reloads images whose files changed. Checks at most once a second.</summary>
    public void RefreshChanged()
    {
        var now = DateTime.UtcNow;
        if (now - _lastRefresh < RefreshInterval)
            return;
        _lastRefresh = now;

        foreach (var (path, entry) in _entries.ToList())
        {
            if (Stamp(path) == entry.Stamp)
                continue;
            if (entry.Texture is not null)
                _gl.DeleteTexture(entry.Texture.Handle);
            _entries[path] = Load(path);
        }
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values.Where(e => e.Texture is not null))
            _gl.DeleteTexture(entry.Texture!.Handle);
        foreach (var entry in _text.Values)
            _gl.DeleteTexture(entry.Texture.Handle);
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

    // Pixel art is the common jam style, so images sample with nearest-neighbour filtering; text is smoothed.
    private Texture Upload(byte[] rgba, int width, int height, bool smooth = false)
    {
        var handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
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
