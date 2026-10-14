using System.Reflection;

namespace Griff.Scripting;

/// <summary>
/// A line of a script a breakpoint can stop at, in a build of the scripts with probes: its number in the build's
/// <see cref="ProbeTable"/>, the script and line (as compiled), the member it's in ("Player.Update"), and the variables in
/// scope there, named in the order the probe passes their values (and whether it passes the object it runs on, "this").
/// </summary>
public sealed record ScriptProbe(int Id, string Path, int Line, string Where, IReadOnlyList<string> Names, bool HasThis);

/// <summary>
/// The probes of one build of the scripts (see <see cref="ScriptInstrumenter"/>), and the switches that turn each on: a
/// probe whose switch is on stops the game there (see <see cref="Griff.Debugging.ScriptProbes"/>).
/// </summary>
public sealed class ProbeTable
{
    private readonly Dictionary<string, ScriptProbe[]> _byFile;
    private readonly Dictionary<int, ScriptProbe> _byId;
    private readonly Dictionary<int, FieldInfo> _switches = [];

    public ProbeTable(IReadOnlyList<ScriptProbe> probes)
    {
        Probes = probes;
        _byId = probes.ToDictionary(p => p.Id);
        _byFile = probes.GroupBy(p => Normalize(p.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.Line).ToArray(), StringComparer.OrdinalIgnoreCase);
    }

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
