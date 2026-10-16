using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Griff.Scenes.Serialization;

namespace Griff.Scenes;

/// <summary>
/// What a component type exposes, worked out once per type: what's saved (<see cref="Saved"/>), what anything can set off
/// and read (<see cref="Actions"/>, <see cref="Outputs"/>), whether it takes input (<see cref="Focus"/>), the part of
/// the world it covers (<see cref="Area"/>), what it sends in a network game (<see cref="Sent"/>), what tables and cards
/// may set (<see cref="Settable"/>) and what agents read and set (<see cref="Live"/>, <see cref="Saved"/>). The
/// serializer, the Inspector, triggers and wires, the network, tables, cards and the agent tools all ask this, so they
/// can't disagree about a component. Held only while its type is: a script compiled again leaves its old types to unload.
/// </summary>
public sealed class ComponentModel
{
    private static readonly ConditionalWeakTable<Type, ComponentModel> Models = new();

    /// <summary>The model of <paramref name="type"/>, worked out the first time it's asked for.</summary>
    public static ComponentModel Of(Type type) => Models.GetValue(type, static t => new ComponentModel(t));

    /// <summary>The model of <paramref name="component"/>'s type.</summary>
    public static ComponentModel Of(Component component) => Of(component.GetType());

    // Each part is found the first time it's asked for, and found again if finding it failed (a part's mistake, such as an
    // action that can't be called, is told to whoever asks for that part, as it always has been).
    private readonly Lazy<IReadOnlyList<PropertyDescriptor>> _saved;
    private readonly Lazy<IReadOnlyList<Action<SavedData>>> _upgrades;
    private readonly Lazy<(IReadOnlyList<ComponentAction> Actions, IReadOnlyList<ComponentActions.PoweredProperty> Powered)> _actions;
    private readonly Lazy<IReadOnlyList<ComponentOutput>> _outputs;
    private readonly Lazy<ComponentFocus.FocusProperty[]> _focus;
    private readonly Lazy<StrongBox<Func<Component, Box>?>> _area;
    private readonly Lazy<IReadOnlyList<SentProperty>> _sent;
    private readonly Lazy<IReadOnlyList<PropertyDescriptor>> _settable;
    private readonly Lazy<IReadOnlyDictionary<string, PropertyInfo>> _rolls;
    private readonly Lazy<IReadOnlyList<PropertyInfo>> _live;

    private ComponentModel(Type type)
    {
        Type = type;
        Name = type.GetCustomAttribute<ComponentNameAttribute>()?.Name ?? type.FullName!;
        FormerNames = type.GetCustomAttributes<FormerNameAttribute>(inherit: false)
            .SelectMany(a => a.Name.Contains('.') || type.Namespace is null ? [a.Name] : new[] { a.Name, $"{type.Namespace}.{a.Name}" })
            .Where(n => n != Name)
            .Distinct()
            .ToList();
        Version = type.GetCustomAttribute<ComponentVersionAttribute>()?.Version ?? 1;
        Category = type.GetCustomAttribute<CategoryAttribute>()?.Name;
        Requires = type.GetCustomAttributes<RequiresAttribute>().Select(a => new ComponentLink(a.Type, a.Note) { Unless = a.Unless }).ToList();
        WorksWith = type.GetCustomAttributes<WorksWithAttribute>().Select(a => new ComponentLink(a.Type, a.Note)).ToList();
        NetLocal = type.IsDefined(typeof(NetLocalAttribute), true);
        PlayerMovement = type.IsDefined(typeof(PlayerMovementAttribute), true);

        const LazyThreadSafetyMode Once = LazyThreadSafetyMode.PublicationOnly;
        _saved = new(() => ComponentDescriptor.DiscoverProperties(type), Once);
        _upgrades = new(() => ComponentDescriptor.DiscoverUpgrades(type, Version), Once);
        _actions = new(() => ComponentActions.Discover(type), Once);
        _outputs = new(() => ComponentOutputs.Discover(type), Once);
        _focus = new(() => ComponentFocus.Discover(type), Once);
        _area = new(() => new(ComponentAreas.Discover(type)), Once);
        _sent = new(FindSent, Once);
        _settable = new(() => [.. Saved.Where(p => Takes(p.Type))], Once);
        _rolls = new(FindRolls, Once);
        _live = new(FindLive, Once);
    }

    public Type Type { get; }

    /// <summary>The name written to scene files: its <see cref="ComponentNameAttribute"/>, or its full name.</summary>
    public string Name { get; }

    /// <summary>Names it was saved under before (see <see cref="ComponentDescriptor.FormerNames"/>).</summary>
    public IReadOnlyList<string> FormerNames { get; }

