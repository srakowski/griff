namespace Griff.Rendering;

/// <summary>
/// Puts items in order by a few keys at once, keeping the order they came in among equals, in time that grows only with
/// how many there are (a radix sort): a frame of tens of thousands of sprites is put back to front without comparing
/// them. Keys are given least important first (fill <see cref="Keys"/>, then <see cref="Apply"/>, for each), each byte of
/// a key a pass that's skipped when every item's is the same, so a big scene on one layer costs a look at each key and
/// little more. Reused from frame to frame, it allocates nothing once it's held the most items it will.
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
        if (_order.Length < count)
        {
            var size = Math.Max(count, _order.Length * 2);
            (_order, _spare, _keys) = (new int[size], new int[size], new uint[size]);
        }

        Count = count;
        for (var i = 0; i < count; i++)
            _order[i] = i;
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
