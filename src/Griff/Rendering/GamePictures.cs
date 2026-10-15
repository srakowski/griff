namespace Griff.Rendering;

/// <summary>
/// Pictures a running game makes for itself rather than reads from its files, such as the screenshots of its saved games
/// (see <see cref="Runtime.Game.SaveGame"/>), drawn by naming them where an image's path goes: their names start with
/// <see cref="Mark"/> ("@save/1"). Each frame carries its game's (see <see cref="RenderFrame.Pictures"/>), and a renderer
/// loads one again whenever it changes.
/// </summary>
public sealed class GamePictures
{
    /// <summary>What a picture's name starts with, so no file in Assets is taken for one.</summary>
    public const char Mark = '@';

    private readonly Dictionary<string, (byte[] Png, int Version)> _pictures = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private int _version;

    /// <summary>True if <paramref name="name"/> names one of a game's pictures rather than a file.</summary>
    public static bool IsPicture(string? name) => name is { Length: > 1 } && name[0] == Mark;

    /// <summary>Gives <paramref name="name"/> the PNG <paramref name="png"/>, replacing what it had; null takes it away.</summary>
    public void Set(string name, byte[]? png)
    {
        lock (_lock)
        {
            if (png is null)
                _pictures.Remove(name);
            else
                _pictures[name] = (png, ++_version);
        }
    }

    /// <summary>
    /// The PNG called <paramref name="name"/> and a number that changes whenever it does (0, with no PNG, for none).
    /// </summary>
    public (byte[]? Png, int Version) Get(string name)
    {
        lock (_lock)
            return _pictures.TryGetValue(name, out var picture) ? picture : (null, 0);
    }

    /// <summary>The names of every picture, for a test or a look at what's there.</summary>
    public IReadOnlyList<string> Names
    {
        get
        {
            lock (_lock)
                return [.. _pictures.Keys];
        }
    }
}
