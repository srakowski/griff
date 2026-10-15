using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Griff.Scenes;

namespace Griff.Agent;

/// <summary>
/// Reaches into any object by reflection, the way a person reaches into a window: read its properties, change the ones
/// that can change, press its commands and call its methods. Paths walk from object to object:
/// <c>Presets[Coin].Command</c>, <c>Groups[0].Sliders[2].Value</c>, <c>Scene.Roots[Player]</c>. A list item can be
/// picked by position or by its name, title or label. This is how the editor's windows, panels and services reach agents
/// without anyone writing a tool for them.
/// </summary>
public static partial class ObjectSurface
{
    private const int MaxItems = 100;

    /// <summary>A description of the object at <paramref name="path"/>: its values, commands, methods and items.</summary>
    public static JsonObject Describe(object root, string? path)
    {
        var target = Resolve(root, path);
        if (target is null)
            return new JsonObject { ["path"] = path ?? "", ["value"] = null };

        var type = target.GetType();
        var result = new JsonObject { ["path"] = path ?? "", ["type"] = type.Name };
        if (IsLeaf(type))
        {
            result["value"] = AgentValues.ToJson(target);
            return result;
        }

        var values = new JsonObject();
        var commands = new JsonObject();
        foreach (var property in Properties(type))
        {
            if (typeof(ICommand).IsAssignableFrom(property.PropertyType))
            {
                commands[property.Name] = TryGet(property, target, out var command) && command is ICommand c
                    ? (SafeCanExecute(c) ? "available" : "unavailable now")
                    : "none";
                continue;
            }

            if (typeof(Delegate).IsAssignableFrom(property.PropertyType))
                continue;

            var shown = TryGet(property, target, out var value) ? Shown(value) : "(can't be read now)";
            if (CanSet(property) && (IsLeaf(property.PropertyType) || Nullable.GetUnderlyingType(property.PropertyType) is not null))
                values[property.Name] = new JsonObject { ["value"] = shown, ["settable"] = AgentValues.Describe(property.PropertyType) };
            else
                values[property.Name] = shown;
        }

        if (values.Count > 0)
            result["properties"] = values;
        if (commands.Count > 0)
            result["commands"] = commands;

        var methods = Methods(type).Select(m => (JsonNode)Signature(m)).ToArray();
        if (methods.Length > 0)
            result["methods"] = new JsonArray(methods);

        if (target is IEnumerable items and not string)
        {
            var list = new JsonArray();
            var index = 0;
            foreach (var item in items)
            {
                if (index == MaxItems)
                {
                    list.Add($"... and more");
                    break;
                }

                list.Add($"[{index++}] {(item is null ? "null" : IsLeaf(item.GetType()) ? AgentValues.ToJson(item)?.ToJsonString(ToolResult.Compact) : AgentValues.Summary(item))}");
            }

            result["items"] = list;
        }

        return result;
    }

    /// <summary>
    /// Sets the property at <paramref name="path"/> from JSON, converted to the property's type; an entity is named as
    /// <paramref name="entities"/> finds it.
    /// </summary>
    /// <returns>The value it now has.</returns>
    public static JsonNode? Set(object root, string path, JsonElement value, Func<string, Entity?>? entities = null)
    {
        var (owner, member, indexes) = Split(root, path);
        if (indexes.Count > 0)
            throw new AgentException("Set a property, not a list item.");
        var property = FindProperty(owner.GetType(), member)
            ?? throw new AgentException($"{owner.GetType().Name} has no property \"{member}\". {Listing(owner)}");
        if (!CanSet(property))
            throw new AgentException($"{owner.GetType().Name}.{property.Name} can't be changed.");

        try
        {
            property.SetValue(owner, Convert(owner, value, property.PropertyType, entities, except: property));
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // A setter that refuses the value says why.
            throw ex.InnerException is AgentException agent ? agent : new AgentException($"{property.Name} can't be {value}: {ex.InnerException.Message}");
        }

        return TryGet(property, owner, out var now) ? Shown(now) : null;
    }

