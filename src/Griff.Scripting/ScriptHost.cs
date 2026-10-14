using System.Runtime.Loader;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Scripting;

public sealed class ReloadResult
{
    public required bool Success { get; init; }

    public required IReadOnlyList<ScriptDiagnostic> Diagnostics { get; init; }

    /// <summary>Component types now available from scripts.</summary>
    public IReadOnlyList<Type> ComponentTypes { get; init; } = [];

    /// <summary>Old component → its rebuilt replacement, across all the scenes it rebuilt in.</summary>
    public IReadOnlyDictionary<Component, Component> Replaced { get; init; } = new Dictionary<Component, Component>();

    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True if the sources matched the loaded scripts, so nothing was rebuilt.</summary>
    public bool Unchanged { get; init; }
}

/// <summary>
/// Owns the loaded script assembly. <see cref="Reload"/> compiles the scripts, and only if that succeeds swaps
/// the old assembly out: component types are re-registered and every live script component in the given scenes
/// is rebuilt in place with its saved properties. A failed compile changes nothing, so the game keeps running.
/// </summary>
public sealed class ScriptHost(ComponentRegistry registry)
{
    private AssemblyLoadContext? _context;
    private List<Type> _types = [];
    private string? _loadedSources;

    /// <summary>Component types from the currently loaded scripts.</summary>
    public IReadOnlyList<Type> ComponentTypes => _types;

    /// <summary>
    /// Build the scripts with breakpoint probes (see <see cref="ScriptInstrumenter"/>), as the editor does, so a
    /// breakpoint can stop them without building them again.
    /// </summary>
    public bool WithProbes { get; init; }

    /// <summary>The probes of the loaded scripts (empty without <see cref="WithProbes"/>): what breakpoints switch on.</summary>
    public ProbeTable Probes { get; private set; } = ProbeTable.Empty;

    /// <summary>Raised after scripts are swapped, with the result (also returned by <see cref="Reload"/>).</summary>
    public event EventHandler<ReloadResult>? Reloaded;

    /// <summary>
    /// Compiles and swaps in <paramref name="sources"/>. If they're identical to what's loaded, nothing is
    /// rebuilt: running scripts keep their state (editors often save a file several times in a row).
    /// </summary>
    public ReloadResult Reload(IReadOnlyList<ScriptSource> sources, IEnumerable<Scene> liveScenes)
    {
        var fingerprint = string.Join("\0", sources.Select(s => s.Path + "\0" + s.Text));
        if (fingerprint == _loadedSources)
            return new ReloadResult { Success = true, Diagnostics = [], ComponentTypes = _types, Unchanged = true };

        var result = Apply(ScriptCompiler.Compile(sources, probes: WithProbes), liveScenes);
        if (result.Success)
            _loadedSources = fingerprint;
        return result;
    }

    public ReloadResult ReloadFolder(string folder, IEnumerable<Scene> liveScenes) =>
        Reload(ScriptCompiler.ReadFolder(folder), liveScenes);

    /// <summary>Unregisters script types and turns their components into MissingComponents (data kept).</summary>
    public void Unload(IEnumerable<Scene> liveScenes)
    {
        var serializer = new SceneSerializer(registry);
        var snapshot = ComponentSwap.Capture(liveScenes.ToList(), serializer, IsFromScripts);
        UnregisterCurrent();
        ComponentSwap.Restore(snapshot, serializer);
        _loadedSources = null;
        Probes.Clear();
        Probes = ProbeTable.Empty;
    }

    private ReloadResult Apply(CompileResult compiled, IEnumerable<Scene> liveScenes)
    {
        if (!compiled.Success)
            return new ReloadResult { Success = false, Diagnostics = compiled.Diagnostics };

        var context = new AssemblyLoadContext("GriffScripts", isCollectible: true);
        var assembly = context.LoadFromStream(new MemoryStream(compiled.Assembly!), new MemoryStream(compiled.Symbols!));
        DocComments.Register(assembly, compiled.Documentation);
        compiled.Probes.Attach(assembly);
        var newTypes = ComponentRegistry.ComponentTypesIn(assembly);

        // Capture with the old types still registered, swap, then rebuild with the new ones.
        var scenes = liveScenes.ToList();
        var serializer = new SceneSerializer(registry);
        var snapshot = ComponentSwap.Capture(scenes, serializer, IsFromScripts);
        UnregisterCurrent();

        var warnings = new List<string>();
        var registered = new List<Type>();
        foreach (var type in newTypes)
        {
            try
            {
                registry.Register(type);
                registered.Add(type);
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
            {
                warnings.Add($"{type.Name}: {ex.Message}");
            }
        }

        var replaced = ComponentSwap.Restore(snapshot, serializer, warnings);

        var old = _context;
        _context = context;
        _types = registered;
        // The old scripts may still be running (held at a breakpoint): they stop at none from now on.
        Probes.Clear();
        Probes = compiled.Probes;
        old?.Unload();

        var result = new ReloadResult
        {
            Success = true,
            Diagnostics = compiled.Diagnostics,
            ComponentTypes = registered,
            Replaced = replaced,
            Warnings = warnings,
        };
        Reloaded?.Invoke(this, result);
        return result;
    }

    private bool IsFromScripts(Component component) => _types.Contains(component.GetType());

    private void UnregisterCurrent()
    {
        foreach (var type in _types)
            registry.Unregister(type);
        _types = [];
    }
}
