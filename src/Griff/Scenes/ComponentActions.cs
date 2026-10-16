using System.Globalization;
using System.Reflection;

namespace Griff.Scenes;

/// <summary>
/// Marks a method of a component as something it does that anything in the game can set off with no code: a
/// <see cref="Trigger"/> when a message arrives, a wire as its signal comes on (see the Contraptions module's Wire), or a
/// script with <c>Game.Do</c>. Actions are found by this attribute, the way components are found, so a script
/// component's own methods count as soon as they're marked. The method returns nothing and takes at most one value it's
/// done with: a number (<see cref="float"/>), a whole number (<see cref="int"/>), true or false (<see cref="bool"/>) or a
/// name (<see cref="string"/>). Its <c>///</c> summary says what it does, in the Inspector and to agents. A script's
/// action reaches the game through its own <c>Game</c>; one of Griff's components holds only its own state, so an action
/// that needs the game takes the service it needs as its first parameter (an IGameService, such as the panels or the
/// flags), which the system that owns that part of the game provides.
/// </summary>
/// <example>
/// <code>
/// // In a script:
/// [Action] public void Explode() => Game.Destroy(Entity);
/// [Action] public void Charge(float amount) => Energy += amount;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class ActionAttribute(string? name = null) : Attribute
{
    /// <summary>What it's called, e.g. "Start"; the method's own name if not given. Spaces aren't needed ("EndTurn" reads "End Turn").</summary>
    public string? Name { get; } = name;
}

/// <summary>
/// A component whose actions (see <see cref="ActionAttribute"/>) can't always be done: a fighter's spell without the mana,
/// any move off its turn. A <see cref="Button"/> whose Action is Do is faded while none of its targets can do it. A script
/// component can implement it too.
/// </summary>
public interface IActionReady
{
    /// <summary>True if its action called <paramref name="action"/> (the action's name without spaces, e.g. "CastSpell") can be done now.</summary>
    bool IsReady(string action);
}

/// <summary>
/// Marks a component's true-or-false property as what a <see cref="Switch"/> (or a wire into Power) turns on and off,
/// e.g. a mover's Running or a particle emitter's Emitting. With <see cref="Inverted"/>, on sets it false: a door's
/// Locked. An entity with none of these appears and disappears instead. A script component's property counts too.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class PoweredAttribute : Attribute
{
    /// <summary>Powered on sets it false, and off true: a door is unlocked while it's on.</summary>
    public bool Inverted { get; init; }
}

/// <summary>
/// One thing a component does that anything can set off (see <see cref="ActionAttribute"/>): its name, what it's done with,
/// and the call itself, which allocates nothing.
/// </summary>
public sealed class ComponentAction
{
    private readonly Action<Component, IGameServices?, float, string?> _invoke;
    private readonly object? _default;

    internal ComponentAction(Type component, MethodInfo method, string name, Type? valueType, string? valueName, object? fallback, bool takesGame,
        Action<Component, IGameServices?, float, string?> invoke, IReadOnlyList<string> formerNames)
    {
        FormerNames = formerNames;
        ComponentType = component;
        Method = method;
        Name = name;
        ValueType = valueType;
        ValueName = valueName;
        _default = fallback;
        TakesGame = takesGame;
        _invoke = invoke;
    }

    /// <summary>The component type it belongs to.</summary>
    public Type ComponentType { get; }

    /// <summary>The method it calls.</summary>
    public MethodInfo Method { get; }

    /// <summary>What it's called, without spaces, e.g. "EndTurn".</summary>
    public string Name { get; }

    /// <summary>What it's called with spaces between the words, e.g. "End Turn".</summary>
    public string DisplayName => Spaced(Name);

    /// <summary>"Health.Damage": the component's name and the action's, which names it even on an entity where two components have one called the same.</summary>
    public string QualifiedName => $"{ComponentType.Name}.{Name}";

    /// <summary>
    /// The kind of value it's done with: <see cref="float"/>, <see cref="int"/>, <see cref="bool"/> or <see cref="string"/>;
    /// null for one that takes none.
    /// </summary>
    public Type? ValueType { get; }

