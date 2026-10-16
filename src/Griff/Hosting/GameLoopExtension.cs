namespace Griff.Hosting;

/// <summary>What became of a game whose frame was ending with an exception (see <see cref="IGameLoopExtension.Fault"/>).</summary>
public enum FaultHold
{
    /// <summary>It wasn't held: the game pauses on the error.</summary>
    NotHeld,

    /// <summary>It was held, and let go: the exception ends the frame, and the game plays on or stays paused as it was left.</summary>
    Resumed,

    /// <summary>It was held, and stopped or rewound meanwhile (or stopped at a breakpoint): that game is gone.</summary>
    Abandoned,
}

/// <summary>
/// Something a host adds to the loop playing its game (<see cref="GameLoop.Add"/>) to watch or steer its frames: the editor
/// adds its debugger, which holds a game where a script stopped at a breakpoint or threw, and an agent's playtest pointer.
/// They're the editor's, so a shipped game's loop has none and its player carries none of their code.
/// </summary>
public interface IGameLoopExtension
{
    /// <summary>
    /// How many times it has held a game so far: a frame across which this changes stood still (a breakpoint), so the time
    /// it stood isn't time the game owes, and the frame isn't timed.
    /// </summary>
    long Holds => 0;

    /// <summary>
    /// Asked before each frame's steps, once the mouse and fingers are in the world: what the game reads this frame may be
    /// set here (where an agent points the mouse).
    /// </summary>
    void BeforeSteps(GameLoop loop)
    {
    }

    /// <summary>The real mouse moved over the view (see <see cref="GameLoop.PointAt"/>).</summary>
    void PointerMoved(GameLoop loop)
    {
    }

    /// <summary>
    /// Gameplay code threw <paramref name="exception"/>, ending the frame, and nothing in the game caught it: asked before
    /// anything unwinds, so the game can be held where it was thrown, for as long as it should stay. Says whether it was, and
    /// whether the game was stopped or rewound meanwhile.
    /// </summary>
    FaultHold Fault(GameLoop loop, Exception exception) => FaultHold.NotHeld;
}
