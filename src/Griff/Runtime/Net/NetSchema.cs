using System.Reflection;
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
internal sealed class NetSlot(SlotKind kind, Component? component, PropertyInfo? property, Type type, bool ownerOwned,
    Func<object?>? get = null, Action<object?>? set = null)
{
    public SlotKind Kind { get; } = kind;

    public Component? Component { get; } = component;

    public PropertyInfo? Property { get; } = property;

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
        SlotKind.Extra => get!(),
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
            case SlotKind.Extra: set!(value); break;
            default:
                try
                {
                    Property!.SetValue(Component!, value);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or TargetInvocationException)
                {
                    // A setter that refused the value: keep what's there.
                }

                break;
        }
    }
}

/// <summary>
/// Which of an entity's values are sent, in an order both ends agree on (they have the same components). Each component
/// says what it sends (see <see cref="NetLocalAttribute"/>, <see cref="PlayerMovementAttribute"/>,
/// <see cref="SentAttribute"/>, <see cref="NotSentAttribute"/> and <see cref="INetState"/>); this knows none of them by name.
/// </summary>
internal static class NetSchema
{
    /// <summary>True if <paramref name="entity"/> moves for its player with a no-code movement component.</summary>
    public static bool HasMovement(Entity entity) => PlayerMovementAttribute.IsOn(entity);

    /// <summary>True if <paramref name="entity"/> itself (not counting its parents) stays on each machine.</summary>
    public static bool IsLocalItself(Entity entity)
    {
        if (entity.GetComponent<Networked>() is { } networked && networked.Sync != NetSync.Auto)
            return networked.Sync == NetSync.Local;
        return NetLocalAttribute.IsOn(entity);
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

        foreach (var component in entity.Components)
        {
            if (component is Transform or MissingComponent || !registry.IsRegistered(component.GetType()))
                continue;
            var type = component.GetType();
            var movement = PlayerMovementAttribute.IsOn(component);
            NetState? state = null;
            if (component is INetState stated)
                stated.Network(state = new NetState());

            // Its saved properties, but those it keeps to itself.
            foreach (var property in registry.Get(type).Properties)
            {
                var info = property.Info;
                if ((state?.Skipped.Contains(property.Name) ?? false) || info.IsDefined(typeof(NotSentAttribute), true))
                    continue;
                var owned = ownerMoved && (movement || info.GetCustomAttribute<SentAttribute>(true) is { ByMover: true });
                slots.Add(new NetSlot(SlotKind.Property, component, info, property.Type, owned));
            }

            // What it keeps while playing that others need to see.
            foreach (var (info, byMover) in NetState.SentOn(type))
            {
                if (registry.Get(type).Properties.Any(p => p.Name == info.Name) || (state?.Skipped.Contains(info.Name) ?? false))
                    continue;
                if (!NetCodec.CanSend(info.PropertyType))
                    throw new NotSupportedException($"{type.Name}.{info.Name} is [Sent], but a {info.PropertyType.Name} can't be sent.");
                slots.Add(new NetSlot(SlotKind.Property, component, info, info.PropertyType, ownerMoved && (movement || byMover)));
            }

            foreach (var (valueType, get, set) in state?.Values ?? [])
            {
                if (!NetCodec.CanSend(valueType))
                    throw new NotSupportedException($"{type.Name} sends a {valueType.Name}, which can't be sent.");
                slots.Add(new NetSlot(SlotKind.Extra, component, null, valueType, ownerMoved && movement, get, set));
            }
        }

        return slots;
    }
}