    /// <summary>What its value is, from the method's parameter, e.g. "amount" or "flag"; null for one that takes none.</summary>
    public string? ValueName { get; }

    /// <summary>
    /// For an action done with a name: what the name names, when its parameter is marked <see cref="NamesAttribute"/> (a
    /// trigger's Set Flag a flag, its Load Scene a scene), so a trigger's or button's With picks from those; null otherwise.
    /// </summary>
    public NamesAttribute? ValueNames => ValueType == typeof(string)
        ? Method.GetParameters().LastOrDefault()?.GetCustomAttribute<NamesAttribute>()
        : null;

    /// <summary>True if it needs a service of the game it runs in (it's set off only while one runs).</summary>
    public bool TakesGame { get; }

    /// <summary>
    /// What it's done with when nothing says: the method's default for the value if it has one, otherwise 1 for a number,
    /// true, or nothing for a name.
    /// </summary>
    public object? DefaultValue => _default;

    /// <summary>"a number", "a whole number", "true or false" or "a name"; null for one that takes no value.</summary>
    public string? ValueKind => ValueType == typeof(float) ? "a number"
        : ValueType == typeof(int) ? "a whole number"
        : ValueType == typeof(bool) ? "true or false"
        : ValueType == typeof(string) ? "a name"
        : null;

    /// <summary>
    /// Does it on <paramref name="component"/>, with <paramref name="value"/> read as its value (a number, true or false, or
    /// a name), or its <see cref="DefaultValue"/> when that's empty or doesn't read as one.
    /// </summary>
    internal void Invoke(Component component, IGameServices? game, string? value)
    {
        if (ValueType is null || ValueType == typeof(string))
        {
            _invoke(component, game, 0, string.IsNullOrWhiteSpace(value) ? _default as string : value.Trim());
            return;
        }

        _invoke(component, game, ReadNumber(value), null);
    }

    /// <summary>Does it on <paramref name="component"/> with <paramref name="value"/> as its value: a wire's signal, a script's number.</summary>
    internal void Invoke(Component component, IGameServices? game, float value) =>
        _invoke(component, game, value, ValueType == typeof(string) ? value.ToString(CultureInfo.InvariantCulture) : null);

    // The number a value says (true is 1, false 0), or the default's.
    private float ReadNumber(string? value)
    {
        var text = value.AsSpan().Trim();
        if (text.Length > 0)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return number;
            if (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("on", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (text.Equals("false", StringComparison.OrdinalIgnoreCase) || text.Equals("off", StringComparison.OrdinalIgnoreCase))
                return 0;
        }

        return _default switch
        {
            float f => f,
            int i => i,
            bool b => b ? 1 : 0,
            _ => 1,
        };
    }

    /// <summary>Names it had before (<see cref="FormerNameAttribute"/>): what still names it.</summary>
    public IReadOnlyList<string> FormerNames { get; }

    /// <summary>
    /// True if <paramref name="name"/> names it: its name or a former one (<see cref="FormerNames"/>), whatever the
    /// capitals and spaces ("end turn" is EndTurn), or with its component's in front ("Health.Damage").
    /// </summary>
    public bool IsCalled(ReadOnlySpan<char> name)
    {
        name = name.Trim();
        var dot = name.LastIndexOf('.');
        if (dot >= 0)
        {
            if (!SameWords(name[..dot], ComponentType.Name))
                return false;
            name = name[(dot + 1)..];
        }

        if (SameWords(name, Name))
            return true;
        for (var i = 0; i < FormerNames.Count; i++)
        {
            if (SameWords(name, FormerNames[i]))
                return true;
        }

        return false;
    }

    /// <summary>True if two names are the same, whatever their capitals and spaces.</summary>
    internal static bool SameWords(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        int i = 0, j = 0;
        while (true)
        {
            while (i < a.Length && char.IsWhiteSpace(a[i]))
                i++;
            while (j < b.Length && char.IsWhiteSpace(b[j]))
                j++;
            if (i == a.Length || j == b.Length)
                return i == a.Length && j == b.Length;
            if (char.ToUpperInvariant(a[i]) != char.ToUpperInvariant(b[j]))
                return false;
            i++;
            j++;
        }
    }

    // "EndTurn" as "End Turn".
    internal static string Spaced(string name)
    {
        var text = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
                text.Append(' ');
            text.Append(name[i]);
        }

        return text.ToString();
    }

