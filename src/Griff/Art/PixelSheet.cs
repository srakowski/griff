using System.Globalization;
using System.Text;

namespace Griff.Art;

/// <summary>What a painted picture is for: a sprite's frames played in order, or a tilemap's tiles.</summary>
public enum SheetKind
{
    Sprite,
    Tileset,
}

/// <summary>
/// How a painted picture is cut up: cells of <see cref="CellWidth"/> by <see cref="CellHeight"/> pixels,
/// <see cref="Columns"/> across and <see cref="Rows"/> down. A sprite uses the first <see cref="Frames"/> cells as
/// frames played <see cref="FramesPerSecond"/> times a second.
/// </summary>
public sealed record SheetLayout(SheetKind Kind, int CellWidth, int CellHeight, int Columns, int Rows, int Frames, float FramesPerSecond)
{
    public const int MaxCellSize = 256;
    public const int MaxCells = 64;

    public static SheetLayout Sprite(int size = 16, int frames = 1, float fps = 8) =>
        new(SheetKind.Sprite, size, size, frames, 1, frames, fps);

    public static SheetLayout Tileset(int size = 16, int columns = 4, int rows = 4) =>
        new(SheetKind.Tileset, size, size, columns, rows, columns * rows, 8);

    public int Cells => Columns * Rows;

    public int Width => CellWidth * Columns;

    public int Height => CellHeight * Rows;

    /// <summary>Sizes and counts brought into range.</summary>
    public SheetLayout Clamped()
    {
        var width = Math.Clamp(CellWidth, 1, MaxCellSize);
        var height = Math.Clamp(CellHeight, 1, MaxCellSize);
        var columns = Math.Clamp(Columns, 1, MaxCells);
        var rows = Math.Clamp(Rows, 1, Math.Max(1, MaxCells / columns));
        var frames = Math.Clamp(Frames, 1, columns * rows);
        var fps = float.IsFinite(FramesPerSecond) ? Math.Clamp(FramesPerSecond, 1, 60) : 8;
        return new SheetLayout(Kind, width, height, columns, rows, frames, fps);
    }

    /// <summary>The layout as the text kept in a painted PNG.</summary>
    public string ToText()
    {
        var text = new StringBuilder("griff-art 1\n");
        text.Append(CultureInfo.InvariantCulture, $"kind {Kind.ToString().ToLowerInvariant()}\n");
        text.Append(CultureInfo.InvariantCulture, $"cell {CellWidth} {CellHeight}\n");
        text.Append(CultureInfo.InvariantCulture, $"grid {Columns} {Rows}\n");
        text.Append(CultureInfo.InvariantCulture, $"frames {Frames}\n");
        text.Append(CultureInfo.InvariantCulture, $"fps {FramesPerSecond}\n");
        return text.ToString();
    }

    /// <summary>Reads <see cref="ToText"/>; null if it isn't a layout.</summary>
    public static SheetLayout? Parse(string? text)
    {
        if (text is null || !text.StartsWith("griff-art ", StringComparison.Ordinal))
            return null;
        var layout = Sprite();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int Int(int i) => parts.Length > i && int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 1;
            layout = parts[0] switch
            {
                "kind" when parts.Length > 1 => layout with { Kind = parts[1] == "tileset" ? SheetKind.Tileset : SheetKind.Sprite },
                "cell" => layout with { CellWidth = Int(1), CellHeight = Int(2) },
                "grid" => layout with { Columns = Int(1), Rows = Int(2) },
                "frames" => layout with { Frames = Int(1) },
                "fps" when parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) => layout with { FramesPerSecond = fps },
                _ => layout,
            };
        }

        return layout.Clamped();
    }
}

/// <summary>
/// A picture being painted, cut into cells (a sprite's frames or a tileset's tiles). Pixels are 0xAARRGGBB, which
/// is also how a Bgra32 bitmap holds them; 0 is clear. Drawing happens within one cell at a time, in that cell's
/// own pixel coordinates (0, 0 at its top left), and never spills into the next.
/// </summary>
public sealed class PixelSheet
{
    private uint[] _pixels;

