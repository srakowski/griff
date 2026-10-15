using System.Security.Cryptography;

namespace Griff.Fonts;

/// <summary>
/// One of the free fonts that ship with Griff for games (<see cref="FreeFonts"/>): what it is, where it came from and
/// under which license. It isn't inside any game until it's picked: then it and its license are copied into the
/// project's Assets (<see cref="AddTo"/>), and the game ships it as one of the project's font files.
/// </summary>
public sealed class FreeFont
{
    internal FreeFont(string name, string kind, string fileName, string licenseFile, string by, string version, string source, string sha256)
    {
        Name = name;
        Kind = kind;
        FileName = fileName;
        LicenseFile = licenseFile;
        By = by;
        Version = version;
        Source = source;
        Sha256 = sha256;
    }

    /// <summary>Its name, e.g. "Press Start 2P".</summary>
    public string Name { get; }

    /// <summary>What kind of font it is: "pixel", "sans", "serif" or "mono".</summary>
    public string Kind { get; }

    /// <summary>The font file, as released (e.g. "PressStart2P-Regular.ttf").</summary>
    public string FileName { get; }

    /// <summary>Its license's file, beside it (e.g. "PressStart2P-OFL.txt").</summary>
    public string LicenseFile { get; }

    /// <summary>Who made it.</summary>
    public string By { get; }

    /// <summary>The version of the file (from its name table).</summary>
    public string Version { get; }

    /// <summary>Where the file was downloaded from, unchanged.</summary>
    public string Source { get; }

    /// <summary>The license: every one is the SIL Open Font License 1.1.</summary>
    public string License => "SIL Open Font License 1.1";

    /// <summary>The SHA-256 of the file as released, which a copy in a project's Assets is recognized by.</summary>
    public string Sha256 { get; }

    /// <summary>Where it goes in a project's Assets once it's picked: "Fonts/PressStart2P-Regular.ttf".</summary>
    public string AssetPath => FreeFonts.AssetFolder + "/" + FileName;

    /// <summary>Its license's place in Assets, beside it.</summary>
    public string LicenseAssetPath => FreeFonts.AssetFolder + "/" + LicenseFile;

    /// <summary>The font file Griff carries (in <see cref="FreeFonts.Folder"/>).</summary>
    public string FilePath => Path.Combine(FreeFonts.Folder, FileName);

    /// <summary>The license file Griff carries beside it.</summary>
    public string LicensePath => Path.Combine(FreeFonts.Folder, LicenseFile);

    /// <summary>What it is, for third-party notices: "Press Start 2P, by CodeMan38, under the SIL Open Font License 1.1".</summary>
    public string About => $"{Name} {Version} ({Kind} font), by {By}, under the {License}";

    /// <summary>
    /// Copies the font and its license into <paramref name="assetRoot"/>'s <see cref="FreeFonts.AssetFolder"/> (unless
    /// they're there already) and returns the font's path in Assets, which a Text's Font is set to. A different file
    /// already at that path is left alone and throws.
    /// </summary>
    public string AddTo(string assetRoot)
    {
        var folder = Directory.CreateDirectory(Path.Combine(assetRoot, FreeFonts.AssetFolder)).FullName;
        var font = Path.Combine(folder, FileName);
        if (!File.Exists(font))
            File.Copy(FilePath, font);
        else if (!IsCopy(font))
            throw new IOException($"Assets already has a different {FreeFonts.AssetFolder}/{FileName}; rename or move it to add {Name}.");
        var license = Path.Combine(folder, LicenseFile);
        if (!File.Exists(license))
            File.Copy(LicensePath, license);
        return AssetPath;
    }

    /// <summary>True when <paramref name="file"/> is this font, byte for byte (an unchanged copy).</summary>
    public bool IsCopy(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || (File.Exists(FilePath) && new FileInfo(FilePath).Length != info.Length))
                return false;
            using var stream = info.OpenRead();
            return Convert.ToHexStringLower(SHA256.HashData(stream)) == Sha256;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public override string ToString() => Name;
}

