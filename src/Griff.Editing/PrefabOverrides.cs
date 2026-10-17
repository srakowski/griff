using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>
/// What a placed copy of a prefab changes from it: fields set differently, components and children added or taken
/// away, on the copy or anything inside it. Overrides aren't stored; they're whatever differs from the prefab, so
/// setting a field back to the prefab's value is reverting it. When the prefab is saved, <see cref="Merge"/> keeps each
/// copy's overrides and takes everything else from the new prefab.
/// </summary>
/// <remarks>
/// Copies and prefabs are matched part by part: children by name (the second "Spark" with the second "Spark"), and
/// components by type (the second Light with the second Light). A copy's own name, place (position and rotation),
/// enabled, locked and hidden state are its own, never overrides.
/// </remarks>
public static class PrefabOverrides
{
    /// <summary>The copy whose prefab <paramref name="entity"/> comes from: itself, or the nearest copy it's inside. Null if none.</summary>
    public static Entity? CopyFor(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.Prefab is not null)
                return e;
        }

        return null;
    }

    /// <summary>
    /// The part of <paramref name="prefab"/> (an entity made from the prefab of <paramref name="copy"/>) that
    /// <paramref name="entity"/>, the copy or something inside it, was made from. Null for a child the copy added.
    /// </summary>
    public static Entity? Counterpart(Entity prefab, Entity copy, Entity entity)
    {
        if (entity == copy)
            return prefab;
        if (entity.Parent is not { } parent || Counterpart(prefab, copy, parent) is not { } inPrefab)
            return null;
        return Match(entity, parent.Children, inPrefab.Children);
    }

    /// <summary>The component on <paramref name="counterpart"/> that <paramref name="component"/> matches, or null if the copy added it.</summary>
    public static Component? Counterpart(Entity counterpart, Component component)
    {
        if (component is Transform)
            return counterpart.Transform;
        return component.Entity is { } entity ? Match(component, entity.Components, counterpart.Components) : null;
    }

    /// <summary>
    /// True if <paramref name="property"/> of <paramref name="component"/> is set differently from the prefab's
    /// <paramref name="prefabComponent"/>. A copy's own position and rotation never count, nor the position of a part
    /// pinned to the screen.
    /// </summary>
    public static bool Differs(Component component, Component prefabComponent, PropertyDescriptor property) =>
        !IsOwn(component, property) && !ValueCodec.AreEqual(property.GetValue(component), property.GetValue(prefabComponent));

    /// <summary>
    /// True for a copy's position and rotation, which are where it's placed rather than something it changes, and for the
    /// position of anything a <see cref="ScreenAnchor"/> places, which is worked out from its anchor.
    /// </summary>
    public static bool IsOwn(Component component, PropertyDescriptor property) =>
        component is Transform && (component.Entity?.Prefab is not null && property.Name is nameof(Transform.Position) or nameof(Transform.Rotation) ||
                                   component.Entity?.GetComponent<ScreenAnchor>() is not null && property.Name is nameof(Transform.Position));

    /// <summary>A property value from one component that's safe to put on another (a list is copied).</summary>
    public static object? CopyValue(object? value) => value is IReadOnlyList<string> lines ? lines.ToArray() : value;

    /// <summary>
    /// Everything <paramref name="copy"/> (and what's inside it) changes from <paramref name="prefab"/>, an entity
    /// made from its prefab, in Hierarchy order. A copy of another prefab inside it counts against its own prefab, so
    /// it's left out.
    /// </summary>
    public static IReadOnlyList<PrefabOverride> Find(Entity copy, Entity prefab, ComponentRegistry registry)
    {
        var overrides = new List<PrefabOverride>();
        Find(copy, prefab, "", registry, overrides);
        return overrides;
    }

    private static void Find(Entity entity, Entity prefab, string path, ComponentRegistry registry, List<PrefabOverride> overrides)
    {
        if (path.Length > 0 && entity.IsEnabled != prefab.IsEnabled)
            overrides.Add(new PrefabOverride(entity, path, PrefabOverrideKind.Enabled, null, null));

        foreach (var component in entity.Components)
        {
            if (Counterpart(prefab, component) is not { } prefabComponent)
            {
                overrides.Add(new PrefabOverride(entity, path, PrefabOverrideKind.AddedComponent, component, null));
                continue;
            }

            if (component is MissingComponent)
                continue;
            foreach (var property in registry.Get(component.GetType()).Properties)
            {
                if (Differs(component, prefabComponent, property))
                    overrides.Add(new PrefabOverride(entity, path, PrefabOverrideKind.Field, component, property));
            }
        }

        foreach (var prefabComponent in prefab.Components)
        {
            if (Match(prefabComponent, prefab.Components, entity.Components) is null)
                overrides.Add(new PrefabOverride(entity, path, PrefabOverrideKind.RemovedComponent, prefabComponent, null));
        }

        foreach (var child in entity.Children)
        {
            var childPath = path.Length == 0 ? child.Name : $"{path}/{child.Name}";
            if (Match(child, entity.Children, prefab.Children) is not { } prefabChild)
                overrides.Add(new PrefabOverride(child, childPath, PrefabOverrideKind.AddedChild, null, null));
            else if (child.Prefab is null)
                Find(child, prefabChild, childPath, registry, overrides);
        }

        foreach (var prefabChild in prefab.Children)
        {
            if (Match(prefabChild, prefab.Children, entity.Children) is null)
                overrides.Add(new PrefabOverride(prefabChild, path.Length == 0 ? prefabChild.Name : $"{path}/{prefabChild.Name}", PrefabOverrideKind.RemovedChild, null, null));
        }
    }

    /// <summary>
    /// Brings <paramref name="fresh"/>, a detached entity just made from a prefab's new version, into line with
    /// <paramref name="copy"/>'s overrides of the old version (<paramref name="old"/>, made from it): fields the copy set
    /// differently keep the copy's values, components and children it added come along (copied), and ones it took
    /// away stay away. Everything else is the new prefab's. Returns <paramref name="fresh"/>.
    /// </summary>
    public static Entity Merge(Entity copy, Entity old, Entity fresh, SceneSerializer serializer)
    {
        MergeEntity(copy, old, fresh, serializer, serializer.Components);
        return fresh;
    }

    private static void MergeEntity(Entity copy, Entity old, Entity fresh, SceneSerializer serializer, ComponentRegistry registry)
    {
        if (copy.Parent is not null && copy.Prefab is null)
        {
            // Inside the copy: whether it's on is the copy's to change too. (The copy itself keeps its own anyway.)
            if (copy.IsEnabled != old.IsEnabled)
                fresh.IsEnabled = copy.IsEnabled;
            if (copy.IsLocked != old.IsLocked)
                fresh.IsLocked = copy.IsLocked;
            if (copy.IsHidden != old.IsHidden)
                fresh.IsHidden = copy.IsHidden;
        }

        // Components: the fresh ones the copy has keep its changed fields; ones it took away go.
        foreach (var freshComponent in fresh.Components.ToList())
        {
            if (Match(freshComponent, fresh.Components, old.Components) is not { } oldComponent)
                continue;
            if (Match(oldComponent, old.Components, copy.Components) is not { } copyComponent)
            {
                if (freshComponent is not Transform)
                    fresh.RemoveComponent(freshComponent);
                continue;
            }

            if (freshComponent is MissingComponent || copyComponent is MissingComponent)
                continue;
            foreach (var property in registry.Get(freshComponent.GetType()).Properties)
            {
                if (!ValueCodec.AreEqual(property.GetValue(copyComponent), property.GetValue(oldComponent)))
                    property.SetValue(freshComponent, CopyValue(property.GetValue(copyComponent)));
            }
        }

        // ...and ones it added come along.
        foreach (var copyComponent in copy.Components)
        {
            if (copyComponent is not Transform && Match(copyComponent, copy.Components, old.Components) is null)
                fresh.AddComponent(serializer.DeserializeComponent(serializer.SerializeComponent(copyComponent)));
        }

        // Children, the same way.
        foreach (var freshChild in fresh.Children.ToList())
        {
            if (Match(freshChild, fresh.Children, old.Children) is not { } oldChild)
                continue;
            if (Match(oldChild, old.Children, copy.Children) is not { } copyChild)
            {
                fresh.RemoveChild(freshChild);
                continue;
            }

            // A copy of another prefab inside follows that prefab, not this one.
            if (copyChild.Prefab is null)
                MergeEntity(copyChild, oldChild, freshChild, serializer, registry);
        }

        foreach (var copyChild in copy.Children)
        {
            if (Match(copyChild, copy.Children, old.Children) is null)
            {
                var added = serializer.Clone(copyChild);
                fresh.AddChild(added);
            }
        }
    }

    // The nth child called this among `to` for the nth called this among `from`.
    private static Entity? Match(Entity entity, IReadOnlyList<Entity> from, IReadOnlyList<Entity> to)
    {
        var nth = from.TakeWhile(e => e != entity).Count(e => e.Name == entity.Name);
        return to.Where(e => e.Name == entity.Name).ElementAtOrDefault(nth);
    }

    // The nth component of this kind among `to` for the nth of it among `from`.
    private static Component? Match(Component component, IReadOnlyList<Component> from, IReadOnlyList<Component> to)
    {
        var kind = Kind(component);
        var nth = from.TakeWhile(c => c != component).Count(c => Kind(c) == kind);
        return to.Where(c => Kind(c) == kind).ElementAtOrDefault(nth);
    }

    private static string Kind(Component component) => component is MissingComponent missing ? "?" + missing.TypeName : component.GetType().FullName!;
}

