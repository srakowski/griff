using System.ComponentModel;

namespace Griff.Debugging;

/// <summary>
/// A line of a script reached with a breakpoint on it, in a build of the scripts with probes (see
/// <see cref="ScriptProbes"/>): which build (its table of probes, <paramref name="Probes"/>), which of its probes, the
/// object the code runs on (null in a static method or a lambda) and the values of the variables in scope there, in the
/// order the build's table names them.
/// </summary>
public readonly record struct ScriptBreak(Type Probes, int Probe, object? This, object?[] Values);

/// <summary>What holds a game at a breakpoint: the editor's debugger.</summary>
public interface IScriptBreakHandler
{
    /// <summary>
    /// A script reached a line with a breakpoint on: hold it there for as long as it should stay (or not at all, say when
    /// it isn't a game's frame running it). True stops the script there rather than letting it carry on (the game stopped
    /// or rewound while it was held), which ends the frame with a <see cref="ScriptAbortedException"/>.
    /// </summary>
    bool Break(in ScriptBreak hit);
}

/// <summary>
/// What a game stopped or rewound at a breakpoint ends its frame with: the scripts running are left where they are,
/// since that game is gone. A game's host catches it as the end of that frame, not as an error in the game.
/// </summary>
public sealed class ScriptAbortedException() : Exception("The game stopped at a breakpoint, so the script ran no further.");

/// <summary>
/// Breakpoints in scripts. The editor builds scripts with a probe at the start of each line of code, a test of a flag of
/// that line's own that is false until a breakpoint is put there, so a line without one costs a read of a flag that's
/// false, and an exported game has no probes at all. A line whose flag is on calls <see cref="Hit"/> with the values in
/// scope, which hands them to <see cref="Handler"/> to hold the game there. Not for scripts to call.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ScriptProbes
{
    // Code the host runs on a thread while a script is held there (or while the host reads a game for itself) doesn't stop
    // at breakpoints of its own.
    [ThreadStatic]
    private static int t_quiet;

    private static long s_breaks;

    [ThreadStatic]
    private static IScriptBreakHandler? t_handler;

    [ThreadStatic]
    private static bool t_armed;

    /// <summary>
    /// What a breakpoint reached on this thread calls: the editor's debugger, for the games it runs on its own thread (a
    /// script on another thread doesn't stop), or null for nothing.
    /// </summary>
    public static IScriptBreakHandler? Handler
    {
        get => t_handler;
        set => t_handler = value;
    }

    /// <summary>
    /// True while a breakpoint is set for this thread's games. A crowd's scripts (see <see cref="Runtime.CrowdAttribute"/>)
    /// then run on the game's thread, where a breakpoint can hold them, rather than across the cores; what they do is the
    /// same either way.
    /// </summary>
    public static bool Armed
    {
        get => t_armed;
        set => t_armed = value;
    }

    /// <summary>
    /// How many times a game has been held at a breakpoint so far: a host timing its frames sees the count change across a
    /// frame that was held, and doesn't count the time it stood still.
    /// </summary>
    public static long Breaks => Interlocked.Read(ref s_breaks);

    /// <summary>True while this thread runs code that doesn't stop at breakpoints (see <see cref="Quiet"/>).</summary>
    public static bool IsQuiet => t_quiet > 0;

    /// <summary>
    /// Until the result is disposed, scripts this thread runs don't stop at breakpoints: for a host reading the game for
    /// itself (a rewind snapshot reading a script's properties) rather than playing it.
    /// </summary>
    public static QuietScope Quiet()
    {
        t_quiet++;
        return new QuietScope();
    }

    /// <summary>Ends a <see cref="Quiet"/> stretch.</summary>
    public readonly struct QuietScope : IDisposable
    {
        public void Dispose() => t_quiet--;
    }

    /// <summary>
    /// A probe whose line has a breakpoint: called by the scripts' build with the values in scope there. Holds the game
    /// (through <see cref="Handler"/>) for as long as it says; throws <see cref="ScriptAbortedException"/> if the game was
    /// stopped meanwhile.
    /// </summary>
    public static void Hit(Type probes, int probe, object? self, object?[] values)
    {
        if (t_handler is not { } handler || t_quiet > 0)
            return;

        // While it's held, the editor works on this thread: what it does mustn't draw on the game's random numbers or count
        // as a crowd's script, and scripts it runs (reading a property, say) don't stop again.
        var random = Rand.Use(null);
        var crowd = Crowds.SetAside();
        t_quiet++;
        bool abort;
        try
        {
            Interlocked.Increment(ref s_breaks);
            abort = handler.Break(new ScriptBreak(probes, probe, self, values));
        }
        finally
        {
            t_quiet--;
            Crowds.Restore(crowd);
            Rand.Use(random);
            Interlocked.Increment(ref s_breaks);
        }

        if (abort)
            throw new ScriptAbortedException();
    }
}