    public override string ToString() => QualifiedName;
}

/// <summary>
/// The actions (<see cref="ActionAttribute"/>) and powered properties (<see cref="PoweredAttribute"/>) of component types,
/// found the first time each type is asked about and kept for as long as it's loaded.
/// </summary>
public static class ComponentActions
{
    /// <summary>A property a switch turns on and off, and how to set it.</summary>
    internal sealed record PoweredProperty(PropertyInfo Property, bool Inverted, Action<Component, bool> Set);

    /// <summary>
    /// The actions of <paramref name="type"/> (its base types' too), in the order they're declared, base types first.
    /// </summary>
    /// <exception cref="NotSupportedException">One of its actions can't be called (see <see cref="ActionAttribute"/>), or two have the same name.</exception>
    public static IReadOnlyList<ComponentAction> Of(Type type) => ComponentModel.Of(type).Actions;

    /// <summary>The properties of <paramref name="type"/> a switch turns on and off (see <see cref="PoweredAttribute"/>).</summary>
    internal static IReadOnlyList<PoweredProperty> PoweredOf(Type type) => ComponentModel.Of(type).Powered;

    /// <summary>The action of <paramref name="component"/> called <paramref name="name"/> (see <see cref="ComponentAction.IsCalled"/>), or null.</summary>
    public static ComponentAction? Find(Component component, ReadOnlySpan<char> name)
    {
        var actions = Of(component.GetType());
        for (var i = 0; i < actions.Count; i++)
        {
            if (actions[i].IsCalled(name))
                return actions[i];
        }

        return null;
    }

    /// <summary>
    /// Every action of <paramref name="entities"/>' components, each once, in the order the entities and their components
    /// come: what a <see cref="Trigger"/> on them can do.
    /// </summary>
    public static IReadOnlyList<ComponentAction> On(IEnumerable<Entity> entities) =>
        [.. entities.SelectMany(e => e.Components).SelectMany(c => Of(c.GetType())).Distinct()];

    // Found once per type, by its ComponentModel.
    internal static (IReadOnlyList<ComponentAction> Actions, IReadOnlyList<PoweredProperty> Powered) Discover(Type type)
    {
        var actions = new List<ComponentAction>();
        var powered = new List<PoweredProperty>();
        var chain = new List<Type>();
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
            chain.Insert(0, t);

        foreach (var declaring in chain)
        {
            foreach (var method in declaring.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .OrderBy(m => m.MetadataToken))
            {
                if (method.GetCustomAttribute<ActionAttribute>(inherit: false) is not { } attribute)
                    continue;

                // An override of an action is the same action.
                if (method.GetBaseDefinition() is var root && root != method && actions.Any(a => a.Method.GetBaseDefinition() == root))
                {
                    var at = actions.FindIndex(a => a.Method.GetBaseDefinition() == root);
                    actions[at] = Make(type, method, attribute);
                    continue;
                }

                var action = Make(type, method, attribute);
                if (actions.Any(a => SameName(a.Name, action.Name)))
                    throw new NotSupportedException($"{type.Name} has two actions called {action.Name}. Give one another name with [Action(\"...\")].");
                actions.Add(action);
            }

            foreach (var property in declaring.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.GetCustomAttribute<PoweredAttribute>(inherit: false) is not { } attribute)
                    continue;
                if (property.PropertyType != typeof(bool) || property.SetMethod is null || property.GetIndexParameters().Length > 0)
                    throw new NotSupportedException($"{type.Name}.{property.Name} is marked [Powered], so it should be a true-or-false property that can be set.");
                powered.Add(new PoweredProperty(property, attribute.Inverted, BindSetter(property)));
            }
        }

