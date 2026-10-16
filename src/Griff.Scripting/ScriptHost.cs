using System.Reflection;
using System.Runtime.Loader;
using Griff.Runtime;
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

    /// <summary>The modules the scripts' project uses, which they got a <c>using</c> for; null for every one.</summary>
    public IReadOnlyList<GameModule>? Modules { get; internal set; }

    /// <summary>The scripts loaded, when they were rebuilt.</summary>
    public Assembly? Assembly { get; init; }

    /// <summary>The loaded scripts' <c>///</c> comments as documentation XML (the editor registers them with <c>DocComments</c>).</summary>
    public string? Documentation { get; init; }
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

    /// <summary>The scripts loaded now, or null before any have built: their components, and anything else they define (balance simulations).</summary>
    public Assembly? Assembly { get; private set; }

    /// <summary>Component types from the currently loaded scripts.</summary>
    public IReadOnlyList<Type> ComponentTypes => _types;

    /// <summary>
    /// How the scripts are built, given the modules their project uses (null: every one): <see cref="ScriptCompiler.Compile"/>
    /// to start, or one that adds to them (the editor builds them with breakpoint probes, so a breakpoint can stop them
    /// without building them again).
    /// </summary>
    public Func<IReadOnlyList<ScriptSource>, IReadOnlyList<GameModule>?, CompileResult> Compiler { get; init; } =
        (sources, modules) => ScriptCompiler.Compile(sources, modules: modules);

    /// <summary>The modules the loaded scripts' project uses, which they got a <c>using</c> for; null for every one.</summary>
    public IReadOnlyList<GameModule>? Modules { get; private set; }

    /// <summary>What the loaded build carries besides the scripts' code (the editor's breakpoint probes), or null.</summary>
    public IScriptAttachment? Attachment { get; private set; }

    /// <summary>Raised after scripts are swapped, with the result (also returned by <see cref="Reload"/>).</summary>
    public event EventHandler<ReloadResult>? Reloaded;

    /// <summary>
    /// Compiles and swaps in <paramref name="sources"/>. If they're identical to what's loaded, nothing is
    /// rebuilt: running scripts keep their state (editors often save a file several times in a row).
    /// </summary>
    /// <param name="modules">The modules their project uses, which they get a <c>using</c> for; null for every one.</param>
    public ReloadResult Reload(IReadOnlyList<ScriptSource> sources, IEnumerable<Scene> liveScenes, IReadOnlyList<GameModule>? modules = null)
    {
        var fingerprint = string.Join("\0", sources.Select(s => s.Path + "\0" + s.Text)) + "\0" +
                          (modules is null ? "*" : string.Join(",", modules.Select(m => m.Name)));
        if (fingerprint == _loadedSources)
            return new ReloadResult { Success = true, Diagnostics = [], ComponentTypes = _types, Unchanged = true };

        var result = Apply(Compiler(sources, modules), liveScenes);
        result.Modules = modules;
        if (result.Success)
        {
            _loadedSources = fingerprint;
            Modules = modules;
        }

        return result;
    }

    /// <summary>
    /// Compiles and swaps in every script under <paramref name="folder"/> (a project's Assets), with a <c>using</c> for each
    /// module the project uses: its scenes and prefabs as saved, and <paramref name="liveScenes"/> as they are.
    /// </summary>
    public ReloadResult ReloadFolder(string folder, IEnumerable<Scene> liveScenes)
    {
        var scenes = liveScenes.ToList();
        return Reload(ScriptCompiler.ReadFolder(folder), scenes, ProjectModules.Of(folder, null, scenes));
    }

    /// <summary>Unregisters script types and turns their components into MissingComponents (data kept).</summary>
    public void Unload(IEnumerable<Scene> liveScenes)
    {
        var serializer = new SceneSerializer(registry);
        var snapshot = ComponentSwap.Capture(liveScenes.ToList(), serializer, IsFromScripts);
        UnregisterCurrent();
        ComponentSwap.Restore(snapshot, serializer);
        _loadedSources = null;
        Assembly = null;
        Attachment?.Detach();
        Attachment = null;
    }

    private ReloadResult Apply(CompileResult compiled, IEnumerable<Scene> liveScenes)
    {
        if (!compiled.Success)
            return new ReloadResult { Success = false, Diagnostics = compiled.Diagnostics };

        var context = new AssemblyLoadContext("GriffScripts", isCollectible: true);
        var assembly = context.LoadFromStream(new MemoryStream(compiled.Assembly!), new MemoryStream(compiled.Symbols!));
        compiled.Attachment?.Attach(assembly);
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
        Assembly = assembly;
        _types = registered;
        // The old scripts may still be running (held at a breakpoint): they stop at none from now on.
        Attachment?.Detach();
        Attachment = compiled.Attachment;
        old?.Unload();

        var result = new ReloadResult
        {
            Success = true,
            Diagnostics = compiled.Diagnostics,
            ComponentTypes = registered,
            Replaced = replaced,
            Warnings = warnings,
            Assembly = assembly,
            Documentation = compiled.Documentation,
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
