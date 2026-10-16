using Griff.Editing;

namespace Griff.Fonts;

/// <summary>
/// The fonts a project's text can be set in, grouped as the Inspector's font picker and the agents' component_describe
/// show them: first those safe to ship (the built-in font, Griff's <see cref="FreeFonts"/>, which picking copies into
/// Assets, and the project's own font files), then, set apart with a warning, fonts only installed on this PC.
/// </summary>
public static class FontChoices
{
    /// <summary>Fonts that go with the game wherever it's played.</summary>
    public static NameGroup Safe { get; } = new("Safe to ship");

    /// <summary>Fonts on this PC only, which are never copied into a game.</summary>
    public static NameGroup Installed { get; } = new("Only on this PC",
        "Installed fonts aren't shipped: they may be missing on players' PCs, and in a browser the built-in font draws instead.");

    /// <summary>The fonts for a project whose Assets are <paramref name="assetRoot"/> (null for none).</summary>
    public static FontNames For(string? assetRoot)
    {
        var choices = new List<NameChoice>();
        var usable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var builtIn = BuiltInFonts.Default;
        choices.Add(new NameChoice(builtIn.Name, "built in", Safe));
        usable.Add(builtIn.Name);

        using var fonts = new ProjectFonts(assetRoot is null ? null : new Projects.AssetFolder(assetRoot));
        var files = fonts.Files();
        foreach (var free in FreeFonts.All)
        {
            var added = files.Contains(free.AssetPath, StringComparer.OrdinalIgnoreCase);
            choices.Add(new NameChoice(free.AssetPath, added ? $"{free.Kind}, free, in Assets" : $"{free.Kind}, free: copied into Assets",
                Safe, free.Name));
        }

        foreach (var file in files)
        {
            usable.Add(file);
            if (FreeFonts.Find(file) is null)
                choices.Add(new NameChoice(file, "in Assets", Safe, Path.GetFileNameWithoutExtension(file)));
        }

        foreach (var family in InstalledFonts.Families)
        {
            if (BuiltInFonts.Find(family) is not null)
                continue;
            choices.Add(new NameChoice(family, null, Installed));
            usable.Add(family);
        }

        return new FontNames(choices, usable);
    }

    /// <summary>
    /// What a Font naming <paramref name="font"/> draws in for the font picker's preview: a free font not in Assets yet
    /// from Griff's own copy, anything else as it names it.
    /// </summary>
    public static string? PreviewFont(string? font, string? assetRoot) =>
        FreeFonts.Find(font) is { } free && (assetRoot is null || !File.Exists(Path.Combine(assetRoot, free.AssetPath)))
            ? free.FilePath
            : font;
}
