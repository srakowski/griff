using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>
/// On a field of a script (a <see cref="Behavior"/> or a <see cref="GameSystem"/>) or of an object one keeps: a saved game
/// leaves it out, so after a load it holds what the script's <c>Start</c> (or its class) made it: a cache worked out again
/// from the rest, a list kept only to reuse, something set up again in <c>OnLoaded</c>. A rewind still keeps it
/// (<see cref="NotStateAttribute"/> leaves a field out of both). On an auto property, write <c>[field: NotSaved]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class NotSavedAttribute : Attribute
{
}

/// <summary>
/// A script (a <see cref="Behavior"/> or a <see cref="GameSystem"/>) that writes its own model into a saved game rather
/// than having its fields saved for it: <see cref="SaveState"/> writes what it wants kept, by name, and
/// <see cref="LoadState"/> reads it back as the game is loaded (after the script's <c>Start</c>, before its
/// <c>OnLoaded</c>). Values are anything a script's field can hold in a saved game: numbers, text, enums, vectors, plain
/// objects and records, lists, arrays and dictionaries of them, and entities and components (kept as which they are).
/// None of its fields are saved, so it saves all it needs.
/// </summary>
/// <example><code>
/// public class Goals : Behavior, ISavesState
/// {
///     private readonly List&lt;Goal&gt; _ladder = Goal.Ladder();
///     private int _reached;
///
///     public void SaveState(SaveWriter save) => save.Set("Reached", _reached);
///
///     public void LoadState(SaveReader save) => _reached = save.Get("Reached", 0);
/// }
/// </code></example>
public interface ISavesState
{
    /// <summary>Writes what the script keeps in a saved game, as the game is saved.</summary>
    void SaveState(SaveWriter save);

    /// <summary>
    /// Reads back what <see cref="SaveState"/> wrote, as the game is loaded. A game saved by an earlier build may not have
    /// a value, or have one of another kind: <see cref="SaveReader.Get{T}"/> gives the fallback then.
    /// </summary>
    void LoadState(SaveReader save);
}

/// <summary>What a script that saves its own model (<see cref="ISavesState"/>) writes into a saved game.</summary>
public sealed class SaveWriter
{
    private readonly ModelWriter _model;
    private readonly JsonObject _into;
    private readonly string _label;

    internal SaveWriter(ModelWriter model, JsonObject into, string label) => (_model, _into, _label) = (model, into, label);

    /// <summary>
    /// Saves <paramref name="value"/> as <paramref name="name"/>, replacing what was there. Something that can't be saved
    /// (a delegate, a coroutine) is left out, and said once.
    /// </summary>
    public void Set<T>(string name, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_model.Write(value, typeof(T), $"{_label}.{name}", out var node))
            _into[SavedData.Key(name)] = node;
    }
}

/// <summary>What a script that saves its own model (<see cref="ISavesState"/>) reads back from a saved game.</summary>
public sealed class SaveReader
{
    private readonly ModelReader _model;
    private readonly JsonObject _from;
    private readonly string _label;

    internal SaveReader(ModelReader model, JsonObject from, string label) => (_model, _from, _label) = (model, from, label);

    /// <summary>True if a value was saved as <paramref name="name"/>.</summary>
    public bool Has(string name) => _from.ContainsKey(SavedData.Key(name));

    /// <summary>
    /// The value saved as <paramref name="name"/>, or <paramref name="fallback"/> if there's none or it isn't a
    /// <typeparamref name="T"/> (said once).
    /// </summary>
    public T? Get<T>(string name, T? fallback = default)
    {
        if (!_from.TryGetPropertyValue(SavedData.Key(name), out var node) || !_model.TryRead(node, typeof(T), fallback, $"{_label}.{name}", out var value))
            return fallback;
        return value is null ? default : (T)value;
    }
}

/// <summary>One field a saved game keeps, by name (<see cref="Key"/>, and the keys it had before), or why it doesn't.</summary>
internal sealed record SavedField(FieldInfo Info, string Name, string Key, string[] Former, string? WhyNot);

