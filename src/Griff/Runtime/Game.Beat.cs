using Griff.Scenes;

namespace Griff.Runtime;

// Beat clocks: keeping time with the music, and things that bop to it.
public sealed partial class Game
{
    /// <summary>
    /// The clock something names (an entity with a Beat Clock), or the scene's first when it names none.
    /// </summary>
    public BeatClock? ClockFor(string? name) =>
        _beat.ClockFor(name);

    /// <summary>
    /// Whether something stepping to a clock every so many beats steps this frame.
    /// </summary>
    public bool OnBeat(string? clock, int every, int offset) =>
        _beat.OnBeat(clock, every, offset);
}
