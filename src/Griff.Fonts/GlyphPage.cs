namespace Griff.Fonts;

/// <summary>
/// One page of a <see cref="GlyphAtlas"/>: <see cref="Size"/> pixels square of coverage (0 to 255 a pixel), which glyphs
/// are rasterized into as they're first drawn. A renderer keeps a texture of it, white with the coverage as alpha, and
/// brings it up to date from <see cref="TakeChanges"/> before drawing from it.
/// </summary>
public sealed class GlyphPage
{
    /// <summary>The width and height of a page, in pixels.</summary>
    public const int Size = 1024;

    // Glyphs are packed in rows (shelves), left to right, each a pixel of nothing from the next.
    private int _rowTop;
    private int _rowHeight;
    private int _rowX;
    private int _changedTop = int.MaxValue;
    private int _changedBottom = -1;

    // A new page is all to copy, so a renderer's texture of it starts out clear.
    internal GlyphPage(int index)
    {
        Index = index;
        Changed(0, Size);
    }

    /// <summary>Which page it is: its place in <see cref="GlyphAtlas.Pages"/>.</summary>
    public int Index { get; }

    /// <summary>The coverage, row by row from the top, <see cref="Size"/> bytes a row.</summary>
    public byte[] Coverage { get; } = new byte[Size * Size];

    /// <summary>Whether rows have changed since the last <see cref="TakeChanges"/>.</summary>
    public bool HasChanges => _changedBottom >= _changedTop;

    /// <summary>
    /// The rows changed since it was last asked (<paramref name="top"/> and how many), if any, which a renderer copies to
    /// its texture (see <see cref="CopyRgba"/>).
    /// </summary>
    public bool TakeChanges(out int top, out int rows)
    {
        (top, rows) = (_changedTop, _changedBottom - _changedTop + 1);
        if (!HasChanges)
            return false;
        (_changedTop, _changedBottom) = (int.MaxValue, -1);
        return true;
    }

    /// <summary>
    /// Writes <paramref name="rows"/> rows from <paramref name="top"/> as straight-alpha RGBA, white with the coverage as
    /// alpha (the tint colors it, and white under clear pixels keeps smoothed edges from going dark).
    /// </summary>
    public void CopyRgba(int top, int rows, Span<byte> into)
    {
        var coverage = Coverage.AsSpan(top * Size, rows * Size);
        if (into.Length < coverage.Length * 4)
            throw new ArgumentException("Too small for the rows.", nameof(into));
        for (var i = 0; i < coverage.Length; i++)
        {
            var at = i * 4;
            into[at] = into[at + 1] = into[at + 2] = 255;
            into[at + 3] = coverage[i];
        }
    }

    /// <summary>Finds room for a box this big (with a pixel round it), or says there's none left.</summary>
    internal bool TryPlace(int width, int height, out int x, out int y)
    {
        var (w, h) = (width + 2, height + 2);
        if (w > Size || h > Size)
        {
            (x, y) = (0, 0);
            return false;
        }

        if (_rowX + w > Size)
            (_rowTop, _rowX, _rowHeight) = (_rowTop + _rowHeight, 0, 0);
        if (_rowTop + h > Size)
        {
            (x, y) = (0, 0);
            return false;
        }

        (x, y) = (_rowX, _rowTop);
        _rowX += w;
        _rowHeight = Math.Max(_rowHeight, h);
        return true;
    }

    /// <summary>Marks rows as changed, for the renderer to copy.</summary>
    internal void Changed(int top, int rows)
    {
        _changedTop = Math.Min(_changedTop, top);
        _changedBottom = Math.Max(_changedBottom, top + rows - 1);
    }

    /// <summary>Empties the page: nothing on it, and all of it to copy again.</summary>
    internal void Clear()
    {
        Array.Clear(Coverage);
        (_rowTop, _rowHeight, _rowX) = (0, 0, 0);
        Changed(0, Size);
    }
}
