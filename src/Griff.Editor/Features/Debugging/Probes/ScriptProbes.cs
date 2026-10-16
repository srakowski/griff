using System.Reflection;
using Griff.Scripting;

namespace Griff.Editor.Features.Debugging.Probes;

/// <summary>
/// A line of a script a breakpoint can stop at, in a build of the scripts with probes: its number in the build's
/// <see cref="ProbeTable"/>, the script and line (as compiled), the member it's in ("Player.Update"), and the variables in
/// scope there, named in the order the probe passes their values (and whether it passes the object it runs on, "this").
/// </summary>
public sealed record ScriptProbe(int Id, string Path, int Line, string Where, IReadOnlyList<string> Names, bool HasThis)
{
    /// <summary>
    /// The method, accessor, lambda or local function the line is in, as where it starts in the script as built (-1 for
    /// none): lines with the same <see cref="Path"/> and Function are in the same one.
    /// </summary>
    public int Function { get; init; } = -1;

    /// <summary>The first line its function runs each time it's called: reaching it means a new call has begun.</summary>
    public bool Entry { get; init; }

    /// <summary>Whether <paramref name="other"/> is a line of the same method (or lambda, or local function).</summary>
    public bool InSameFunction(ScriptProbe other) => Function >= 0 && Function == other.Function && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A stretch of a script's code that notes an exception coming up through it (a filter that catches nothing: see
/// <see cref="Griff.Debugging.ScriptProbes.Threw"/>), in a build of the scripts with probes: its number in the build's
/// <see cref="ProbeTable"/>, the script and the lines it covers (as compiled), the member it's in, and the variables in scope
/// all through it, named in the order it passes their values (and whether it passes "this").
/// </summary>
public sealed record ScriptGuard(int Id, string Path, int Line, int EndLine, string Where, IReadOnlyList<string> Names, bool HasThis);

/// <summary>
/// The probes of one build of the scripts (see <see cref="ScriptInstrumenter"/>), and the switches that turn each on: a
/// probe whose switch is on stops the game there (see <see cref="Griff.Debugging.ScriptProbes"/>). Also the build's guards,
/// which note where an exception came up through the scripts.
/// </summary>
public sealed class ProbeTable : IScriptAttachment
{
    private readonly Dictionary<string, ScriptProbe[]> _byFile;
    private readonly Dictionary<int, ScriptProbe> _byId;
    private readonly Dictionary<int, ScriptGuard> _guards;
    private readonly Dictionary<int, FieldInfo> _switches = [];

    public ProbeTable(IReadOnlyList<ScriptProbe> probes, IReadOnlyList<ScriptGuard>? guards = null)
    {
        Probes = probes;
        Guards = guards ?? [];
        _byId = probes.ToDictionary(p => p.Id);
        _guards = Guards.ToDictionary(g => g.Id);
        _byFile = probes.GroupBy(p => Normalize(p.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Line).ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The build's guards (see <see cref="ScriptInstrumenter"/>).</summary>
    public IReadOnlyList<ScriptGuard> Guards { get; }

    /// <summary>The guard numbered <paramref name="id"/>, or null.</summary>
    public ScriptGuard? Guard(int id) => _guards.GetValueOrDefault(id);

    /// <summary>The probe on <paramref name="line"/> of the script at <paramref name="path"/>, or else the last one before it.</summary>
    public ScriptProbe? AtOrBefore(string path, int line) =>
        _byFile.TryGetValue(Normalize(path), out var probes) ? probes.LastOrDefault(p => p.Line <= line) : null;

    /// <summary>No probes: a build without them.</summary>
    public static ProbeTable Empty { get; } = new([]);

    private Dictionary<string, string> _sources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The text of the script at <paramref name="path"/> as it was built (its probes' lines are lines of it), or null for
    /// a script that wasn't.
    /// </summary>
    public string? SourceOf(string path) => _sources.GetValueOrDefault(Normalize(path));

    internal void KeepSources(IEnumerable<ScriptSource> sources) =>
        _sources = sources.GroupBy(s => Normalize(s.Path), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last().Text, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ScriptProbe> Probes { get; }

    /// <summary>The build's class of switches, once its assembly is loaded (see <see cref="Attach"/>).</summary>
    public Type? Switches { get; private set; }

    /// <summary>Finds the build's switches in its loaded <paramref name="assembly"/>.</summary>
    public void Attach(Assembly assembly)
    {
        Switches = assembly.GetType(ScriptInstrumenter.SwitchesClass);
        _switches.Clear();
        foreach (var probe in Probes)
        {
            if (Switches?.GetField("P" + probe.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), BindingFlags.Public | BindingFlags.Static) is { } field)
                _switches[probe.Id] = field;
        }
    }

    /// <summary>The probe numbered <paramref name="id"/>, or null.</summary>
    public ScriptProbe? this[int id] => _byId.GetValueOrDefault(id);

    /// <summary>The probe on <paramref name="line"/> of the script at <paramref name="path"/>, or the next one after it in that script.</summary>
    public ScriptProbe? AtOrAfter(string path, int line) =>
        _byFile.TryGetValue(Normalize(path), out var probes) ? probes.FirstOrDefault(p => p.Line >= line) : null;

    /// <summary>The probes of the script at <paramref name="path"/>, by line.</summary>
    public IReadOnlyList<ScriptProbe> In(string path) => _byFile.TryGetValue(Normalize(path), out var probes) ? probes : [];

    /// <summary>Turns a probe's switch on or off.</summary>
    public void Set(ScriptProbe probe, bool on)
    {
        if (_switches.TryGetValue(probe.Id, out var field))
            field.SetValue(null, on);
    }

    /// <summary>Turns every switch on (while the debugger steps, so every line can say whether the step ends there), or off.</summary>
    public void SetAll(bool on)
    {
        foreach (var field in _switches.Values)
            field.SetValue(null, on);
    }

    /// <summary>The build is replaced or unloaded: its scripts may still be running (held at a breakpoint), and stop at none.</summary>
    public void Detach() => Clear();

    /// <summary>Turns every switch off.</summary>
    public void Clear()
    {
        foreach (var field in _switches.Values)
            field.SetValue(null, false);
    }

    private static string Normalize(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            return path;
        }
    }
}
