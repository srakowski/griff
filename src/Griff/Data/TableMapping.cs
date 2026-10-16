using System.Reflection;
using Griff.Scenes;

namespace Griff.Data;

/// <summary>
/// Fills a class of a script's own from a table row: each public settable property takes the column of the same name
/// (ignoring case, spaces and underscores, so MaxHealth takes "Max Health"), and a string property called Name takes the
/// row's name unless there's a Name column. Numbers, whole numbers, text, yes/no, dice, choices (as an enum or text) and
/// <see cref="TableValue"/> itself all fit; properties with no column keep what they start as.
/// </summary>
internal static class TableMapping
{
    // Made once per type, by its ComponentModel (held only while the type is, so a script reloaded in the editor doesn't
    // keep its last version's types alive).
    public static T Create<T>(TableRow row) where T : new() => (T)ComponentModel.Of(typeof(T)).Maker(row);

    /// <summary>Makes a key that matches a column and a property that are written differently: "Max Health", "max_health", "MaxHealth".</summary>
    public static string Key(string name) => new([.. name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    // Fills each of the type's properties a table may (see ComponentModel.Filled) from its column.
    internal static Func<TableRow, object> Build(Type type, IReadOnlyList<PropertyInfo> properties)
    {
        return row =>
        {
            var made = Activator.CreateInstance(type)!;
            var table = row.Table;
            foreach (var property in properties)
            {
                var key = Key(property.Name);
                var column = -1;
                for (var i = 0; i < table.Columns.Count && column < 0; i++)
                {
                    if (Key(table.Columns[i].Name) == key)
                        column = i;
                }

                if (column < 0 && table.ColumnIndex(property.Name) is >= 0 and var renamed)
                    column = renamed;
                if (column < 0)
                {
                    if (key == "name" && property.PropertyType == typeof(string))
                        property.SetValue(made, row.Name);
                    continue;
                }

                var value = row.Cells[column];
                try
                {
                    property.SetValue(made, Convert(value, property.PropertyType));
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidCastException)
                {
                    throw new FormatException($"{table.Name}: {row.Name}'s {table.Columns[column].Name} ({value}) doesn't fit {type.Name}.{property.Name}, a {property.PropertyType.Name}.", ex);
                }
            }

            return made;
        };
    }

    /// <summary>
    /// <paramref name="value"/> as a <paramref name="type"/>: float, double, int, long, bool, string, <see cref="Dice"/>,
    /// <see cref="TableValue"/> or an enum (a choice by its name). Throws for a type it can't fill.
    /// </summary>
    public static object? Convert(TableValue value, Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target != type && value.IsEmpty)
            return null;
        if (target == typeof(TableValue)) return value;
        if (target == typeof(float)) return (float)value.AsNumber;
        if (target == typeof(double)) return value.AsNumber;
        if (target == typeof(int)) return (int)value.AsWholeNumber;
        if (target == typeof(long)) return value.AsWholeNumber;
        if (target == typeof(bool)) return value.AsYesNo;
        if (target == typeof(string)) return value.IsEmpty ? null : value.ToString();
        if (target == typeof(Dice)) return value.AsDice;
        if (target.IsEnum)
        {
            if (value.IsEmpty)
                return Activator.CreateInstance(target);
            var text = value.ToString();
            var name = Enum.GetNames(target).FirstOrDefault(n => Key(n) == Key(text));
            return name is not null ? Enum.Parse(target, name) : throw new FormatException($"\"{text}\" isn't one of {string.Join(", ", Enum.GetNames(target))}.");
        }

        throw new ArgumentException($"A table's cell can't fill a {type.Name}.");
    }
}