/// <summary>
/// The fields a saved game keeps of one type: a script's own (those its classes declare, but those the scene file holds
/// already), or a plain object's or struct's. As a rewind snapshot keeps them, less those marked
/// <see cref="NotSavedAttribute"/> and events.
/// </summary>
internal sealed class SavePlan
{
    private static readonly ConditionalWeakTable<Type, SavePlan> s_scripts = new();
    private static readonly ConditionalWeakTable<Type, SavePlan> s_objects = new();
    private static readonly ConditionalWeakTable<Type, StrongBox<string?>> s_whyNot = new();
    private static readonly Assembly s_griff = typeof(Entity).Assembly;

    private SavePlan(SavedField[] fields) => Fields = fields;

    public SavedField[] Fields { get; }

    /// <summary>A game's own script (or a class of its scripts'): not Griff's nor a module's.</summary>
    public static bool IsScript(Type type) => IsScriptAssembly(type.Assembly);

    public static bool IsScriptAssembly(Assembly assembly) => assembly != s_griff && !ModuleAssemblies.IsModule(assembly) && StatePlan.IsCopyable(assembly);

    /// <summary>A script's: the fields its own classes declare, but those behind the properties the scene file holds.</summary>
    public static SavePlan OfScript(Type type, ComponentRegistry? registry) => s_scripts.GetValue(type, t =>
    {
        var saved = (registry?.TryGet(t) is { } descriptor
                ? descriptor.Properties.Select(p => p.Name)
                : typeof(Component).IsAssignableFrom(t)
                    ? ComponentModel.Writable(t).Where(p => ValueCodec.IsSupported(p.PropertyType)).Select(p => p.Name)
                    : [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fields = new List<SavedField>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var at = t; at is not null && IsScript(at); at = at.BaseType)
        {
            foreach (var field in at.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (Keeps(at, field) && !saved.Contains(Backs(field.Name)))
                    fields.Add(Describe(at, field, keys));
            }
        }

        return new SavePlan([.. fields]);
    });

    /// <summary>A plain object's or a struct's: every field of its class and those it's built on.</summary>
    public static SavePlan Of(Type type) => s_objects.GetValue(type, static t =>
    {
        var fields = new List<SavedField>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var at = t; at is not null && at != typeof(object) && at != typeof(ValueType) && at != typeof(Random); at = at.BaseType)
        {
            foreach (var field in at.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (Keeps(at, field))
                    fields.Add(Describe(at, field, keys));
            }
        }

        return new SavePlan([.. fields]);
    });

    // Not state (rewind leaves it out too), not saved, or an event's list of handlers (wiring, set up again by Start).
    private static bool Keeps(Type type, FieldInfo field)
    {
        if (field.IsDefined(typeof(NotStateAttribute)) || field.IsDefined(typeof(NotSavedAttribute)))
            return false;
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        if (type.GetEvent(field.Name, declared) is not null)
            return false;
        return !(field.Name.StartsWith('<') && type.GetProperty(Backs(field.Name), declared) is { } property && property.IsDefined(typeof(NotSavedAttribute)));
    }

    private static SavedField Describe(Type type, FieldInfo field, HashSet<string> keys)
    {
        var name = field.Name.StartsWith('<') ? Backs(field.Name) : field.Name;
        var key = SavedData.Key(name);
        // A private field of the same name in a class it's built on.
        if (!keys.Add(key))
            keys.Add(key = $"{type.Name}.{key}");
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var former = field.GetCustomAttributes<FormerNameAttribute>()
            .Concat(field.Name.StartsWith('<') && type.GetProperty(name, declared) is { } property ? property.GetCustomAttributes<FormerNameAttribute>() : [])
            .Select(f => SavedData.Key(f.Name))
            .ToArray();
        return new SavedField(field, name, key, former, WhyNot(field.FieldType));
    }

    // The property a field is behind, by its name: an auto property's own (<Size>k__BackingField), a primary constructor's
    // parameter (<size>P), or one named like it (_size).
    private static string Backs(string field) =>
        field.StartsWith('<') && field.IndexOf('>') is var end and > 1 ? field[1..end] : field.TrimStart('_');

