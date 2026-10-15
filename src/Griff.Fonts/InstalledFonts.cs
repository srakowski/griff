using System.Collections.Concurrent;
using System.Text;

namespace Griff.Fonts;

/// <summary>
/// Fonts installed on this computer, found by family name (a <see cref="Scenes.Text.Font"/> such as "Consolas"): the
/// files in Windows' font folders, the machine's and the user's. Elsewhere (the browser) there are none, and text in an
/// installed font draws in the built-in one. They're drawn on the computer they're on and never copied anywhere: not into a
/// project, an export or anything baked.
/// </summary>
public static class InstalledFonts
{
    // Weight the built-in font has, and the one an installed family's face is picked nearest to (semibold).
    private const int Weight = 600;

    private static readonly Lazy<Dictionary<string, List<Candidate>>> s_families = new(Scan);
    private static readonly ConcurrentDictionary<string, FontFace?> s_faces = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<IReadOnlyList<string>> s_sorted = new(() => s_families.Value.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList());
    private static readonly ConcurrentDictionary<int, FontFace?> s_fallbacks = new();
    private static readonly ConcurrentDictionary<string, System.Collections.BitArray?> s_coverage = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<IReadOnlyList<string>> s_fallbackFamilies = new(FallbackOrder);

    /// <summary>The installed font families, by name, in order (read from the folders the first time it's asked).</summary>
    public static IReadOnlyList<string> Families => s_sorted.Value;

    /// <summary>
    /// The installed font to draw <paramref name="codepoint"/> in when the text's font, the built-in font and its fallback
    /// haven't got it (Chinese, Japanese, Korean, emoji, scripts from Arabic to Thai), as WPF fell back to Windows' fonts:
    /// the first of Windows' own fonts for those (<see cref="FallbackFamilies"/>) that's installed and has it, or null.
    /// Only ever on the player's own computer: in a browser there are none, and nothing is copied or kept.
    /// </summary>
    public static FontFace? Fallback(int codepoint) => s_fallbacks.GetOrAdd(codepoint, c =>
    {
        // Which characters a family has is indexed once (a bit each, 25 KB) with its face let go again; only a family that has
        // the character is kept loaded, so a character nobody has doesn't leave every fallback font in memory (~100 MB).
        foreach (var family in s_fallbackFamilies.Value)
        {
            if (Covers(family, c) && Find(family) is { } face)
                return face;
        }

        return null;
    });

    private const int IndexedCodepoints = 0x32400; // through the planes with assigned characters

    private static bool Covers(string family, int codepoint)
    {
        var coverage = s_coverage.GetOrAdd(family.Trim(), name =>
        {
            if (s_faces.TryGetValue(name, out var kept) && kept is not null)
                return Index(kept);
            return Load(name) is { } face ? Index(face) : null;
        });
        return coverage is not null && codepoint >= 0 && codepoint < IndexedCodepoints && coverage[codepoint];
    }

    private static System.Collections.BitArray Index(FontFace face)
    {
        var bits = new System.Collections.BitArray(IndexedCodepoints);
        for (var c = 0; c < IndexedCodepoints; c++)
            bits[c] = face.GlyphOf(c) > 0;
        return bits;
    }

    /// <summary>How many installed fonts are loaded and kept now (the ones asked for by name, and the fallbacks that drew a character).</summary>
    public static int LoadedFaces => s_faces.Count(f => f.Value is not null);

    /// <summary>
    /// The installed fonts characters are looked for in, in order: Windows' fonts for the scripts and symbols the built-in
    /// fonts lack, the one for the computer's own language first among Chinese, Japanese and Korean.
    /// </summary>
    public static IReadOnlyList<string> FallbackFamilies => s_fallbackFamilies.Value;

