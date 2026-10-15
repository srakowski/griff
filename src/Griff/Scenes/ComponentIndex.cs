using System.Collections;

namespace Griff.Scenes;

/// <summary>
/// The components of one type in a scene, as they were when asked for (see <see cref="Scene.All{T}"/>), in hierarchy order:
/// entities depth first, and each entity's in the order they're attached. Spawning, destroying or adding components while
/// going through it is safe; it doesn't change. Going through it with <c>foreach</c> allocates nothing.
/// </summary>
public readonly struct ComponentList<T> : IReadOnlyList<T>
{
    private readonly T[]? _items;

    internal ComponentList(T[] items, long serial = 0) => (_items, Serial) = (items, serial);

    // The snapshot itself.
    internal T[] Items => _items ?? [];

    // Which snapshot it is: the same as long as nothing has changed, so a caller can tell nothing has (an array can be
    // filled again, so it can't go by that). 0 for a list made apart from the scene's, which is never taken as the same.
    internal long Serial { get; }

    public int Count => _items?.Length ?? 0;

    public T this[int index] => (_items ?? [])[index];

    /// <summary>The first of them, or null if there are none.</summary>
    public T? FirstOrDefault() => Count > 0 ? _items![0] : default;

    public ReadOnlySpan<T> AsSpan() => _items;

    public Enumerator GetEnumerator() => new(_items ?? []);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => (_items ?? []).GetEnumerator();

    public struct Enumerator(T[] items)
    {
        private int _index = -1;

        public readonly T Current => items[_index];

        public bool MoveNext() => ++_index < items.Length;
    }
}

// One type's components in a scene, kept in hierarchy order (see Scene.All). The scene appends what's spawned at the end
// of the hierarchy, drops what leaves when next asked (Stale), and rebuilds the list from the hierarchy after anything
// else (Dirty).
internal abstract class ComponentIndex(Scene scene, Type type)
{
    protected Scene Scene { get; } = scene;

    public Type Type { get; } = type;

    public List<Component> Items { get; } = [];

    // Rebuild from the hierarchy when next asked.
    public bool Dirty { get; set; } = true;

    // Holds components that have left the scene (or their entity): drop them when next asked.
    public bool Stale { get; set; }

    public void Compact()
    {
        var kept = 0;
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Entity?.Scene == Scene)
                Items[kept++] = Items[i];
        }

        Items.RemoveRange(kept, Items.Count - kept);
        Stale = false;
        Changed();
    }

    // The list has changed: its snapshots are out of date.
    public abstract void Changed();
}

// The snapshots of one type's components: all of them, those on active entities and those running (not paused). Each is
// made when first asked for after a change, into an array replaced at least two steps before where there is one (see
// SnapshotPool), and kept until something changes. One handed out to be kept (to scripts and the editor) is never filled
// again.
internal sealed class ComponentIndex<T>(Scene scene) : ComponentIndex(scene, typeof(T)) where T : class
{
    private readonly SnapshotPool<T> _pool = new();
    private T[]? _all;
    private long _allSerial;
    private T[]? _active;
    private long _activeSerial;
    private long _activeFor;
    private T[]? _running;
    private long _runningSerial;
    private (long Active, long Pause) _runningFor;

    // The snapshots in use that have been handed out to be kept (at most the three above).
    private readonly T[]?[] _kept = new T[]?[3];

    public override void Changed()
    {
        var (all, active, running) = (_all, _active, _running);
        (_all, _active, _running) = (null, null, null);
        Retire(all);
        if (active != all)
            Retire(active);
        if (running != all && running != active)
            Retire(running);
    }

    // `keep`: handed out to be kept, so never filled again.
    public ComponentList<T> All(bool keep)
    {
        if (_all is null)
        {
            var all = _pool.Rent(Items.Count, Scene.Steps);
            for (var i = 0; i < all.Length; i++)
                all[i] = (T)(object)Items[i];
            (_all, _allSerial) = (all, Snapshots.Next());
        }

        if (keep)
            Keep(_all);
        return new(_all, _allSerial);
    }

    // Those on active entities, as of the scene's activeVersion (which changes when anything is enabled, disabled or moved).
    public ComponentList<T> Active(long activeVersion, bool keep)
    {
        if (_active is null || _activeFor != activeVersion)
        {
            var old = _active;
            _ = All(false);
            (_active, _activeSerial) = Only(_all!, _allSerial, old, _activeSerial, static item => ((Component)(object)item).Entity!.IsActive);
            _activeFor = activeVersion;
            if (old != _active)
                Retire(old);
        }

        if (keep)
            Keep(_active);
        return new(_active, _activeSerial);
    }

    // Those on active entities that aren't paused (see Entity.IsPaused), as of the scene's activeVersion and pauseVersion.
    public ComponentList<T> Running(long activeVersion, long pauseVersion, bool keep)
    {
        if (_running is null || _runningFor != (activeVersion, pauseVersion))
        {
            var old = _running;
            _ = Active(activeVersion, false);
            (_running, _runningSerial) = Only(_active!, _activeSerial, old, _runningSerial, static item => !((Component)(object)item).Entity!.IsPaused);
            _runningFor = (activeVersion, pauseVersion);
            if (old != _running)
                Retire(old);
        }

        if (keep)
            Keep(_running);
        return new(_running, _runningSerial);
    }

    // Those of `from` that pass: `from` itself when they all do, the last list made of them (`last`) when it's the same
    // (something else was enabled or disabled), or else a snapshot of their own.
    private (T[] Items, long Serial) Only(T[] from, long fromSerial, T[]? last, long lastSerial, Func<T, bool> test)
    {
        var count = 0;
        var same = last is not null && last != from;
        foreach (var item in from)
        {
            if (!test(item))
                continue;
            same = same && count < last!.Length && ReferenceEquals(last[count], item);
            count++;
        }

        if (count == from.Length)
            return (from, fromSerial);
        if (same && count == last!.Length)
            return (last, lastSerial);
        var only = _pool.Rent(count, Scene.Steps);
        count = 0;
        foreach (var item in from)
        {
            if (test(item))
                only[count++] = item;
        }

        return (only, Snapshots.Next());
    }

    private void Keep(T[] array)
    {
        if (array.Length == 0)
            return;
        var free = -1;
        for (var i = 0; i < _kept.Length; i++)
        {
            if (_kept[i] == array)
                return;
            if (_kept[i] is null && free < 0)
                free = i;
        }

        // A kept snapshot leaves the list as it's replaced, so there's always room.
        if (free >= 0)
            _kept[free] = array;
    }

    // `array` has been replaced: filled again later, unless it's still in use or was handed out to be kept.
    private void Retire(T[]? array)
    {
        if (array is null || array == _all || array == _active || array == _running)
            return;
        for (var i = 0; i < _kept.Length; i++)
        {
            if (_kept[i] == array)
            {
                _kept[i] = null;
                return;
            }
        }

        _pool.Retire(array, Scene.Steps);
    }
}
