using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>
/// Fields that name entities (properties marked <see cref="EntityNameAttribute"/>, a Switch's Targets or a camera's
/// Follow target): renaming an entity renames it in them too, and a name that matches nothing can be flagged.
/// </summary>
public static class EntityReferences
{
    /// <summary>A field that names entities: the component and its property.</summary>
    public readonly record struct Reference(Component Component, PropertyDescriptor Property)
    {
        public EntityNameAttribute Kind => Property.EntityName!;

        /// <summary>The names it holds: one, or several for a field that names several (separated by commas).</summary>
        public IReadOnlyList<string> Names => EntityReferences.Names(Property.GetValue(Component), Kind);

        /// <summary>"Follow.Target": what agents and messages call it.</summary>
        public string Label => $"{Component.GetType().Name}.{Property.Name}";
    }

    /// <summary>The names a field's value holds: split at commas for one that names several, empty when it's unset.</summary>
    public static IReadOnlyList<string> Names(object? value, EntityNameAttribute kind)
    {
        var text = (value as string ?? "").Trim();
        if (text.Length == 0)
            return [];
        return kind.Many ? text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [text];
    }

    /// <summary>Every field on <paramref name="entity"/>'s components that names entities.</summary>
    public static IEnumerable<Reference> On(Entity entity, ComponentRegistry registry) =>
        entity.Components
            .Where(c => registry.IsRegistered(c.GetType()))
            .SelectMany(c => registry.Get(c.GetType()).Properties.Where(p => p.EntityName is not null).Select(p => new Reference(c, p)));

    /// <summary>
    /// The entities a field on <paramref name="owner"/> can name: what's inside it for one like an attack's Hitbox,
    /// otherwise the whole scene.
    /// </summary>
    public static IEnumerable<Entity> Candidates(Entity owner, EntityNameAttribute kind) =>
        kind.Inside ? owner.SelfAndDescendants().Skip(1) : owner.Scene?.Entities ?? owner.SelfAndDescendants();

    /// <summary>
    /// The names <paramref name="reference"/> holds that match nothing: no entity it can name has that name, and
    /// neither does a prefab (one in <paramref name="prefabs"/>), which a game can bring in while it runs.
    /// </summary>
    public static IReadOnlyList<string> Unmatched(Reference reference, IReadOnlyCollection<string>? prefabs = null)
    {
        if (reference.Component.Entity is not { } owner)
            return [];
        var names = reference.Names;
        if (names.Count == 0)
            return [];
        var known = Candidates(owner, reference.Kind).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        return [.. names.Where(n => !known.Contains(n) && (reference.Kind.Inside || prefabs?.Contains(n) != true)).Distinct()];
    }

    /// <summary>Every field in the scene that holds a name matching nothing, with the names it can't find.</summary>
    public static IEnumerable<(Reference Reference, IReadOnlyList<string> Names)> UnmatchedIn(
        Scene scene, ComponentRegistry registry, IReadOnlyCollection<string>? prefabs = null) =>
        scene.Entities
            .SelectMany(e => On(e, registry))
            .Select(r => (Reference: r, Names: Unmatched(r, prefabs)))
            .Where(r => r.Names.Count > 0);

    /// <summary>
    /// The entity a field on <paramref name="owner"/> means by <paramref name="name"/>, as the game finds it: one inside
    /// the owner first (a lever's own "Hint"), otherwise the only one in the scene with that name. Null when nothing
    /// has the name, or several outside the owner do (the game picks between them as it plays).
    /// </summary>
    public static Entity? Resolve(Entity owner, EntityNameAttribute kind, string name)
    {
        var own = owner.SelfAndDescendants().Skip(kind.Inside ? 1 : 0).FirstOrDefault(e => e.Name == name);
        if (own is not null || kind.Inside)
            return own;
        var named = Candidates(owner, kind).Where(e => e.Name == name).Take(2).ToList();
        return named.Count == 1 ? named[0] : null;
    }

    /// <summary>
    /// Renames <paramref name="entity"/>, and every field in its scene that names it by its old name, as one undo step.
    /// A field is left alone when its name means another entity (see <see cref="Resolve"/>): one inside its own
    /// entity, or any of several with that name.
    /// </summary>
    public static IEdit Rename(Entity entity, string name, ComponentRegistry registry)
    {
        var oldName = entity.Name;
        var edits = new List<IEdit> { new RenameEntityEdit(entity, name) };
        if (entity.Scene is { } scene && name != oldName)
        {
            foreach (var reference in scene.Entities.SelectMany(e => On(e, registry)))
            {
                var owner = reference.Component.Entity!;
                var names = reference.Names;
                if (!names.Contains(oldName) || Resolve(owner, reference.Kind, oldName) != entity)
                    continue;
                var renamed = names.Select(n => n == oldName ? name : n).Distinct().ToList();
                edits.Add(new SetPropertyEdit(reference.Component, reference.Property, string.Join(", ", renamed)));
            }
        }

        return edits.Count == 1 ? edits[0] : new CompositeEdit(edits[0].Description, edits);
    }
}
