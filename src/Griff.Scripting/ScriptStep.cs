using System.Reflection;

namespace Griff.Scripting;

/// <summary>How the debugger steps on from the line a script stopped at.</summary>
public enum StepKind
{
    /// <summary>To the next line of script that runs: into a method of a script the line calls.</summary>
    Into,

    /// <summary>To the next line of this method, running what the line calls without stopping in it.</summary>
    Over,

    /// <summary>Out of this method: to the next line of the one that called it.</summary>
    Out,
}

/// <summary>
/// One step of the debugger, from the line a script stopped at: while it's under way every line's probe is on, and this
/// says at each line a script reaches whether the step ends there. Calls are told apart by where on the thread's stack a
/// line runs (see <see cref="Griff.Debugging.ScriptBreak.Stack"/>): a method the line calls runs deeper, the one it returns
/// to higher, and a call of a method runs all its lines at one place. A step that leaves the scripts (its method returns
/// into Griff) ends at the next line of script that runs, wherever that is; one that doesn't come to a line of script
/// before the frame ends is ended by the debugger there.
/// </summary>
public sealed class ScriptStep
{
    private readonly ScriptProbe? _from;
    private readonly nint _stack;
    private readonly MethodBase?[] _callers;
    // Places deeper than the step's own method found to be inside a call it made (by function, and where it runs).
    private readonly HashSet<(string Path, int Function, nint Stack)> _inside = [];

    /// <param name="kind">Into, over or out.</param>
    /// <param name="from">The line it stopped at, or null when the game stands between frames (every step is then Into).</param>
    /// <param name="stack">Where that line ran (see <see cref="Griff.Debugging.ScriptBreak.Stack"/>).</param>
    /// <param name="callers">The methods running under that line, outermost first, ending with its own (see <see cref="Griff.Debugging.ScriptProbes.CallStack"/>).</param>
    public ScriptStep(StepKind kind, ScriptProbe? from, nint stack, MethodBase?[] callers)
    {
        Kind = from is null ? StepKind.Into : kind;
        _from = from;
        _stack = stack;
        _callers = callers;
    }

    public StepKind Kind { get; }

    /// <summary>The line it steps from, or null for a step from between frames.</summary>
    public ScriptProbe? From => _from;

    /// <summary>
    /// Whether the step ends at <paramref name="probe"/>, a line reached at <paramref name="stack"/>. <paramref name="callers"/>
    /// gives the methods running under it (only asked for when where it runs can't say, and not again for the same place).
    /// </summary>
    public bool EndsAt(ScriptProbe probe, nint stack, Func<MethodBase?[]> callers)
    {
        if (Kind == StepKind.Into || _from is null)
            return true;

        // Higher than the method stepped from: it returned, to a script that called it or into Griff.
        if (stack > _stack)
            return true;

        var same = probe.InSameFunction(_from);
        if (stack == _stack)
        {
            // A call beginning where the method stepped from ran: that one returned, and something else is called.
            if (probe.Entry)
                return true;
            // Its own next line (stepping over); stepping out goes on until it returns, unless it already has.
            return Kind == StepKind.Over || !same;
        }

        // Deeper. A place seen inside a call the method made goes on.
        var key = (probe.Path, probe.Function, stack);
        if (_inside.Contains(key))
            return false;
        // The method's own catch or finally block runs a little deeper than the rest of it.
        if (same && !probe.Entry)
            return Kind == StepKind.Over;
        // A call: the method's own (go on), or one Griff made after it returned (the step ends there).
        if (Inside(callers()))
        {
            _inside.Add(key);
            return false;
        }

        return true;
    }

    // Whether the method stepped from is still running under these methods: they begin with the ones under it then.
    private bool Inside(MethodBase?[] now)
    {
        if (now.Length <= _callers.Length)
            return false;
        for (var i = 0; i < _callers.Length; i++)
        {
            if (!Equals(now[i], _callers[i]))
                return false;
        }

        return true;
    }
}
