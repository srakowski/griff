using System.Text.Json;
using System.Text.Json.Nodes;
using Griff.Fonts;
using Griff.Projects;

namespace Griff.Exporting;

/// <summary>
/// What an export says about the fonts a project's scenes and prefabs set text in: a warning for each installed font
/// (never copied into a game: it's licensed to the computer it's on) and each font file Assets hasn't got. Both draw in
/// the built-in font where the font isn't there.
/// </summary>
public static class ExportFonts
{
    /// <summary>
    /// The warnings for <paramref name="project"/>, in file order: for a <paramref name="web"/> export, where a browser has
    /// only the game's own files, or for the desktop, where players may not have the font.
    /// </summary>
    public static IReadOnlyList<string> Warnings(Project project, bool web)
    {
        var warnings = new List<string>();
        if (!Directory.Exists(project.AssetsPath))
            return warnings;
        var files = Directory.EnumerateFiles(project.AssetsPath, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(Project.SceneExtension, StringComparison.OrdinalIgnoreCase) || f.EndsWith(Project.PrefabExtension, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);
        foreach (var file in files)
        {
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                continue;
            }

            var relative = Path.GetRelativePath(project.AssetsPath, file).Replace(Path.DirectorySeparatorChar, '/');
            foreach (var font in FontsIn(root).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (BuiltInFonts.Find(font) is not null)
                    continue;
                if (ProjectFonts.IsFontFile(font))
                {
                    if (!File.Exists(Path.Combine(project.AssetsPath, font.Replace('\\', '/'))))
                    {
                        warnings.Add($"{relative} sets text in the font file \"{font}\", which isn't in Assets: it draws in the built-in font." +
                                     (FreeFonts.Find(font) is { } free ? $" It's Griff's free font {free.Name}: pick it in the Inspector (or Art > Add Free Font) to add it." : ""));
                    }
                }
                else if (web)
                {
                    warnings.Add($"{relative} sets text in the installed font \"{font}\", which a browser hasn't got: it draws in the " +
                                 "built-in font there. Put a font file (.ttf or .otf) in Assets and choose that to have it everywhere.");
                }
                else
                {
                    warnings.Add($"{relative} sets text in the installed font \"{font}\", which isn't shipped with the game (it's licensed to " +
                                 "this PC): players without it see the built-in font. Choose a font that's safe to ship (the built-in font, one " +
                                 "of Griff's free fonts or a font file in Assets) to have it everywhere.");
                }
            }
        }

        return warnings;
    }

    // Every font a component in the file sets (a "Font", or a key ending in it such as "titleFont"), however deep.
    private static IEnumerable<string> FontsIn(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (key.EndsWith("Font", StringComparison.OrdinalIgnoreCase) && value is JsonValue v && v.TryGetValue<string>(out var font))
                    {
                        if (!string.IsNullOrWhiteSpace(font))
                            yield return font.Trim();
                    }
                    else
                    {
                        foreach (var inner in FontsIn(value))
                            yield return inner;
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    foreach (var inner in FontsIn(item))
                        yield return inner;
                }

                break;
        }
    }
}
