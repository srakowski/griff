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

    internal ComponentList(T[] items) => _items = items;

    // The snapshot itself: the same array as long as nothing has changed, so a caller can tell nothing has.
    internal T[] Items => _items ?? [];

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
internal abstract class ComponentIndex(Type type)
{
    public Type Type { get; } = type;

    public List<Component> Items { get; } = [];

    // Rebuild from the hierarchy when next asked.
    public bool Dirty { get; set; } = true;

    // Holds components that have left the scene (or their entity): drop them when next asked.
    public bool Stale { get; set; }

    public void Compact(Scene scene)
    {
        Items.RemoveAll(c => c.Entity?.Scene != scene);
        Stale = false;
        Changed();
    }

    // The list has changed: its snapshots are out of date.
    public abstract void Changed();
}

internal sealed class ComponentIndex<T>() : ComponentIndex(typeof(T)) where T : class
{
    private T[]? _all;
    private T[]? _active;
    private long _activeFor;

    public override void Changed()
    {
        _all = null;
        _active = null;
    }

    public T[] All()
    {
        if (_all is not null)
            return _all;
        var all = new T[Items.Count];
        for (var i = 0; i < all.Length; i++)
            all[i] = (T)(object)Items[i];
        return _all = all;
    }

    // Those on active entities, as of the scene's activeVersion (which changes when anything is enabled, disabled or moved).
    public T[] Active(long activeVersion)
    {
        if (_active is not null && _activeFor == activeVersion)
            return _active;
        var all = All();
        var count = 0;
        foreach (var item in all)
        {
            if (((Component)(object)item).Entity!.IsActive)
                count++;
        }

        if (count == all.Length)
            _active = all;
        else
        {
            _active = new T[count];
            count = 0;
            foreach (var item in all)
            {
                if (((Component)(object)item).Entity!.IsActive)
                    _active[count++] = item;
            }
        }

        _activeFor = activeVersion;
        return _active;
    }
}
