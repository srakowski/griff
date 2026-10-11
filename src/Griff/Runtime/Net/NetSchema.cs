using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

internal enum SlotKind : byte
{
    Property,
    Name,
    Enabled,
    Position,
    Rotation,
    Scale,
    Extra,
}

/// <summary>One value of an entity that's kept the same on every machine: a component's property, or its name.</summary>
internal sealed class NetSlot(SlotKind kind, Component? component, PropertyDescriptor? property, Type type, bool ownerOwned,
    Func<Component, object?>? get = null, Action<Component, object?>? set = null)
{
    public SlotKind Kind { get; } = kind;

    public Component? Component { get; } = component;

    public PropertyDescriptor? Property { get; } = property;

    public Type Type { get; } = type;

    /// <summary>The player moving the entity (not the host) decides this one: where it is, which way it faces.</summary>
    public bool OwnerOwned { get; } = ownerOwned;

    public bool IsLines => Type == typeof(IReadOnlyList<string>);

    public object? Get(Entity entity) => Kind switch
    {
        SlotKind.Name => entity.Name,
        SlotKind.Enabled => entity.IsEnabled,
        SlotKind.Position => entity.Transform.Position,
        SlotKind.Rotation => entity.Transform.Rotation,
        SlotKind.Scale => entity.Transform.Scale,
        SlotKind.Extra => get!(Component!),
        _ => Property!.GetValue(Component!),
    };

    public void Set(Entity entity, object? value)
    {
        switch (Kind)
        {
            case SlotKind.Name: entity.Name = (string?)value ?? "Entity"; break;
            case SlotKind.Enabled: entity.IsEnabled = (bool)value!; break;
            case SlotKind.Position: entity.Transform.Position = (System.Numerics.Vector3)value!; break;
            case SlotKind.Rotation: entity.Transform.Rotation = (System.Numerics.Vector3)value!; break;
            case SlotKind.Scale: entity.Transform.Scale = (System.Numerics.Vector3)value!; break;
            case SlotKind.Extra: set!(Component!, value); break;
            default:
                try
                {
                    Property!.SetValue(Component!, value);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Reflection.TargetInvocationException)
                {
                    // A setter that refused the value: keep what's there.
                }

                break;
        }
    }
}

/// <summary>Which of an entity's values are sent, in an order both ends agree on (they have the same components).</summary>
internal static class NetSchema
{
    // Moved by the player who controls it, when they move it themselves.
    private static readonly HashSet<Type> MovementComponents = [typeof(PlatformerMovement), typeof(TopDownMovement), typeof(GridMovement), typeof(FirstPersonMovement)];

    // Things each machine keeps for itself: cameras, the HUD, menus, music. Never sent; every machine has its own.
    private static readonly HashSet<Type> LocalComponents =
    [
        typeof(Camera), typeof(ScreenAnchor), typeof(Panel), typeof(Button), typeof(Slider), typeof(Toggle), typeof(Choice),
        typeof(Commander), typeof(FogOfWar), typeof(BackgroundMusic), typeof(Weather), typeof(ScreenEffects), typeof(Parallax),
        typeof(TextField), typeof(Scoreboard), typeof(Multiplayer),
    ];

    public static bool HasMovement(Entity entity) => entity.Components.Any(c => MovementComponents.Contains(c.GetType()));

    /// <summary>True if <paramref name="entity"/> itself (not counting its parents) stays on each machine.</summary>
    public static bool IsLocalItself(Entity entity)
    {
        if (entity.GetComponent<Networked>() is { } networked && networked.Sync != NetSync.Auto)
            return networked.Sync == NetSync.Local;
        return entity.Components.Any(c => LocalComponents.Contains(c.GetType()));
    }

    /// <summary>True if <paramref name="entity"/> or one of its parents stays on each machine.</summary>
    public static bool IsLocal(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (IsLocalItself(e))
                return true;
        }