/// <summary>
/// The free fonts that ship with Griff for games: a pixel font, a clean sans, a serif and a mono, each under the SIL Open
/// Font License 1.1 (which lets them be embedded in and shipped with games, sold or not), unmodified as released, so their
/// Reserved Font Names are respected, and with their licenses. They sit beside the editor (<see cref="Folder"/>, from
/// <c>src/Griff.Fonts/Free</c>, whose README says where each came from) rather than inside games: picking one copies it
/// and its license into the project's Assets, and from there the game ships it like any of the project's fonts, and its
/// export's third-party notices name it.
/// </summary>
public static class FreeFonts
{
    /// <summary>The folder in a project's Assets that a picked free font is copied into.</summary>
    public const string AssetFolder = "Fonts";

    /// <summary>Where Griff's copies are: the FreeFonts folder beside the editor (or the tests).</summary>
    public static string Folder { get; set; } = Path.Combine(AppContext.BaseDirectory, "FreeFonts");

    /// <summary>A pixel font: Press Start 2P, after the arcade games of the 1980s.</summary>
    public static FreeFont Pixel { get; } = new("Press Start 2P", "pixel", "PressStart2P-Regular.ttf", "PressStart2P-OFL.txt",
        "CodeMan38 (Cody \"CodeMan38\" Boisclair)", "3.000",
        "https://github.com/google/fonts/blob/67bc647ce1910e92f461bcc82aae30f21d21a9cc/ofl/pressstart2p/PressStart2P-Regular.ttf",
        "034c77f1f05ec89421e4a63f0e3a4ca1ecf852cc6d2bf611f126f275728e017d");

    /// <summary>A clean sans: Atkinson Hyperlegible Bold, made by the Braille Institute to be easy to read.</summary>
    public static FreeFont Sans { get; } = new("Atkinson Hyperlegible Bold", "sans", "AtkinsonHyperlegible-Bold.ttf", "AtkinsonHyperlegible-OFL.txt",
        "the Braille Institute of America", "1.006",
        "https://github.com/google/fonts/blob/67bc647ce1910e92f461bcc82aae30f21d21a9cc/ofl/atkinsonhyperlegible/AtkinsonHyperlegible-Bold.ttf",
        "5a3b0c8cc8ca545155150b4512a1fa248298df121c50d6557e651e61fbdab92f");

    /// <summary>A serif: Crimson Text SemiBold, an old-style book face by Sebastian Kosch.</summary>
    public static FreeFont Serif { get; } = new("Crimson Text SemiBold", "serif", "CrimsonText-SemiBold.ttf", "CrimsonText-OFL.txt",
        "Sebastian Kosch (The Crimson Text Project Authors)", "1.100",
        "https://github.com/google/fonts/blob/67bc647ce1910e92f461bcc82aae30f21d21a9cc/ofl/crimsontext/CrimsonText-SemiBold.ttf",
        "802e84000740fec2a9fbe0ae09b6b6811bd86a78a0173b15d44450a1530e9410");

    /// <summary>A mono: JetBrains Mono Bold, which the script editor is set in too.</summary>
    public static FreeFont Mono { get; } = new("JetBrains Mono Bold", "mono", "JetBrainsMono-Bold.ttf", "JetBrainsMono-OFL.txt",
        "JetBrains (The JetBrains Mono Project Authors)", "2.304",
        "https://github.com/JetBrains/JetBrainsMono/releases/download/v2.304/JetBrainsMono-2.304.zip (fonts/ttf/JetBrainsMono-Bold.ttf)",
        "5590990c82e097397517f275f430af4546e1c45cff408bde4255dad142479dcb");

    /// <summary>Every free font, in the order the font picker lists them.</summary>
    public static IReadOnlyList<FreeFont> All { get; } = [Pixel, Sans, Serif, Mono];

    /// <summary>The free font called <paramref name="name"/> (its name, file name or path in Assets, any case), or null.</summary>
    public static FreeFont? Find(string? name)
    {
        var text = name?.Trim().Replace('\\', '/');
        if (string.IsNullOrEmpty(text))
            return null;
        return All.FirstOrDefault(f => f.Name.Equals(text, StringComparison.OrdinalIgnoreCase) ||
                                       f.FileName.Equals(text, StringComparison.OrdinalIgnoreCase) ||
                                       f.AssetPath.Equals(text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The free font <paramref name="file"/> is an unchanged copy of (by its SHA-256; its name doesn't matter), or null.</summary>
    public static FreeFont? CopyOf(string file)
    {
        return All.FirstOrDefault(f => f.IsCopy(file));
    }
}