    /// <summary>A value written as itself: a number, text, an enum, a vector, a color, a time.</summary>
    public static bool IsScalar(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) ||
        type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || IsCodecValue(type);

    // A value a scene file writes too, written as it does (a vector as [x, y], a color as its hex).
    public static bool IsCodecValue(Type type) =>
        type == typeof(Color) || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Quaternion);

    /// <summary>
    /// Null if a value of <paramref name="type"/> can be saved (anything of a type that can hold one, such as object or
    /// an interface, by what it holds); "" if it's a link to a part of the game, which the script's Start sets again;
    /// otherwise why it can't be saved.
    /// </summary>
    public static string? WhyNot(Type type) => s_whyNot.GetValue(type, static t => new StrongBox<string?>(Why(t))).Value;

    private static string? Why(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return Why(inner);
        if (IsScalar(type) || type == typeof(object) || typeof(Entity).IsAssignableFrom(type) || typeof(Component).IsAssignableFrom(type) ||
            typeof(GameSystem).IsAssignableFrom(type))
            return null;
        if (typeof(Delegate).IsAssignableFrom(type))
            return "a delegate (a method to call) can't be saved: set it up again in Start or OnLoaded";
        if (type == typeof(Coroutine) || type == typeof(IEnumerator) || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerator<>) ||
            type.IsDefined(typeof(CompilerGeneratedAttribute)))
            return "a running coroutine can't be saved: start it again in OnLoaded, from what the script's other fields say";
        if (type == typeof(GameTimer) || type == typeof(Tween) || type == typeof(MessageListener))
            return $"a {type.Name} can't be saved: set it up again in Start or OnLoaded";
        if (type == typeof(Scene) || StatePlan.IsPart(type))
            return "";
        if (type == typeof(Random))
            return "a Random's place in its numbers can't be saved: keep a GameRandom instead";
        if (type.IsArray)
            return type.IsSZArray ? Why(type.GetElementType()!) : "an array of more than one dimension can't be saved";
        if (type.IsInterface && type.IsGenericType && type.Namespace == "System.Collections.Generic")
            return Arguments(type);
        if (type.IsInterface || type.IsAbstract)
            return StatePlan.IsCopyable(type.Assembly) ? null : $"a {type.Name} can't be saved";
        var plan = StatePlan.Of(type);
        if (plan.Kind is StateTag.List or StateTag.Map or StateTag.Set or StateTag.Sequence)
            return Arguments(type);
        if (type.IsValueType)
        {
            var framework = !StatePlan.IsCopyable(type.Assembly);
            var pair = type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) || type.FullName!.StartsWith("System.ValueTuple`", StringComparison.Ordinal));
            return framework && !pair && !plan.Unmanaged ? $"a {type.Name} can't be saved" : null;
        }

        return plan.Kind == StateTag.Object ? null : $"a {type.Name} can't be saved";

        // A collection by its items (and keys).
        static string? Arguments(Type type)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (Why(argument) is { Length: > 0 } why)
                    return why;
            }

            return null;
        }
    }

    /// <summary>
    /// How a saved game names a class: its full name ("Contract", "Factory.Job"), a generic one with its arguments'
    /// ("System.Collections.Generic.List&lt;Contract&gt;"), never its assembly's (each build of the scripts has another).
    /// </summary>
    public static string NameOf(Type type)
    {
        if (type.IsArray)
            return NameOf(type.GetElementType()!) + "[]";
        if (!type.IsGenericType)
            return type.FullName ?? type.Name;
        var name = type.GetGenericTypeDefinition().FullName ?? type.Name;
        if (name.IndexOf('`') is var tick and > 0)
            name = name[..tick];
        return $"{name}<{string.Join(",", type.GetGenericArguments().Select(NameOf))}>";
    }

    /// <summary>True if <paramref name="type"/> is saved as <paramref name="name"/>, or was (a <see cref="FormerNameAttribute"/>).</summary>
    public static bool Matches(Type type, string? name) =>
        name is not null && (NameOf(type) == name || FormerNames(type).Contains(name, StringComparer.Ordinal));

    // A class's former names, as given and in its own namespace.
    public static IEnumerable<string> FormerNames(Type type) =>
        type.GetCustomAttributes<FormerNameAttribute>(inherit: false)
            .SelectMany(f => type.Namespace is { } space && !f.Name.Contains('.') ? [f.Name, $"{space}.{f.Name}"] : new[] { f.Name });
}

