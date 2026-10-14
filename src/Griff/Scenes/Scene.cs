namespace Griff.Scenes;

/// <summary>
/// A tree of entities. All hierarchy changes go through the scene so its id index stays consistent.
/// </summary>
/// <remarks>
/// The scene also keeps its components by type (<see cref="All{T}"/>, <see cref="Active{T}"/>) and which entities are
/// active, so a game asking every frame doesn't go through the whole hierarchy to find them.
/// </remarks>
public sealed class Scene
{
    private readonly List<Entity> _roots = [];
    private readonly Dictionary<Guid, Entity> _byId = [];

    // Components by type, and for each type of component the lists it belongs in (its own type's, its base types').
    private readonly Dictionary<Type, ComponentIndex> _indexes = [];
    private readonly Dictionary<Type, ComponentIndex[]> _indexesFor = [];

    // Every entity, depth first, as last asked for; null once the hierarchy has changed.
    private Entity[]? _entities;

    // Whether each entity's Order is its place in the hierarchy (depth first), and the Order the next one appended gets.
    private bool _ordered = true;
    private int _nextOrder;

    // Something in an index needs bringing up to date before it's read.
    private bool _indexesChanged;

    // Drawing a running game between its steps (see Transform): the step the game is taking on this scene (or took last),
    // the step it became the game's scene in (nothing glides into a scene on its first), and while a frame of the game is
    // drawn between two steps, which drawing it is (0 otherwise), how far between them, and the furthest anything moving
    // in a step glides rather than jumps.
    internal long Step;
    internal long FirstStep = -1;
    internal long DrawStamp;
    internal float DrawBlend;
    internal float GlideLimit;

    public IReadOnlyList<Entity> Roots => _roots;

    /// <summary>Every entity in the scene, depth first, as it is now: adding or removing entities while going through it is safe.</summary>
    public IReadOnlyList<Entity> Entities
    {
        get
        {
            if (_entities is not null)
                return _entities;
            var entities = new Entity[_byId.Count];
            var count = 0;
            foreach (var root in _roots)
                Collect(root, entities, ref count);
            _ordered = true;
            _nextOrder = count;
            return _entities = entities;
        }
    }

    // Depth first, numbering them as it goes.
    private static void Collect(Entity entity, Entity[] into, ref int count)
    {
        entity.Order = count;
        into[count++] = entity;
        foreach (var child in entity.ChildList)
            Collect(child, into, ref count);
    }

    public int Count => _byId.Count;

    /// <summary>Changes whenever entities are added, removed or moved. Cheap to poll for structural changes.</summary>
    public long Version { get; private set; }

    /// <summary>Changes whenever an entity could have become active or inactive: enabled, disabled, added or moved.</summary>
    internal long ActiveVersion { get; private set; }

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
            if (entity.Name != name || (activeOnly && !entity.IsActive))
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
    /// Every component of type <typeparamref name="T"/> in the scene, in hierarchy order. A base type or interface finds
    /// every kind of it (<c>All&lt;Collider&gt;()</c> finds box and circle colliders). Kept as the scene changes, so asking
    /// every frame is cheap; see <see cref="ComponentList{T}"/>.
    /// </summary>
    public ComponentList<T> All<T>() where T : class => new(IndexOf<T>().All());

    /// <summary>Like <see cref="All{T}"/>, only those on active entities (see <see cref="Entity.IsActive"/>): what the game runs and draws.</summary>
    public ComponentList<T> Active<T>() where T : class => new(IndexOf<T>().Active(ActiveVersion));

    private ComponentIndex<T> IndexOf<T>() where T : class
    {
        if (!_indexes.TryGetValue(typeof(T), out var index))
        {
            index = new ComponentIndex<T>();
            _indexes.Add(typeof(T), index);
            _indexesFor.Clear();
            _indexesChanged = true;
        }

        BringIndexesUpToDate();
        return (ComponentIndex<T>)index;
    }

    /// <summary>Makes each entity's <see cref="Entity.Order"/> its place in the hierarchy, if anything has moved since.</summary>
    internal void Order()
    {
        if (!_ordered)
            _ = Entities;
    }

    private void BringIndexesUpToDate()
    {
        if (!_indexesChanged)
            return;
        _indexesChanged = false;

        List<ComponentIndex>? dirty = null;
        foreach (var index in _indexes.Values)
        {
            if (index.Dirty)
            {
                index.Items.Clear();
                (dirty ??= []).Add(index);
            }
            else if (index.Stale)
            {
                index.Compact(this);
            }
        }

        if (dirty is null)
            return;
        foreach (var entity in Entities)
        {
            foreach (var component in entity.Components)
            {
                foreach (var index in IndexesFor(component))
                {
                    if (index.Dirty)
                        index.Items.Add(component);
                }
            }
        }

        foreach (var index in dirty)
        {
            index.Dirty = false;
            index.Stale = false;
            index.Changed();
        }
    }

    private ComponentIndex[] IndexesFor(Component component)
    {
        var type = component.GetType();
        if (!_indexesFor.TryGetValue(type, out var indexes))
            _indexesFor[type] = indexes = [.. _indexes.Values.Where(i => i.Type.IsAssignableFrom(type))];
        return indexes;
    }

    // A component has joined an entity in the scene, or moved among its components: its lists are rebuilt.
    internal void ComponentMoved(Component component)
    {
        foreach (var index in IndexesFor(component))
            index.Dirty = true;
        _indexesChanged = true;
    }

    // A component has left: its lists drop it when next asked.
    internal void ComponentLeft(Component component)
    {
        foreach (var index in IndexesFor(component))
            index.Stale = true;
        _indexesChanged = true;
    }

    // Before a component joins: drop those that have left, so one coming back isn't listed twice.
    internal void ComponentJoining()
    {
        foreach (var index in _indexes.Values)
        {
            if (index.Stale && !index.Dirty)
                index.Compact(this);
        }
    }

    // An entity has been enabled or disabled.
    internal void EnabledChanged() => ActiveVersion++;

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

        ComponentJoining();

        // Added at the very end of the hierarchy (as spawning does), it goes on the end of each list; anywhere else, the
        // lists it's in are rebuilt.
        var atEnd = parent is null && (index is null || index >= _roots.Count);
        _ordered &= atEnd;
        Insert(entity, parent, index);
        foreach (var e in entity.SelfAndDescendants())
        {
            e.Scene = this;
            e.ForgetActive();
            // Something new is drawn where it starts out, not glided there from wherever it was made.
            e.Transform.Jumped();
            _byId.Add(e.Id, e);
            if (_ordered)
                e.Order = _nextOrder++;
            foreach (var component in e.Components)
            {
                foreach (var list in IndexesFor(component))
                {
                    if (atEnd && !list.Dirty)
                    {
                        list.Items.Add(component);
                        list.Changed();
                    }
                    else
                    {
                        list.Dirty = true;
                        _indexesChanged = true;
                    }
                }
            }
        }

        Changed();
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
            foreach (var component in e.Components)
                ComponentLeft(component);
        }

        Changed();
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
        foreach (var e in entity.SelfAndDescendants())
        {
            // Its position now means somewhere else (it's from the new parent): drawn where it is, not glided.
            e.Transform.Jumped();
            foreach (var component in e.Components)
                ComponentMoved(component);
        }

        _ordered = false;
        Changed();
    }

    private void Changed()
    {
        _entities = null;
        Version++;
        ActiveVersion++;
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
