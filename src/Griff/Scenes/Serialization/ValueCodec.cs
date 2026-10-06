using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Griff.Scenes.Serialization;

/// <summary>
/// Reads and writes component property values. Vectors are compact arrays (<c>[1, 2, 3]</c>)
/// and enums are written by name, so scene files stay readable and diff cleanly. Lists of strings
/// are written one string per line, so line-based data (like tilemap rows) diffs line by line.
/// </summary>
internal static class ValueCodec
{
    public static bool IsSupported(Type type) =>
        type == typeof(bool) || type == typeof(int) || type == typeof(float) || type == typeof(double) ||
        type == typeof(string) || type.IsEnum || type == typeof(Color) ||
        type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Quaternion) ||
        type == typeof(IReadOnlyList<string>);

    /// <summary>Value equality for property values: lists compare by their items, everything else by Equals.</summary>
    public static bool AreEqual(object? a, object? b) =>
        a is IReadOnlyList<string> x && b is IReadOnlyList<string> y ? x.SequenceEqual(y) : Equals(a, b);

    public static void Write(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNullValue(); break;
            case bool b: writer.WriteBooleanValue(b); break;
            case int i: writer.WriteNumberValue(i); break;
            case float f when !float.IsFinite(f): writer.WriteNullValue(); break;
            case float f: writer.WriteNumberValue(f); break;
            case double d when !double.IsFinite(d): writer.WriteNullValue(); break;
            case double d: writer.WriteNumberValue(d); break;
            case string s: writer.WriteStringValue(s); break;
            case Enum e: writer.WriteStringValue(e.ToString()); break;
            case Color c: writer.WriteStringValue(c.ToHex()); break;
            case Vector2 v: WriteArray(writer, v.X, v.Y); break;
            case Vector3 v: WriteArray(writer, v.X, v.Y, v.Z); break;
            case Vector4 v: WriteArray(writer, v.X, v.Y, v.Z, v.W); break;
            case Quaternion q: WriteArray(writer, q.X, q.Y, q.Z, q.W); break;
            case IReadOnlyList<string> lines:
                writer.WriteStartArray();
                foreach (var line in lines)
                    writer.WriteStringValue(line);
                writer.WriteEndArray();
                break;
            default: throw new NotSupportedException($"Can't write a {value.GetType().Name}.");
        }
    }

    /// <exception cref="FormatException">The JSON doesn't hold a value of <paramref name="type"/>.</exception>
    public static object? Read(JsonElement element, Type type)
    {
        try
        {
            if (type == typeof(bool)) return element.GetBoolean();
            if (type == typeof(int)) return element.GetInt32();
            if (type == typeof(float)) return element.GetSingle();
            if (type == typeof(double)) return element.GetDouble();
            if (type == typeof(string)) return element.ValueKind == JsonValueKind.Null ? null : element.GetString();
            if (type.IsEnum) return Enum.Parse(type, element.GetString()!, ignoreCase: false);
            if (type == typeof(Color))
                return Color.TryParse(element.GetString(), out var color) ? color : throw new FormatException();
            if (type == typeof(IReadOnlyList<string>))
                return element.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();

            var n = ReadArray(element);
            if (type == typeof(Vector2) && n.Length == 2) return new Vector2(n[0], n[1]);
            if (type == typeof(Vector3) && n.Length == 3) return new Vector3(n[0], n[1], n[2]);
            if (type == typeof(Vector4) && n.Length == 4) return new Vector4(n[0], n[1], n[2], n[3]);
            if (type == typeof(Quaternion) && n.Length == 4) return new Quaternion(n[0], n[1], n[2], n[3]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            throw new FormatException($"Expected a {type.Name} but found {element.GetRawText()}.", ex);
        }

        throw new FormatException($"Expected a {type.Name} but found {element.GetRawText()}.");
    }

    // Written as one raw line: the indented writer would otherwise put each number on its own line.
    private static void WriteArray(Utf8JsonWriter writer, params ReadOnlySpan<float> values)
    {
        var parts = new string[values.Length];
        for (var i = 0; i < values.Length; i++)
            parts[i] = FormatFloat(values[i]);
        writer.WriteRawValue($"[{string.Join(", ", parts)}]", skipInputValidation: true);
    }

    // Shortest text that round-trips exactly. Non-finite values have no JSON form; null loads back
    // as a warning and the property's default rather than failing the save.
    private static string FormatFloat(float value) =>
        float.IsFinite(value) ? value.ToString(CultureInfo.InvariantCulture) : "null";

    private static float[] ReadArray(JsonElement element) =>
        element.EnumerateArray().Select(e => e.GetSingle()).ToArray();
}