/// <summary>
/// Writes scripts' fields into a saved game, version-tolerantly, by name: each script's into its component's saved data
/// ("$fields"), each script's system's into the scene's "$systems", and the plain objects and collections they hold
/// once each into the scene's "$objects", pointed to wherever they're held (so shared ones stay shared, and cycles
/// close). Entities, components and systems are written as which they are.
/// </summary>
internal sealed class ModelWriter(Game game)
{
    private readonly JsonArray _objects = [];
    private readonly Dictionary<object, int> _ids = new(ReferenceEqualityComparer.Instance);

    /// <summary>The fields of <paramref name="component"/> if it's a script's (or what it saves itself), or null if none.</summary>
    public JsonObject? FieldsOf(Component component)
    {
        var type = component.GetType();
        return SavePlan.IsScript(type) ? Fields(component, SavePlan.OfScript(type, game.Components), type.Name) : null;
    }

    /// <summary>
    /// The scene's JSON with the scripts' systems and the objects the scripts hold, or as it is if they hold none.
    /// </summary>
    public string Finish(string scene)
    {
        var systems = new JsonArray();
        foreach (var system in game.Systems.ToList())
        {
            var type = system.GetType();
            if (system.IsRemoved || !SavePlan.IsScript(type))
                continue;
            systems.Add(new JsonObject
            {
                ["type"] = SavePlan.NameOf(type),
                ["fields"] = Fields(system, SavePlan.OfScript(type, null), type.Name) ?? [],
            });
        }

        if (systems.Count == 0 && _objects.Count == 0)
            return scene;
        var root = JsonNode.Parse(scene)!.AsObject();
        if (systems.Count > 0)
            root[ModelReader.SystemsKey] = systems;
        if (_objects.Count > 0)
            root[ModelReader.ObjectsKey] = _objects;
        return root.ToJsonString();
    }

    private JsonObject? Fields(object script, SavePlan plan, string label)
    {
        var fields = new JsonObject();
        if (script is ISavesState own)
            own.SaveState(new SaveWriter(this, fields, label));
        else
            WriteFields(script, plan, fields, label);
        return fields.Count > 0 ? fields : null;
    }

    private void WriteFields(object target, SavePlan plan, JsonObject into, string label)
    {
        foreach (var field in plan.Fields)
        {
            if (field.WhyNot is { } why)
            {
                if (why.Length > 0)
                    Skip($"{label}.{field.Name}", why);
                continue;
            }

            if (Write(field.Info.GetValue(target), field.Info.FieldType, $"{label}.{field.Name}", out var node))
                into[field.Key] = node;
        }
    }

    // Said once a game (the problem log says each problem once).
    private void Skip(string label, string why) =>
        game.Warn($"{label} isn't kept in saved games: {why}. After a load it's as the script's Start left it (mark it [NotSaved] to say that's meant).");

