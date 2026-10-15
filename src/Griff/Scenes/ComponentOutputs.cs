using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Griff.Scenes;

/// <summary>
/// Marks a property of a component as something it knows that anything in the game can read with no code: a
/// <see cref="Counter"/>, a <see cref="Bar"/> or a <see cref="Text"/> shows it, and a wire (see the Contraptions module's
/// Wire) carries it. Outputs are found by this attribute, the way components are found, so a script component's own
/// properties count as soon as they're marked. The property gives a number (<see cref="float"/>), a whole number
/// (<see cref="int"/>), true or false (<see cref="bool"/>) or a name (<see cref="string"/>, which only text shows), and
/// reading it changes nothing and allocates nothing. Its <c>///</c> summary says what it is, in the Inspector and to agents.
/// </summary>
/// <remarks>
/// A method that takes one name and gives one of those is an output read by a name: a faction's amount of "Gold" or an
/// inventory's count of "Key", named "Amount:Gold" or "Count:Key" where it's read.
/// </remarks>
/// <example>
/// <code>
/// // In a script:
/// [Output] public int Lives { get; set; } = 3;
/// [Output(OutOf = nameof(MaxFuel))] public float Fuel { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, Inherited = true)]
public sealed class OutputAttribute(string? name = null) : Attribute
{
    /// <summary>What it's called, e.g. "Score"; the property's own name if not given ("MovesLeft" reads "Moves Left").</summary>
    public string? Name { get; } = name;

    /// <summary>
    /// The property of the same component (an output or not) it's out of, e.g. a board's Score out of its Target Score: a
    /// bar is full at it, and a counter or text shows it as <c>{1}</c> (a counter's <c>{2}</c>). Nothing (or 0) is out of
    /// nothing, and a bar keeps its own Max.
    /// </summary>
    public string? OutOf { get; init; }

    /// <summary>For an output read by a name (a method that takes one): a name it's read by, e.g. "Gold", for the Inspector's list and agents.</summary>
    public string? Example { get; init; }
}

/// <summary>
/// Marks a string property as naming an output (see <see cref="OutputAttribute"/>) of the entity another of the component's
/// properties names, or of its own entity (or what it's inside) without one: the editor's Inspector lists their outputs to
/// pick from, and flags a name none of them has.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OutputNameAttribute : Attribute
{
    /// <summary>The property naming the entity whose outputs it names (marked <see cref="EntityNameAttribute"/>); empty for its own entity's.</summary>
    public string? On { get; init; }

    /// <summary>It shows outputs that give a name too (a text), not just numbers and true or false (a counter, bar or wire).</summary>
    public bool TakesNames { get; init; }
}

/// <summary>
/// One thing a component knows that anything can read (see <see cref="OutputAttribute"/>): its name, what it gives, and the
/// read itself, which allocates nothing.
/// </summary>
public sealed class ComponentOutput
{
    private readonly Func<Component, string?, float> _number;
    private readonly Func<Component, string?, string?>? _text;
    private readonly Func<Component, float>? _outOf;

    internal ComponentOutput(Type component, MemberInfo member, string name, Type valueType, string? keyName, string? example,
        Func<Component, string?, float> number, Func<Component, string?, string?>? text, Func<Component, float>? outOf, string? outOfName)
    {
        ComponentType = component;
        Member = member;
        Name = name;
        ValueType = valueType;
        KeyName = keyName;
        Example = example;
        _number = number;
        _text = text;
        _outOf = outOf;
        OutOfName = outOfName;
    }

    /// <summary>The component type it belongs to.</summary>
    public Type ComponentType { get; }

    /// <summary>The property (or, for one read by a name, the method) it reads.</summary>
    public MemberInfo Member { get; }

    /// <summary>What it's called, without spaces, e.g. "MovesLeft".</summary>
    public string Name { get; }

    /// <summary>What it's called with spaces between the words, e.g. "Moves Left".</summary>
    public string DisplayName => ComponentAction.Spaced(Name);

    /// <summary>"MatchBoard.Score": the component's name and the output's, which names it even on an entity where two components have one called the same.</summary>
    public string QualifiedName => $"{ComponentType.Name}.{Name}";