    private static IReadOnlyList<string> FallbackOrder()
    {
        string[] cjk = ["Microsoft YaHei", "Yu Gothic", "Malgun Gothic", "Microsoft JhengHei"];
        var language = System.Globalization.CultureInfo.CurrentUICulture.Name;
        var first = language.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "Yu Gothic"
            : language.StartsWith("ko", StringComparison.OrdinalIgnoreCase) ? "Malgun Gothic"
            : language is "zh-TW" or "zh-HK" or "zh-MO" || language.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ? "Microsoft JhengHei"
            : "Microsoft YaHei";
        return
        [
            "Segoe UI", "Segoe UI Symbol", "Segoe UI Emoji", "Segoe UI Historic",
            first, .. cjk.Where(f => f != first),
            "Nirmala UI", "Leelawadee UI", "Ebrima", "Gadugi", "Javanese Text", "Myanmar Text", "Mongolian Baiti",
            "Microsoft Himalaya", "Microsoft Yi Baiti", "Microsoft New Tai Lue", "Microsoft Tai Le", "Microsoft PhagsPa",
            "Sylfaen", "SimSun", "SimSun-ExtB", "Arial Unicode MS",
        ];
    }

    /// <summary>
    /// The installed family's face nearest semibold and upright (heavier wins a tie), or null if no family has that name.
    /// The folders are read the first time a name is asked for.
    /// </summary>
    public static FontFace? Find(string family) => s_faces.GetOrAdd(family.Trim(), Load);

    private static FontFace? Load(string name)
    {
        if (!s_families.Value.TryGetValue(name, out var candidates))
            return null;
        foreach (var candidate in candidates.OrderBy(c => c.Italic).ThenBy(c => Math.Abs(c.Weight - Weight)).ThenByDescending(c => c.Weight))
        {
            try
            {
                if (FontFace.Load(File.ReadAllBytes(candidate.Path), candidate.Path) is { } face)
                    return face;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private sealed record Candidate(string Path, int Weight, bool Italic);

    private static Dictionary<string, List<Candidate>> Scan()
    {
        var families = new Dictionary<string, List<Candidate>>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
            return families;
        string[] folders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
        ];
        foreach (var folder in folders.Where(f => f.Length > 0 && Directory.Exists(f)))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder).Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                                                    f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
                                                                    f.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (Describe(file) is not var (names, weight, italic))
                    continue;
                foreach (var name in names)
                {
                    if (!families.TryGetValue(name, out var list))
                        families[name] = list = [];
                    list.Add(new Candidate(file, weight, italic));
                }
            }
        }

        return families;
    }

    // A font file's family names (the plain one and the typographic one, in any language) and its weight and slant, read
    // from its first face's name and OS/2 tables without loading the rest.
    private static (List<string> Names, int Weight, bool Italic)? Describe(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var reader = new BinaryReader(file);
            uint start = 0;
            if (Tag(reader) == "ttcf")
            {
                file.Position = 12;
                start = ReadUInt(reader);
            }

            file.Position = start + 4;
            var tables = ReadUShort(reader);
            uint name = 0, os2 = 0;
            for (var i = 0; i < tables; i++)
            {
                file.Position = start + 12 + i * 16;
                var tag = Tag(reader);
                ReadUInt(reader);
                var offset = ReadUInt(reader);
                if (tag == "name")
                    name = offset;
                else if (tag == "OS/2")
                    os2 = offset;
            }

            if (name == 0)
                return null;
            var weight = 400;
            var italic = false;
            if (os2 != 0)
            {
                file.Position = os2 + 4;
                weight = ReadUShort(reader);
                file.Position = os2 + 62;
                italic = (ReadUShort(reader) & 1) != 0;
            }

            file.Position = name + 2;
            var count = ReadUShort(reader);
            var strings = name + ReadUShort(reader);
            var names = new List<string>();
            for (var i = 0; i < count; i++)
            {
                file.Position = name + 6 + i * 12;
                var platform = ReadUShort(reader);
                ReadUShort(reader);
                ReadUShort(reader);
                var id = ReadUShort(reader);
                var length = ReadUShort(reader);
                var offset = ReadUShort(reader);
                if (id is not (1 or 16) || platform is not (0 or 3))
                    continue;
                file.Position = strings + offset;
                var text = Encoding.BigEndianUnicode.GetString(reader.ReadBytes(length)).Trim();
                if (text.Length > 0 && !names.Contains(text, StringComparer.OrdinalIgnoreCase))
                    names.Add(text);
            }

            return (names, weight, italic);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string Tag(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));

    private static int ReadUShort(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(2);
        if (bytes.Length < 2)
            throw new EndOfStreamException();
        return (bytes[0] << 8) | bytes[1];
    }

    private static uint ReadUInt(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (bytes.Length < 4)
            throw new EndOfStreamException();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }
}