    /// <summary>
    /// <paramref name="value"/>, held where a <paramref name="declared"/> goes, as a saved game writes it into
    /// <paramref name="node"/>; false if it can't be (said once, as <paramref name="label"/>) or is a link to a part of the
    /// game, which the script sets again.
    /// </summary>
    public bool Write(object? value, Type declared, string label, out JsonNode? node)
    {
        node = null;
        if (value is null)
            return true;
        var type = value.GetType();
        var exact = type == declared || Nullable.GetUnderlyingType(declared) == type;
        switch (value)
        {
            case Entity entity:
                node = new JsonObject { ["$entity"] = entity.Id.ToString() };
                return true;
            case Component component:
                // One with no entity (removed) is saved as none.
                if (component.Entity is { } owner)
                    node = new JsonObject { ["$component"] = owner.Id.ToString(), ["type"] = SavePlan.NameOf(type), ["nth"] = Nth(owner, component) };
                return true;
            case GameSystem:
                node = new JsonObject { ["$system"] = SavePlan.NameOf(type) };
                return true;
        }

        if (SavePlan.IsScalar(type))
        {
            node = Scalar(value);
            if (!exact)
                node = new JsonObject { ["$type"] = SavePlan.NameOf(type), ["$value"] = node };
            return true;
        }

        if (SavePlan.WhyNot(type) is { } why)
        {
            if (why.Length > 0)
                Skip(label, why);
            return false;
        }

        if (type.IsValueType)
        {
            var fields = new JsonObject();
            if (!exact)
                fields["$type"] = SavePlan.NameOf(type);
            WriteFields(value, SavePlan.Of(type), fields, type.Name);
            node = fields;
            return true;
        }

        node = new JsonObject { ["$ref"] = Id(value, type) };
        return true;
    }

    // The object's place in "$objects", written there the first time it's met.
    private int Id(object value, Type type)
    {
        if (_ids.TryGetValue(value, out var id))
            return id;
        id = _objects.Count;
        _ids[value] = id;
        var entry = new JsonObject { ["type"] = SavePlan.NameOf(type) };
        _objects.Add(entry);
        if (value is IDictionary map)
        {
            var arguments = type.GetGenericArguments();
            var (keyType, valueType) = arguments.Length == 2 ? (arguments[0], arguments[1]) : (typeof(object), typeof(object));
            var pairs = new JsonArray();
            foreach (DictionaryEntry pair in map)
            {
                if (Write(pair.Key, keyType, $"{type.Name} key", out var key) && key is not null)
                    pairs.Add(new JsonArray(key, Write(pair.Value, valueType, $"{type.Name}[{pair.Key}]", out var item) ? item : null));
            }

            entry["pairs"] = pairs;
        }
        else if (type.IsArray || StatePlan.Of(type).Kind is StateTag.List or StateTag.Set or StateTag.Sequence)
        {
            var element = type.IsArray ? type.GetElementType()! : StatePlan.Of(type).Element ?? typeof(object);
            var items = new JsonArray();
            var i = 0;
            foreach (var each in (IEnumerable)value)
                items.Add(Write(each, element, $"{type.Name}[{i++}]", out var item) ? item : null);
            entry["items"] = items;
        }
        else
        {
            var fields = new JsonObject();
            WriteFields(value, SavePlan.Of(type), fields, type.Name);
            entry["fields"] = fields;
        }

        return id;
    }

    // Which of the entity's components of its type it is.
    private static int Nth(Entity entity, Component component)
    {
        var nth = 0;
        foreach (var other in entity.Components)
        {
            if (other == component)
                return nth;
            if (other.GetType() == component.GetType())
                nth++;
        }

        return 0;
    }

    internal static readonly JsonSerializerOptions Options = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    private static JsonNode? Scalar(object value)
    {
        switch (value)
        {
            case Enum e:
                return JsonValue.Create(e.ToString());
            case Color or Vector2 or Vector3 or Vector4 or Quaternion:
                using (var stream = new MemoryStream())
                {
                    using (var writer = new Utf8JsonWriter(stream))
                        ValueCodec.Write(writer, value);
                    return JsonNode.Parse(stream.ToArray());
                }

            default:
                return JsonSerializer.SerializeToNode(value, value.GetType(), Options);
        }
    }
}

/// <summary>
/// Reads scripts' fields back from a saved game (see <see cref="ModelWriter"/>), by name: a field that's gone is ignored,
/// a new one keeps what the script made it, and one of another kind now keeps that too, saying so. Each object is made
/// once, so what was shared is shared again; a collection the field already holds (made by the script) is filled rather
/// than replaced, keeping how it compares its keys.
/// </summary>
internal sealed class ModelReader(Game game, JsonArray? objects, string slot)
{
    public const string ObjectsKey = "$objects";
    public const string SystemsKey = "$systems";

