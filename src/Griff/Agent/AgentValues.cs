using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Agent;

/// <summary>
/// Turns JSON into values and back for agents. Component properties read and write exactly as scene files have them
/// (the same codec), so any kind of value a component can save, an agent can set; it's a little more forgiving on the
/// way in: <c>{"x": 1, "y": 2}</c> for a vector, any case for a choice, one string with line breaks for lines.
/// </summary>
public static class AgentValues
{
    /// <summary>Looks up entities and components named in arguments, for methods that take them.</summary>
    public static Func<string, Entity?>? EntityResolver { get; set; }

    /// <exception cref="AgentException">The JSON can't be read as a <paramref name="type"/>.</exception>
    public static object? FromJson(JsonElement element, Type type, Func<string, Entity?>? entities = null)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return element.ValueKind == JsonValueKind.Null ? null : FromJson(element, underlying, entities);

        try
        {
            if (type == typeof(JsonElement))
                return element.Clone();
            if (type == typeof(object))
                return Plain(element);
            if (type == typeof(string))
                return element.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => element.GetString(),
                    _ => element.GetRawText(),
                };
            if (type == typeof(bool) && element.ValueKind == JsonValueKind.String)
                return bool.Parse(element.GetString()!);
            if (type.IsEnum)
                return Enum.Parse(type, element.ValueKind == JsonValueKind.Number ? element.GetRawText() : element.GetString()!, ignoreCase: true);
            if (IsNumber(type))
                return Convert.ChangeType(element.ValueKind == JsonValueKind.String
                    ? double.Parse(element.GetString()!, CultureInfo.InvariantCulture)
                    : element.GetDouble(), type, CultureInfo.InvariantCulture);
            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Quaternion))
                return ValueCodec.Read(Vectorish(element, type), type);
            if (type == typeof(IReadOnlyList<string>) || type == typeof(string[]) || type == typeof(List<string>) || type == typeof(IEnumerable<string>))
            {
                var lines = element.ValueKind == JsonValueKind.String
                    ? element.GetString()!.Replace("\r\n", "\n").Split('\n')
                    : element.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText()).ToArray();
                return type == typeof(List<string>) ? lines.ToList() : lines;
            }

            if (ValueCodec.IsSupported(type))
                return ValueCodec.Read(element, type);
            if (typeof(Entity).IsAssignableFrom(type))
                return ResolveEntity(element, entities);
            if (typeof(Component).IsAssignableFrom(type))
            {
                var entity = ResolveEntity(element, entities);
                return entity?.Components.FirstOrDefault(type.IsInstanceOfType)
                    ?? throw new AgentException($"{entity?.Name} has no {type.Name}.");
            }

            return element.Deserialize(type);
        }
        catch (AgentException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or JsonException
            or OverflowException or NotSupportedException or KeyNotFoundException)
        {
            throw new AgentException($"Expected {Describe(type)} but got {Clip(element.GetRawText())}.");
        }
    }

    /// <summary>A value as JSON: the scene-file form for property values, a short description for anything else.</summary>
    public static JsonNode? ToJson(object? value, int depth = 1)
    {
        switch (value)
        {
            case null: return null;
            case string s: return s.Length > 4000 ? s[..4000] + $"... ({s.Length} characters)" : s;
            case bool or int or long or short or byte or uint or ulong or ushort or sbyte or decimal: return JsonValue.Create(value);
            case float f: return float.IsFinite(f) ? JsonValue.Create(MathF.Round(f, 4)) : null;
            case double d: return double.IsFinite(d) ? JsonValue.Create(Math.Round(d, 4)) : null;
            case Enum e: return e.ToString();
            case Guid g: return g.ToString();
            case Entity entity: return $"{entity.Name} #{ShortId(entity.Id)}";
            case Component component: return $"{component.GetType().Name} on {component.Entity?.Name ?? "nothing"}";
            case JsonNode node: return node.DeepClone();
            case JsonElement element: return JsonNode.Parse(element.GetRawText());
        }

        var type = value.GetType();
        if (ValueCodec.IsSupported(type))
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
                ValueCodec.Write(writer, value);
            return JsonNode.Parse(stream.ToArray());
        }

        if (value is IEnumerable items and not IDictionary)
        {
            var list = items.Cast<object?>().Take(depth > 0 ? 200 : 0).ToList();
            if (depth <= 0)
                return $"[{Count(items)} items]";
            return new JsonArray(list.Select(i => ToJson(i, depth - 1)).ToArray());
        }

        if (type.IsPrimitive || type.IsValueType && type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            return value.ToString();
        return Summary(value);
    }

    /// <summary>What kind of value a type takes, for schemas and messages: "a number", "X, Y as [x, y]".</summary>
    public static string Describe(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return Describe(underlying) + " or null";
        return type == typeof(bool) ? "true or false"
            : IsNumber(type) ? (type == typeof(int) || type == typeof(long) || type == typeof(uint) ? "a whole number" : "a number")
            : type == typeof(string) ? "text"
            : type.IsEnum ? "one of " + string.Join(", ", Enum.GetNames(type))
            : type == typeof(Color) ? "a color, \"#RRGGBB\" or \"#RRGGBBAA\""
            : type == typeof(Vector2) ? "[x, y]"
            : type == typeof(Vector3) ? "[x, y, z]"
            : type == typeof(Vector4) ? "[x, y, z, w]"
            : type == typeof(Quaternion) ? "[x, y, z, w] (a rotation)"
            : type == typeof(IReadOnlyList<string>) || type == typeof(string[]) || type == typeof(List<string>) ? "lines of text: an array of strings, or one string with line breaks"
            : typeof(Entity).IsAssignableFrom(type) ? "an entity: its name, id or path"
            : typeof(Component).IsAssignableFrom(type) ? $"an entity with a {type.Name}: its name, id or path"
            : type.Name;
    }

    /// <summary>A JSON schema for a value of <paramref name="type"/>.</summary>
    public static JsonObject SchemaOf(Type type, string? description = null)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        var text = description is null ? Describe(type) : $"{description} ({Describe(type)})";
        if (underlying == typeof(bool))
            return Schema.Bool(text);
        if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(uint))
            return Schema.Integer(text);
        if (IsNumber(underlying))
            return Schema.Number(text);
        if (underlying.IsEnum)
            return Schema.String(text, Enum.GetNames(underlying));
        if (underlying == typeof(Vector2) || underlying == typeof(Vector3) || underlying == typeof(Vector4) || underlying == typeof(Quaternion))
            return Schema.Array(Schema.Number("component"), text);
        if (underlying == typeof(string) || underlying == typeof(Color) || typeof(Entity).IsAssignableFrom(underlying) || typeof(Component).IsAssignableFrom(underlying))
            return Schema.String(text);
        return Schema.Any(text);
    }

    public static string ShortId(Guid id) => id.ToString("N")[..8];

    /// <summary>One line about an object: its type and, if it has one, its name, title or label.</summary>
    public static string Summary(object value)
    {
        var type = value.GetType();
        foreach (var name in new[] { "Name", "Title", "Label", "Text", "Id" })
        {
            var property = type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (property?.GetIndexParameters().Length == 0 && property.PropertyType == typeof(string))
            {
                try
                {
                    if (property.GetValue(value) is string { Length: > 0 } label)
                        return $"{type.Name} \"{Clip(label)}\"";
                }
                catch (Exception)
                {
                    // A getter that throws just isn't shown.
                }
            }
        }

        return type.Name;
    }

    internal static bool IsNumber(Type type) =>
        type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(long) || type == typeof(uint)
        || type == typeof(short) || type == typeof(byte) || type == typeof(decimal) || type == typeof(ulong) || type == typeof(ushort);

    private static int Count(IEnumerable items) => items is ICollection c ? c.Count : items.Cast<object?>().Count();

    private static Entity? ResolveEntity(JsonElement element, Func<string, Entity?>? entities)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;
        var reference = element.GetString() ?? "";
        var resolve = entities ?? EntityResolver ?? throw new AgentException("There's no scene to find entities in.");
        return resolve(reference) ?? throw new AgentException($"There's no entity \"{reference}\".");
    }

    // {"x":1,"y":2} or "1, 2" as an array, so the codec reads it.
    private static JsonElement Vectorish(JsonElement element, Type type)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = type == typeof(Vector2) ? new[] { "x", "y" } : type == typeof(Vector3) ? ["x", "y", "z"] : ["x", "y", "z", "w"];
            var values = names.Select(n => element.EnumerateObject().FirstOrDefault(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: JsonValueKind.Number } p
                ? p.Value.GetSingle() : 0f);
            return JsonDocument.Parse($"[{string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture)))}]").RootElement;
        }

        if (element.ValueKind == JsonValueKind.String)
            return JsonDocument.Parse($"[{element.GetString()}]").RootElement;

        // [x, y] for a Vector3 means z = 0, the way 2D games place things.
        if (element.ValueKind == JsonValueKind.Array && type == typeof(Vector3) && element.GetArrayLength() == 2)
            return JsonDocument.Parse($"[{element[0].GetRawText()},{element[1].GetRawText()},0]").RootElement;
        return element;
    }

    private static object? Plain(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.Clone(),
    };

    internal static string Clip(string text) => text.Length > 120 ? text[..117] + "..." : text;
}
