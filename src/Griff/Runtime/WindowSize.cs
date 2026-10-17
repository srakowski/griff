using System.Globalization;

namespace Griff.Runtime;

/// <summary>
/// A size of the game's window in pixels, as a player picks it from an options menu (see <see cref="Game.WindowSize"/>):
/// written and read as "1280x720".
/// </summary>
public readonly record struct WindowSize(int Width, int Height)
{
    /// <summary>The size a built game's window opens at until the player picks one.</summary>
    public static WindowSize Default { get; } = new(1280, 720);

    /// <summary>
    /// The common 16:9 sizes, smallest first, that <see cref="Fitting"/> offers from: from 960x540 to 3840x2160.
    /// </summary>
    public static IReadOnlyList<WindowSize> Common { get; } =
    [
        new(960, 540), new(1024, 576), new(1152, 648), new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080),
        new(2560, 1440), new(3200, 1800), new(3840, 2160),
    ];

    /// <summary>Whether it's a size a window can be: both sides at least a pixel.</summary>
    public bool IsValid => Width > 0 && Height > 0;

    /// <summary>"1280x720".</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}");

    /// <summary>Reads "1280x720" (an "x" or "×" between, spaces allowed), or says it can't.</summary>
    public static bool TryParse(string? text, out WindowSize size)
    {
        size = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var parts = text.Split(['x', 'X', '×'], 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height))
            return false;
        size = new WindowSize(width, height);
        return size.IsValid;
    }

    /// <summary>
    /// The <see cref="Common"/> sizes that fit in <paramref name="room"/> (the most a window's game can show on the screen:
    /// its work area less the window's frame), smallest first; at least the smallest, however little room there is. With
    /// no room given (a host that can't say, a browser), those up to 1920x1080.
    /// </summary>
    public static IReadOnlyList<WindowSize> Fitting(WindowSize? room)
    {
        var (width, height) = room is { IsValid: true } given ? (given.Width, given.Height) : (1920, 1080);
        var fitting = Common.Where(s => s.Width <= width && s.Height <= height).ToList();
        if (fitting.Count == 0)
            fitting.Add(Common[0]);
        return fitting;
    }
}
