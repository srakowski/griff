using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Griff.Scenes;

namespace Griff.Editor.Features.Debugging;

/// <summary>
/// A value in scope where a breakpoint stopped the game: a variable, or a member or item of one. Its members are read
/// when asked for (expanding it), without running the game's code: a script's own fields as they are, and the public
/// properties of Griff's and .NET's objects.
/// </summary>
public sealed class DebugValue
{
    private const int MaxItems = 100;
    private const int MaxText = 200;

    private readonly object? _value;
    private readonly Func<Type, bool> _isScript;
    private IReadOnlyList<DebugValue>? _members;

    internal DebugValue(string variable, object? value, Type? declared, Func<Type, bool> isScript)
    {
        Variable = variable;
        _value = value;
        _isScript = isScript;
        Type = TypeName(value is BaseView view ? view.Type : value?.GetType() ?? declared);
        Value = Describe(value);
        HasMembers = value is not null && !IsLeaf(value.GetType()) && Count(value) is not 0;
    }

    /// <summary>The variable's name, or the member's ("Speed"), or the item's place ("[2]").</summary>
    public string Variable { get; }

    /// <summary>What it holds, as text: 5, "Hero", (1, 2, 0), Entity "Player", List&lt;int&gt; (3 items).</summary>
    public string Value { get; }

    /// <summary>Its type, as C# writes it: float, string, List&lt;int&gt;.</summary>
    public string Type { get; }

    /// <summary>"speed = 5 (float)": the variable, its value and type in one line.</summary>
    public string Label => $"{Variable} = {Value} ({Type})";

    /// <summary>Whether it has members or items to look into.</summary>
    public bool HasMembers { get; }

    /// <summary>Its members (a script's fields, an object's properties) or items (a list's, a dictionary's), read now.</summary>
    public IReadOnlyList<DebugValue> Members => _members ??= HasMembers ? ReadMembers() : [];

    /// <summary>The value itself, for the editor's own use (the Inspector's object, say).</summary>
    internal object? Raw => _value;

    public override string ToString() => Variable;

    /// <summary>
    /// The value and its members to <paramref name="depth"/> levels, a line each, indented: what an agent reads.
    /// </summary>
    public void Write(System.Text.StringBuilder text, int depth, string indent = "")
    {
        text.Append(indent).AppendLine(Label);
        if (depth <= 0 || !HasMembers)
            return;
        foreach (var member in Members)
            member.Write(text, depth - 1, indent + "  ");
    }

    private List<DebugValue> ReadMembers()
    {
        var members = new List<DebugValue>();
        switch (_value)
        {
            case IDictionary dictionary:
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (members.Count == MaxItems)
                        break;
                    members.Add(new DebugValue($"[{Describe(entry.Key)}]", entry.Value, null, _isScript));
                }

                return members;
            // A collection's items (a sequence that isn't one would run code to give them, so it shows its members instead).
            case IEnumerable items when Count(_value) is not null:
                var index = 0;
                try
                {
                    foreach (var item in items)
                    {
                        if (index == MaxItems)
                            break;
                        members.Add(new DebugValue($"[{index++}]", item, null, _isScript));
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                {
                    // A sequence that can't be read again (or changed meanwhile) shows what it gave.
                }

                return members;
        }

        AddMembers(_value!, _value!.GetType(), members);
        return members;
    }

    // A script's own fields as they are (an auto-property's by the property's name), then what it builds on (a Behavior's
    // Entity, Transform...) as one value, "base"; other objects' public properties and fields.
    private void AddMembers(object target, Type type, List<DebugValue> members)
    {
        if (_isScript(type))
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsDefined(typeof(CompilerGeneratedAttribute)) && !field.Name.EndsWith(">k__BackingField", StringComparison.Ordinal))
                    continue;
                var name = field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>')] : field.Name;
                members.Add(new DebugValue(name, Read(() => field.GetValue(target)), field.FieldType, _isScript));
            }

            if (type.BaseType is { } baseType && baseType != typeof(object))
            {
                if (_isScript(baseType))
                    AddMembers(target, baseType, members);
                else
                    members.Add(new DebugValue("base", new BaseView(target, baseType), baseType, _isScript));
            }

