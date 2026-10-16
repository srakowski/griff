namespace Griff.Runtime;

/// <summary>
/// Puts items in order by a few keys at once, keeping the order they came in among equals, in time that grows only with
/// how many there are (a radix sort): a frame of tens of thousands of sprites is put back to front without comparing
/// them. Keys are given least important first (fill <see cref="Keys"/>, then <see cref="Apply"/>, for each), each byte of
/// a key a pass that's skipped when every item's is the same, so a big scene on one layer costs a look at each key and
/// little more. Items already in order but for a few stretches are merged instead (<see cref="TryMerge"/>). Reused from
/// frame to frame, it allocates nothing once it's held the most items it will.
/// </summary>
internal sealed class StableSort
{
    private int[] _order = [];
    private int[] _spare = [];
    private uint[] _keys = [];
    private readonly int[] _counts = new int[4 * 256];

    /// <summary>How many items are being put in order, and the order so far: item indexes, first to last.</summary>
    public int Count { get; private set; }

    public ReadOnlySpan<int> Order => _order.AsSpan(0, Count);

    /// <summary>Starts putting <paramref name="count"/> items in order; until a key is added, they're in the order they came.</summary>
    public void Begin(int count)
    {
        Make(count);
        for (var i = 0; i < count; i++)
            _order[i] = i;
    }

    private void Make(int count)
    {
        if (_order.Length < count)
        {
            var size = Math.Max(count, _order.Length * 2);
            (_order, _spare, _keys) = (new int[size], new int[size], new uint[size]);
        }

        Count = count;
    }

    /// <summary>
    /// Puts <paramref name="items"/> in order by <paramref name="comparer"/> when they're in order already but for at most
    /// <paramref name="maxRuns"/> stretches of them (a crowd on one layer with a few things over it, say), by merging the
    /// stretches rather than sorting: the order sorting by the same keys gives, keeping the order they came in among equals,
    /// in a look at each item and a little more. Returns how many stretches there were, <see cref="Order"/> then being
    /// theirs (all of them in the order they came, for one), or 0, having done nothing, for more than that.
    /// </summary>
    public int TryMerge<T, TComparer>(ReadOnlySpan<T> items, TComparer comparer, int maxRuns) where TComparer : struct, IComparer<T>
    {
        // Where each stretch starts, and then where the last one ends.
        Span<int> starts = stackalloc int[maxRuns + 1];
        var runs = 1;
        for (var i = 1; i < items.Length; i++)
        {
            if (comparer.Compare(items[i - 1], items[i]) <= 0)
                continue;
            if (runs == maxRuns)
                return 0;
            starts[runs++] = i;
        }

        starts[runs] = items.Length;
        Make(items.Length);
        var order = _order.AsSpan(0, items.Length);
        if (runs == 1)
        {
            for (var i = 0; i < order.Length; i++)
                order[i] = i;
            return 1;
        }

        // Each stretch's next item, and where it ends.
        Span<int> heads = stackalloc int[runs];
        Span<int> ends = stackalloc int[runs];
        for (var r = 0; r < runs; r++)
            (heads[r], ends[r]) = (starts[r], starts[r + 1]);

        var at = 0;
        while (at < order.Length)
        {
            // The stretch whose next item goes first: the earliest of those that tie, keeping the order they came in.
            var first = -1;
            for (var r = 0; r < runs; r++)
            {
                if (heads[r] < ends[r] && (first < 0 || comparer.Compare(items[heads[r]], items[heads[first]]) < 0))
                    first = r;
            }

            // It goes on until another stretch's next item has to come first: at a tie, one that came before it does.
            var end = ends[first];
            for (var r = 0; r < runs; r++)
            {
                if (r != first && heads[r] < ends[r])
                    end = Cut(items, heads[first], end, items[heads[r]], comparer, strictly: r < first);
            }

            for (var i = heads[first]; i < end; i++)
                order[at++] = i;
            heads[first] = end;
        }

        return runs;
    }

    // Where the items from `from` (which goes before `next`) to `to`, in order, stop going before `next` (at a tie, they
    // don't when `strictly`). Looks one ahead, then two, four and so on, then halves what's left: a short stretch is found
    // in a few looks, a long one in a few dozen.
    private static int Cut<T, TComparer>(ReadOnlySpan<T> items, int from, int to, in T next, TComparer comparer, bool strictly)
        where TComparer : struct, IComparer<T>
    {
        var ahead = from + 1;
        var step = 1;
        while (ahead < to && GoesFirst(items[ahead], next, comparer, strictly))
        {
            from = ahead;
            ahead = Math.Min(to, ahead + step);
            step *= 2;
        }

        // Past `from`, and no further than `ahead`.
        var (low, high) = (from + 1, ahead);
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (GoesFirst(items[middle], next, comparer, strictly))
                low = middle + 1;
            else
                high = middle;
        }

        return low;

        static bool GoesFirst(in T item, in T next, TComparer comparer, bool strictly)
        {
            var c = comparer.Compare(item, next);
            return strictly ? c < 0 : c <= 0;
        }
    }

    /// <summary>The keys of the items, by item index, to fill before <see cref="Apply"/>.</summary>
    public Span<uint> Keys => _keys.AsSpan(0, Count);

    /// <summary>Orders the items by <see cref="Keys"/> (lowest first), keeping the order they had among equals.</summary>
    public void Apply()
    {
        var count = Count;
        var keys = _keys;
        var counts = _counts.AsSpan();
        counts.Clear();
        for (var i = 0; i < count; i++)
        {
            var key = keys[i];
            counts[(int)(key & 0xFF)]++;
            counts[256 + (int)((key >> 8) & 0xFF)]++;
            counts[512 + (int)((key >> 16) & 0xFF)]++;
            counts[768 + (int)(key >> 24)]++;
        }

        for (var digit = 0; digit < 4; digit++)
        {
            var digitCounts = counts.Slice(digit * 256, 256);
            var shift = digit * 8;
            // Every item has the same digit here: this pass would leave them as they are.
            if (digitCounts[(int)((keys[_order[0]] >> shift) & 0xFF)] == count)
                continue;

            var at = 0;
            for (var bucket = 0; bucket < 256; bucket++)
                (digitCounts[bucket], at) = (at, at + digitCounts[bucket]);
            for (var i = 0; i < count; i++)
            {
                var item = _order[i];
                _spare[digitCounts[(int)((keys[item] >> shift) & 0xFF)]++] = item;
            }

            (_order, _spare) = (_spare, _order);
        }
    }

    /// <summary>A float as a key that orders as <see cref="float.CompareTo(float)"/> does: NaN first, and 0 and -0 alike.</summary>
    public static uint KeyOf(float value)
    {
        if (float.IsNaN(value))
            return 0;
        if (value == 0)
            return 0x80000000;
        var bits = BitConverter.SingleToUInt32Bits(value);
        return (bits & 0x80000000) != 0 ? ~bits : bits | 0x80000000;
    }

    /// <summary>An int as a key that orders as the int does.</summary>
    public static uint KeyOf(int value) => (uint)value ^ 0x80000000;
}
