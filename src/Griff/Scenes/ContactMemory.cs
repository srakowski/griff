using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// What physics remembers of a scene from one step to the next: how hard each point where bodies that rotate touch was
/// pushed, gripped and held from rolling, as a first guess for the next step's pushes (so a tower's weight is carried down it from the first and it
/// stands). It belongs to the scene, so a rewind snapshot keeps it with everything else and the game played on from one
/// goes the same to the last bit.
/// </summary>
internal sealed class ContactMemory
{
    private readonly Dictionary<ContactKey, Vector3> _pushes = [];

    public int Count => _pushes.Count;

    /// <summary>
    /// The push apart (X), the grip along (Y) and the hold against rolling (Z, a twist) remembered for a point.
    /// </summary>
    public bool TryGet(in ContactKey key, out Vector3 push) => _pushes.TryGetValue(key, out push);

    public void Set(in ContactKey key, Vector3 push) => _pushes[key] = push;

    public void Clear() => _pushes.Clear();

    /// <summary>Forgets every point but those of <paramref name="bodies"/> (entity ids).</summary>
    public void KeepOnly(HashSet<Guid> bodies)
    {
        if (bodies.Count == 0)
        {
            _pushes.Clear();
            return;
        }

        foreach (var key in _pushes.Keys)
        {
            if (!bodies.Contains(key.Body))
                _pushes.Remove(key);
        }
    }

    /// <summary>Makes <paramref name="other"/> remember just what this does.</summary>
    public void CopyTo(ContactMemory other)
    {
        other._pushes.Clear();
        foreach (var (key, push) in _pushes)
            other._pushes[key] = push;
    }
}

/// <summary>
/// A point where a body that rotates (entity <paramref name="Body"/>) touches something (<paramref name="Solid"/>): which
/// of the solid's shapes (a tile run, a cell) and which corners and sides make it, the same from one step to the next.
/// </summary>
internal readonly record struct ContactKey(Guid Body, Guid Solid, int Part, int Feature);
