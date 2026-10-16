using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Griff.Scenes;

/// <summary>
/// Marks a component's <see cref="Box"/> property as the part of the world it covers: a <see cref="Room"/>'s, a
/// tilemap's, a collider's, a strategy map's. Something that works within an entity (a <see cref="TurnOrder"/>'s or a move
/// history's Within) covers whatever stands in its area as well as whatever is inside it. Found by this attribute, so a
/// script component's own property counts too.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class AreaAttribute : Attribute;

/// <summary>
/// The areas of component types (see <see cref="AreaAttribute"/>), found the first time each type is asked about and
/// kept for as long as it's loaded. Reading them allocates nothing.
/// </summary>
public static class ComponentAreas
{
    private static readonly ConditionalWeakTable<Type, Holder> Cache = new();

    private sealed record Holder(Func<Component, Box>? Read);

    /// <summary>True if components of <paramref name="type"/> cover an area of the world (see <see cref="AreaAttribute"/>).</summary>
    public static bool Has(Type type) => Get(type) is not null;

    /// <summary>
    /// The part of the world <paramref name="entity"/> covers: the area of the first of its components that has one (see
    /// <see cref="AreaAttribute"/>), or null for none.
    /// </summary>
    public static Box? Of(Entity entity)
    {
        var components = entity.ComponentList;
        for (var i = 0; i < components.Count; i++)
        {
            if (Get(components[i].GetType()) is { } read)
                return read(components[i]);
        }

        return null;
    }

    private static Func<Component, Box>? Get(Type type) => Cache.GetValue(type, Discover).Read;

    private static Holder Discover(Type type)
    {
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            foreach (var property in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!property.IsDefined(typeof(AreaAttribute), inherit: false))
                    continue;
                if (property.PropertyType != typeof(Box) || property.GetMethod is null || property.GetIndexParameters().Length > 0)
                    throw new NotSupportedException($"{type.Name}.{property.Name} is marked [Area], so it should be a Box property that can be read.");
                var bind = typeof(ComponentAreas).GetMethod(nameof(Getter), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(property.DeclaringType!);
                return new Holder((Func<Component, Box>)bind.Invoke(null, [property.GetMethod])!);
            }
        }

        return new Holder(null);
    }

    private static Func<Component, Box> Getter<T>(MethodInfo getter) where T : class
    {
        var d = getter.CreateDelegate<Func<T, Box>>();
        return c => d((T)(object)c);
    }
}

/// <summary>
/// The part of a scene something works on, from a property naming an entity (a <see cref="TurnOrder"/>'s Within): the
/// whole scene when it names none; otherwise that entity and everything inside it (a group), and whatever stands in the
/// part of the world it covers (an area: a <see cref="Room"/>'s, a tilemap's, a collider's; see
/// <see cref="AreaAttribute"/>). A name nothing in the scene has covers nothing. Worked out once (see <see cref="Of"/>)
/// and asked about as often as needed, allocating nothing.
/// </summary>
public readonly struct Scope
{
    private readonly bool _nothing;
    private readonly Box? _area;

    private Scope(Entity? root, Box? area, bool nothing)
    {
        Root = root;
        _area = area;
        _nothing = nothing;
    }

    /// <summary>The whole scene.</summary>
    public static Scope WholeScene => default;

    /// <summary>The entity it's within: null for the whole scene (or for a name nothing has).</summary>
    public Entity? Root { get; }

    /// <summary>True if it covers the whole scene: nothing was named.</summary>
    public bool IsWholeScene => Root is null && !_nothing;

    /// <summary>The part of the world it covers, if its entity covers one (see <see cref="AreaAttribute"/>).</summary>
    public Box? Area => _area;

    /// <summary>
    /// The scope a property of <paramref name="owner"/>'s names with <paramref name="within"/>: the entity called that (one
    /// inside the owner first, otherwise the first in its scene), or the whole scene for none.
    /// </summary>
    public static Scope Of(Entity? owner, string? within)
    {
        if (string.IsNullOrWhiteSpace(within))
            return WholeScene;
        var root = owner is null ? null : Named(owner, within.AsSpan().Trim());
        return root is null ? new Scope(null, null, nothing: true) : Of(root);
    }

    /// <summary>The scope of everything inside <paramref name="root"/> and standing in its area.</summary>
    public static Scope Of(Entity root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new Scope(root, ComponentAreas.Of(root), nothing: false);
    }

    /// <summary>True if it covers <paramref name="entity"/>: the whole scene, or inside its entity, or standing in its area.</summary>
    public bool Covers(Entity? entity)
    {
        if (entity is null || _nothing)
            return false;
        if (Root is null || entity.IsSelfOrDescendantOf(Root))
            return true;
        if (_area is not { } area)
            return false;
        var at = entity.Transform.WorldPosition;
        return area.Contains(new Vector2(at.X, at.Y));
    }

    /// <summary>True if it covers <paramref name="point"/> in the world: the whole scene, or in its area.</summary>
    public bool Covers(Vector2 point) => !_nothing && (Root is null || (_area is { } area && area.Contains(point)));

    // The entity called that: inside the owner first, otherwise the first in its scene.
    private static Entity? Named(Entity owner, ReadOnlySpan<char> name)
    {
        if (Inside(owner, name) is { } inside)
            return inside;
        if (owner.Scene is not { } scene)
            return null;
        // One in the same part of the scene (a minigame being played, or outside them) first.
        Entity? elsewhere = null;
        var entities = scene.EntitiesNow;
        for (var i = 0; i < entities.Length; i++)
        {
            if (!name.SequenceEqual(entities[i].Name))
                continue;
            if (Scene.SamePart(entities[i], owner))
                return entities[i];
            elsewhere ??= entities[i];
        }

        return elsewhere;

        static Entity? Inside(Entity entity, ReadOnlySpan<char> name)
        {
            if (name.SequenceEqual(entity.Name))
                return entity;
            var children = entity.ChildList;
            for (var i = 0; i < children.Count; i++)
            {
                if (Inside(children[i], name) is { } found)
                    return found;
            }

            return null;
        }
    }
}