public enum PrefabOverrideKind
{
    /// <summary>A field set differently from the prefab.</summary>
    Field,

    /// <summary>A component the prefab doesn't have.</summary>
    AddedComponent,

    /// <summary>A component of the prefab's that the copy doesn't have (<see cref="PrefabOverride.Component"/> is the prefab's).</summary>
    RemovedComponent,

    /// <summary>A child the prefab doesn't have.</summary>
    AddedChild,

    /// <summary>A child of the prefab's that the copy doesn't have (<see cref="PrefabOverride.Entity"/> is the prefab's).</summary>
    RemovedChild,

    /// <summary>Something inside the copy turned on or off unlike in the prefab.</summary>
    Enabled,
}

/// <summary>
/// One thing a copy changes from its prefab. <see cref="Path"/> is where, from the copy: "" for the copy itself, or
/// "Gun/Muzzle" for something inside it.
/// </summary>
public sealed record PrefabOverride(Entity Entity, string Path, PrefabOverrideKind Kind, Component? Component, PropertyDescriptor? Property)
{
    /// <summary>How it reads to a person or an agent: "Sprite.Tint", "Gun: added Light", "removed child Spark".</summary>
    public override string ToString()
    {
        var where = Path.Length == 0 ? "" : Path + ": ";
        var component = Component is MissingComponent missing ? missing.TypeName : Component?.GetType().Name;
        return Kind switch
        {
            PrefabOverrideKind.Field => $"{where}{component}.{Property!.Name}",
            PrefabOverrideKind.AddedComponent => $"{where}added {component}",
            PrefabOverrideKind.RemovedComponent => $"{where}removed {component}",
            PrefabOverrideKind.AddedChild => $"added child {Path}",
            PrefabOverrideKind.RemovedChild => $"removed child {Path}",
            _ => $"{where}{(Entity.IsEnabled ? "enabled" : "disabled")}",
        };
    }
}