        // Anything that can have focus can be given it (see FocusAttribute), unless it says for itself what Focus does.
        if (ComponentFocus.Has(type) && !actions.Any(a => SameName(a.Name, FocusName)))
            actions.Add(new ComponentAction(type, FocusMethod, FocusName, null, null, null, takesGame: true, (c, g, _, _) => ComponentFocus.Focus(c, Need<IInputFocus>(g, FocusMethod)), []));

        return (actions, powered);
    }

    private const string FocusName = "Focus";

    private static readonly MethodInfo FocusMethod =
        typeof(ComponentFocus).GetMethod(nameof(ComponentFocus.Focus), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static bool SameName(string a, string b) => ComponentAction.SameWords(a, b);

    private static ComponentAction Make(Type type, MethodInfo method, ActionAttribute attribute)
    {
        var name = (attribute.Name ?? method.Name).Replace(" ", "", StringComparison.Ordinal);
        if (name.Length == 0 || name.Contains('.', StringComparison.Ordinal))
            throw new NotSupportedException($"{type.Name}.{method.Name}'s action needs a name without dots.");
        var parameters = method.GetParameters();
        var service = parameters.Length > 0 && parameters[0].ParameterType.IsInterface && typeof(IGameService).IsAssignableFrom(parameters[0].ParameterType)
            ? parameters[0].ParameterType
            : null;
        var takesGame = service is not null;
        var values = parameters.Skip(takesGame ? 1 : 0).ToList();
        var valueType = values.Count == 1 ? values[0].ParameterType : null;
        var supported = valueType is null || valueType == typeof(float) || valueType == typeof(int) || valueType == typeof(bool) || valueType == typeof(string);
        if (method.ReturnType != typeof(void) || values.Count > 1 || !supported || method.IsGenericMethodDefinition || method.IsStatic ||
            parameters.Any(p => p.ParameterType.IsByRef))
        {
            throw new NotSupportedException(
                $"{type.Name}.{method.Name} is marked [Action], so it should return nothing and take one " +
                "number, whole number, true-or-false or name at most, e.g. void Damage(float amount).");
        }

        var fallback = valueType is null ? null
            : values[0].HasDefaultValue && values[0].DefaultValue is { } given ? given
            : valueType == typeof(float) ? 1f
            : valueType == typeof(int) ? 1
            : valueType == typeof(bool) ? true
            : null;
        return new ComponentAction(type, method, name, valueType, valueType is null ? null : values[0].Name, fallback, takesGame, Bind(method, service, valueType),
            [.. method.GetCustomAttributes<FormerNameAttribute>(inherit: false).Select(a => a.Name.Replace(" ", "", StringComparison.Ordinal))]);
    }

    // A call that allocates nothing: the method bound as an open delegate on its declaring type, with the service it asks
    // for found among the game's (when it asks for one) and the value passed as a number (or a name) and changed to what
    // the method takes.
    private static Action<Component, IGameServices?, float, string?> Bind(MethodInfo method, Type? service, Type? valueType)
    {
        var shape = (service is not null, valueType) switch
        {
            (false, null) => nameof(BindNone),
            (true, null) => nameof(BindGame),
            (false, _) when valueType == typeof(float) => nameof(BindFloat),
            (true, _) when valueType == typeof(float) => nameof(BindGameFloat),
            (false, _) when valueType == typeof(int) => nameof(BindInt),
            (true, _) when valueType == typeof(int) => nameof(BindGameInt),
            (false, _) when valueType == typeof(bool) => nameof(BindBool),
            (true, _) when valueType == typeof(bool) => nameof(BindGameBool),
            (false, _) => nameof(BindString),
            _ => nameof(BindGameString),
        };
        var generic = typeof(ComponentActions).GetMethod(shape, BindingFlags.NonPublic | BindingFlags.Static)!;
        var binder = service is null ? generic.MakeGenericMethod(method.DeclaringType!) : generic.MakeGenericMethod(method.DeclaringType!, service);
        return (Action<Component, IGameServices?, float, string?>)binder.Invoke(null, [method])!;
    }

    // The service an action asks for, from the game running it.
    private static TService Need<TService>(IGameServices? game, MethodInfo method) where TService : class, IGameService =>
        game is null ? throw new InvalidOperationException($"{method.DeclaringType!.Name}.{method.Name} can only be done while the game runs.")
        : game.Get<TService>() ?? throw new InvalidOperationException(
            $"{method.DeclaringType!.Name}.{method.Name} needs the game's {typeof(TService).Name}, which none of its systems provides.");

    private static Action<Component, IGameServices?, float, string?> BindNone<T>(MethodInfo m) where T : class
    {
        var d = m.CreateDelegate<Action<T>>();
        return (c, _, _, _) => d((T)(object)c);
    }

    private static Action<Component, IGameServices?, float, string?> BindGame<T, TService>(MethodInfo m) where T : class where TService : class, IGameService
    {
        var d = m.CreateDelegate<Action<T, TService>>();
        return (c, g, _, _) => d((T)(object)c, Need<TService>(g, m));
    }

    private static Action<Component, IGameServices?, float, string?> BindFloat<T>(MethodInfo m) where T : class
    {
        var d = m.CreateDelegate<Action<T, float>>();
        return (c, _, n, _) => d((T)(object)c, n);
    }

    private static Action<Component, IGameServices?, float, string?> BindGameFloat<T, TService>(MethodInfo m) where T : class where TService : class, IGameService
    {
        var d = m.CreateDelegate<Action<T, TService, float>>();
        return (c, g, n, _) => d((T)(object)c, Need<TService>(g, m), n);
    }

    private static Action<Component, IGameServices?, float, string?> BindInt<T>(MethodInfo m) where T : class
    {
        var d = m.CreateDelegate<Action<T, int>>();
        return (c, _, n, _) => d((T)(object)c, (int)MathF.Round(n));
    }

    private static Action<Component, IGameServices?, float, string?> BindGameInt<T, TService>(MethodInfo m) where T : class where TService : class, IGameService
    {
        var d = m.CreateDelegate<Action<T, TService, int>>();
        return (c, g, n, _) => d((T)(object)c, Need<TService>(g, m), (int)MathF.Round(n));
    }

    private static Action<Component, IGameServices?, float, string?> BindBool<T>(MethodInfo m) where T : class
    {
        var d = m.CreateDelegate<Action<T, bool>>();
        return (c, _, n, _) => d((T)(object)c, n != 0);
    }

    private static Action<Component, IGameServices?, float, string?> BindGameBool<T, TService>(MethodInfo m) where T : class where TService : class, IGameService
    {
        var d = m.CreateDelegate<Action<T, TService, bool>>();
        return (c, g, n, _) => d((T)(object)c, Need<TService>(g, m), n != 0);
    }

    private static Action<Component, IGameServices?, float, string?> BindString<T>(MethodInfo m) where T : class
    {
        var d = m.CreateDelegate<Action<T, string?>>();
        return (c, _, _, s) => d((T)(object)c, s);
    }

    private static Action<Component, IGameServices?, float, string?> BindGameString<T, TService>(MethodInfo m) where T : class where TService : class, IGameService
    {
        var d = m.CreateDelegate<Action<T, TService, string?>>();
        return (c, g, _, s) => d((T)(object)c, Need<TService>(g, m), s);
    }

    private static Action<Component, bool> BindSetter(PropertyInfo property)
    {
        var binder = typeof(ComponentActions).GetMethod(nameof(BindBoolSetter), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(property.DeclaringType!);
        return (Action<Component, bool>)binder.Invoke(null, [property.SetMethod!])!;
    }

    private static Action<Component, bool> BindBoolSetter<T>(MethodInfo setter) where T : class
    {
        var d = setter.CreateDelegate<Action<T, bool>>();
        return (c, on) => d((T)(object)c, on);
    }
}
