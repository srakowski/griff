namespace Griff.Scenes;

/// <summary>
/// A tree of entities. All hierarchy changes go through the scene so its id index stays consistent.
/// </summary>
public sealed class Scene
{
    private readonly List<Entity> _roots = [];
    private readonly Dictionary<Guid, Entity> _byId = [];

    public IReadOnlyList<Entity> Roots => _roots;

    /// <summary>Every entity in the scene, depth first.</summary>
    public IEnumerable<Entity> Entities => _roots.SelectMany(r => r.SelfAndDescendants());

    public int Count => _byId.Count;

    /// <summary>Changes whenever entities are added, removed or moved. Cheap to poll for structural changes.</summary>
    public long Version { get; private set; }

    public Entity? Find(Guid id) => _byId.GetValueOrDefault(id);

    /// <summary>
    /// The entity called <paramref name="name"/>. When several are (every player's character is "Player" in a network
    /// game), the one this machine's player plays comes first, so a camera or health bar follows your own.
    /// </summary>
    public Entity? FindNamed(string name, bool activeOnly = false)
    {
        Entity? first = null;
        foreach (var entity in Entities)
        {
            if (entity.Name != name || (activeOnly && !Rendering.SceneRenderer.IsActive(entity)))
                continue;
            if (PreferNamed is null || PreferNamed(entity))
                return entity;
            first ??= entity;
        }

        return first;
    }

    /// <summary>Set in a network game: which of several entities with the same name <see cref="FindNamed"/> picks first.</summary>
    internal Func<Entity, bool>? PreferNamed { get; set; }

    /// <summary>
    /// Adds <paramref name="entity"/> (and its descendants) under <paramref name="parent"/>,
    /// or as a root if parent is null, at <paramref name="index"/> or the end.
    /// </summary>
    public void Add(Entity entity, Entity? parent = null, int? index = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Scene is not null)
            throw new InvalidOperationException($"\"{entity.Name}\" is already in a scene.");
        if (entity.Parent is not null)
            throw new InvalidOperationException($"\"{entity.Name}\" still has a parent.");
        EnsureInScene(parent);

        foreach (var e in entity.SelfAndDescendants())
        {
            if (_byId.ContainsKey(e.Id))
                throw new InvalidOperationException($"An entity with id {e.Id} is already in the scene.");
        }

        Insert(entity, parent, index);
        foreach (var e in entity.SelfAndDescendants())
        {
            e.Scene = this;
            _byId.Add(e.Id, e);
        }

        Version++;
    }

    /// <summary>Removes <paramref name="entity"/> and its descendants. They keep their own hierarchy.</summary>
    public void Remove(Entity entity)
    {
        EnsureInScene(entity);
        Detach(entity);
        foreach (var e in entity.SelfAndDescendants())
        {
            e.Scene = null;
            _byId.Remove(e.Id);
        }

        Version++;
    }

    /// <summary>Moves <paramref name="entity"/> under a new parent (or to the root) at <paramref name="index"/>.</summary>
    public void Move(Entity entity, Entity? newParent, int? index = null)
    {
        EnsureInScene(entity);
        EnsureInScene(newParent);
        if (newParent is not null && newParent.IsSelfOrDescendantOf(entity))
            throw new InvalidOperationException($"\"{entity.Name}\" can't be moved under itself or one of its children.");

        Detach(entity);
        Insert(entity, newParent, index);
        Version++;
    }

    /// <summary>Position of <paramref name="entity"/> among its siblings.</summary>
    public int IndexOf(Entity entity)
    {
        EnsureInScene(entity);
        return SiblingsOf(entity.Parent).IndexOf(entity);
    }

    private List<Entity> SiblingsOf(Entity? parent) => parent?.ChildList ?? _roots;

    private void Insert(Entity entity, Entity? parent, int? index)
    {
        var siblings = SiblingsOf(parent);
        siblings.Insert(Math.Clamp(index ?? siblings.Count, 0, siblings.Count), entity);
        entity.Parent = parent;
    }

    private void Detach(Entity entity)
    {
        SiblingsOf(entity.Parent).Remove(entity);
        entity.Parent = null;
    }

    private void EnsureInScene(Entity? entity)
    {
        if (entity is not null && entity.Scene != this)
            throw new InvalidOperationException($"\"{entity.Name}\" isn't in this scene.");
    }
}
