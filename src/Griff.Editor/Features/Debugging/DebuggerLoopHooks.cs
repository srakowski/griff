using Griff.Debugging;
using Griff.Hosting;

namespace Griff.Editor.Features.Debugging;

/// <summary>
/// The editor's debugger in the loop of each game it plays (see <see cref="IGameLoopExtension"/>): a frame ending with an
/// exception asks it to hold the game where a script threw (<see cref="ScriptProbes.Fault"/>, which the
/// <see cref="Debugger"/> answers), a script stopped at a breakpoint leaves the frame with its game gone, and the time a
/// game stood at one isn't time it owes. An exported game's loop has none of this.
/// </summary>
internal sealed class DebuggerLoopHooks : IGameLoopExtension
{
    /// <summary>The one there is: it keeps nothing of its own (what it asks is the debugger's).</summary>
    public static DebuggerLoopHooks Instance { get; } = new();

    private DebuggerLoopHooks()
    {
    }

    public long Holds => ScriptProbes.Breaks;

    public FaultHold Fault(GameLoop loop, Exception exception) =>
        exception is ScriptAbortedException
            ? FaultHold.Abandoned
            : ScriptProbes.Fault(exception) switch
            {
                ScriptFaultHold.Resumed => FaultHold.Resumed,
                ScriptFaultHold.Abandoned => FaultHold.Abandoned,
                _ => FaultHold.NotHeld,
            };
}