    private readonly Dictionary<int, object?> _made = [];
    private readonly HashSet<object> _taken = new(ReferenceEqualityComparer.Instance);
    private Dictionary<string, Type>? _types;

    /// <summary>
    /// Reads <paramref name="script"/>'s saved <paramref name="fields"/> now, and gives what puts them into it (at once,
    /// or once it has started).
    /// </summary>
    public Action Read(object script, SavePlan plan, JsonObject fields, string label)
    {
        if (script is ISavesState own)
        {
            var reader = new SaveReader(this, fields, label);
            return () => own.LoadState(reader);
        }

        var values = new List<(FieldInfo Field, object? Value)>();
        foreach (var field in plan.Fields)
        {
            if (field.WhyNot is null && Node(fields, field) is var (found, node) && found &&
                TryRead(node, field.Info.FieldType, field.Info.GetValue(script), $"{label}.{field.Name}", out var value))
                values.Add((field.Info, value));
        }

        return () =>
        {
            foreach (var (field, value) in values)
                field.SetValue(script, value);
        };
    }

    // The field's saved value, under its name or one it had.
    private static (bool Found, JsonNode? Node) Node(JsonObject from, SavedField field)
    {
        if (from.TryGetPropertyValue(field.Key, out var node))
            return (true, node);
        foreach (var former in field.Former)
        {
            if (from.TryGetPropertyValue(former, out node))
                return (true, node);
        }

        return (false, null);
    }

    /// <summary>
    /// <paramref name="node"/> read as a <paramref name="declared"/> (filling <paramref name="current"/>, what the place
    /// holds now, if it's a collection of the same type); false, said once, if it isn't one now.
    /// </summary>
    public bool TryRead(JsonNode? node, Type declared, object? current, string label, out object? value)
    {
        try
        {
            value = Read(node, declared, current);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or ArgumentException or NotSupportedException or
                                       InvalidCastException or MissingMethodException or TargetInvocationException)
        {
            game.Warn($"Loading the game saved in \"{slot}\": {label}: {(ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message} Kept the default.");
            value = null;
            return false;
        }
    }

    private object? Read(JsonNode? node, Type declared, object? current)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        if (node is null)
            return !declared.IsValueType || type != declared ? null : throw Mismatch(node, declared);
        if (node is JsonValue or JsonArray)
            return SavePlan.IsScalar(type) ? Scalar(node, type) : throw Mismatch(node, declared);
        var saved = node.AsObject();
        if (saved["$ref"] is JsonValue reference)
            return Object(reference.GetValue<int>(), declared, current);
        if (saved["$entity"] is JsonValue entity)
            return Fits(Guid.TryParse(entity.GetValue<string>(), out var id) ? game.Scene.Find(id) : null, node, declared, typeof(Entity));
        if (saved["$component"] is JsonValue owner)
        {
            var on = Guid.TryParse(owner.GetValue<string>(), out var id) ? game.Scene.Find(id) : null;
            var name = saved["type"]?.GetValue<string>();
            var nth = saved["nth"]?.GetValue<int>() ?? 0;
            return Fits(on?.Components.Where(c => SavePlan.Matches(c.GetType(), name)).ElementAtOrDefault(nth), node, declared, typeof(Component));
        }

        if (saved["$system"] is JsonValue system)
        {
            var name = system.GetValue<string>();
            return Fits(game.Systems.FirstOrDefault(s => !s.IsRemoved && SavePlan.Matches(s.GetType(), name)), node, declared, typeof(GameSystem));
        }

        var named = saved["$type"]?.GetValue<string>() is { } typeName ? Resolve(typeName, type) ?? throw new FormatException($"Its type {typeName} is gone.") : type;
        if (!declared.IsAssignableFrom(named) && !type.IsAssignableFrom(named))
            throw Mismatch(node, declared);
        if (saved.ContainsKey("$value"))
            return Read(saved["$value"], named, null);