        return false;
    }

    public static List<NetSlot> Build(Entity entity, ComponentRegistry registry, bool ownerMoved)
    {
        var slots = new List<NetSlot>
        {
            new(SlotKind.Name, null, null, typeof(string), false),
            new(SlotKind.Enabled, null, null, typeof(bool), false),
            new(SlotKind.Position, entity.Transform, null, typeof(System.Numerics.Vector3), ownerMoved),
            new(SlotKind.Rotation, entity.Transform, null, typeof(System.Numerics.Vector3), ownerMoved),
            new(SlotKind.Scale, entity.Transform, null, typeof(System.Numerics.Vector3), false),
        };

        var animated = entity.GetComponent<Animation>() is not null;
        foreach (var component in entity.Components)
        {
            if (component is Transform or MissingComponent || !registry.IsRegistered(component.GetType()))
                continue;
            var type = component.GetType();
            var movement = MovementComponents.Contains(type);
            foreach (var property in registry.Get(type).Properties)
            {
                if (Skip(type, property.Name, animated))
                    continue;
                var owned = ownerMoved && (movement || OwnerProperty(type, property.Name));
                slots.Add(new NetSlot(SlotKind.Property, component, property, property.Type, owned));
            }

            if (Extras.TryGetValue(type, out var extras))
            {
                foreach (var (valueType, get, set) in extras)
                    slots.Add(new NetSlot(SlotKind.Extra, component, null, valueType, false, get, set));
            }
        }

        return slots;
    }

    // A building with no rally point. (Not NaN, which never equals itself, so it would be sent again and again.)
    private static readonly System.Numerics.Vector2 NoRally = new(float.MaxValue);

    // What a game keeps while it runs (not in scene files) that other players need to see: digging, training, carrying.
    private static readonly Dictionary<Type, (Type Type, Func<Component, object?> Get, Action<Component, object?> Set)[]> Extras = new()
    {
        [typeof(BlockBuilder)] =
        [
            (typeof(float), c => ((BlockBuilder)c).Progress, (c, v) => ((BlockBuilder)c).Progress = (float)v!),
            (typeof(int), c => ((BlockBuilder)c).Cracks, (c, v) => ((BlockBuilder)c).Cracks = (int)v!),
        ],
        [typeof(Producer)] =
        [
            (typeof(IReadOnlyList<string>), c => ((Producer)c).QueueList.ToArray(), (c, v) =>
            {
                var queue = ((Producer)c).QueueList;
                queue.Clear();
                queue.AddRange((IReadOnlyList<string>?)v ?? []);
            }),
            (typeof(float), c => ((Producer)c).Progress, (c, v) => ((Producer)c).Progress = (float)v!),
        ],
        [typeof(Worker)] = [(typeof(int), c => ((Worker)c).Load, (c, v) => ((Worker)c).Load = (int)v!)],
        [typeof(Unit)] = [(typeof(UnitOrder), c => ((Unit)c).Order, (c, v) => ((Unit)c).Order = (UnitOrder)v!)],
        [typeof(Faction)] = [(typeof(bool), c => ((Faction)c).IsDefeated, (c, v) => ((Faction)c).IsDefeated = (bool)v!)],
        [typeof(Building)] =
        [
            (typeof(System.Numerics.Vector2), c => ((Building)c).Rally ?? NoRally,
                (c, v) => ((Building)c).Rally = v is System.Numerics.Vector2 rally && rally != NoRally ? rally : null),
        ],
    };

    // Each machine works these out for itself (animation frames) or nobody else needs them (a lifetime's age).
    private static bool Skip(Type type, string property, bool animated) =>
        (type == typeof(Animation) && property == nameof(Animation.Time)) ||
        (type == typeof(Sprite) && property == nameof(Sprite.Frame) && animated) ||
        (type == typeof(Lifetime) && property == nameof(Lifetime.Age));

    // What a player's own movement changes, besides the movement components themselves.
    private static bool OwnerProperty(Type type, string property) =>
        (type == typeof(Body) && property == nameof(Body.Velocity)) ||
        (type == typeof(Sprite) && property is nameof(Sprite.FlipX) or nameof(Sprite.FlipY)) ||
        (type == typeof(Animation) && property is nameof(Animation.Clip) or nameof(Animation.Playing));
}