    /// <summary>What it gives: <see cref="float"/>, <see cref="int"/>, <see cref="bool"/> or <see cref="string"/>.</summary>
    public Type ValueType { get; }

    /// <summary>True for one that gives a number, a whole number or true or false (1 or 0): what a counter, a bar and a wire can read.</summary>
    public bool IsNumber => ValueType != typeof(string);

    /// <summary>"a number", "a whole number", "true or false" or "a name".</summary>
    public string ValueKind => ValueType == typeof(float) ? "a number"
        : ValueType == typeof(int) ? "a whole number"
        : ValueType == typeof(bool) ? "true or false"
        : "a name";

    /// <summary>For one read by a name, what the name is, from the method's parameter, e.g. "kind"; null for one that isn't.</summary>
    public string? KeyName { get; }

    /// <summary>For one read by a name, a name it's read by, e.g. "Gold" (see <see cref="OutputAttribute.Example"/>).</summary>
    public string? Example { get; }

    /// <summary>True for one read by a name: "Amount:Gold".</summary>
    public bool IsKeyed => KeyName is not null;

    /// <summary>The property it's out of (see <see cref="OutputAttribute.OutOf"/>), e.g. "TargetScore"; null for none.</summary>
    public string? OutOfName { get; }

    /// <summary>
    /// How it's written where it's read: its name, or for one read by a name, its name and an example, e.g. "Amount:Gold".
    /// </summary>
    public string Reference => IsKeyed ? $"{Name}:{Example ?? KeyName}" : Name;

    /// <summary>What it gives on <paramref name="component"/> as a number: true is 1 and false 0, and a name 0. <paramref name="key"/> is the name for one read by a name.</summary>
    public float ReadNumber(Component component, string? key = null) => _number(component, key);

    /// <summary>What it gives on <paramref name="component"/> for one that gives a name; null for one that gives a number.</summary>
    public string? ReadText(Component component, string? key = null) => _text?.Invoke(component, key);

    /// <summary>What it's out of on <paramref name="component"/> (see <see cref="OutputAttribute.OutOf"/>); 0 for nothing.</summary>
    public float ReadOutOf(Component component) => _outOf?.Invoke(component) ?? 0;

    /// <summary>
    /// True if <paramref name="name"/> names it: its name, whatever the capitals and spaces ("moves left" is MovesLeft), or
    /// with its component's in front ("MatchBoard.Score"). The name it's read by (after a colon) isn't part of it.
    /// </summary>
    public bool IsCalled(ReadOnlySpan<char> name)
    {
        name = name.Trim();
        var dot = name.LastIndexOf('.');
        if (dot >= 0)
        {
            if (!ComponentAction.SameWords(name[..dot], ComponentType.Name))
                return false;
            name = name[(dot + 1)..];
        }

        return ComponentAction.SameWords(name, Name);
    }

    public override string ToString() => QualifiedName;
}

/// <summary>
/// The outputs (<see cref="OutputAttribute"/>) of component types, found the first time each type is asked about and kept
/// for as long as it's loaded, and reading one by name.
/// </summary>
public static class ComponentOutputs
{
    private static readonly ConditionalWeakTable<Type, IReadOnlyList<ComponentOutput>> Cache = new();

    /// <summary>The outputs of <paramref name="type"/> (its base types' too), in the order they're declared, base types first.</summary>
    /// <exception cref="NotSupportedException">One of its outputs can't be read (see <see cref="OutputAttribute"/>), or two have the same name.</exception>
    public static IReadOnlyList<ComponentOutput> Of(Type type) => Cache.GetValue(type, Discover);

    /// <summary>
    /// The output of <paramref name="component"/> that <paramref name="reference"/> names (see <see cref="ComponentOutput.IsCalled"/>;
    /// anything after a colon is the name it's read by), or null.
    /// </summary>
    public static ComponentOutput? Find(Component component, ReadOnlySpan<char> reference)
    {
        var name = NameOf(reference);
        var outputs = Of(component.GetType());
        for (var i = 0; i < outputs.Count; i++)
        {
            if (outputs[i].IsCalled(name))
                return outputs[i];
        }

        return null;
    }

