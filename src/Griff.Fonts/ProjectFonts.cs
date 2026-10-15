namespace Griff.Fonts;

/// <summary>
/// The font files in a project's Assets folder (.ttf, .otf or .ttc), which ship with its game: the game maker's own to
/// license. Each is read the first time it's named and again when the file changes.
/// </summary>
public sealed class ProjectFonts(string? assetRoot)
{
    private static readonly string[] Extensions = [".ttf", ".otf", ".ttc"];

    private readonly Dictionary<string, FontFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FontFile> _all = [];

    /// <summary>The Assets folder the files are in; null for none.</summary>
    public string? AssetRoot { get; } = assetRoot;

    /// <summary>Whether a font name is a font file (rather than an installed font's name).</summary>
    public static bool IsFontFile(string name) => Extensions.Any(e => name.Trim().EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>The font files in the Assets folder, relative to it with '/' between folders, in order.</summary>
    public IReadOnlyList<string> Files() =>
        AssetRoot is null || !Directory.Exists(AssetRoot)
            ? []
            : Directory.EnumerateFiles(AssetRoot, "*", SearchOption.AllDirectories)
                .Where(IsFontFile)
                .Select(f => Path.GetRelativePath(AssetRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>The face in a font file (relative to Assets), or null if it's missing, mid-save or not a font.</summary>
    public FontFace? Find(string path)
    {
        if (!_files.TryGetValue(path, out var file))
        {
            file = new FontFile(AssetRoot is null ? null : Path.GetFullPath(Path.Combine(AssetRoot, path.Trim().Replace('\\', '/'))));
            file.Load();
            _files[path] = file;
            _all.Add(file);
        }

        return file.Face;
    }

    /// <summary>Reads again the files named so far that changed since they were read. True if one did.</summary>
    public bool RefreshChanged()
    {
        var changed = false;

        // By index: going through the dictionary would make garbage.
        for (var i = 0; i < _all.Count; i++)
        {
            var file = _all[i];
            if (file.Path is not null && file.Stamp != FontFile.StampOf(file.Path))
            {
                file.Load();
                changed = true;
            }
        }

        return changed;
    }

    // A font file, and when it was written, so a change reloads it.
    private sealed class FontFile(string? path)
    {
        public string? Path { get; } = path;

        public DateTime Stamp { get; private set; }

        public FontFace? Face { get; private set; }

        public static DateTime StampOf(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

        public void Load()
        {
            Face = null;
            if (Path is null)
                return;
            Stamp = StampOf(Path);
            if (Stamp == DateTime.MinValue)
                return;
            try
            {
                Face = FontFace.Load(File.ReadAllBytes(Path), Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