    /// <summary>The version of its saved data, from <see cref="ComponentVersionAttribute"/>; 1 without one.</summary>
    public int Version { get; }

    /// <summary>Where the editor's catalog lists it, from <see cref="CategoryAttribute"/>; null if it doesn't say.</summary>
    public string? Category { get; }

    /// <summary>Components it needs on the same entity, from <see cref="RequiresAttribute"/>.</summary>
    public IReadOnlyList<ComponentLink> Requires { get; }

    /// <summary>Components it says it goes well with, from its own <see cref="WorksWithAttribute"/>s.</summary>
    public IReadOnlyList<ComponentLink> WorksWith { get; }

    /// <summary>
    /// What's saved: its public properties that can be read and set, but those marked <see cref="DoNotSerializeAttribute"/>, in
    /// declaration order (base class first). The Inspector shows these, agents set them and tables and cards set some.
    /// </summary>
    /// <exception cref="NotSupportedException">One has a type that can't be saved, or is marked for what it isn't.</exception>
    public IReadOnlyList<PropertyDescriptor> Saved => _saved.Value;

    /// <summary>The saved property called <paramref name="name"/> (as in code), or null.</summary>
    public PropertyDescriptor? SavedProperty(string name)
    {
        var saved = Saved;
        for (var i = 0; i < saved.Count; i++)
        {
            if (saved[i].Name == name)
                return saved[i];
        }

        return null;
    }

    /// <summary>
    /// The property called <paramref name="name"/> that can be read of it: a saved one (<see cref="Saved"/>), else one it
    /// says while the game runs (<see cref="Live"/>); null for neither. What an attribute names by name (a
    /// <see cref="RequiresAttribute.Unless"/>, an <see cref="OutputNameAttribute.On"/>) is read through this.
    /// </summary>
    public PropertyInfo? Property(string name, StringComparison comparison = StringComparison.Ordinal)
    {
        foreach (var property in Saved)
        {
            if (string.Equals(property.Name, name, comparison))
                return property.Info;
        }

        foreach (var property in Live)
        {
            if (string.Equals(property.Name, name, comparison))
                return property;
        }

        return null;
    }

