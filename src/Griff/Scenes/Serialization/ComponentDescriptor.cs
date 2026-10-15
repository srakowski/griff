using System.Reflection;
using System.Text.Json;

namespace Griff.Scenes.Serialization;

/// <summary>
/// What the engine knows about a component type: the name it's saved under and its editable properties.
/// The serializer and the editor's inspector both work from this, so they can't disagree.
/// </summary>
public sealed class ComponentDescriptor
{
    internal ComponentDescriptor(Type type)
    {
        Type = type;
        Name = type.GetCustomAttribute<ComponentNameAttribute>()?.Name ?? type.FullName!;
        DisplayName = type.Name;
        Properties = DiscoverProperties(type);
        Category = type.GetCustomAttribute<CategoryAttribute>()?.Name;
        Requires = type.GetCustomAttributes<RequiresAttribute>().Select(a => new ComponentLink(a.Type, a.Note)).ToList();
        _worksWith = type.GetCustomAttributes<WorksWithAttribute>().Select(a => new ComponentLink(a.Type, a.Note)).ToList();
        FormerNames = type.GetCustomAttributes<FormerNameAttribute>(inherit: false)
            .SelectMany(a => a.Name.Contains('.') || type.Namespace is null ? [a.Name] : new[] { a.Name, $"{type.Namespace}.{a.Name}" })
            .Where(n => n != Name)
            .Distinct()
            .ToList();
        Version = type.GetCustomAttribute<ComponentVersionAttribute>()?.Version ?? 1;
        _upgrades = DiscoverUpgrades(type, Version);
        Actions = ComponentActions.Of(type);
        Outputs = ComponentOutputs.Of(type);
    }

    // The step up from version n is at n - 1.
    private readonly List<Action<SavedData>> _upgrades;

    public Type Type { get; }

    /// <summary>The name written to scene files.</summary>
    public string Name { get; }

    /// <summary>The name shown in the editor.</summary>
    public string DisplayName { get; }

    /// <summary>Saved, editable properties in declaration order (base class first).</summary>
    public IReadOnlyList<PropertyDescriptor> Properties { get; }

    /// <summary>
    /// What it does that anything can set off (see <see cref="ActionAttribute"/>): a trigger's Do, a wire's Input, a
    /// script's <c>Game.Do</c>.
    /// </summary>
    public IReadOnlyList<ComponentAction> Actions { get; }

    /// <summary>
    /// What it knows that anything can read (see <see cref="OutputAttribute"/>): a counter, bar or text showing it, a wire
    /// carrying it.
    /// </summary>
    public IReadOnlyList<ComponentOutput> Outputs { get; }

    /// <summary>Where the editor's catalog lists it, from <see cref="CategoryAttribute"/>; null if it doesn't say.</summary>
    public string? Category { get; }

    /// <summary>Components it needs on the same entity, from <see cref="RequiresAttribute"/>.</summary>
    public IReadOnlyList<ComponentLink> Requires { get; }

    /// <summary>
    /// Components that go well with it, from <see cref="WorksWithAttribute"/>: its own, then those of other registered
    /// components that list themselves here (<see cref="WorksWithAttribute.Back"/>).
    /// </summary>
    public IReadOnlyList<ComponentLink> WorksWith => _worksWith;

    private readonly List<ComponentLink> _worksWith;

    // Another component lists itself on this one's list (see WorksWithAttribute.Back), or stops doing so.
    internal void AddWorksWith(ComponentLink link)
    {
        if (!_worksWith.Any(l => l.Type == link.Type))
            _worksWith.Add(link);
    }

    internal void RemoveWorksWith(Type type) => _worksWith.RemoveAll(l => l.Type == type && l.Back);

    /// <summary>
    /// Names it was saved under before, from <see cref="FormerNameAttribute"/> (a short one also with the class's
    /// namespace in front): scenes that use them still load, and save under <see cref="Name"/>.
    /// </summary>
    public IReadOnlyList<string> FormerNames { get; }

    /// <summary>The version of its saved data, from <see cref="ComponentVersionAttribute"/>; 1 without one.</summary>
    public int Version { get; }

    public Component CreateInstance() => (Component)Activator.CreateInstance(Type)!;

    /// <summary>Runs the <see cref="UpgradeFromAttribute"/> steps that bring data saved at <paramref name="from"/> up to <see cref="Version"/>.</summary>
    internal void Upgrade(SavedData data, int from)
    {
        for (var version = Math.Max(from, 1); version < Version; version++)
            _upgrades[version - 1](data);
    }

