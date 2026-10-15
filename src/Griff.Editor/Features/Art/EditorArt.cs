using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Griff.Art;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Scenes;

namespace Griff.Editor.Features.Art;

/// <summary>
/// Pictures in the editor: the project's images for pickers, small previews of them for the Inspector, reading them
/// to paint over, and the Sprite Painter.
/// </summary>
public sealed class EditorArt
{
    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    private readonly IWorkspace _workspace;
    private readonly Dictionary<string, (DateTime Stamp, BitmapSource? Image)> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private SpritePainterWindow? _painter;

    public EditorArt(IWorkspace workspace)
    {
        _workspace = workspace;
    }

    public string? AssetsPath => _workspace.Project?.AssetsPath;

    /// <summary>
    /// The palette last picked in the Sprite Painter (or in a color field's picker, which offers the same ones): each
    /// starts on it.
    /// </summary>
    public Palette Palette { get; set; } = Palette.Jam;

    /// <summary>Raised with the asset path of each picture the Sprite Painter saves, so previews of it catch up.</summary>
    public event EventHandler<string>? ImageSaved;

    /// <summary>Every image in the project, as asset paths ("Sprites/player.png").</summary>
    public IReadOnlyList<string> Images()
    {
        if (AssetsPath is not { } root || !Directory.Exists(root))
            return [];
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p => Extensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .Select(p => Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(p => !p.StartsWith('.') && !p.StartsWith("Build/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The full path of an asset path, or null when there's no project or no such file.</summary>
    public string? Resolve(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || AssetsPath is not { } root)
            return null;
        var path = Path.Combine(root, assetPath.Trim().Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : null;
    }

    /// <summary>A small preview of an image for the Inspector, or null if there's no such image. Kept until the file changes.</summary>
    public BitmapSource? Thumbnail(string? assetPath)
    {
        if (Resolve(assetPath) is not { } path)
            return null;
        var stamp = File.GetLastWriteTimeUtc(path);
        if (_thumbnails.TryGetValue(path, out var cached) && cached.Stamp == stamp)
            return cached.Image;
        BitmapSource? image = null;
        try
        {
            image = Decode(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or ArgumentException)
        {
        }

        _thumbnails[path] = (stamp, image);
        return image;
    }

    /// <summary>
    /// Reads a picture to paint over: one painted in Griff opens as it was made; any other image is cut up by
    /// <paramref name="layout"/>. Null if it can't be read.
    /// </summary>
    public PixelSheet? Load(string assetPath, SheetLayout? layout = null)
    {
        if (Resolve(assetPath) is not { } path)
            return null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (PixelSheet.FromPng(bytes, layout) is { } painted && SheetLayout.Parse(Png.ReadText(bytes)) is not null)
                return painted;
            if (Decode(bytes) is not { } image)
                return null;
            var pixels = new uint[image.PixelWidth * image.PixelHeight];
            image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            return PixelSheet.FromPixels(pixels, image.PixelWidth, image.PixelHeight,
                layout ?? SheetLayout.Sprite(Math.Max(image.PixelWidth, image.PixelHeight)));
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// An image's pixels, four bytes each (blue, green, red, then alpha), row by row from the top, with its size; null if
    /// there's no such image or it can't be read.
    /// </summary>
    public (byte[] Pixels, int Width, int Height)? Pixels(string? assetPath)
    {
        if (Resolve(assetPath) is not { } path)
            return null;
        try
        {
            if (Decode(File.ReadAllBytes(path)) is not { } image)
                return null;
            var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
            image.CopyPixels(pixels, image.PixelWidth * 4, 0);
            return (pixels, image.PixelWidth, image.PixelHeight);
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>How a picture painted in Griff is cut up, or null for any other image.</summary>
    public SheetLayout? LayoutOf(string? assetPath)
    {
        if (Resolve(assetPath) is not { } path)
            return null;
        try
        {
            return SheetLayout.Parse(Png.ReadText(File.ReadAllBytes(path)));
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// How many tiles across and down <paramref name="image"/> holds as a tileset for <paramref name="tilemap"/>: a picture
    /// painted in Griff says; any other is cut into tiles as many pixels wide and high as the map's first tileset's (16
    /// without one). At least one each way.
    /// </summary>
    public (int Columns, int Rows) TileGrid(string image, Tilemap tilemap)
    {
        if (LayoutOf(image) is { } layout)
            return (layout.Columns, layout.Rows);
        if (Thumbnail(image) is not { } picture)
            return (1, 1);

        var (width, height) = (16.0, 16.0);
        if (Thumbnail(tilemap.Image) is { } first)
            (width, height) = ((double)first.PixelWidth / Math.Max(1, tilemap.Columns), (double)first.PixelHeight / Math.Max(1, tilemap.Rows));
        return (Math.Max(1, (int)Math.Round(picture.PixelWidth / Math.Max(1, width))), Math.Max(1, (int)Math.Round(picture.PixelHeight / Math.Max(1, height))));
    }

    /// <summary>Writes a picture into the project. Returns the full path.</summary>
    public string Save(PixelSheet sheet, string assetPath)
    {
        var root = AssetsPath ?? throw new InvalidOperationException("Open a project to save pictures into it.");
        var path = Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, sheet.ToPng());
        _thumbnails.Remove(path);
        ImageSaved?.Invoke(this, assetPath);
        return path;
    }

    /// <summary>
    /// Opens the Sprite Painter: on <paramref name="editing"/> (an image in the project) to paint over, or on a new
    /// picture laid out as <paramref name="layout"/>. <paramref name="saved"/> is called with the asset path and layout
    /// of each picture it saves.
    /// </summary>
    public SpritePainterWindow OpenPainter(string? editing = null, SheetLayout? layout = null, Action<string, SheetLayout>? saved = null)
    {
        if (_painter is null)
        {
            var model = new SpritePainterViewModel(this, layout ?? SheetLayout.Sprite());
            _painter = new SpritePainterWindow(model) { Owner = Application.Current?.MainWindow };
            _painter.Closed += (_, _) => _painter = null;
            _painter.Show();
        }
        else
        {
            if (editing is null)
                _painter.Model.New(layout ?? SheetLayout.Sprite());
            _painter.Activate();
        }

        if (editing is not null && !_painter.Model.Open(editing, layout))
        {
            // A picture that isn't there yet is painted new under that name; one that can't be read is left alone.
            var exists = Resolve(editing) is not null;
            _painter.Model.New(layout ?? SheetLayout.Sprite(), exists ? null : editing);
            if (exists)
                _painter.Model.Status = $"{editing} can't be painted here (too big, or not a picture)";
        }
        _painter.Model.Saved = saved;
        return _painter;
    }

    /// <summary>The Sprite Painter, while it's open.</summary>
    public SpritePainterWindow? Painter => _painter;

    /// <summary>
    /// Opens the Sprite Painter on what <paramref name="component"/>'s image field shows (or a new picture shaped for
    /// it), and puts each picture it saves to use there as one undo step.
    /// </summary>
    public void PaintFor(Component component, string property = "Image")
    {
        var current = component.GetType().GetProperty(property)?.GetValue(component) as string;
        if (Resolve(current) is null)
            current = null;
        OpenPainter(current, LayoutFor(component), (image, used) =>
        {
            if (_workspace.Scene is { } scene && ArtEdits.UseSheet(_workspace.Components, component, property, image, used) is { } edit)
            {
                scene.Execute(edit);
                scene.History.Seal();
            }
        });
        if (current is null && _painter is not null && component.Entity is { } entity)
            _painter.Model.Name = _painter.Model.SuggestName(Slug(entity.Name));
    }

    /// <summary>
    /// How a picture for <paramref name="component"/> is cut up when it has none, or one painted elsewhere: a
    /// tilemap's tiles, or a sprite's frames.
    /// </summary>
    public static SheetLayout LayoutFor(Component component) => component switch
    {
        Tilemap t => SheetLayout.Tileset(16, Math.Max(1, t.Columns), Math.Max(1, t.Rows)),
        Sprite s when s.Columns * s.Rows > 1 => SheetLayout.Sprite(16, s.Columns * s.Rows) with { Columns = s.Columns, Rows = s.Rows },
        _ => SheetLayout.Sprite(),
    };

    /// <summary>A file name made from an entity's name: "Big Slime" becomes "big-slime".</summary>
    public static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "sprite" : slug;
    }

    // Any image the system can read, as straight-alpha 32-bit pixels.
    private static FormatConvertedBitmap? Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.FirstOrDefault();
        if (frame is null)
            return null;
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }
}
