using System.Numerics;

namespace Griff.Runtime;

/// <summary>
/// A broadphase for physics: shapes (by their number in a list) filed in the squares of a grid they overlap, so a moving
/// body only tests the shapes near it rather than every one in the scene. Shapes are moved in it as they move.
/// </summary>
internal sealed class ShapeGrid
{
    // A shape covering more squares than this is kept aside and tested by everything (a long floor, a huge trigger).
    private const int MaxSquares = 64;

    private readonly Dictionary<(int X, int Y), List<int>> _squares = [];
    private readonly Stack<List<int>> _spare = [];

    // Each shape's squares (or Large), and the shapes kept aside.
    private readonly List<Span> _spans = [];
    private readonly List<int> _large = [];

    // Which shapes a query has already found, by the query's stamp.
    private int[] _seen = [];
    private int _stamp;

    private Box[] _bounds = [];
    private float _size = 1;

    /// <summary>How many shapes are in it.</summary>
    public int Count => _spans.Count;

    /// <summary>The side of each square, in world units: twice the typical shape's.</summary>
    public float SquareSize => _size;

    /// <summary>Empties the grid and files <paramref name="count"/> shapes, numbered from 0, by their bounds.</summary>
    public void Build(int count, Func<int, Box> boundsOf)
    {
        foreach (var square in _squares.Values)
        {
            square.Clear();
            _spare.Push(square);
        }

        _squares.Clear();
        _spans.Clear();
        _large.Clear();
        if (_seen.Length < count)
        {
            _seen = new int[Math.Max(count, _seen.Length * 2)];
            _bounds = new Box[_seen.Length];
        }

        // Squares about twice the size of a typical shape (going by its narrow side, so long tile runs don't count).
        var sum = 0f;
        var sized = 0;
        var bounds = _bounds;
        for (var i = 0; i < count; i++)
        {
            bounds[i] = boundsOf(i);
            var narrow = MathF.Min(bounds[i].HalfSize.X, bounds[i].HalfSize.Y) * 2;
            if (float.IsFinite(narrow) && narrow > 0)
            {
                sum += narrow;
                sized++;
            }
        }

        _size = Math.Clamp(sized > 0 ? sum / sized * 2 : 1, 0.5f, 64f);
        for (var i = 0; i < count; i++)
        {
            var span = SpanOf(bounds[i]);
            _spans.Add(span);
            File(i, span);
        }
    }

    /// <summary>Moves shape <paramref name="shape"/> to where its bounds are now.</summary>
    public void Move(int shape, Box bounds)
    {
        var from = _spans[shape];
        var to = SpanOf(bounds);
        if (from == to)
            return;
        Unfile(shape, from);
        _spans[shape] = to;
        File(shape, to);
    }

    /// <summary>
    /// Fills <paramref name="found"/> with the shapes whose squares <paramref name="region"/> touches, in order: every
    /// shape that overlaps it, and some near it.
    /// </summary>
    public void Near(Box region, List<int> found)
    {
        found.Clear();
        var span = SpanOf(region);

        // A region bigger than the shapes have squares for: just test them all.
        if (span.IsLarge || (long)(span.X1 - span.X0 + 1) * (span.Y1 - span.Y0 + 1) > Math.Max(_squares.Count, 16))
        {
            for (var i = 0; i < _spans.Count; i++)
                found.Add(i);
            return;
        }

        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seen);
            _stamp = 1;
        }

        foreach (var shape in _large)
            Found(shape, found);
        for (var x = span.X0; x <= span.X1; x++)
        {
            for (var y = span.Y0; y <= span.Y1; y++)
            {
                if (!_squares.TryGetValue((x, y), out var square))
                    continue;
                foreach (var shape in square)
                    Found(shape, found);
            }
        }

        found.Sort();
    }

    private void Found(int shape, List<int> found)
    {
        if (_seen[shape] == _stamp)
            return;
        _seen[shape] = _stamp;
        found.Add(shape);
    }

    private void File(int shape, Span span)
    {
        if (span.IsLarge)
        {
            _large.Add(shape);
            return;
        }

        for (var x = span.X0; x <= span.X1; x++)
        {
            for (var y = span.Y0; y <= span.Y1; y++)
            {
                if (!_squares.TryGetValue((x, y), out var square))
                    _squares[(x, y)] = square = _spare.Count > 0 ? _spare.Pop() : [];
                square.Add(shape);
            }
        }
    }

    private void Unfile(int shape, Span span)
    {
        if (span.IsLarge)
        {
            _large.Remove(shape);
            return;
        }

        for (var x = span.X0; x <= span.X1; x++)
        {
            for (var y = span.Y0; y <= span.Y1; y++)
            {
                if (_squares.TryGetValue((x, y), out var square))
                    square.Remove(shape);
            }
        }
    }

    private Span SpanOf(Box bounds)
    {
        var min = bounds.Min / _size;
        var max = bounds.Max / _size;
        if (!(float.IsFinite(min.X) && float.IsFinite(min.Y) && float.IsFinite(max.X) && float.IsFinite(max.Y)) ||
            MathF.Abs(min.X) > 1e6f || MathF.Abs(min.Y) > 1e6f || MathF.Abs(max.X) > 1e6f || MathF.Abs(max.Y) > 1e6f)
            return Span.Large;
        var span = new Span((int)MathF.Floor(min.X), (int)MathF.Floor(min.Y), (int)MathF.Floor(max.X), (int)MathF.Floor(max.Y));
        return (long)(span.X1 - span.X0 + 1) * (span.Y1 - span.Y0 + 1) > MaxSquares ? Span.Large : span;
    }

    // The squares a shape covers, from (X0, Y0) to (X1, Y1).
    private readonly record struct Span(int X0, int Y0, int X1, int Y1)
    {
        public static readonly Span Large = new(1, 1, 0, 0);

        public bool IsLarge => X0 > X1;
    }
}
