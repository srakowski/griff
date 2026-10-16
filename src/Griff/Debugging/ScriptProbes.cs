using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Griff.Debugging;

/// <summary>
/// A line of a script reached with a breakpoint on it, in a build of the scripts with probes (see
/// <see cref="ScriptProbes"/>): which build (its table of probes, <paramref name="Probes"/>), which of its probes, the
/// object the code runs on (null in a static method or a lambda), the values of the variables in scope there, in the
/// order the build's table names them, and where on the thread's stack the line runs (<paramref name="Stack"/>: lower is
/// deeper, so a method a line calls runs lower than the line, and one it returns to higher; the same call of a method runs
/// all its lines at one place).
/// </summary>
public readonly record struct ScriptBreak(Type Probes, int Probe, object? This, object?[] Values, nint Stack = 0);

/// <summary>What holds a game at a breakpoint: the editor's debugger.</summary>
public interface IScriptBreakHandler
{
    /// <summary>
    /// A script reached a line with a breakpoint on: hold it there for as long as it should stay (or not at all, say when
    /// it isn't a game's frame running it). True stops the script there rather than letting it carry on (the game stopped
    /// or rewound while it was held), which ends the frame with a <see cref="ScriptAbortedException"/>.
    /// </summary>
    bool Break(in ScriptBreak hit);

    /// <summary>
    /// Whether a line whose switch is on stops (and <see cref="Break"/> is called): a breakpoint's line always does, but
    /// while the debugger steps every line's switch is on, and only the line the step ends at stops. Asked of every line
    /// reached while stepping, so it should be quick.
    /// </summary>
    bool Stops(in ScriptBreak hit) => true;

    /// <summary>
    /// A game's frame is ending with an exception (one a script threw, or one that came up through a script: see
    /// <see cref="ScriptProbes.ThrownAt"/>) that nothing in the game catches: hold the game there, before anything unwinds,
    /// for as long as it should stay, or not at all. Says whether it was held, and whether the game was stopped or rewound
    /// meanwhile.
    /// </summary>
    ScriptFaultHold Fault(Exception exception) => ScriptFaultHold.NotHeld;
}

/// <summary>What became of a game whose frame was ending with an exception (see <see cref="IScriptBreakHandler.Fault"/>).</summary>
public enum ScriptFaultHold
{
    /// <summary>It wasn't held: the game pauses on the error, as it does without a debugger.</summary>
    NotHeld,

    /// <summary>It was held, and let go: the exception ends the frame, and the game plays on or stays paused as it was left.</summary>
    Resumed,

    /// <summary>It was held, and stopped or rewound meanwhile: that game is gone.</summary>
    Abandoned,
}

/// <summary>
/// Where an exception came up through a script, in a build of the scripts with probes: the build (<paramref name="Probes"/>,
/// its class of switches), the guard that saw it first (see <see cref="ScriptProbes.Threw"/>), the object the code runs on
/// (null in a static method or a lambda), the values of the variables in scope there, in the order the build's guard names
/// them, and the method the guard is in.
/// </summary>
public sealed record ScriptThrow(Type Probes, int Guard, object? This, object?[] Values, MethodBase? Method);

/// <summary>
/// What a game stopped or rewound at a breakpoint ends its frame with: the scripts running are left where they are,
/// since that game is gone. A game's host catches it as the end of that frame, not as an error in the game.
/// </summary>
public sealed class ScriptAbortedException() : Exception("The game stopped at a breakpoint, so the script ran no further.");

