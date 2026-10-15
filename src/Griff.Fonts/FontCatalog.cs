using System.Diagnostics;

namespace Griff.Fonts;

/// <summary>
/// The one place a <see cref="Scenes.Text.Font"/> becomes a face: empty or its name for the built-in font
/// (<see cref="BuiltInFonts"/>), a font file in the project's Assets (<see cref="ProjectFonts"/>), or an installed font's
/// name (<see cref="InstalledFonts"/>, drawn on the computer it's on and never shipped). Whatever can't be found or read
/// draws in the built-in font.
/// </summary>
public sealed class FontCatalog
{
    private readonly Dictionary<string, FontFace> _faces = new(StringComparer.Ordinal);
    private readonly long _refreshTicks = Stopwatch.Frequency;
    private long _lastRefresh = Stopwatch.GetTimestamp();

    /// <param name="assetRoot">The project's Assets folder, which font files are found in; null for none.</param>
    public FontCatalog(string? assetRoot) => Project = new ProjectFonts(assetRoot);

    /// <summary>The font files in the project's Assets.</summary>
    public ProjectFonts Project { get; }

    /// <summary>The face <paramref name="font"/> names, or the built-in font's.</summary>
    public FontFace FaceFor(string? font)
    {
        if (string.IsNullOrWhiteSpace(font))
            return BuiltInFonts.Default.Face;
        if (!_faces.TryGetValue(font, out var face))
            _faces[font] = face = Resolve(font);
        return face;
    }

    /// <summary>
    /// Reads again the project's font files that changed (checked at most once a second), so editing a font updates the
    /// running game. True if one did.
    /// </summary>
    public bool RefreshChanged()
    {
        var now = Stopwatch.GetTimestamp();
        if (now - _lastRefresh < _refreshTicks)
            return false;
        _lastRefresh = now;
        if (!Project.RefreshChanged())
            return false;
        _faces.Clear();
        return true;
    }

    private FontFace Resolve(string font) =>
        (ProjectFonts.IsFontFile(font) ? Project.Find(font) : BuiltInFonts.Find(font)?.Face ?? InstalledFonts.Find(font)) ?? BuiltInFonts.Default.Face;
}
