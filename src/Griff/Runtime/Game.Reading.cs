
namespace Griff.Runtime;

// Reading dialogue: skipping through lines read before, reading on by itself and the logs of the lines said (see Backlog).
// Each member forwards to ReadingSystem; what the player prefers and has read is kept by a system that says so (see
// IReadingPreferences: the Visual Novels module's).
public sealed partial class Game
{
    /// <summary>
    /// True while dialogue skips: each line read before is shown and passed in a moment, until a line not read before
    /// (unless the player's setting goes through those too: see <see cref="IReadingPreferences"/>), choices or the end of the talk, which turn it off. A Toggle set to Skip, or a
    /// script, turns it on; a visual novel stage's Skip action skips while it's held.
    /// </summary>
    public bool Skipping
    {
        get => _reading.Skipping;
        set => _reading.Skipping = value;
    }

    /// <summary>
    /// True while dialogue reads on by itself: once a line has typed out, the next comes after a pause as long as the line
    /// (see <see cref="AutoDelay"/>). It waits at choices. A Toggle set to Auto, or a script, turns it on and off; so does
    /// a visual novel stage's Auto action. It stays on from one talk to the next.
    /// </summary>
    public bool AutoReading
    {
        get => _reading.AutoReading;
        set => _reading.AutoReading = value;
    }

    /// <summary>True while dialogue is skipping: <see cref="Skipping"/>, or a skip key held.</summary>
    public bool IsSkipping => _reading.IsSkipping;

    /// <summary>
    /// Set each frame by what reads a skip key (a visual novel stage's Skip action), while it's held.
    /// </summary>
    public bool SkipHeld
    {
        get => _reading.SkipHeld;
        set => _reading.SkipHeld = value;
    }

    /// <summary>
    /// Seconds <see cref="AutoReading"/> waits after <paramref name="said"/> has typed out before reading on: longer for
    /// a longer line, shorter the quicker the player likes it (see <see cref="IReadingPreferences.AutoSpeed"/>).
    /// </summary>
    public float AutoDelay(string? said) =>
        _reading.AutoDelay(said);
}