    public PixelSheet(SheetLayout layout)
    {
        Layout = layout.Clamped();
        _pixels = new uint[Layout.Width * Layout.Height];
    }

    public SheetLayout Layout { get; private set; }

    public int Width => Layout.Width;

    public int Height => Layout.Height;

    /// <summary>The whole picture, row by row.</summary>
    public ReadOnlySpan<uint> Pixels => _pixels;

    /// <summary>A picture from a file's pixels, cut up by <paramref name="layout"/> (whose cell size is worked out from the picture).</summary>
    /// <exception cref="ArgumentException">The cells would be bigger than <see cref="SheetLayout.MaxCellSize"/>.</exception>
    public static PixelSheet FromPixels(uint[] pixels, int width, int height, SheetLayout layout)
    {
        var columns = Math.Clamp(layout.Columns, 1, Math.Max(1, width));
        var rows = Math.Clamp(layout.Rows, 1, Math.Max(1, height));
        if (width / columns > SheetLayout.MaxCellSize || height / rows > SheetLayout.MaxCellSize)
            throw new ArgumentException($"Frames bigger than {SheetLayout.MaxCellSize} pixels can't be painted.", nameof(layout));
        var sheet = new PixelSheet(layout with { CellWidth = Math.Max(1, width / columns), CellHeight = Math.Max(1, height / rows), Columns = columns, Rows = rows });
        for (var y = 0; y < Math.Min(height, sheet.Height); y++)
            for (var x = 0; x < Math.Min(width, sheet.Width); x++)
                sheet._pixels[y * sheet.Width + x] = pixels[y * width + x];
        return sheet;
    }

    /// <summary>A picture from RGBA bytes.</summary>
    public static PixelSheet FromRgba(byte[] rgba, int width, int height, SheetLayout layout)
    {
        var pixels = new uint[width * height];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = Pack(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
        return FromPixels(pixels, width, height, layout);
    }

    /// <summary>Reads a PNG that Griff wrote (keeping its layout), or null for other files.</summary>
    public static PixelSheet? FromPng(byte[] png, SheetLayout? fallback = null)
    {
        if (Png.Decode(png) is not var (rgba, width, height))
            return null;
        return FromRgba(rgba, width, height, SheetLayout.Parse(Png.ReadText(png)) ?? fallback ?? SheetLayout.Sprite(Math.Max(width, height)));
    }

    public byte[] ToRgba()
    {
        var rgba = new byte[_pixels.Length * 4];
        for (var i = 0; i < _pixels.Length; i++)
            (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]) = Unpack(_pixels[i]);
        return rgba;
    }

    /// <summary>The picture as a PNG that remembers its layout.</summary>
    public byte[] ToPng() => Png.Encode(ToRgba(), Width, Height, Layout.ToText());

    public static uint Pack(byte r, byte g, byte b, byte a) => a == 0 ? 0 : (uint)(a << 24 | r << 16 | g << 8 | b);

    public static (byte R, byte G, byte B, byte A) Unpack(uint color) =>
        ((byte)(color >> 16), (byte)(color >> 8), (byte)color, (byte)(color >> 24));

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Layout.CellWidth && y < Layout.CellHeight;

    public uint Get(int cell, int x, int y) => Contains(x, y) && IsCell(cell) ? _pixels[Index(cell, x, y)] : 0;

    public void Set(int cell, int x, int y, uint color)
    {
        if (Contains(x, y) && IsCell(cell))
            _pixels[Index(cell, x, y)] = color;
    }