        // A struct, by its fields.
        if (!named.IsValueType || SavePlan.IsScalar(named))
            throw Mismatch(node, declared);
        var made = current?.GetType() == named ? current : Activator.CreateInstance(named)!;
        ReadInto(made, SavePlan.Of(named), saved, named.Name);
        return made;
    }

    // What a reference found is, if it fits the place (nothing, if it's gone); else it doesn't.
    private static object? Fits(object? found, JsonNode node, Type declared, Type kind) =>
        found is null ? (declared.IsAssignableFrom(kind) || kind.IsAssignableFrom(declared) ? null : throw Mismatch(node, declared))
            : declared.IsInstanceOfType(found) ? found : throw Mismatch(node, declared);

    private void ReadInto(object target, SavePlan plan, JsonObject from, string label)
    {
        foreach (var field in plan.Fields)
        {
            if (field.WhyNot is null && Node(from, field) is var (found, node) && found &&
                TryRead(node, field.Info.FieldType, field.Info.GetValue(target), $"{label}.{field.Name}", out var value))
                field.Info.SetValue(target, value);
        }
    }

    // The object saved at `id` in "$objects", made the first time it's met.
    private object? Object(int id, Type declared, object? current)
    {
        if (_made.TryGetValue(id, out var made))
            return made is null || declared.IsInstanceOfType(made) ? made : throw new FormatException($"Expected a {declared.Name} but found a {made.GetType().Name}.");
        if (objects is null || id < 0 || id >= objects.Count || objects[id] is not JsonObject entry)
            throw new FormatException("What it held is missing from the save.");
        var name = entry["type"]?.GetValue<string>() ?? "";
        if (entry["pairs"] is JsonArray pairs)
            return Pairs(id, pairs, Collection(declared, name, typeof(Dictionary<,>)), current);
        if (entry["items"] is JsonArray items)
        {
            var sets = name.StartsWith("System.Collections.Generic.HashSet<", StringComparison.Ordinal);
            return Items(id, items, declared.IsArray ? declared : Collection(declared, name, sets ? typeof(HashSet<>) : typeof(List<>), sets ? typeof(List<>) : typeof(HashSet<>)), current);
        }

        var plain = Resolve(name, declared) ?? throw new FormatException($"Its class {name} is gone.");
        if (!declared.IsAssignableFrom(plain) || SavePlan.WhyNot(plain) is not null || plain.IsAbstract)
            throw new FormatException($"Expected a {declared.Name} but found a {name}.");
        var instance = Reuse(current, plain) ?? New(plain);
        _made[id] = instance;
        _taken.Add(instance);
        ReadInto(instance, SavePlan.Of(plain), entry["fields"] as JsonObject ?? [], plain.Name);
        return instance;
    }

    // The concrete collection to make for a place of type `declared`: itself, or for an interface (IList<T>, ISet<T>,
    // IReadOnlyDictionary<K, V>...) the first of `usual` (List<T>, HashSet<T>, Dictionary<K, V>) that fits it.
    private static Type Collection(Type declared, string name, params Type[] usual)
    {
        if (!declared.IsInterface && !declared.IsAbstract && declared != typeof(object))
            return declared;
        if (declared.IsGenericType && declared.GetGenericArguments() is var arguments)
        {
            foreach (var kind in usual)
            {
                if (kind.GetGenericArguments().Length == arguments.Length && kind.MakeGenericType(arguments) is var made && declared.IsAssignableFrom(made))
                    return made;
            }
        }

        throw new FormatException($"Expected a {declared.Name} but found a {name}.");
    }

    private object Items(int id, JsonArray items, Type type, object? current)
    {
        if (type.IsArray)
        {
            var element = type.GetElementType()!;
            var array = Array.CreateInstance(element, items.Count);
            _made[id] = array;
            for (var i = 0; i < items.Count; i++)
            {
                if (TryRead(items[i], element, null, $"{type.Name}[{i}]", out var item))
                    array.SetValue(item, i);
            }

            return array;
        }

        var plan = StatePlan.Of(type);
        if (plan.Kind is not (StateTag.List or StateTag.Set or StateTag.Sequence) || plan.Add is null || plan.Element is null || typeof(IDictionary).IsAssignableFrom(type))
            throw new FormatException($"Expected a {type.Name} but found a list.");
        var list = Reuse(current, type) ?? Activator.CreateInstance(type)!;
        type.GetMethod("Clear", Type.EmptyTypes)?.Invoke(list, null);
        _made[id] = list;
        _taken.Add(list);
        var values = new List<object?>();
        for (var i = 0; i < items.Count; i++)
        {
            if (TryRead(items[i], plan.Element, null, $"{type.Name}[{i}]", out var item))
                values.Add(item);
        }

        // A stack is written from its top.
        if (plan.Reversed)
            values.Reverse();
        foreach (var value in values)
            plan.Add.Invoke(list, [value]);
        return list;
    }

    private object Pairs(int id, JsonArray pairs, Type type, object? current)
    {
        if (!typeof(IDictionary).IsAssignableFrom(type) || type.GetGenericArguments() is not { Length: 2 } arguments)
            throw new FormatException($"Expected a {type.Name} but found a dictionary.");
        var map = (IDictionary)(Reuse(current, type) ?? Activator.CreateInstance(type)!);
        map.Clear();
        _made[id] = map;
        _taken.Add(map);
        foreach (var pair in pairs.OfType<JsonArray>())
        {
            if (pair.Count == 2 && TryRead(pair[0], arguments[0], null, $"{type.Name} key", out var key) && key is not null &&
                TryRead(pair[1], arguments[1], null, $"{type.Name}[{key}]", out var value))
                map[key] = value;
        }

        return map;
    }

    // What the place holds now, to fill, if it's of the type and isn't filled from another saved object already.
    private object? Reuse(object? current, Type type) =>
        current is not null && current.GetType() == type && !_taken.Contains(current) ? current : null;

    // A new one, as its parameterless constructor makes it (so a field added since keeps its starting value), or blank.
    private static object New(Type type) =>
        type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) is { } constructor
            ? constructor.Invoke(null)
            : RuntimeHelpers.GetUninitializedObject(type);

    // The class saved as `name`: the place's own, or one of the game's scripts' (or a former name of one), or Griff's.
    private Type? Resolve(string name, Type declared)
    {
        if (SavePlan.NameOf(declared) == name)
            return declared;
        if (_types is null)
        {
            _types = new Dictionary<string, Type>(StringComparer.Ordinal);
            var assemblies = (game.Components?.All.Select(d => d.Type.Assembly) ?? [])
                .Concat(game.Systems.Select(s => s.GetType().Assembly))
                .Where(SavePlan.IsScriptAssembly)
                .Append(typeof(Entity).Assembly)
                .Distinct()
                .ToList();
            var former = new List<(string, Type)>();
            foreach (var assembly in assemblies)
            {
                foreach (var type in TypesIn(assembly))
                {
                    if (type.IsGenericTypeDefinition || type.IsDefined(typeof(CompilerGeneratedAttribute)))
                        continue;
                    _types.TryAdd(SavePlan.NameOf(type), type);
                    former.AddRange(SavePlan.FormerNames(type).Select(f => (f, type)));
                }
            }

            foreach (var (was, type) in former)
                _types.TryAdd(was, type);
        }

        return _types.GetValueOrDefault(name) ?? TypesIn(declared.Assembly).FirstOrDefault(t => SavePlan.Matches(t, name)) ?? Type.GetType(name);

        static Type[] TypesIn(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return [.. ex.Types.OfType<Type>()];
            }
        }
    }

    private static object? Scalar(JsonNode node, Type type)
    {
        if (type.IsEnum)
            return Enum.Parse(type, node.GetValue<string>(), ignoreCase: false);
        if (SavePlan.IsCodecValue(type))
            return ValueCodec.Read(JsonSerializer.SerializeToElement(node), type);
        return node.Deserialize(type, ModelWriter.Options);
    }

    private static FormatException Mismatch(JsonNode? node, Type declared)
    {
        var text = node?.ToJsonString() ?? "null";
        return new FormatException($"Expected a {declared.Name} but found {(text.Length > 40 ? text[..40] + "..." : text)}.");
    }
}
