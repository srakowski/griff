using System.Collections.Concurrent;
using System.Reflection;

namespace Griff.Scenes;

// How a component says what it sends in a network game. Without any of these, the host sends every saved property of
// every component (Griff's and scripts' alike) whenever it changes.

/// <summary>
/// Each machine in a network game keeps its own copy of an entity with this component: a camera, the HUD, a menu, the
/// music. Nothing on it is sent, and it follows that machine's own player. A <see cref="Networked"/> component's
/// <see cref="Networked.Sync"/> overrides it.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NetLocalAttribute : Attribute
{
    private static readonly ConcurrentDictionary<Type, bool> Marked = new();

    /// <summary>True if <paramref name="component"/>'s type is marked.</summary>
    public static bool IsOn(Component component) => Marked.GetOrAdd(component.GetType(), t => t.IsDefined(typeof(NetLocalAttribute), true));

    /// <summary>True if one of <paramref name="entity"/>'s components is marked.</summary>
    public static bool IsOn(Entity entity)
    {
        foreach (var component in entity.Components)
        {
            if (IsOn(component))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Marks a component that moves its entity for the player who controls it, with no code: a platformer's or top-down
/// game's movement. In a network game that player's own machine moves it (no waiting for the host) and tells everyone
/// where it is, which way it faces, and all of this component's values.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PlayerMovementAttribute : Attribute
{
    private static readonly ConcurrentDictionary<Type, bool> Marked = new();

    /// <summary>True if <paramref name="component"/>'s type is marked.</summary>
    public static bool IsOn(Component component) => Marked.GetOrAdd(component.GetType(), t => t.IsDefined(typeof(PlayerMovementAttribute), true));

    /// <summary>True if one of <paramref name="entity"/>'s components is marked.</summary>
    public static bool IsOn(Entity entity)
    {
        foreach (var component in entity.Components)
        {
            if (IsOn(component))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Sends a property in a network game. On a value kept only while playing (not saved in the scene, so not sent by
/// itself), it's sent as saved ones are: how far a block is dug, how much a worker carries. Its setter can be private.
/// With <see cref="ByMover"/>, the player moving the entity decides it rather than the host (see
/// <see cref="PlayerMovementAttribute"/>).
/// </summary>
/// <example><c>[Sent] public float Charge { get; private set; }</c></example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SentAttribute : Attribute
{
    /// <summary>When a player moves the entity themselves, their machine decides this (its speed, which way it faces).</summary>
    public bool ByMover { get; init; }
}

/// <summary>
/// Keeps a saved property from being sent in a network game: each machine works it out for itself (an animation's
/// time) or nobody else needs it (how old something is).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotSentAttribute : Attribute;

/// <summary>
/// A component that says for itself what it sends in a network game, besides its saved and <see cref="SentAttribute"/>
/// properties: values it keeps in a form that can't be sent as it is (a board's gems, sent as lines of text), or a
/// property it doesn't send just now.
/// </summary>
public interface INetState
{
    /// <summary>
    /// Says what's sent (called when the entity joins a network game, the same on every machine): add values to
    /// <paramref name="state"/>, or skip properties.
    /// </summary>
    void Network(NetState state);
}

/// <summary>What an <see cref="INetState"/> component sends: see <see cref="INetState.Network"/>.</summary>
public sealed class NetState
{
    internal List<(Type Type, Func<object?> Get, Action<object?> Set)> Values { get; } = [];

    internal HashSet<string> Skipped { get; } = [];

    /// <summary>
    /// Sends a value the host reads with <paramref name="get"/> and every other machine sets with <paramref name="set"/>.
    /// <typeparamref name="T"/> is one a property can be: a bool, number, string, enum, color, vector, or
    /// <c>IReadOnlyList&lt;string&gt;</c> (lines, of which only those that changed are sent).
    /// </summary>
    public void Add<T>(Func<T> get, Action<T> set)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        Values.Add((typeof(T), () => get(), value => set((T)value!)));
    }

    /// <summary>Doesn't send the saved property called <paramref name="property"/>.</summary>
    public void Skip(string property) => Skipped.Add(property);

    // The properties of a type marked [Sent], public or not, with whether the mover decides them.
    private static readonly ConcurrentDictionary<Type, (PropertyInfo Property, bool ByMover)[]> SentProperties = new();

    internal static (PropertyInfo Property, bool ByMover)[] SentOn(Type type) => SentProperties.GetOrAdd(type, t =>
        t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .Select(p => (Property: p, Sent: p.GetCustomAttribute<SentAttribute>()))
            .Where(x => x.Sent is not null)
            .OrderBy(x => x.Property.MetadataToken)
            .Select(x => (x.Property, x.Sent!.ByMover))
            .ToArray());
}