    /// <summary>One cell's pixels, row by row.</summary>
    public uint[] Cell(int cell)
    {
        var (w, h) = (Layout.CellWidth, Layout.CellHeight);
        var pixels = new uint[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                pixels[y * w + x] = Get(cell, x, y);
        return pixels;
    }

    /// <summary>Puts pixels (row by row, the cell's size) into a cell.</summary>
    public void SetCell(int cell, ReadOnlySpan<uint> pixels)
    {
        var (w, h) = (Layout.CellWidth, Layout.CellHeight);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                Set(cell, x, y, y * w + x < pixels.Length ? pixels[y * w + x] : 0);
    }

    public void Clear(int cell) => SetCell(cell, []);

    /// <summary>A line of pixels from one point to another, both included.</summary>
    public void Line(int cell, int x0, int y0, int x1, int y1, uint color)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            Set(cell, x0, y0, color);
            if (x0 == x1 && y0 == y1)
                return;
            var e2 = 2 * error;
            if (e2 >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    /// <summary>A rectangle between two corners: its outline, or filled.</summary>
    public void Rectangle(int cell, int x0, int y0, int x1, int y1, uint color, bool filled)
    {
        var (left, right) = (Math.Min(x0, x1), Math.Max(x0, x1));
        var (top, bottom) = (Math.Min(y0, y1), Math.Max(y0, y1));
        for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
                if (filled || x == left || x == right || y == top || y == bottom)
                    Set(cell, x, y, color);
    }

    /// <summary>An ellipse inside the box between two corners: its outline, or filled.</summary>
    public void Ellipse(int cell, int x0, int y0, int x1, int y1, uint color, bool filled)
    {
        var (left, right) = (Math.Min(x0, x1), Math.Max(x0, x1));
        var (top, bottom) = (Math.Min(y0, y1), Math.Max(y0, y1));
        double cx = (left + right) / 2.0, cy = (top + bottom) / 2.0;
        double rx = (right - left) / 2.0 + 0.5, ry = (bottom - top) / 2.0 + 0.5;
        bool Inside(int x, int y) => Math.Pow((x - cx) / rx, 2) + Math.Pow((y - cy) / ry, 2) <= 1.0;
        for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
            {
                if (!Inside(x, y))
                    continue;
                // The outline is the inside pixels next to an outside one.
                if (filled || !Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1))
                    Set(cell, x, y, color);
            }
    }

    /// <summary>Fills the area of one color that (x, y) is in, up to its edges (not diagonally).</summary>
    public void Fill(int cell, int x, int y, uint color)
    {
        if (!Contains(x, y) || !IsCell(cell))
            return;
        var target = Get(cell, x, y);
        if (target == color)
            return;
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        while (stack.TryPop(out var p))
        {
            if (!Contains(p.X, p.Y) || Get(cell, p.X, p.Y) != target)
                continue;
            Set(cell, p.X, p.Y, color);
            stack.Push((p.X + 1, p.Y));
            stack.Push((p.X - 1, p.Y));
            stack.Push((p.X, p.Y + 1));
            stack.Push((p.X, p.Y - 1));
        }
    }

    /// <summary>Mirrors a cell left to right, or top to bottom.</summary>
    public void Flip(int cell, bool horizontal)
    {
        var (w, h) = (Layout.CellWidth, Layout.CellHeight);
        var old = Cell(cell);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                Set(cell, x, y, horizontal ? old[y * w + (w - 1 - x)] : old[(h - 1 - y) * w + x]);
    }

    /// <summary>Slides a cell's pixels by (dx, dy), wrapping round the edges.</summary>
    public void Shift(int cell, int dx, int dy)
    {
        var (w, h) = (Layout.CellWidth, Layout.CellHeight);
        var old = Cell(cell);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                Set(cell, ((x + dx) % w + w) % w, ((y + dy) % h + h) % h, old[y * w + x]);
    }

    /// <summary>Draws a dark outline round everything in a cell that isn't clear.</summary>
    public void Outline(int cell, uint color)
    {
        var (w, h) = (Layout.CellWidth, Layout.CellHeight);
        var old = Cell(cell);
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && old[y * w + x] != 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (!Solid(x, y) && (Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y - 1) || Solid(x, y + 1)))
                    Set(cell, x, y, color);
    }

