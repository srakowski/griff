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

        /// <summary>
        /// False when the field is set aside by its <see cref="EntityNameAttribute.Unless"/> (a door's Target when it goes
        /// to another Scene), so its names aren't links and aren't flagged.
        /// </summary>
        public bool IsUsed => Kind.Unless is not { } unless || !IsSet(Component.GetType().GetProperty(unless)?.GetValue(Component));

        private static bool IsSet(object? value) => value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            bool on => on,
            float number => number != 0,
            int number => number != 0,
            _ => true,
        };
    }

    /// <summary>
    /// A field on <see cref="From"/> naming <see cref="To"/>: the Scene view draws it as an arrow between them when either
    /// is selected.
    /// </summary>
    public readonly record struct Link(Reference Reference, Entity To)
    {
        /// <summary>The entity whose field names <see cref="To"/>.</summary>
        public Entity From => Reference.Component.Entity!;

        /// <summary>
        /// "Lever to Door: Switch.Targets", the way the arrow points (from what a wire's or trigger's From names to the
        /// field's own entity, "Button to Lamp: Wire.From"), with what the arrow says when the component words it itself:
        /// "Board to Score Label: Counter.Track (Score)".
        /// </summary>
        public string Label =>
            $"{Tail.Name} to {Head.Name}: {Reference.Label}{(Reference.Component.LinkLabel(Reference.Property.Name) is { } words ? $" ({words})" : "")}";

        /// <summary>
        /// Where the Scene view's arrow starts: the field's entity, or the one it names for a field that says where
        /// something comes from (<see cref="EntityNameAttribute.Incoming"/>, a wire's or a trigger's From).
        /// </summary>
        public Entity Tail => Reference.Kind.Incoming ? To : From;

        /// <summary>Where the arrow points: the entity named, or the field's own for an incoming one.</summary>
        public Entity Head => Reference.Kind.Incoming ? From : To;

        /// <summary>
        /// What the arrow says: the component's own words for it (a trigger's "Score: Damage 2", see
        /// <see cref="Component.LinkLabel"/>), or its name and the field's, "Switch Targets".
        /// </summary>
        public string Words => Reference.Component.LinkLabel(Reference.Property.Name) ?? $"{Reference.Component.GetType().Name} {Reference.Property.Name}";
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
    /// neither does a prefab (one in <paramref name="prefabs"/>), which a game can bring in while it runs. In a prefab on
    /// its own (<paramref name="inPrefab"/>), only names of things inside the field's entity can be checked: the rest
    /// name things in whatever scene the prefab is placed in.
    /// </summary>
    public static IReadOnlyList<string> Unmatched(Reference reference, IReadOnlyCollection<string>? prefabs = null, bool inPrefab = false)
    {
        if (reference.Component.Entity is not { } owner || !reference.IsUsed || (inPrefab && !reference.Kind.Inside))
            return [];
        var names = reference.Names;
        if (names.Count == 0)
            return [];
        var known = Candidates(owner, reference.Kind).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        return [.. names.Where(n => !known.Contains(n) && (reference.Kind.Inside || prefabs?.Contains(n) != true)).Distinct()];
    }

    /// <summary>
    /// Every field in the scene (or a prefab on its own, see <see cref="Unmatched"/>) that holds a name matching nothing,
    /// with the names it can't find.
    /// </summary>
    public static IEnumerable<(Reference Reference, IReadOnlyList<string> Names)> UnmatchedIn(
        Scene scene, ComponentRegistry registry, IReadOnlyCollection<string>? prefabs = null, bool inPrefab = false) =>
        scene.Entities
            .SelectMany(e => On(e, registry))
            .Select(r => (Reference: r, Names: Unmatched(r, prefabs, inPrefab)))
            .Where(r => r.Names.Count > 0);

    /// <summary>
    /// Every entity a field on <paramref name="owner"/> can mean by <paramref name="name"/>, as a switch finds its
    /// targets: those inside the owner with the name (itself too, unless the field names one inside it), otherwise every
    /// one in the scene with it.
    /// </summary>
    public static IReadOnlyList<Entity> Named(Entity owner, EntityNameAttribute kind, string name)
    {
        var own = owner.SelfAndDescendants().Skip(kind.Inside ? 1 : 0).Where(e => e.Name == name).ToList();
        if (own.Count > 0 || kind.Inside || owner.Scene is null)
            return own;
        return [.. owner.Scene.Entities.Where(e => e.Name == name)];
    }

    /// <summary>
    /// The links out of <paramref name="entity"/>: each entity its fields name (see <see cref="Named"/>), other than
    /// itself, once per field. Fields set aside by their <see cref="EntityNameAttribute.Unless"/> have none.
    /// </summary>
    public static IEnumerable<Link> From(Entity entity, ComponentRegistry registry) =>
        On(entity, registry)
            .Where(r => r.IsUsed)
            .SelectMany(r => r.Names.SelectMany(n => Named(entity, r.Kind, n)).Distinct().Where(e => e != entity).Select(e => new Link(r, e)));

    /// <summary>The links into <paramref name="entity"/>: fields on the other entities in its scene that name it.</summary>
    public static IEnumerable<Link> To(Entity entity, ComponentRegistry registry) =>
        (entity.Scene?.Entities ?? []).Where(e => e != entity).SelectMany(e => From(e, registry)).Where(l => l.To == entity);

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