    // A value from JSON; an object given by its label or position is one of the owner's list items.
    private static object? Convert(object owner, JsonElement value, Type type, Func<string, Entity?>? entities, PropertyInfo? except = null) =>
        IsLeaf(type) || type == typeof(object) || value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)
            ? AgentValues.FromJson(value, type, entities)
            : Choice(owner, type, except, value, entities);

    // A property or argument holding one of a list's items (a picked template, a selected row, a component to add), given
    // by the item's label or position: the item is looked for in the lists the same object offers.
    private static object? Choice(object owner, Type type, PropertyInfo? except, JsonElement value, Func<string, Entity?>? entities)
    {
        var label = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
        foreach (var list in Properties(owner.GetType()).Where(p => p != except && typeof(IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string)))
        {
            if (!TryGet(list, owner, out var items) || items is not IEnumerable enumerable || !enumerable.Cast<object?>().Any(type.IsInstanceOfType))
                continue;
            try
            {
                if (Item(items, label) is { } item && type.IsInstanceOfType(item))
                    return item;
            }
            catch (AgentException)
            {
                // Not in this list; try the next.
            }
        }

        return AgentValues.FromJson(value, type, entities);
    }

    /// <summary>
    /// Presses the command or calls the method at <paramref name="path"/>. <paramref name="args"/> is an array of
    /// arguments in order, an object of them by name, or for a command, its parameter; an entity is named as
    /// <paramref name="entities"/> finds it. Awaits what it returns.
    /// </summary>
    public static async Task<JsonNode?> InvokeAsync(object root, string path, JsonElement? args, Func<string, Entity?>? entities = null)
    {
        var (owner, member, indexes) = Split(root, path);
        if (indexes.Count == 0 && FindProperty(owner.GetType(), member) is { } property && typeof(ICommand).IsAssignableFrom(property.PropertyType))
        {
            var command = property.GetValue(owner) as ICommand ?? throw new AgentException($"{property.Name} isn't set.");
            object? parameter = args is { ValueKind: JsonValueKind.Array } a && a.GetArrayLength() == 1 ? Plain(a[0])
                : args is { ValueKind: not (JsonValueKind.Array or JsonValueKind.Undefined or JsonValueKind.Null) } p ? Plain(p)
                : null;
            if (!SafeCanExecute(command, parameter))
                throw new AgentException($"{property.Name} can't be used right now.");
            command.Execute(parameter);
            return "done";
        }

        var target = indexes.Count == 0 ? owner : Index(owner, member, indexes);
        if (indexes.Count > 0 && target is ICommand itemCommand)
        {
            itemCommand.Execute(null);
            return "done";
        }

        var candidates = Methods(owner.GetType()).Where(m => m.Name.Equals(member, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0)
        {
            // A plain property: there's nothing to call, so say what it holds.
            if (FindProperty(owner.GetType(), member) is { } plain && TryGet(plain, owner, out var value))
                return Shown(value, depth: 1);
            throw new AgentException($"{owner.GetType().Name} has no command or method \"{member}\". {Listing(owner)}");
        }

        var (method, values) = Bind(owner, candidates, args, entities);
        object? returned;
        try
        {
            returned = method.Invoke(owner, values);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException is AgentException agent ? agent : new AgentException($"{method.Name} failed: {ex.InnerException.Message}");
        }

        if (returned is Task task)
        {
            await task;
            var resultProperty = task.GetType().GetProperty("Result");
            returned = resultProperty is not null && task.GetType().IsGenericType && resultProperty.PropertyType.Name != "VoidTaskResult"
                ? resultProperty.GetValue(task)
                : null;
            return returned is null ? "done" : Shown(returned, depth: 2);
        }

        return method.ReturnType == typeof(void) ? "done" : Shown(returned, depth: 2);
    }

    /// <summary>
    /// The object whose property or method <paramref name="path"/> names: what <see cref="Set"/> changes and
    /// <see cref="InvokeAsync"/> calls on ("Scene.Roots[Player].Name" is the Player entity's).
    /// </summary>
    public static object OwnerOf(object root, string path) => string.IsNullOrWhiteSpace(path) ? root : Split(root, path).Owner;

    /// <summary>The object at <paramref name="path"/> (the root for an empty path).</summary>
    public static object? Resolve(object root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return root;
        var (owner, member, indexes) = Split(root, path);
        var property = FindProperty(owner.GetType(), member);
        if (property is null)
        {
            if (indexes.Count == 0 && owner is IEnumerable && member.Length > 0 && char.IsDigit(member[0]))
                return Item(owner, member);
            throw new AgentException($"{owner.GetType().Name} has no property \"{member}\". {Listing(owner)}");
        }

        var value = property.GetValue(owner);
        foreach (var index in indexes)
            value = Item(value ?? throw new AgentException($"{property.Name} is empty."), index);
        return value;
    }

    // The owner of the last segment, the last segment's name, and any [indexes] after it.
    private static (object Owner, string Member, IReadOnlyList<string> Indexes) Split(object root, string path)
    {
        var segments = Segments().Matches(path).Select(m => m.Value).ToList();
        if (segments.Count == 0)
            throw new AgentException("The path is empty.");

        object current = root;
        for (var i = 0; i < segments.Count - 1; i++)
            current = Step(current, segments[i]) ?? throw new AgentException($"\"{segments[i]}\" is empty, so the path stops there.");

        var last = segments[^1];
        var (name, indexes) = Parse(last);
        if (name.Length == 0)
        {
            // A path that ends in an index on its own, e.g. "[2]".
            object owner = current;
            for (var i = 0; i < indexes.Count - 1; i++)
                owner = Item(owner, indexes[i])!;
            return (owner, indexes[^1], []);
        }

        return (current, name, indexes);
    }

    private static object? Step(object current, string segment)
    {
        var (name, indexes) = Parse(segment);
        object? value = current;
        if (name.Length > 0)
        {
            var property = FindProperty(current.GetType(), name)
                ?? throw new AgentException($"{current.GetType().Name} has no property \"{name}\". {Listing(current)}");
            value = property.GetValue(current);
        }

        foreach (var index in indexes)
            value = Item(value ?? throw new AgentException($"\"{name}\" is empty."), index);
        return value;
    }

    private static object? Index(object owner, string member, IReadOnlyList<string> indexes)
    {
        var property = FindProperty(owner.GetType(), member) ?? throw new AgentException($"{owner.GetType().Name} has no property \"{member}\".");
        var value = property.GetValue(owner);
        foreach (var index in indexes)
            value = Item(value ?? throw new AgentException($"{member} is empty."), index);
        return value;
    }

    private static (string Name, List<string> Indexes) Parse(string segment)
    {
        var bracket = segment.IndexOf('[');
        if (bracket < 0)
            return (segment, []);
        var indexes = IndexPattern().Matches(segment[bracket..]).Select(m => m.Groups[1].Value.Trim().Trim('"', '\'')).ToList();
        return (segment[..bracket], indexes);
    }

    /// <summary>An item of a list or dictionary, by position or by its name, title, label or text.</summary>
    private static object? Item(object collection, string index)
    {
        if (collection is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (string.Equals(entry.Key?.ToString(), index, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }

            throw new AgentException($"There's no \"{index}\" in it. Keys: {string.Join(", ", dictionary.Keys.Cast<object>().Take(40))}");
        }

        if (collection is not IEnumerable items || collection is string)
            throw new AgentException($"{collection.GetType().Name} isn't a list.");

        var list = items.Cast<object?>().ToList();
        if (int.TryParse(index, out var position))
        {
            if (position < 0)
                position += list.Count;
            return position >= 0 && position < list.Count ? list[position]
                : throw new AgentException($"There are {list.Count} items; [{index}] is out of range.");
        }

        var match = list.FirstOrDefault(i => i is not null && Labels(i).Any(l => l.Equals(index, StringComparison.OrdinalIgnoreCase)));
        return match ?? throw new AgentException(
            $"No item is called \"{index}\". Items: {string.Join(", ", list.Take(40).Select(i => i is null ? "null" : AgentValues.Summary(i)))}");
    }

    private static IEnumerable<string> Labels(object item)
    {
        foreach (var name in new[] { "Name", "Title", "Label", "Text", "Id", "Header", "DisplayText", "DisplayName" })
        {
            var property = item.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property?.GetIndexParameters().Length == 0 && TryGet(property, item, out var value) && value is not null)
                yield return value.ToString() ?? "";
        }

        yield return item.ToString() ?? "";
    }

    private static (MethodInfo Method, object?[] Values) Bind(object owner, List<MethodInfo> candidates, JsonElement? args, Func<string, Entity?>? entities)
    {
        var positional = args is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray().ToList() : null;
        var named = args is { ValueKind: JsonValueKind.Object } obj ? obj : (JsonElement?)null;
        if (args is { ValueKind: not (JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Undefined or JsonValueKind.Null) } single)
            positional = [single];
        positional ??= named is null ? [] : null;

        var errors = new List<string>();
        foreach (var method in candidates.OrderBy(m => m.GetParameters().Length))
        {
            var parameters = method.GetParameters();
            try
            {
                var values = new object?[parameters.Length];
                for (var i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    JsonElement? supplied = positional is not null
                        ? (i < positional.Count ? positional[i] : null)
                        : named!.Value.EnumerateObject().FirstOrDefault(p => p.Name.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: not JsonValueKind.Undefined } found
                            ? found.Value : null;
                    if (supplied is { } element)
                        values[i] = Convert(owner, element, parameter.ParameterType, entities);
                    else if (parameter.HasDefaultValue)
                        values[i] = parameter.DefaultValue;
                    else if (parameter.ParameterType == typeof(CancellationToken))
                        values[i] = CancellationToken.None;
                    else
                        throw new AgentException($"\"{parameter.Name}\" is needed.");
                }

                if (positional is not null && positional.Count > parameters.Length)
                    throw new AgentException($"{method.Name} takes {parameters.Length} argument(s).");
                return (method, values);
            }
            catch (AgentException ex)
            {
                errors.Add($"{Signature(method)}: {ex.Message}");
            }
        }

        throw new AgentException("The arguments don't fit. " + string.Join(" ", errors));
    }

    private static IEnumerable<PropertyInfo> Properties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod?.IsPublic == true && IsOwn(p.DeclaringType))
            .GroupBy(p => p.Name)
            .Select(g => g.First());

    private static IEnumerable<MethodInfo> Methods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && !m.IsGenericMethodDefinition && IsOwn(m.DeclaringType)
                && m.Name is not ("ToString" or "Equals" or "GetHashCode" or "GetType" or "Dispose" or "DisposeAsync" or "Deconstruct" or "<Clone>$")
                && m.GetParameters().All(p => !p.ParameterType.IsByRef && !p.IsOut && !typeof(Delegate).IsAssignableFrom(p.ParameterType)));

    // The app's own members, not those of the framework classes it builds on.
    private static bool IsOwn(Type? declaring) =>
        declaring is not null && declaring != typeof(object) && declaring.Namespace is { } ns
        && !ns.StartsWith("System", StringComparison.Ordinal) && !ns.StartsWith("Microsoft", StringComparison.Ordinal)
        && !ns.StartsWith("CommunityToolkit", StringComparison.Ordinal) && !ns.StartsWith("MS.", StringComparison.Ordinal);

    private static PropertyInfo? FindProperty(Type type, string name) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string Listing(object owner)
    {
        var type = owner.GetType();
        var properties = Properties(type).Select(p => p.Name).Take(60).ToList();
        var methods = Methods(type).Select(m => m.Name).Distinct().Take(60).ToList();
        return $"It has properties: {(properties.Count == 0 ? "none" : string.Join(", ", properties))}; methods: {(methods.Count == 0 ? "none" : string.Join(", ", methods))}.";
    }

    private static string Signature(MethodInfo method)
    {
        var parameters = string.Join(", ", method.GetParameters().Select(p =>
            $"{p.Name}: {AgentValues.Describe(p.ParameterType)}{(p.HasDefaultValue ? " (optional)" : "")}"));
        var returns = method.ReturnType == typeof(void) || method.ReturnType == typeof(Task) ? "" : $" -> {FriendlyType(method.ReturnType)}";
        return $"{method.Name}({parameters}){returns}";
    }

    // A nullable reads as what it holds "or null", and a tuple as its items in brackets, "[a whole number, text]".
    private static string FriendlyType(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>) ? FriendlyType(type.GetGenericArguments()[0])
        : Nullable.GetUnderlyingType(type) is { } held && !IsLeaf(held) ? FriendlyType(held) + " or null"
        : typeof(System.Runtime.CompilerServices.ITuple).IsAssignableFrom(type) && type.IsGenericType ? $"[{string.Join(", ", type.GetGenericArguments().Select(FriendlyType))}]"
        : IsLeaf(type) ? AgentValues.Describe(type)
        : type.IsGenericType ? $"{type.Name.Split('`')[0]}<{string.Join(", ", type.GetGenericArguments().Select(FriendlyType))}>"
        : type.Name;

    // A public setter, but not an init-only one: those are set as the object is made (a record's values), not after.
    private static bool CanSet(PropertyInfo property) =>
        property.SetMethod is { IsPublic: true } setter
        && !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));

    private static bool IsLeaf(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid)
        || type == typeof(Color) || type == typeof(System.Numerics.Vector2) || type == typeof(System.Numerics.Vector3)
        || type == typeof(System.Numerics.Vector4) || type == typeof(System.Numerics.Quaternion)
        || Nullable.GetUnderlyingType(type) is { } u && IsLeaf(u);

    private static JsonNode? Shown(object? value, int depth = 0) =>
        value is null ? null
        : IsLeaf(value.GetType()) || value is IReadOnlyList<string> ? AgentValues.ToJson(value)
        : value is IEnumerable items and not string ? (depth > 0 ? AgentValues.ToJson(value, depth) : $"[{items.Cast<object?>().Count()} items]")
        : value is System.Runtime.CompilerServices.ITuple tuple ? new JsonArray(Enumerable.Range(0, tuple.Length).Select(i => Shown(tuple[i], depth)).ToArray())
        : depth > 0 && value is JsonNode node ? node.DeepClone()
        : AgentValues.Summary(value);

    private static bool TryGet(PropertyInfo property, object target, out object? value)
    {
        try
        {
            value = property.GetValue(target);
            return true;
        }
        catch (Exception)
        {
            value = null;
            return false;
        }
    }

    private static bool SafeCanExecute(ICommand command, object? parameter = null)
    {
        try
        {
            return command.CanExecute(parameter);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static object? Plain(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    // "Groups[0].Sliders[2].Value" -> "Groups[0]", "Sliders[2]", "Value"; dots inside [..] don't split.
    [GeneratedRegex(@"(?:[^.\[\]]+|\[[^\]]*\])+")]
    private static partial Regex Segments();

    [GeneratedRegex(@"\[([^\]]*)\]")]
    private static partial Regex IndexPattern();
}