    /// <summary>
    /// Changes the layout, keeping each cell's pixels where it can: cells keep their number (frame 3 stays frame 3)
    /// and their top-left corner when the cell size changes.
    /// </summary>
    public void Relayout(SheetLayout layout)
    {
        var old = this.Copy();
        Layout = layout.Clamped();
        _pixels = new uint[Layout.Width * Layout.Height];
        for (var cell = 0; cell < Math.Min(old.Layout.Cells, Layout.Cells); cell++)
            for (var y = 0; y < Math.Min(old.Layout.CellHeight, Layout.CellHeight); y++)
                for (var x = 0; x < Math.Min(old.Layout.CellWidth, Layout.CellWidth); x++)
                    Set(cell, x, y, old.Get(cell, x, y));
    }

    /// <summary>Puts in a new cell at <paramref name="at"/> (a copy of <paramref name="copyOf"/>, or clear), moving later cells along.</summary>
    public void InsertCell(int at, int? copyOf = null)
    {
        var cells = Enumerable.Range(0, Layout.Cells).Select(Cell).ToList();
        cells.Insert(Math.Clamp(at, 0, cells.Count), copyOf is { } from && from >= 0 && from < cells.Count ? (uint[])cells[from].Clone() : []);
        var frames = Math.Min(Layout.Frames + 1, SheetLayout.MaxCells);
        var layout = Layout.Kind == SheetKind.Sprite
            ? Layout with { Columns = frames, Rows = 1, Frames = frames }
            : Layout with { Rows = (cells.Count + Layout.Columns - 1) / Layout.Columns, Frames = Layout.Frames + 1 };
        Rebuild(layout, cells);
    }

    /// <summary>Takes a cell out, moving later cells back. A sheet always keeps one.</summary>
    public void RemoveCell(int at)
    {
        if (Layout.Frames <= 1 || at < 0 || at >= Layout.Cells)
            return;
        var cells = Enumerable.Range(0, Layout.Cells).Select(Cell).ToList();
        cells.RemoveAt(at);
        var frames = Layout.Frames - 1;
        var layout = Layout.Kind == SheetKind.Sprite ? Layout with { Columns = frames, Rows = 1, Frames = frames } : Layout with { Frames = frames };
        Rebuild(layout, cells);
    }

    /// <summary>Swaps two cells' pixels.</summary>
    public void SwapCells(int a, int b)
    {
        if (!IsCell(a) || !IsCell(b) || a == b)
            return;
        var first = Cell(a);
        SetCell(a, Cell(b));
        SetCell(b, first);
    }

    public PixelSheet Copy()
    {
        var copy = new PixelSheet(Layout);
        _pixels.CopyTo(copy._pixels, 0);
        return copy;
    }

    /// <summary>Makes this sheet the same as <paramref name="other"/>: for undo.</summary>
    public void CopyFrom(PixelSheet other)
    {
        Layout = other.Layout;
        _pixels = (uint[])other._pixels.Clone();
    }

    public bool SameAs(PixelSheet other) => Layout == other.Layout && _pixels.AsSpan().SequenceEqual(other._pixels);

    private void Rebuild(SheetLayout layout, List<uint[]> cells)
    {
        Layout = layout.Clamped();
        _pixels = new uint[Layout.Width * Layout.Height];
        for (var cell = 0; cell < Math.Min(cells.Count, Layout.Cells); cell++)
            SetCell(cell, cells[cell]);
    }

    private bool IsCell(int cell) => cell >= 0 && cell < Layout.Cells;

    private int Index(int cell, int x, int y)
    {
        var column = cell % Layout.Columns;
        var row = cell / Layout.Columns;
        return (row * Layout.CellHeight + y) * Layout.Width + column * Layout.CellWidth + x;
    }
}