/// <summary>
/// Breakpoints in scripts. The editor builds scripts with a probe at the start of each line of code, a test of a flag of
/// that line's own that is false until a breakpoint is put there, so a line without one costs a read of a flag that's
/// false, and an exported game has no probes at all. A line whose flag is on calls <see cref="Hit"/> with the values in
/// scope, which hands them to <see cref="Handler"/> to hold the game there. The build's guards note an exception coming up
/// through a script with the values in scope where it was thrown (<see cref="Threw"/>), and a game's host asks
/// <see cref="Fault"/> before an exception ends its frame, so the editor can hold the game at the line that threw. Not for
/// scripts to call.
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
    // Compiled once, fully (never again by tiering), so its frame is always the same size: the stack position it measures
    // stands for its caller's.
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    public static void Hit(Type probes, int probe, object? self, object?[] values)
    {
        if (t_handler is not { } handler || t_quiet > 0)
            return;
        var hit = new ScriptBreak(probes, probe, self, values, StackPosition());
        if (!handler.Stops(hit))
            return;

        // While it's held, the editor works on this thread: what it does mustn't draw on the game's random numbers or count
        // as a crowd's script, and scripts it runs (reading a property, say) don't stop again.
        var random = Rand.Use(GameRandom.Unseeded());
        var crowd = Crowds.SetAside();
        t_quiet++;
        bool abort;
        try
        {
            Interlocked.Increment(ref s_breaks);
            abort = handler.Break(hit);
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

    // Where on this thread's stack the caller of Hit is: the address of a local here, a fixed distance below Hit's caller's
    // frame (both this and Hit are compiled once, fully optimized, and never inlined).
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static nint StackPosition()
    {
        byte here = 0;
        return Unsafe.ByteOffset(ref Unsafe.NullRef<byte>(), ref here);
    }

    private static readonly MethodInfo s_hit = typeof(ScriptProbes).GetMethod(nameof(Hit))!;

    // ----- Exceptions -----

    private static int s_watchers;
    private static readonly ConditionalWeakTable<Exception, ScriptThrow> s_throws = [];

    /// <summary>
    /// Whether exceptions coming up through scripts are noted with the values in scope where they were thrown (see
    /// <see cref="Threw"/>): while anything watches them (<see cref="WatchThrows"/>), on every thread, so a crowd's scripts
    /// running across the cores are noted too.
    /// </summary>
    public static bool WatchesThrows => Volatile.Read(ref s_watchers) > 0;

    /// <summary>
    /// Starts watching exceptions (<see cref="WatchesThrows"/>), or with false stops, for one watcher (the editor's debugger
    /// while it pauses on them): each start is matched by a stop.
    /// </summary>
    public static void WatchThrows(bool watch)
    {
        if (watch)
            Interlocked.Increment(ref s_watchers);
        else
            Interlocked.Decrement(ref s_watchers);
    }

    /// <summary>
    /// The test a build's guard makes first: whether <paramref name="exception"/>, coming up through it, is still to be noted
    /// (exceptions are watched, it isn't noted yet, and it isn't a stopped game's).
    /// </summary>
    public static bool Watches(Exception exception) =>
        WatchesThrows && t_quiet == 0 && exception is not ScriptAbortedException && !s_throws.TryGetValue(exception, out _);

    /// <summary>
    /// A guard of the scripts' build (a filter around a stretch of a method's code, which catches nothing) saw
    /// <paramref name="exception"/> come up through it, before anything unwound: notes where, with the values in scope there,
    /// for the editor to show if it ends the game's frame. The first guard to see it is the innermost, so it's the one kept.
    /// Always false, so the exception goes on as if the guard weren't there.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Threw(Exception exception, Type probes, int guard, object? self, object?[] values)
    {
        // The method the guard is in: its filter runs as part of it.
        var method = new StackFrame(1, false).GetMethod();
        s_throws.TryAdd(exception, new ScriptThrow(probes, guard, self, values, method));
        return false;
    }

    /// <summary>Where a guard noted <paramref name="exception"/> coming up through a script (see <see cref="Threw"/>), or null.</summary>
    public static ScriptThrow? ThrownAt(Exception exception) => s_throws.TryGetValue(exception, out var thrown) ? thrown : null;

    private static readonly ConditionalWeakTable<Exception, object> s_routines = [];

    // A coroutine's routine (an iterator's object, whose fields are its variables) was running when `exception` came up
    // through it: a guard can't stand around a `yield return`, so its variables are read from there instead.
    internal static void ThrewInRoutine(Exception exception, object routine) => s_routines.TryAdd(exception, routine);

    /// <summary>
    /// The coroutine routine (an iterator's object) that was running when <paramref name="exception"/> came up through it,
    /// or null: its fields hold the routine's variables, and "this".
    /// </summary>
    public static object? RoutineOf(Exception exception) => s_routines.TryGetValue(exception, out var routine) ? routine : null;

    /// <summary>
    /// A game's frame (on this thread) is ending with <paramref name="exception"/>, which nothing in the game catches: called
    /// by its host before anything unwinds, so <see cref="Handler"/> can hold the game there. While it's held, as at a
    /// breakpoint, the editor's work on this thread draws on none of the game's random numbers, isn't a crowd's, and doesn't
    /// stop at breakpoints.
    /// </summary>
    public static ScriptFaultHold Fault(Exception exception)
    {
        if (t_handler is not { } handler || t_quiet > 0 || exception is ScriptAbortedException)
            return ScriptFaultHold.NotHeld;

        var random = Rand.Use(GameRandom.Unseeded());
        var crowd = Crowds.SetAside();
        t_quiet++;
        try
        {
            Interlocked.Increment(ref s_breaks);
            return handler.Fault(exception);
        }
        finally
        {
            t_quiet--;
            Crowds.Restore(crowd);
            Rand.Use(random);
            Interlocked.Increment(ref s_breaks);
        }
    }

    /// <summary>
    /// The methods running on this thread under the line of script that reached a probe, outermost first, ending with the
    /// one that line is in: for a handler (see <see cref="IScriptBreakHandler"/>) to tell whether a call it saw start is
    /// still under way. Empty when called other than from a handler.
    /// </summary>
    public static MethodBase?[] CallStack()
    {
        var frames = new StackTrace(1, false).GetFrames();
        var at = Array.FindIndex(frames, f => f.GetMethod() == s_hit);
        if (at < 0)
            return [];
        var methods = new MethodBase?[frames.Length - at - 1];
        for (var i = 0; i < methods.Length; i++)
            methods[i] = frames[frames.Length - 1 - i].GetMethod();
        return methods;
    }
}
