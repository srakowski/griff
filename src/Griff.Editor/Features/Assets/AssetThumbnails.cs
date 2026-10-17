using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Griff.Editor.Features.Assets;

/// <summary>
/// Small pictures of the images in Assets, for the Assets panel's tiles. Each is read once and kept until its file
/// changes (it's read again when its time or size on disk differs, or when the panel hears it changed).
/// </summary>
public sealed class AssetThumbnails
{
    /// <summary>The longest side a thumbnail keeps, in pixels; bigger pictures are shrunk to it, smaller kept as they are.</summary>
    public const int MaxSize = 128;

    private readonly Dictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many times a picture has been read from disk (rather than kept), for tests.</summary>
    internal int Reads { get; private set; }

    /// <summary>The picture's thumbnail, or null if it isn't there or can't be read as a picture.</summary>
    public ImageSource? Get(string fullPath)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                _cache.Remove(fullPath);
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        if (_cache.TryGetValue(fullPath, out var kept) && kept.Written == info.LastWriteTimeUtc && kept.Length == info.Length)
            return kept.Picture;

        Reads++;
        var (picture, readable) = Load(fullPath);
        if (!readable)
            return null; // Being written just now, say: tried again next time.
        _cache[fullPath] = new Entry(info.LastWriteTimeUtc, info.Length, picture);
        return picture;
    }

    /// <summary>Forgets a picture's thumbnail, so it's read again the next time it's shown.</summary>
    public void Forget(string fullPath) => _cache.Remove(fullPath);

    // The picture shrunk to fit MaxSize, and whether the file could be read at all (a file that isn't a picture is
    // readable, with no picture).
    private static (ImageSource? Picture, bool Readable) Load(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, false);
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            BitmapSource frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            var longest = Math.Max(frame.PixelWidth, frame.PixelHeight);
            if (longest > MaxSize)
            {
                var scale = (double)MaxSize / longest;
                frame = new CachedBitmap(new TransformedBitmap(frame, new ScaleTransform(scale, scale)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }

            frame.Freeze();
            return (frame, true);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or IOException)
        {
            return (null, true);
        }
    }

    private sealed record Entry(DateTime Written, long Length, ImageSource? Picture);
}
