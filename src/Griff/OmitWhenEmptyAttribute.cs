using System.Collections;

namespace Griff;

/// <summary>
/// Leaves a property out of scene and prefab files while it's empty (null, an empty string or an empty list), so a
/// property added to a component later doesn't change the files of games that don't use it: they save byte for byte as
/// they did before it. It reads back as its default, empty, as a file saved before it was added does.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class OmitWhenEmptyAttribute : Attribute
{
    /// <summary>True if <paramref name="value"/> is empty as this attribute means it: null, "" or a list with nothing in it.</summary>
    public static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string text => text.Length == 0,
        ICollection list => list.Count == 0,
        _ => false,
    };
}
