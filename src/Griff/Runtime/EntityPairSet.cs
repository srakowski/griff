using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Pairs of entities, each pair once, in the order they were first added: who touched what in a frame's physics (see
/// <see cref="Game.Touches"/>) and which triggers have what in them (<see cref="Physics.TriggerPairs"/>). Whatever goes
/// by them (which of two collectors gets a pickup, which trigger hears of something first) goes the same way every time,
/// after a rewind too, rather than in a hash set's order.
/// </summary>
public sealed class EntityPairSet : IReadOnlyCollection<(Entity, Entity)>
{
    private readonly HashSet<(Entity, Entity)> _seen = [];
    private readonly List<(Entity, Entity)> _inOrder = [];

    /// <summary>How many pairs there are.</summary>
    public int Count => _inOrder.Count;

    /// <summary>Adds a pair, after the others; false if it's there already.</summary>
    public bool Add((Entity, Entity) pair)
    {
        if (!_seen.Add(pair))
            return false;
        _inOrder.Add(pair);
        return true;
    }

    /// <summary>True if <paramref name="pair"/> touched.</summary>
    public bool Contains((Entity, Entity) pair) => _seen.Contains(pair);

    /// <summary>Forgets every pair.</summary>
    public void Clear()
    {
        _seen.Clear();
        _inOrder.Clear();
    }

    /// <summary>Makes room for <paramref name="capacity"/> pairs, so adding them makes no garbage.</summary>
    public void EnsureCapacity(int capacity)
    {
        _seen.EnsureCapacity(capacity);
        _inOrder.EnsureCapacity(capacity);
    }

    /// <summary>The pairs, in the order they were added.</summary>
    public List<(Entity, Entity)>.Enumerator GetEnumerator() => _inOrder.GetEnumerator();

    IEnumerator<(Entity, Entity)> IEnumerable<(Entity, Entity)>.GetEnumerator() => GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
