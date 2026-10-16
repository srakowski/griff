namespace Griff.Scenes;

// Arrays handed out as snapshots (a scene's lists of components and of entities, the game's lists of scripts) and since
// replaced, kept to be filled again, so enabling, disabling, spawning or destroying something doesn't make new ones each
// time. A snapshot doesn't change while it's gone through, so one is filled again only two steps of the game after it was
// replaced: whatever asked for it in a step is done with it by then (the engine asks again each step rather than keep one).
// Snapshots handed to scripts and the editor never come here, as they may keep them for longer (see Scene.All).
internal sealed class SnapshotPool<T>
{
    private const int Most = 8;

    private readonly T[]?[] _arrays = new T[]?[Most];
    private readonly long[] _replaced = new long[Most];
    private int _count;

    // An array `length` long to fill, in step `now` of the game: one replaced at least two steps ago, or a new one.
    public T[] Rent(int length, long now)
    {
        if (length == 0)
            return [];
        for (var i = 0; i < _count; i++)
        {
            if (_arrays[i]!.Length == length && now >= _replaced[i] + 2)
            {
                var array = _arrays[i]!;
                RemoveAt(i);
                return array;
            }
        }

        return new T[length];
    }

    // `array` has been replaced in step `now` (none while the game isn't running: nothing is filled again then). Past the
    // most kept, the one replaced longest ago goes.
    public void Retire(T[]? array, long now)
    {
        if (array is null || array.Length == 0 || now <= 0)
            return;
        for (var i = 0; i < _count; i++)
        {
            if (ReferenceEquals(_arrays[i], array))
                return;
        }

        if (_count == Most)
            RemoveAt(0);
        (_arrays[_count], _replaced[_count]) = (array, now);
        _count++;
    }

    private void RemoveAt(int index)
    {
        _count--;
        for (var i = index; i < _count; i++)
            (_arrays[i], _replaced[i]) = (_arrays[i + 1], _replaced[i + 1]);
        _arrays[_count] = null;
    }
}
