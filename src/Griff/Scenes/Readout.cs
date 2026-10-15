using System.Globalization;
using System.Text;

namespace Griff.Scenes;

/// <summary>
/// How a <see cref="Text"/> showing an output reads: its Value with <c>{0}</c> (the output) and <c>{1}</c> (what it's out of)
/// filled in, each with a format after a colon if it says ("{0:000}", "{0:0.0}"). Anything else in braces is left for the
/// input prompts ("Press {Interact}").
/// </summary>
internal static class Readout
{
    /// <summary>
    /// <paramref name="template"/> with the output filled in: <paramref name="name"/> for one that gives a name, otherwise
    /// <paramref name="number"/> (a whole number as one, true or false as On or Off). A template without <c>{0}</c> or
    /// <c>{1}</c> is just the output.
    /// </summary>
    public static string Fill(string? template, float number, string? name, float outOf, Type valueType)
    {
        if (string.IsNullOrEmpty(template) || (!template.Contains("{0", StringComparison.Ordinal) && !template.Contains("{1", StringComparison.Ordinal)))
            return Write(number, name, valueType, null);

        var text = new StringBuilder(template.Length + 8);
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '{' && i + 2 < template.Length && template[i + 1] is '0' or '1' && template[i + 2] is '}' or ':' &&
                template.IndexOf('}', i + 2) is var close and > 0)
            {
                var format = template[i + 2] == ':' ? template[(i + 3)..close] : null;
                text.Append(template[i + 1] == '0' ? Write(number, name, valueType, format) : Write(outOf, null, typeof(float), format));
                i = close;
                continue;
            }

            text.Append(c);
        }

        return text.ToString();
    }

    private static string Write(float number, string? name, Type valueType, string? format)
    {
        if (valueType == typeof(string))
            return name ?? "";
        if (valueType == typeof(bool))
            return number != 0 ? "On" : "Off";
        try
        {
            if (valueType == typeof(int) || (format is null && number == MathF.Round(number)))
                return ((int)MathF.Round(number)).ToString(format, CultureInfo.InvariantCulture);
            return number.ToString(format ?? "0.##", CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return number.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
