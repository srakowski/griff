namespace Griff.Scenes.Serialization;

/// <summary>The component types a scene may contain, keyed by their saved name.</summary>
public sealed class ComponentRegistry
{
    private readonly Dictionary<string, ComponentDescriptor> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, ComponentDescriptor> _byType = [];

    /// <summary>A registry containing Griff's built-in components.</summary>
    public static ComponentRegistry CreateDefault()
    {
        var registry = new ComponentRegistry();
        registry.Register<Transform>();
        registry.Register<Camera>();
        registry.Register<Sprite>();
        registry.Register<Animation>();
        registry.Register<Text>();
        registry.Register<BoxCollider>();
        registry.Register<CircleCollider>();
        registry.Register<Body>();
        registry.Register<Tilemap>();
        registry.Register<ParticleEmitter>();
        registry.Register<ScreenAnchor>();
        registry.Register<Button>();
        registry.Register<Follow>();
        registry.Register<Parallax>();
        registry.Register<Health>();
        registry.Register<DamageOnTouch>();
        registry.Register<Lifetime>();
        registry.Register<Spawner>();
        registry.Register<Typewriter>();
        registry.Register<Pickup>();
        registry.Register<Counter>();
        registry.Register<Bar>();
        registry.Register<Slider>();
        registry.Register<Toggle>();
        registry.Register<Mover>();
        registry.Register<Patrol>();
        registry.Register<Chase>();
        registry.Register<Shooter>();
        registry.Register<Checkpoint>();
        registry.Register<Door>();
        registry.Register<Dialogue>();
        registry.Register<Speaker>();
        registry.Register<DialogueBox>();
        registry.Register<Switch>();
        registry.Register<Inventory>();
        registry.Register<Draggable>();
        registry.Register<DropTarget>();
        registry.Register<Grabber>();
        registry.Register<Trail>();
        registry.Register<Panel>();
        registry.Register<Choice>();
        registry.Register<PlatformerMovement>();
        registry.Register<TopDownMovement>();
        registry.Register<GridMovement>();
        registry.Register<GridChase>();
        registry.Register<GridPatrol>();
        registry.Register<TurnOrder>();
        registry.Register<TurnTaker>();
        registry.Register<Ladder>();
        registry.Register<Pushable>();
        registry.Register<Bouncer>();
        registry.Register<Crumble>();
        registry.Register<Light>();
        registry.Register<Room>();
        registry.Register<Attack>();
        registry.Register<SoundSource>();
        registry.Register<BackgroundMusic>();
        registry.Register<SoundPlayer>();
        registry.Register<ScreenEffects>();
        registry.Register<Weather>();
        registry.Register<Faction>();
        registry.Register<Unit>();
        registry.Register<Building>();
        registry.Register<Producer>();
        registry.Register<Worker>();
        registry.Register<Resource>();
        registry.Register<Fighter>();
        registry.Register<Commander>();
        registry.Register<FogOfWar>();
        registry.Register<Opponent>();
        registry.Register<FirstPersonView>();
        registry.Register<FirstPersonMovement>();
        registry.Register<Weapon>();
        registry.Register<Billboard>();
        registry.Register<Block>();
        registry.Register<WorldGenerator>();
        registry.Register<Blocks>();
        registry.Register<BlockBuilder>();
        registry.Register<Hotbar>();
        registry.Register<Crafting>();
        registry.Register<SandboxTools>();
        registry.Register<Weld>();
        registry.Register<Rope>();
        registry.Register<Thruster>();
        registry.Register<WireGate>();
        registry.Register<WireButton>();
        registry.Register<WireSensor>();
        registry.Register<WireLamp>();
        registry.Register<WireDisplay>();
        registry.Register<Wire>();
        registry.Register<Multiplayer>();
        registry.Register<Networked>();
        registry.Register<PlayerSpawn>();
        registry.Register<Scoreboard>();
        registry.Register<TextField>();
        registry.Register<NameTag>();
        return registry;
    }

    public IReadOnlyCollection<ComponentDescriptor> All => _byType.Values;

    public ComponentDescriptor Register<T>() where T : Component, new() => Register(typeof(T));

    public ComponentDescriptor Register(Type type)
    {
        if (_byType.TryGetValue(type, out var existing))
            return existing;

        if (!type.IsSubclassOf(typeof(Component)) || type.IsAbstract || type == typeof(MissingComponent))
            throw new ArgumentException($"{type.Name} isn't a concrete component type.", nameof(type));
        if (type.GetConstructor(Type.EmptyTypes) is null)
            throw new ArgumentException($"{type.Name} needs a public parameterless constructor.", nameof(type));

        var descriptor = new ComponentDescriptor(type);
        if (_byName.TryGetValue(descriptor.Name, out var clash))
            throw new InvalidOperationException($"{type.FullName} and {clash.Type.FullName} are both saved as \"{descriptor.Name}\".");

        _byName.Add(descriptor.Name, descriptor);
        _byType.Add(type, descriptor);
        return descriptor;
    }

    /// <summary>Removes a type, e.g. a script being replaced by a newer build. Built-ins can be removed too, so be careful.</summary>
    public bool Unregister(Type type)
    {
        if (!_byType.Remove(type, out var descriptor))
            return false;
        _byName.Remove(descriptor.Name);
        return true;
    }

    public bool IsRegistered(Type type) => _byType.ContainsKey(type);

    public ComponentDescriptor Get(Type type) =>
        _byType.TryGetValue(type, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException($"Component type {type.FullName} isn't registered.");

    /// <summary>The type's descriptor, or null if it isn't registered (a script that was removed).</summary>
    public ComponentDescriptor? TryGet(Type type) => _byType.GetValueOrDefault(type);

    public ComponentDescriptor? Find(string name) => _byName.GetValueOrDefault(name);
}
