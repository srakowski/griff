using System.Globalization;
using System.Numerics;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>One saved property of a catalog entry: what kind of value it takes, what it starts at, and what it does.</summary>
public sealed record CatalogProperty(string Name, string Kind, string Default, DocText Doc);

/// <summary>Everything the editor's component catalog shows about one component type.</summary>
public sealed class CatalogEntry
{
    internal CatalogEntry(ComponentDescriptor descriptor, IReadOnlyList<ComponentDescriptor> requiredBy)
    {
        Descriptor = descriptor;
        IsScript = descriptor.Type.Assembly != typeof(Component).Assembly;
        Category = descriptor.Category ?? (IsScript ? ComponentCatalog.ScriptsCategory : ComponentCatalog.OtherCategory);
        Summary = DocComments.Summary(descriptor.Type);
        Remarks = DocComments.Remarks(descriptor.Type);
        RequiredBy = requiredBy;
        Properties = DescribeProperties(descriptor);
    }

    public ComponentDescriptor Descriptor { get; }

    public string Name => Descriptor.DisplayName;

    public string Category { get; }

    /// <summary>True for components from the project's scripts.</summary>
    public bool IsScript { get; }

    public DocText Summary { get; }

    public DocText Remarks { get; }

    public IReadOnlyList<ComponentLink> Requires => Descriptor.Requires;

    public IReadOnlyList<ComponentLink> WorksWith => Descriptor.WorksWith;

    /// <summary>Components that need this one.</summary>
    public IReadOnlyList<ComponentDescriptor> RequiredBy { get; }

    public IReadOnlyList<CatalogProperty> Properties { get; }

    private static List<CatalogProperty> DescribeProperties(ComponentDescriptor descriptor)
    {
        Component? sample;
        try
        {
            sample = descriptor.CreateInstance();
        }
        catch (Exception)
        {
            sample = null; // A script whose constructor throws still gets documented, just without defaults.
        }

        return descriptor.Properties
            .Select(p => new CatalogProperty(
                p.Name,
                ComponentCatalog.KindOf(p.Type),
                sample is null ? "" : ComponentCatalog.Format(SafeGet(p, sample)),
                DocComments.Summary(p.Info)))
            .ToList();
    }

