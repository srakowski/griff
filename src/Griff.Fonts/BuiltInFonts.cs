namespace Griff.Fonts;

/// <summary>
/// One of the fonts built into Griff.Fonts, which every game carries (inside this assembly): its name, what it is, and
/// its license's text, which every export's third-party notices carry.
/// </summary>
public sealed class BuiltInFont
{
    private readonly Lazy<FontFace> _face;

    internal BuiltInFont(string name, string fileName, string about, string licenseFile)
    {
        Name = name;
        About = about;
        License = Read(licenseFile);
        _face = new Lazy<FontFace>(() => FontFace.Embedded(fileName));
    }

    /// <summary>The font's name, e.g. "Selawik Semibold".</summary>
    public string Name { get; }

    /// <summary>What it's for, who made it and under which license.</summary>
    public string About { get; }

    /// <summary>The text of its license.</summary>
    public string License { get; }

    /// <summary>The font, read the first time it's asked for.</summary>
    public FontFace Face => _face.Value;

    private static string Read(string fileName)
    {
        var name = "Griff.Fonts." + fileName;
        using var stream = typeof(BuiltInFont).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"{name} isn't in Griff.Fonts.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// The fonts built into Griff, the only fonts it ships in games of its own accord: each may be shipped and embedded in
/// games under its license, and goes with its license's text.
/// </summary>
/// <remarks>
/// Selawik Semibold is Microsoft's file as released (1.01), unmodified: its license (the SIL Open Font License 1.1)
/// reserves the name Selawik, so it's never changed or cut down. The fallback is DejaVu Sans Bold 2.37 cut down to the
/// blocks the built-in font lacks (Latin Extended, IPA, Greek, Cyrillic, punctuation, arrows, math, box drawing, shapes,
/// symbols and dingbats) with fontTools, and renamed "DejaVu Sans Griff Subset" in its name table, since Bitstream
/// Vera's license lets a modified font be shared only under a name without "Bitstream" or "Vera" (and Arev's without
/// "Arev" or "Tavmjong Bah"); its copyright notices are kept.
/// </remarks>
public static class BuiltInFonts
{
    /// <summary>The font text is drawn in when it names none: Selawik Semibold.</summary>
    public static BuiltInFont Default { get; } = new("Selawik Semibold", "Selawik-Semibold.ttf",
        "Selawik Semibold (the built-in font), by Microsoft, under the SIL Open Font License 1.1", "Selawik-LICENSE.txt");

    /// <summary>Where characters a font hasn't got come from, after the default font: a part of DejaVu Sans Bold.</summary>
    public static BuiltInFont Fallback { get; } = new("DejaVu Sans Griff Subset", "DejaVuSans-Bold-Subset.ttf",
        "DejaVu Sans Bold, a subset renamed DejaVu Sans Griff Subset (the fallback for symbols, Greek and Cyrillic), " +
        "under the Bitstream Vera and Arev fonts licenses, with DejaVu's changes in the public domain", "DejaVu-LICENSE.txt");

    /// <summary>Every built-in font.</summary>
    public static IReadOnlyList<BuiltInFont> All { get; } = [Default, Fallback];
}