    // One static void (SavedData) step from each version below the current one, no more and no fewer.
    private static List<Action<SavedData>> DiscoverUpgrades(Type type, int version)
    {
        if (version < 1)
            throw new NotSupportedException($"{type.Name}'s [ComponentVersion] is {version}; versions start at 1.");

        var steps = new Action<SavedData>?[version - 1];
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (method.GetCustomAttribute<UpgradeFromAttribute>() is not { } step)
                continue;
            var parameters = method.GetParameters();
            if (method.ReturnType != typeof(void) || parameters.Length != 1 || parameters[0].ParameterType != typeof(SavedData))
                throw new NotSupportedException($"{type.Name}.{method.Name} upgrades saved data, so it should be static void {method.Name}(SavedData data).");
            if (step.Version < 1 || step.Version >= version)
                throw new NotSupportedException($"{type.Name}.{method.Name} upgrades from version {step.Version}, but {type.Name} is at version {version}. Raise its [ComponentVersion] to {Math.Max(step.Version + 1, 2)}.");
            if (steps[step.Version - 1] is not null)
                throw new NotSupportedException($"{type.Name} has two steps that upgrade from version {step.Version}.");
            steps[step.Version - 1] = method.CreateDelegate<Action<SavedData>>();
        }

        for (var i = 0; i < steps.Length; i++)
        {
            if (steps[i] is null)
                throw new NotSupportedException($"{type.Name} is at version {version} but has no step that upgrades from version {i + 1}. Add a static method marked [UpgradeFrom({i + 1})].");
        }

        return steps.Select(s => s!).ToList();
    }

    private static List<PropertyDescriptor> DiscoverProperties(Type type)
    {
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod?.IsPublic == true && p.SetMethod?.IsPublic == true)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => p.GetCustomAttribute<DoNotSerializeAttribute>() is null)
            .OrderBy(p => InheritanceDepth(p.DeclaringType!))
            .ThenBy(p => p.MetadataToken)
            .ToList();

        foreach (var property in properties)
        {
            if (!ValueCodec.IsSupported(property.PropertyType))
            {
                throw new NotSupportedException(
                    $"{type.Name}.{property.Name} has type {property.PropertyType.Name}, which can't be saved yet. " +
                    $"Mark it [DoNotSerialize] or use a supported type.");
            }
        }

        // A group runs on from its [Group] to the next one in the same class.
        var descriptors = new List<PropertyDescriptor>();
        string? group = null;
        Type? declaring = null;
        foreach (var property in properties)
        {
            if (property.DeclaringType != declaring)
                (group, declaring) = (null, property.DeclaringType);
            group = property.GetCustomAttribute<GroupAttribute>()?.Name ?? group;
            descriptors.Add(new PropertyDescriptor(property, group));
        }

        foreach (var descriptor in descriptors)
            descriptor.Condition = descriptor.ShownWhen is { } shownWhen ? descriptors.FirstOrDefault(d => d.Name == shownWhen.Property) : null;
        return descriptors;
    }

    private static int InheritanceDepth(Type type)
    {
        var depth = 0;
        for (var t = type.BaseType; t is not null; t = t.BaseType)
            depth++;
        return depth;
    }
}

/// <summary>
/// A component type another one needs or works with, plus a note on why. <see cref="Type"/> may be a base type such as
/// <see cref="Collider"/>, which any kind of it satisfies.
/// </summary>
public sealed record ComponentLink(Type Type, string? Note)
{
    // Put on the list by the other component (see WorksWithAttribute.Back).
    internal bool Back { get; init; }

    /// <summary>True if <paramref name="entity"/> has a component of this type (or a subtype).</summary>
    public bool IsMetBy(Entity entity) => entity.Components.Any(c => Type.IsInstanceOfType(c));
}

public sealed class PropertyDescriptor
{
    private readonly PropertyInfo _property;

    internal PropertyDescriptor(PropertyInfo property, string? group = null)
    {
        _property = property;
        Group = group;
        ShownWhen = property.GetCustomAttribute<ShownWhenAttribute>();
        SerializedName = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
        FormerNames = property.GetCustomAttributes<FormerNameAttribute>().Select(a => a.Name).ToList();
        FormerSerializedNames = FormerNames.Select(n => JsonNamingPolicy.CamelCase.ConvertName(n)).Where(n => n != SerializedName).Distinct().ToList();
        Distance = property.PropertyType == typeof(float) ? property.GetCustomAttribute<DistanceAttribute>() : null;
        EntityName = property.PropertyType == typeof(string) ? property.GetCustomAttribute<EntityNameAttribute>() : null;
        Names = property.PropertyType != typeof(string) ? null
            : property.GetCustomAttribute<NamesAttribute>() ?? (EntityName is null && IsFontName(property.Name) ? FontNames : null);
        ActionName = property.PropertyType == typeof(string) ? property.GetCustomAttribute<ActionNameAttribute>() : null;
        OutputName = property.PropertyType == typeof(string) ? property.GetCustomAttribute<OutputNameAttribute>() : null;
    }

