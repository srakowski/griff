using System.Reflection;
using System.Runtime.CompilerServices;

namespace Griff.Scenes;

/// <summary>
/// Marks a component's true-or-false property as whether it takes the player's input: a movement's Controllable, a
/// board's, the strategy commander's, the tactics controls'. Without it the component ignores the keys, the mouse and the
/// gamepad, while the rest of it runs on. A component with one has a Focus action (see <see cref="ActionAttribute"/>):
/// done by a <see cref="Trigger"/>, a wire or a script (<c>Game.Focus</c>), it turns the property on for its entity (and
/// whatever is inside it) and off for everything else the same player plays, so a match-3 minigame and a platformer in one
/// scene don't both move on the arrow keys. Found by this attribute, so a script component's own property counts too.
/// </summary>
/// <example>
/// <code>
/// // In a script:
/// [Focus] public bool Controllable { get; set; } = true;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class FocusAttribute : Attribute;

/// <summary>
/// Marks a component that reads the player's input but has no focus of its own (see <see cref="FocusAttribute"/>): it takes
/// the input only while the <see cref="Type"/> it goes with has focus, so the Focus action given to that hands it over too.
/// A first-person weapon fires while its player's movement has the input, a puzzle's move history hears Undo while a grid
/// player it covers has it, and crafting opens while its builder has it. Every genre's components that read input have one
/// or the other, which Griff's own tests hold them to.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class FocusFromAttribute(Type type) : Attribute
{
    /// <summary>The component whose focus it goes by, e.g. a first-person movement's for a weapon.</summary>
    public Type Type { get; } = type;
}

/// <summary>
/// The properties of component types that say whether they take the player's input (see <see cref="FocusAttribute"/>),
/// found the first time each type is asked about and kept for as long as it's loaded. Reading and setting them allocates
/// nothing.
/// </summary>
public static class ComponentFocus
{
    private static readonly ConditionalWeakTable<Type, FocusProperty[]> Cache = new();

    /// <summary>A property that says whether a component takes input, and how to read and set it.</summary>
    internal sealed record FocusProperty(PropertyInfo Property, Func<Component, bool> Get, Action<Component, bool> Set);

    /// <summary>The properties of <paramref name="type"/> (its base types' too) marked <see cref="FocusAttribute"/>.</summary>
    /// <exception cref="NotSupportedException">One isn't a true-or-false property that can be read and set.</exception>
    public static IReadOnlyList<PropertyInfo> PropertiesOf(Type type) => [.. Of(type).Select(p => p.Property)];

    /// <summary>True if components of <paramref name="type"/> can have focus: they have a property marked <see cref="FocusAttribute"/>.</summary>
    public static bool Has(Type type) => Of(type).Length > 0;

    /// <summary>
    /// True if <paramref name="component"/> takes the player's input: every property of it marked
    /// <see cref="FocusAttribute"/> is on (or it has none).
    /// </summary>
    public static bool Takes(Component component)
    {
        foreach (var property in Of(component.GetType()))
        {
            if (!property.Get(component))
                return false;
        }

        return true;
    }

    /// <summary>Turns every property of <paramref name="component"/> marked <see cref="FocusAttribute"/> on or off.</summary>
    internal static void Set(Component component, bool on)
    {
        foreach (var property in Of(component.GetType()))
            property.Set(component, on);
    }

    /// <summary>
    /// Gives its entity the player's input: whatever there and inside it has focus, and everything else the same player
    /// plays loses it (a platformer's player stops on the arrow keys while a board takes them).
    /// </summary>
    internal static void Focus(Component component, IGameContext? game)
    {
        if (component.Entity is { } entity)
            (game ?? throw new InvalidOperationException("Focus can only be done while the game runs.")).Focus(entity);
    }

    internal static FocusProperty[] Of(Type type) => Cache.GetValue(type, Discover);

    private static FocusProperty[] Discover(Type type)
    {
        var found = new List<FocusProperty>();
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            foreach (var property in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!property.IsDefined(typeof(FocusAttribute), inherit: false))
                    continue;
                if (property.PropertyType != typeof(bool) || property.GetMethod is null || property.SetMethod is null || property.GetIndexParameters().Length > 0)
                    throw new NotSupportedException($"{type.Name}.{property.Name} is marked [Focus], so it should be a true-or-false property that can be read and set.");
                found.Insert(0, new FocusProperty(property, BindGetter(property), BindSetter(property)));
            }
        }

        return [.. found];
    }

    private static Func<Component, bool> BindGetter(PropertyInfo property) =>
        (Func<Component, bool>)typeof(ComponentFocus).GetMethod(nameof(Getter), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(property.DeclaringType!).Invoke(null, [property.GetMethod!])!;

    private static Action<Component, bool> BindSetter(PropertyInfo property) =>
        (Action<Component, bool>)typeof(ComponentFocus).GetMethod(nameof(Setter), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(property.DeclaringType!).Invoke(null, [property.SetMethod!])!;

    private static Func<Component, bool> Getter<T>(MethodInfo getter) where T : class
    {
        var d = getter.CreateDelegate<Func<T, bool>>();
        return c => d((T)(object)c);
    }

    private static Action<Component, bool> Setter<T>(MethodInfo setter) where T : class
    {
        var d = setter.CreateDelegate<Action<T, bool>>();
        return (c, on) => d((T)(object)c, on);
    }
}