            return;
        }

        // What a script builds on shows what the script itself can use: protected members too (Transform, Input, Time...).
        var view = target is BaseView b ? b : null;
        var source = view?.Target ?? target;
        var shownType = view?.Type ?? type;
        var flags = BindingFlags.Instance | BindingFlags.Public | (view is null ? 0 : BindingFlags.NonPublic);
        foreach (var property in shownType.GetProperties(flags).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is not { } getter || !(getter.IsPublic || getter.IsFamily || getter.IsFamilyOrAssembly)
                || property.IsDefined(typeof(ObsoleteAttribute)) || members.Any(m => m.Variable == property.Name))
                continue;
            if (members.Count == MaxItems)
                break;
            members.Add(new DebugValue(property.Name, Read(() => property.GetValue(source)), property.PropertyType, _isScript));
        }

        foreach (var field in shownType.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (members.Count == MaxItems)
                break;
            members.Add(new DebugValue(field.Name, Read(() => field.GetValue(source)), field.FieldType, _isScript));
        }
    }

    // A getter that throws shows what it threw rather than stopping the rest.
    private static object? Read(Func<object?> read)
    {
        try
        {
            return read();
        }
        catch (TargetInvocationException ex)
        {
            return new Threw(ex.InnerException ?? ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException or MemberAccessException)
        {
            return new Threw(ex);
        }
    }

    // What an object builds on, read as that type: a Behavior's members, under "base".
    private sealed record BaseView(object Target, Type Type)
    {
        public override string ToString() => TypeName(Type);
    }

    // A member whose getter threw.
    private sealed record Threw(Exception Error)
    {
        public override string ToString() => $"(threw {Error.GetType().Name}: {Error.Message})";
    }

    private static bool IsLeaf(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid)
        || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Quaternion)
        || type == typeof(Color) || type == typeof(TimeSpan) || type == typeof(DateTime) || type == typeof(Threw)
        || typeof(Delegate).IsAssignableFrom(type) || typeof(Type).IsAssignableFrom(type) || typeof(MemberInfo).IsAssignableFrom(type);

    /// <summary>How a value reads: 5, 0.25, "Hero", (1, 2, 0), Entity "Player", Health on "Player", List&lt;int&gt; (3 items).</summary>
    internal static string Describe(object? value)
    {
        var text = value switch
        {
            null => "null",
            string s => "\"" + s + "\"",
            char c => $"'{c}'",
            bool b => b ? "true" : "false",
            float f => f.ToString("0.####", CultureInfo.InvariantCulture),
            double d => d.ToString("0.####", CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            Enum e => $"{e.GetType().Name}.{e}",
            IFormattable when value.GetType().IsPrimitive => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
            Vector2 v => $"({Number(v.X)}, {Number(v.Y)})",
            Vector3 v => $"({Number(v.X)}, {Number(v.Y)}, {Number(v.Z)})",
            Vector4 v => $"({Number(v.X)}, {Number(v.Y)}, {Number(v.Z)}, {Number(v.W)})",
            Quaternion q => $"({Number(q.X)}, {Number(q.Y)}, {Number(q.Z)}, {Number(q.W)})",
            Entity entity => $"Entity \"{entity.Name}\"",
            Component component => component.Entity is { } owner ? $"{component.GetType().Name} on \"{owner.Name}\"" : component.GetType().Name,
            Delegate function => $"{function.Method.Name}()",
            Array array => $"{TypeName(array.GetType().GetElementType())}[{array.Length}]",
            _ when Count(value) is { } count => $"{TypeName(value.GetType())} ({count} item{(count == 1 ? "" : "s")})",
            _ => Overridden(value) ? value.ToString() ?? "" : TypeName(value.GetType()),
        };
        return text.Length > MaxText ? text[..MaxText] + "…" : text;
    }

    // How many items a collection holds (one whose items can be read without running code), or null for anything else.
    private static int? Count(object value)
    {
        if (value is ICollection collection)
            return collection.Count;
        if (value is string)
            return null;
        foreach (var face in value.GetType().GetInterfaces())
        {
            if (face.IsGenericType && face.GetGenericTypeDefinition() is var definition
                && (definition == typeof(IReadOnlyCollection<>) || definition == typeof(ICollection<>))
                && face.GetProperty("Count")?.GetValue(value) is int count)
                return count;
        }

        return null;
    }

    private static string Number(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static bool Overridden(object value) =>
        value.GetType().GetMethod(nameof(ToString), global::System.Type.EmptyTypes) is { } method && method.DeclaringType != typeof(object) && method.DeclaringType != typeof(ValueType);

    /// <summary>A type as C# writes it: float, string, List&lt;int&gt;, int[].</summary>
    internal static string TypeName(Type? type)
    {
        if (type is null)
            return "?";
        if (Nullable.GetUnderlyingType(type) is { } held)
            return TypeName(held) + "?";
        if (type.IsArray)
            return TypeName(type.GetElementType()) + "[]";
        var keyword = global::System.Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "bool",
            TypeCode.Byte => "byte",
            TypeCode.SByte => "sbyte",
            TypeCode.Char => "char",
            TypeCode.Int16 => "short",
            TypeCode.UInt16 => "ushort",
            TypeCode.Int32 => "int",
            TypeCode.UInt32 => "uint",
            TypeCode.Int64 => "long",
            TypeCode.UInt64 => "ulong",
            TypeCode.Single => "float",
            TypeCode.Double => "double",
            TypeCode.Decimal => "decimal",
            TypeCode.String => "string",
            _ => type == typeof(object) ? "object" : null,
        };
        if (keyword is not null && !type.IsEnum)
            return keyword;
        if (!type.IsGenericType)
            return type.Name;
        var name = type.Name;
        var tick = name.IndexOf('`');
        return $"{(tick < 0 ? name : name[..tick])}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
    }
}