    // A string called Font, or ending in Font ("TitleFont"), names a font without being marked.
    private static readonly NamesAttribute FontNames = new(NameKind.Font);

    private static bool IsFontName(string name) => name.EndsWith("Font", StringComparison.Ordinal);

    internal PropertyInfo Info => _property;

    public string Name => _property.Name;

    /// <summary>The key written to scene files.</summary>
    public string SerializedName { get; }

    /// <summary>Names it had in code before, from <see cref="FormerNameAttribute"/>: values saved under them still load.</summary>
    public IReadOnlyList<string> FormerNames { get; }

    /// <summary>The keys <see cref="FormerNames"/> were saved under.</summary>
    internal IReadOnlyList<string> FormerSerializedNames { get; }

    public Type Type => _property.PropertyType;

    /// <summary>For a number marked <see cref="DistanceAttribute"/>: how the Scene view draws it as a circle around the entity.</summary>
    public DistanceAttribute? Distance { get; }

    /// <summary>For a string marked <see cref="EntityNameAttribute"/>: it names entities, which the Inspector picks from a list.</summary>
    public EntityNameAttribute? EntityName { get; }

    /// <summary>
    /// For a string marked <see cref="NamesAttribute"/>: it names a prefab, scene, input action, collision layer, message
    /// or font, which the Inspector picks from a list of the project's. A string called Font (or ending in Font) names a
    /// font without being marked.
    /// </summary>
    public NamesAttribute? Names { get; }

    /// <summary>
    /// For a string marked <see cref="ActionNameAttribute"/>: it names an action of the entities another property names (or
    /// of its own entity), which the Inspector picks from a list of theirs.
    /// </summary>
    public ActionNameAttribute? ActionName { get; }

    /// <summary>
    /// For a string marked <see cref="OutputNameAttribute"/>: it names an output of the entity another property names (or of
    /// its own entity), which the Inspector picks from a list of theirs.
    /// </summary>
    public OutputNameAttribute? OutputName { get; }

    /// <summary>The subheading the Inspector shows it under, from the <see cref="GroupAttribute"/> on it or before it; null for none.</summary>
    public string? Group { get; }

    /// <summary>From <see cref="ShownWhenAttribute"/>: the property it's only used with, which fades it in the Inspector while off.</summary>
    public ShownWhenAttribute? ShownWhen { get; }

    /// <summary>The property <see cref="ShownWhen"/> names, or null when there's none (or it names no property of the component).</summary>
    public PropertyDescriptor? Condition { get; internal set; }

    /// <summary>
    /// False while the property <see cref="ShownWhen"/> names is off on <paramref name="component"/> (false, zero or
    /// empty; for an enum, not one of the <see cref="ShownWhenAttribute.Is"/> values, or its first value without them),
    /// so this one isn't used. True otherwise.
    /// </summary>
    public bool IsShown(Component component)
    {
        if (Condition is null)
            return true;
        return Condition.GetValue(component) switch
        {
            Enum choice when ShownWhen!.Is is { Length: > 0 } values => values.Any(v => Equals(v, choice)),
            Enum choice => !Equals(choice, Enum.GetValues(choice.GetType()).GetValue(0)),
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            bool on => on,
            float number => number != 0,
            int number => number != 0,
            double number => number != 0,
            _ => true,
        };
    }

    /// <summary>
    /// What has to be so for it to be used, e.g. "Dash is on", "Action is LoadScene or Quit" or "Target is set", with the
    /// other property's name written by <paramref name="property"/> and an enum's values by <paramref name="value"/> (as
    /// they are in code by default); null without <see cref="ShownWhen"/>.
    /// </summary>
    public string? ShownWhenText(Func<string, string>? property = null, Func<string, string>? value = null)
    {
        if (Condition is not { } condition)
            return null;
        value ??= v => v;
        var other = property?.Invoke(condition.Name) ?? condition.Name;
        return condition.Type.IsEnum && ShownWhen!.Is is { Length: > 0 } values ? $"{other} is {Words(values.Select(v => value(v.ToString()!)).ToList())}"
            : condition.Type.IsEnum ? $"{other} isn't {value(Enum.GetValues(condition.Type).GetValue(0)!.ToString()!)}"
            : condition.Type == typeof(bool) ? $"{other} is on"
            : $"{other} is set";
    }

    // "A", "A or B", "A, B or C".
    private static string Words(List<string> values) =>
        values.Count == 1 ? values[0] : $"{string.Join(", ", values.Take(values.Count - 1))} or {values[^1]}";

    public object? GetValue(Component component) => _property.GetValue(component);

    public void SetValue(Component component, object? value) => _property.SetValue(component, value);
}
