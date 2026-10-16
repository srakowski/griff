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

    // How many of its entities a network game treats specially here (see Entity.NetFlags): while none are, everything in
    // it runs and moves on this machine, and the game needn't look at each entity to know.
    internal int NetFlagged;

    // The sprites flashing or blinking, which the game moves on each step (and drops once they're done or gone).
    internal readonly List<Sprite> SpriteEffects = [];

    // How hard bodies that rotate pushed where they touched in the last step, the next step's first guess (see ContactMemory).
    internal readonly ContactMemory Contacts = new();

    // Seconds left of a hit stop holding the game still as its last step ended, kept with a rewind's snapshot so a game
    // put back into one finishes it (see Game.HitStop).
    internal float HeldStill;

    // Where solids have gone while the game runs (taken out, turned off, tiles cleared) since its last physics step, which
    // wakes the bodies asleep there, as what held them up may have gone; or, past a few hundred of them, everywhere. The
    // physics step reads them and forgets them. Nothing's kept while the scene isn't being played.
    internal readonly List<Box> Vacated = [];
    internal bool VacatedEverywhere;

    // A solid shape went from this box in the world.
    internal void Vacate(Box box)
    {
        if (Step <= 0)
            return;
        if (Vacated.Count < 256)
            Vacated.Add(box);
        else
            VacatedEverywhere = true;
    }

    // An entity and what's in it are going (or being turned off): where their solids were.
    internal void Vacate(Entity entity)
    {
        if (Step <= 0 || !entity.IsActive)
            return;
        foreach (var component in entity.ComponentList)
        {
            if (component is Collider { IsTrigger: false } collider)
                Vacate(collider.WorldBounds);
            else if (component is Tilemap { Solid: true } map)
                Vacate(map.RowsBounds(0, map.Height - 1));
        }

        foreach (var child in entity.ChildList)
            Vacate(child);
    }

    public IReadOnlyList<Entity> Roots => _roots;

    /// <summary>
    /// Every entity in the scene, depth first, as it is now: adding or removing entities while going through it is safe, and
    /// it doesn't change, however long it's kept.
    /// </summary>
    public IReadOnlyList<Entity> Entities
    {
        get
        {
            var entities = EntitiesNow;
            _entitiesKept = true;
            return entities;
        }
    }

    // The same, for the engine to go through in the step it asked in (see SnapshotPool): it's filled again once it has been
    // replaced, unless it was also handed out to be kept.
    internal Entity[] EntitiesNow
    {
        get
        {
            if (_entities is not null)
                return _entities;
            var entities = _entityArrays.Rent(_byId.Count, Steps);
            var count = 0;
            foreach (var root in _roots)
                Collect(root, entities, ref count);
            _ordered = true;
            _nextOrder = count;
            _entitiesKept = false;
            return _entities = entities;
        }
    }

    private readonly SnapshotPool<Entity> _entityArrays = new();
    private bool _entitiesKept;

    // How many steps the game has taken on this scene: arrays replaced two steps ago can be filled again (see SnapshotPool).
    internal long Steps;

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
    /// game), the one this machine's player plays comes first, so a camera or health bar follows your own. While a minigame
    /// is played in the scene (see <see cref="MinigameRoot"/>), one in the same part of the scene as <paramref name="near"/>
    /// comes first (the minigame it's in, or the rest of the scene), or without it, one in the minigame that has the screen.
    /// </summary>
    public Entity? FindNamed(string name, bool activeOnly = false, Entity? near = null)
    {
        if (ActiveNow<MinigameRoot>().Count > 0)
        {
            var world = near is not null ? MinigameRoot.Of(near) : MinigameRoot.Shown(this);
            if (Find(name, activeOnly, world, worldOnly: true) is { } same)
                return same;
        }

        return Find(name, activeOnly, null, worldOnly: false);
    }

    // The first called that (this player's own first), only in `world` (a minigame's root, or null for outside them) if it
    // says so.
    private Entity? Find(string name, bool activeOnly, Entity? world, bool worldOnly)
    {
        Entity? first = null;
        foreach (var entity in EntitiesNow)
        {
            if (entity.Name != name || (activeOnly && !entity.IsActive) || (worldOnly && MinigameRoot.Of(entity) != world))
                continue;
            if (PreferNamed is null || PreferNamed(entity))
                return entity;
            first ??= entity;
        }

        return first;
    }

    /// <summary>
    /// True if <paramref name="entity"/> is in the same part of the scene as <paramref name="near"/>: the same minigame being
    /// played in it (see <see cref="MinigameRoot"/>), or both outside them. Always true while no minigame is.
    /// </summary>
    public static bool SamePart(Entity entity, Entity? near) => near is null || MinigameRoot.Of(entity) == MinigameRoot.Of(near);

    /// <summary>Set in a network game: which of several entities with the same name <see cref="FindNamed"/> picks first.</summary>
    internal Func<Entity, bool>? PreferNamed { get; set; }

    /// <summary>
    /// Every component of type <typeparamref name="T"/> in the scene, in hierarchy order. A base type or interface finds
    /// every kind of it (<c>All&lt;Collider&gt;()</c> finds box and circle colliders). Kept as the scene changes, so asking
    /// every frame is cheap; see <see cref="ComponentList{T}"/>. It doesn't change, however long it's kept.
    /// </summary>
    public ComponentList<T> All<T>() where T : class => IndexOf<T>().All(keep: true);

    /// <summary>Like <see cref="All{T}"/>, only those on active entities (see <see cref="Entity.IsActive"/>): what the game runs and draws.</summary>
    public ComponentList<T> Active<T>() where T : class => IndexOf<T>().Active(ActiveVersion, keep: true);

    /// <summary>
    /// Like <see cref="Active{T}"/>, less those on paused entities (see <see cref="Entity.IsPaused"/>): what the game moves on
    /// each step. The same list as Active while nothing is paused.
    /// </summary>
    public ComponentList<T> Running<T>() where T : class =>
        _paused.Count == 0 ? Active<T>() : IndexOf<T>().Running(ActiveVersion, PauseVersion, keep: true);

    // The same lists for the engine, which goes through them in the step it asked in and doesn't keep them: once replaced,
    // they're filled again two steps later (see SnapshotPool), so enabling, disabling and destroying make no garbage.
    internal ComponentList<T> AllNow<T>() where T : class => IndexOf<T>().All(keep: false);

    internal ComponentList<T> ActiveNow<T>() where T : class => IndexOf<T>().Active(ActiveVersion, keep: false);

    internal ComponentList<T> RunningNow<T>() where T : class =>
        _paused.Count == 0 ? ActiveNow<T>() : IndexOf<T>().Running(ActiveVersion, PauseVersion, keep: false);

    // The entities held still with everything inside them (see Entity.IsPaused), and a number that changes whenever they do.
    private readonly List<Entity> _paused = [];

    /// <summary>The entities the game holds still with everything inside them, while it runs (see <see cref="Entity.IsPaused"/>).</summary>
    public IReadOnlyList<Entity> PausedGroups => _paused;

    internal long PauseVersion { get; private set; }

    /// <summary>
    /// Makes <paramref name="groups"/> the paused groups in place of the last ones (see <see cref="Entity.IsPaused"/>).
    /// Returns whether that changed anything.
    /// </summary>
    internal bool SetPaused(List<Entity> groups)
    {
        if (groups.Count == _paused.Count)
        {
            var same = true;
            foreach (var group in groups)
            {
                if (group.PausedIn != this)
                {
                    same = false;
                    break;
                }
            }

            if (same)
                return false;
        }

        foreach (var old in _paused)
            old.PausedIn = null;
        _paused.Clear();
        foreach (var group in groups)
        {
            if (group.Scene != this || group.PausedIn == this)
                continue;
            group.PausedIn = this;
            _paused.Add(group);
        }

        PauseVersion++;
        return true;
    }

    private ComponentIndex<T> IndexOf<T>() where T : class
    {
        if (!_indexes.TryGetValue(typeof(T), out var index))
        {
            index = new ComponentIndex<T>(this);
            _indexes.Add(typeof(T), index);
            // The component types it lists learn of it; the rest are as they were (worked out afresh, a scene's first
            // step, asking for a hundred kinds, spent most of its time and garbage here).
            List<Type>? lists = null;
            foreach (var type in _indexesFor.Keys)
            {
                if (typeof(T).IsAssignableFrom(type))
                    (lists ??= []).Add(type);
            }

            foreach (var type in lists ?? [])
                _indexesFor[type] = [.. _indexesFor[type], index];
            _indexesChanged = true;
        }

        BringIndexesUpToDate();
        return (ComponentIndex<T>)index;
    }

    /// <summary>Makes each entity's <see cref="Entity.Order"/> its place in the hierarchy, if anything has moved since.</summary>
    internal void Order()
    {
        if (!_ordered)
            _ = EntitiesNow;
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
                index.Compact();
            }
        }

        if (dirty is null)
            return;
        foreach (var entity in EntitiesNow)
        {
            foreach (var component in entity.ComponentList)
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
        return _indexesFor.TryGetValue(type, out var indexes) ? indexes : _indexesFor[type] = IndexesListing(type);
    }

    // Worked out once a type, apart, so the lambda's closure isn't made on every call (every component, each time the
    // lists are rebuilt or something is added) but only when it's needed.
    private ComponentIndex[] IndexesListing(Type type) => [.. _indexes.Values.Where(i => i.Type.IsAssignableFrom(type))];

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
                index.Compact();
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
        Crowds.ThrowIfRunning("add entities");
        if (entity.Scene is not null)
            throw new InvalidOperationException($"\"{entity.Name}\" is already in a scene.");
        if (entity.Parent is not null)
            throw new InvalidOperationException($"\"{entity.Name}\" still has a parent.");
        EnsureInScene(parent);
        ThrowIfIdTaken(entity);
        ComponentJoining();

        // Added at the very end of the hierarchy (as spawning does), it goes on the end of each list; anywhere else, the
        // lists it's in are rebuilt.
        var atEnd = parent is null && (index is null || index >= _roots.Count);
        _ordered &= atEnd;
        Insert(entity, parent, index);
        Joined(entity, atEnd);
        Changed();
    }

    private void ThrowIfIdTaken(Entity entity)
    {
        if (_byId.ContainsKey(entity.Id))
            throw new InvalidOperationException($"An entity with id {entity.Id} is already in the scene.");
        foreach (var child in entity.ChildList)
            ThrowIfIdTaken(child);
    }

    // `entity` and what's inside it, depth first, have joined the scene.
    private void Joined(Entity entity, bool atEnd)
    {
        entity.Scene = this;
        entity.ForgetActive();
        if (entity.NetFlags != NetFlags.None)
            NetFlagged++;
        // Something new is drawn where it starts out, not glided there from wherever it was made.
        entity.Transform.Jumped();
        _byId.Add(entity.Id, entity);
        if (_ordered)
            entity.Order = _nextOrder++;
        foreach (var component in entity.ComponentList)
        {
            // A sprite flashed before it was added flashes here.
            (component as Sprite)?.ListEffects();
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

        foreach (var child in entity.ChildList)
            Joined(child, atEnd);
    }

    /// <summary>Removes <paramref name="entity"/> and its descendants. They keep their own hierarchy.</summary>
    public void Remove(Entity entity)
    {
        Crowds.ThrowIfRunning("remove entities");
        EnsureInScene(entity);
        Vacate(entity);
        Detach(entity);
        Left(entity);
        Changed();
    }

    // `entity` and what's inside it have left the scene.
    private void Left(Entity entity)
    {
        entity.Scene = null;
        if (entity.NetFlags != NetFlags.None)
            NetFlagged--;
        _byId.Remove(entity.Id);
        foreach (var component in entity.ComponentList)
            ComponentLeft(component);
        foreach (var child in entity.ChildList)
            Left(child);
    }

    /// <summary>Moves <paramref name="entity"/> under a new parent (or to the root) at <paramref name="index"/>.</summary>
    public void Move(Entity entity, Entity? newParent, int? index = null)
    {
        Crowds.ThrowIfRunning("move entities in the hierarchy");
        EnsureInScene(entity);
        EnsureInScene(newParent);
        if (newParent is not null && newParent.IsSelfOrDescendantOf(entity))
            throw new InvalidOperationException($"\"{entity.Name}\" can't be moved under itself or one of its children.");

        Detach(entity);
        Insert(entity, newParent, index);
        Moved(entity);
        _ordered = false;
        Changed();
    }

    private void Moved(Entity entity)
    {
        // Its position now means somewhere else (it's from the new parent): drawn where it is, not glided.
        entity.Transform.Jumped();
        foreach (var component in entity.ComponentList)
            ComponentMoved(component);
        foreach (var child in entity.ChildList)
            Moved(child);
    }

    private void Changed()
    {
        if (!_entitiesKept)
            _entityArrays.Retire(_entities, Steps);
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
