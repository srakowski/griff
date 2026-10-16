using System.Reflection;

namespace Griff;

/// <summary>
/// Excludes a public read/write property from scene files and the inspector, and from what a table row fills (a script's
/// class filled from a row, <c>row.To&lt;T&gt;()</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DoNotSerializeAttribute : Attribute
{
    // Public, readable and settable, not an indexer, not marked [DoNotSerialize]: the one rule for what's open to set, in a
    // scene file, the inspector or a table row.
    internal static IEnumerable<PropertyInfo> Writable(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod?.IsPublic == true && p.SetMethod?.IsPublic == true)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => p.GetCustomAttribute<DoNotSerializeAttribute>() is null);
}