    private static object? SafeGet(PropertyDescriptor property, Component component)
    {
        try
        {
            return property.GetValue(component);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// The component catalog: every component that can be added, with its documentation and how it depends on others, and
/// what adding one to an entity brings along with it.
/// </summary>
public static class ComponentCatalog
{
    public const string ScriptsCategory = "Scripts";
    public const string OtherCategory = "Other";

    /// <summary>The order categories are listed in; any others follow alphabetically, then Other, then Scripts.</summary>
    public static IReadOnlyList<string> CategoryOrder { get; } =
        ["Rendering", "Physics", "Movement", "Combat", "Gameplay", "Level", "Story", "UI", "Audio"];

    /// <summary>Every component that can be added (all but Transform), by category, then name.</summary>
    public static IReadOnlyList<CatalogEntry> Build(ComponentRegistry registry)
    {
        var all = registry.All.Where(d => d.Type != typeof(Transform)).ToList();
        return all
            .Select(d => new CatalogEntry(d, all.Where(o => o.Requires.Any(r => r.Type.IsAssignableFrom(d.Type))).OrderBy(o => o.DisplayName).ToList()))
            .OrderBy(e => CategoryRank(e.Category))
            .ThenBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static int CategoryRank(string category)
    {
        var index = CategoryOrder.ToList().FindIndex(c => c.Equals(category, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index
            : category == ScriptsCategory ? int.MaxValue
            : category == OtherCategory ? int.MaxValue - 1
            : CategoryOrder.Count;
    }

    /// <summary>
    /// The registered component that fills a requirement: the type itself, or for a base type like
    /// <see cref="Collider"/>, the first kind of it by name (a <see cref="BoxCollider"/>).
    /// </summary>
    public static ComponentDescriptor? Satisfier(ComponentRegistry registry, Type required) =>
        registry.All
            .Where(d => required.IsAssignableFrom(d.Type))
            .OrderBy(d => d.Type == required ? 0 : 1)
            .ThenBy(d => d.DisplayName, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>The registered kinds of component that fill a requirement, e.g. BoxCollider and CircleCollider for a Collider.</summary>
    public static IReadOnlyList<ComponentDescriptor> Options(ComponentRegistry registry, Type required) =>
        registry.All.Where(d => required.IsAssignableFrom(d.Type)).OrderBy(d => d.DisplayName, StringComparer.Ordinal).ToList();

    /// <summary>
    /// What adding <paramref name="descriptor"/> to <paramref name="entity"/> adds, in order: each requirement the entity
    /// doesn't meet yet (and theirs, first), then the component itself.
    /// </summary>
    public static IReadOnlyList<ComponentDescriptor> PlanAdd(ComponentRegistry registry, ComponentDescriptor descriptor, Entity? entity)
    {
        var plan = new List<ComponentDescriptor>();
        Visit(descriptor, []);
        return plan;

        void Visit(ComponentDescriptor d, HashSet<Type> visiting)
        {
            if (!visiting.Add(d.Type))
                return;
            foreach (var requirement in d.Requires)
            {
                if ((entity is not null && requirement.IsMetBy(entity)) || plan.Any(p => requirement.Type.IsAssignableFrom(p.Type)))
                    continue;
                if (Satisfier(registry, requirement.Type) is { } satisfier)
                    Visit(satisfier, visiting);
            }

            plan.Add(d);
        }
    }

    /// <summary>The edits that add <paramref name="descriptor"/> and what it needs to <paramref name="entity"/>, as one undo step.</summary>
    public static IEdit AddWithRequirements(ComponentRegistry registry, ComponentDescriptor descriptor, Entity entity)
    {
        var edits = PlanAdd(registry, descriptor, entity).Select(d => (IEdit)new AddComponentEdit(entity, d.CreateInstance())).ToList();
        return edits.Count == 1 ? edits[0] : new CompositeEdit($"Add {descriptor.DisplayName}", edits);
    }

    /// <summary>
    /// What <paramref name="component"/> needs that its entity doesn't have (a PlatformerMovement's Body), so it won't
    /// work until one is added. Empty for a component that isn't on an entity, or isn't registered.
    /// </summary>
    public static IReadOnlyList<ComponentLink> Unmet(ComponentRegistry registry, Component component) =>
        component.Entity is { } entity && registry.TryGet(component.GetType()) is { } descriptor
            ? descriptor.Requires.Where(r => !r.IsMetBy(entity, component)).ToList()
            : [];

    /// <summary>
    /// The components on <paramref name="component"/>'s entity that need it: each requires something it fills, and
    /// nothing else on the entity fills that too (with two colliders, either can go).
    /// </summary>
    public static IReadOnlyList<Component> Dependents(ComponentRegistry registry, Component component)
    {
        if (component.Entity is not { } entity)
            return [];
        var others = entity.Components.Where(c => c != component).ToList();
        return others
            .Where(c => registry.TryGet(c.GetType()) is { } descriptor && descriptor.Requires.Any(r =>
                r.Type.IsInstanceOfType(component) && !others.Any(r.Type.IsInstanceOfType)))
            .ToList();
    }

    /// <summary>"a Body" or "an Inventory": a requirement as a sentence names it.</summary>
    public static string Article(string name) => ("aeiouAEIOU".Contains(name[0]) ? "an " : "a ") + name;

    /// <summary>A friendly name for a property's kind of value.</summary>
    public static string KindOf(Type type) =>
        type == typeof(bool) ? "On/off"
        : type == typeof(int) ? "Whole number"
        : type == typeof(float) || type == typeof(double) ? "Number"
        : type == typeof(string) ? "Text"
        : type.IsEnum ? "Choice of " + string.Join(", ", Enum.GetNames(type))
        : type == typeof(Color) ? "Color"
        : type == typeof(Vector2) ? "X, Y"
        : type == typeof(Vector3) ? "X, Y, Z"
        : type == typeof(Vector4) ? "X, Y, Z, W"
        : type == typeof(Quaternion) ? "Rotation"
        : type == typeof(IReadOnlyList<string>) ? "Lines of text"
        : type.Name;

    /// <summary>A value as the catalog shows it, e.g. 7, 0.5, On, #FFFFFFFF, (empty).</summary>
    public static string Format(object? value) => value switch
    {
        null => "(empty)",
        bool b => b ? "On" : "Off",
        float f => f.ToString("0.###", CultureInfo.InvariantCulture),
        double d => d.ToString("0.###", CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        string s => s.Length == 0 ? "(empty)" : s,
        Vector2 v => $"{Format(v.X)}, {Format(v.Y)}",
        Vector3 v => $"{Format(v.X)}, {Format(v.Y)}, {Format(v.Z)}",
        Vector4 v => $"{Format(v.X)}, {Format(v.Y)}, {Format(v.Z)}, {Format(v.W)}",
        Quaternion q => q == Quaternion.Identity ? "None" : $"{Format(q.X)}, {Format(q.Y)}, {Format(q.Z)}, {Format(q.W)}",
        IReadOnlyList<string> lines => lines.Count == 0 || (lines.Count == 1 && lines[0].Length == 0) ? "(empty)" : $"{lines.Count} line{(lines.Count == 1 ? "" : "s")}",
        _ => value.ToString() ?? "",
    };
}