    /// <summary>
    /// The first component of <paramref name="entity"/> with an output <paramref name="reference"/> names, and that output;
    /// null when none has one.
    /// </summary>
    public static (Component Component, ComponentOutput Output)? Find(Entity entity, ReadOnlySpan<char> reference)
    {
        var components = entity.ComponentList;
        for (var i = 0; i < components.Count; i++)
        {
            if (Find(components[i], reference) is { } output)
                return (components[i], output);
        }

        return null;
    }

    /// <summary>
    /// Every output of <paramref name="entities"/>' components, each once, in the order the entities and their components
    /// come: what a counter, bar, text or wire reading them can pick.
    /// </summary>
    public static IReadOnlyList<ComponentOutput> On(IEnumerable<Entity> entities) =>
        [.. entities.SelectMany(e => e.Components).SelectMany(c => Of(c.GetType())).Distinct()];

    /// <summary>
    /// What <paramref name="entity"/>'s output <paramref name="reference"/> (e.g. "Score", "MatchBoard.Score" or
    /// "Amount:Gold") gives right now, as a number; 0 if it has no such output.
    /// </summary>
    public static float Read(Entity entity, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || Find(entity, reference) is not { } found)
            return 0;
        return found.Output.ReadNumber(found.Component, found.Output.IsKeyed ? KeyOf(reference) : null);
    }

    /// <summary>The output's name in a reference: "Amount" of "Amount:Gold".</summary>
    public static ReadOnlySpan<char> NameOf(ReadOnlySpan<char> reference)
    {
        var colon = reference.IndexOf(':');
        return (colon < 0 ? reference : reference[..colon]).Trim();
    }

    /// <summary>The name an output is read by in a reference: "Gold" of "Amount:Gold"; empty without one.</summary>
    public static string KeyOf(string reference)
    {
        var colon = reference.IndexOf(':');
        return colon < 0 ? "" : reference.AsSpan(colon + 1).Trim().ToString();
    }

    private static IReadOnlyList<ComponentOutput> Discover(Type type)
    {
        var outputs = new List<ComponentOutput>();
        var chain = new List<Type>();
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
            chain.Insert(0, t);

        foreach (var declaring in chain)
        {
            // Properties by their getters, so they and methods come in the order they're declared.
            const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            var members = declaring.GetProperties(Members).Cast<MemberInfo>()
                .Concat(declaring.GetMethods(Members).Where(m => !m.IsSpecialName))
                .OrderBy(m => (m as PropertyInfo)?.GetMethod?.MetadataToken ?? m.MetadataToken);
            foreach (var member in members)
            {
                if (member.GetCustomAttribute<OutputAttribute>(inherit: false) is not { } attribute)
                    continue;
                var output = Make(type, member, attribute);
                var root = Getter(member).GetBaseDefinition();

                // An override of an output is the same output.
                var at = outputs.FindIndex(o => Getter(o.Member).GetBaseDefinition() == root);
                if (at >= 0)
                {
                    outputs[at] = output;
                    continue;
                }

                if (outputs.Any(o => ComponentAction.SameWords(o.Name, output.Name)))
                    throw new NotSupportedException($"{type.Name} has two outputs called {output.Name}. Give one another name with [Output(\"...\")].");
                outputs.Add(output);
            }
        }

        return outputs;
    }

    private static MethodInfo Getter(MemberInfo member) => member as MethodInfo ?? ((PropertyInfo)member).GetMethod!;

    private static ComponentOutput Make(Type type, MemberInfo member, OutputAttribute attribute)
    {
        var name = (attribute.Name ?? member.Name).Replace(" ", "", StringComparison.Ordinal);
        if (name.Length == 0 || name.Contains('.', StringComparison.Ordinal) || name.Contains(':', StringComparison.Ordinal))
            throw new NotSupportedException($"{type.Name}.{member.Name}'s output needs a name without dots or colons.");

        MethodInfo getter;
        string? keyName = null;
        if (member is PropertyInfo property)
        {
            if (property.GetMethod is null || property.GetIndexParameters().Length > 0)
                throw new NotSupportedException($"{type.Name}.{property.Name} is marked [Output], so it should be a property that can be read.");
            getter = property.GetMethod;
        }
        else
        {
            getter = (MethodInfo)member;
            var parameters = getter.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string) || getter.IsGenericMethodDefinition)
                throw new NotSupportedException($"{type.Name}.{getter.Name} is marked [Output], so it should take one name, e.g. int Amount(string kind).");
            keyName = parameters[0].Name;
        }

        var valueType = getter.ReturnType;
        if (getter.IsStatic || !(valueType == typeof(float) || valueType == typeof(int) || valueType == typeof(bool) || valueType == typeof(string)))
        {
            throw new NotSupportedException(
                $"{type.Name}.{member.Name} is marked [Output], so it should give a number, whole number, true-or-false or name, e.g. public int Score {{ get; }}.");
        }

        var (number, text) = Bind(getter, keyName is not null, valueType);
        Func<Component, float>? outOf = null;
        if (!string.IsNullOrWhiteSpace(attribute.OutOf))
        {
            var of = type.GetProperty(attribute.OutOf, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (of?.GetMethod is not { } ofGetter || of.GetIndexParameters().Length > 0 ||
                !(of.PropertyType == typeof(float) || of.PropertyType == typeof(int)))
                throw new NotSupportedException($"{type.Name}.{member.Name}'s output is out of {attribute.OutOf}, which should be a number property of {type.Name}.");
            var read = Bind(ofGetter, keyed: false, of.PropertyType).Number;
            outOf = c => read(c, null);
        }

        return new ComponentOutput(type, member, name, valueType, keyName, attribute.Example, number, text, outOf, attribute.OutOf);
    }

    // Reads that allocate nothing: the getter bound as an open delegate on its declaring type, its value given as a number
    // (and, for a name, as itself).
    private static (Func<Component, string?, float> Number, Func<Component, string?, string?>? Text) Bind(MethodInfo getter, bool keyed, Type valueType)
    {
        var declaring = getter.DeclaringType!;
        if (valueType == typeof(string))
        {
            var text = Make<string?>(nameof(BindText));
            return ((c, k) => float.TryParse(text(c, k), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0, text);
        }

        return (valueType == typeof(float) ? Make<float>(nameof(BindFloat))
            : valueType == typeof(int) ? Make<float>(nameof(BindInt))
            : Make<float>(nameof(BindBool)), null);

        Func<Component, string?, T> Make<T>(string binder) =>
            (Func<Component, string?, T>)typeof(ComponentOutputs).GetMethod(binder, BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(declaring).Invoke(null, [getter, keyed])!;
    }

    private static Func<Component, string?, float> BindFloat<T>(MethodInfo m, bool keyed) where T : class
    {
        if (keyed)
        {
            var byName = m.CreateDelegate<Func<T, string, float>>();
            return (c, k) => byName((T)(object)c, k ?? "");
        }

        var d = m.CreateDelegate<Func<T, float>>();
        return (c, _) => d((T)(object)c);
    }

    private static Func<Component, string?, float> BindInt<T>(MethodInfo m, bool keyed) where T : class
    {
        if (keyed)
        {
            var byName = m.CreateDelegate<Func<T, string, int>>();
            return (c, k) => byName((T)(object)c, k ?? "");
        }

        var d = m.CreateDelegate<Func<T, int>>();
        return (c, _) => d((T)(object)c);
    }

    private static Func<Component, string?, float> BindBool<T>(MethodInfo m, bool keyed) where T : class
    {
        if (keyed)
        {
            var byName = m.CreateDelegate<Func<T, string, bool>>();
            return (c, k) => byName((T)(object)c, k ?? "") ? 1 : 0;
        }

        var d = m.CreateDelegate<Func<T, bool>>();
        return (c, _) => d((T)(object)c) ? 1 : 0;
    }

    private static Func<Component, string?, string?> BindText<T>(MethodInfo m, bool keyed) where T : class
    {
        if (keyed)
        {
            var byName = m.CreateDelegate<Func<T, string, string?>>();
            return (c, k) => byName((T)(object)c, k ?? "");
        }

        var d = m.CreateDelegate<Func<T, string?>>();
        return (c, _) => d((T)(object)c);
    }
}