    /// <summary>
    /// Its method called <paramref name="name"/> taking <paramref name="parameters"/>, public or not, static or not: what a field
    /// editor names by name (a text's preview, a picture's cells). Null for none.
    /// </summary>
    public MethodInfo? Method(string name, params Type[] parameters) =>
        Type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, parameters);

    /// <summary>
    /// The size the Scene view's handles resize: its first saved <see cref="Vector2"/> marked <see cref="ResizableAttribute"/>
    /// (a button's clickable area), or null for none.
    /// </summary>
    public (PropertyInfo Property, ResizableAttribute Area)? Resizable =>
        Saved.FirstOrDefault(p => p.Resizable is not null) is { } sized ? (sized.Info, sized.Resizable!) : null;

    // The step up from version n is at n - 1.
    internal IReadOnlyList<Action<SavedData>> Upgrades => _upgrades.Value;

    /// <summary>What it does that anything can set off (see <see cref="ActionAttribute"/>), base types' first.</summary>
    /// <exception cref="NotSupportedException">One of its actions can't be called, or two have the same name.</exception>
    public IReadOnlyList<ComponentAction> Actions => _actions.Value.Actions;

    // What a switch turns on and off (see PoweredAttribute).
    internal IReadOnlyList<ComponentActions.PoweredProperty> Powered => _actions.Value.Powered;

    /// <summary>What it knows that anything can read (see <see cref="OutputAttribute"/>), base types' first.</summary>
    /// <exception cref="NotSupportedException">One of its outputs can't be read, or two have the same name.</exception>
    public IReadOnlyList<ComponentOutput> Outputs => _outputs.Value;

    /// <summary>Its properties marked <see cref="FocusAttribute"/>: what says whether it takes the player's input.</summary>
    public IReadOnlyList<PropertyInfo> Focus => [.. FocusProperties.Select(p => p.Property)];

    internal ComponentFocus.FocusProperty[] FocusProperties => _focus.Value;

    /// <summary>True if it covers a part of the world (a property marked <see cref="AreaAttribute"/>).</summary>
    public bool HasArea => Area is not null;

    // Reads the part of the world it covers; null for none.
    internal Func<Component, Box>? Area => _area.Value.Value;

    /// <summary>True if it's marked <see cref="NetLocalAttribute"/>: each machine keeps its own, and nothing of its entity is sent.</summary>
    public bool NetLocal { get; }

    /// <summary>True if it's marked <see cref="PlayerMovementAttribute"/>: a player's own machine moves it.</summary>
    public bool PlayerMovement { get; }

    /// <summary>
    /// What a network game sends of it: its saved properties but those marked <see cref="NotSentAttribute"/>, then the
    /// others marked <see cref="SentAttribute"/>. A component that's <see cref="INetState"/> may skip some and add its own.
    /// </summary>
    public IReadOnlyList<SentProperty> Sent => _sent.Value;

    /// <summary>
    /// What a table's row (a <see cref="TableValues"/>) and a card's effect may set: its saved properties that take a value
    /// from a table, a number, whole number, true or false, name, choice or color.
    /// </summary>
    public IReadOnlyList<PropertyDescriptor> Settable => _settable.Value;

    /// <summary>The name property marked <see cref="RollsForAttribute"/> <paramref name="property"/>, or null.</summary>
    public PropertyInfo? RollsFor(string property) => _rolls.Value.GetValueOrDefault(property);

    /// <summary>
    /// What it says about itself while the game runs and doesn't save, which agents read but don't set: its other public values
    /// (Spawned, IsCollected, a beat clock's Beat, one marked <see cref="DoNotSerializeAttribute"/>) as simple values. Lists and
    /// the like are left out.
    /// </summary>
    public IReadOnlyList<PropertyInfo> Live => _live.Value;

    /// <summary>True if a table's cell can set a property of <paramref name="type"/> (see <see cref="Settable"/>).</summary>
    public static bool Takes(Type type) =>
        type == typeof(float) || type == typeof(double) || type == typeof(int) || type == typeof(bool) || type == typeof(string) || type.IsEnum || type == typeof(Color);

    // The one rule for what's open to set (see DoNotSerializeAttribute.Writable).
    internal static IEnumerable<PropertyInfo> Writable(Type type) => DoNotSerializeAttribute.Writable(type);

    private List<SentProperty> FindSent()
    {
        var sent = new List<SentProperty>();
        foreach (var property in Saved)
        {
            var info = property.Info;
            if (info.IsDefined(typeof(NotSentAttribute), true))
                continue;
            // Who has the player's input (see FocusAttribute) is the host's to say, even on a player's own character.
            sent.Add(new SentProperty(info, info.GetCustomAttribute<SentAttribute>(true) is { ByMover: true }, Saved: true,
                Focus: info.IsDefined(typeof(FocusAttribute), true)));
        }

        // What it keeps while playing that others need to see: its properties marked [Sent], public or not.
        foreach (var (info, mark) in Type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                     .Select(p => (Property: p, Sent: p.GetCustomAttribute<SentAttribute>()))
                     .Where(x => x.Sent is not null)
                     .OrderBy(x => x.Property.MetadataToken))
        {
            if (SavedProperty(info.Name) is null)
                sent.Add(new SentProperty(info, mark!.ByMover, Saved: false, Focus: false));
        }

        return sent;
    }

    private Dictionary<string, PropertyInfo> FindRolls()
    {
        var rolls = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
        foreach (var property in Saved)
        {
            if (property.Type == typeof(string) && property.Info.GetCustomAttribute<RollsForAttribute>() is { } mark)
                rolls.TryAdd(mark.Property, property.Info);
        }

        return rolls;
    }

    private List<PropertyInfo> FindLive()
    {
        var saved = Saved.Select(p => p.Name).ToHashSet();
        var live = new List<PropertyInfo>();
        foreach (var property in Type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (saved.Contains(property.Name) || property.DeclaringType == typeof(Component) || property.GetMethod is not { IsPublic: true } ||
                property.GetIndexParameters().Length > 0)
                continue;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (ValueCodec.IsSupported(type) || IsNumber(type) || IsTuple(type) || typeof(Entity).IsAssignableFrom(type) || typeof(Component).IsAssignableFrom(type))
                live.Add(property);
        }

        return live;
    }

    private static bool IsNumber(Type type) =>
        type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(long) || type == typeof(uint)
        || type == typeof(short) || type == typeof(byte) || type == typeof(decimal) || type == typeof(ulong) || type == typeof(ushort);

    private static bool IsTuple(Type type) =>
        type.IsValueType && type.IsGenericType && typeof(ITuple).IsAssignableFrom(type) && type.GetGenericArguments().All(ValueCodec.IsSupported);
}

/// <summary>
/// A property a network game sends (see <see cref="ComponentModel.Sent"/>): whether the player who moves it decides it
/// (<see cref="SentAttribute.ByMover"/>), whether it's saved, and whether it says who has the player's input, which the
/// host decides even on a player's own character.
/// </summary>
public sealed record SentProperty(PropertyInfo Property, bool ByMover, bool Saved, bool Focus);
