namespace Griff.Scenes;

/// <summary>
/// Reads one output (see <see cref="OutputAttribute"/>) for a component that shows or carries it (a counter, a bar, a text,
/// a wire), finding it by the names in that component's fields once and again only when they change or what it found
/// leaves: reading it every step allocates nothing.
/// </summary>
/// <remarks>
/// The entity is the one the track field names (one inside the reader's own entity first, otherwise the scene's, a network
/// game's own player's first), or without a name the reader's own entity, or the nearest it's inside, with that output.
/// </remarks>
internal sealed class OutputReader
{
    private Entity? _owner;
    private string? _track;
    private string? _output;
    private Entity? _entity;
    private Component? _component;
    private ComponentOutput? _found;
    private string? _key;
    private bool _resolved;

    /// <summary>The component it reads, once found.</summary>
    public Component? Component => _component;

    /// <summary>The output it reads, once found.</summary>
    public ComponentOutput? Output => _found;

    /// <summary>
    /// Finds the output <paramref name="output"/> names on the entity <paramref name="track"/> names (or
    /// <paramref name="owner"/>'s own), unless it already has; true once it has one to read.
    /// </summary>
    public bool Find(Entity owner, string? track, string? output)
    {
        if (_resolved && owner == _owner && ReferenceEquals(track, _track) && ReferenceEquals(output, _output) && StillThere(owner, track))
            return _found is not null;

        (_owner, _track, _output, _resolved) = (owner, track, output, true);
        (_entity, _component, _found, _key) = (null, null, null, null);
        if (string.IsNullOrWhiteSpace(output) || owner.Scene is not { } scene)
            return false;

        var name = output.AsSpan();
        if (string.IsNullOrWhiteSpace(track))
        {
            for (var e = owner; e is not null; e = e.Parent)
            {
                if (Take(e, name))
                    break;
            }
        }
        else if ((Inside(owner, track.AsSpan().Trim()) ?? Named(scene, track.AsSpan().Trim())) is { } named)
            Take(named, name);

        // Not found: look again next time (it may be spawned, renamed or given the component).
        if (_found is null)
            _resolved = false;
        return _found is not null;
    }

    /// <summary>What the output gives now, as a number (true is 1); 0 before it's found.</summary>
    public float Number => _found is { } output ? output.ReadNumber(_component!, _key) : 0;

    /// <summary>What the output gives now, for one that gives a name; null for a number, or before it's found.</summary>
    public string? Text => _found is { } output ? output.ReadText(_component!, _key) : null;

    /// <summary>What the output is out of now (see <see cref="OutputAttribute.OutOf"/>); 0 for nothing.</summary>
    public float OutOf => _found is { } output ? output.ReadOutOf(_component!) : 0;

    // What it found is still in the scene with its component, and still the one a network game's own player would pick.
    private bool StillThere(Entity owner, string? track)
    {
        if (_found is null)
            return false;
        if (_entity!.Scene != owner.Scene || _component!.Entity != _entity)
            return false;
        return string.IsNullOrWhiteSpace(track) || owner.Scene?.PreferNamed is not { } prefer || prefer(_entity);
    }

    private bool Take(Entity entity, ReadOnlySpan<char> name)
    {
        if (ComponentOutputs.Find(entity, name) is not { } found)
            return false;
        (_entity, _component, _found) = (entity, found.Component, found.Output);
        _key = found.Output.IsKeyed ? ComponentOutputs.KeyOf(_output!) : null;
        return true;
    }

    private static Entity? Inside(Entity entity, ReadOnlySpan<char> name)
    {
        if (name.SequenceEqual(entity.Name))
            return entity;
        foreach (var child in entity.ChildList)
        {
            if (Inside(child, name) is { } found)
                return found;
        }

        return null;
    }

    // As Scene.FindNamed finds it: this machine's player's own first in a network game.
    private static Entity? Named(Scene scene, ReadOnlySpan<char> name)
    {
        Entity? first = null;
        var entities = scene.Entities;
        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (!name.SequenceEqual(entity.Name))
                continue;
            if (scene.PreferNamed is null || scene.PreferNamed(entity))
                return entity;
            first ??= entity;
        }

        return first;
    }
}
