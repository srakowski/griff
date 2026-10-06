using System.Runtime.Loader;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Scripting;

public sealed class ReloadResult
{
    public required bool Success { get; init; }

    public required IReadOnlyList<ScriptDiagnostic> Diagnostics { get; init; }

    /// <summary>Component types now available from scripts.</summary>
    public IReadOnlyList<Type> ComponentTypes { get; init; } = [];

    /// <summary>Old component → its rebuilt replacement, across all migrated scenes.</summary>
    public IReadOnlyDictionary<Component, Component> Replaced { get; init; } = new Dictionary<Component, Component>();

    public IReadOnlyList<string> Warnings { get; init; } = [];
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

    /// <summary>Component types from the currently loaded scripts.</summary>
    public IReadOnlyList<Type> ComponentTypes => _types;

    /// <summary>Raised after scripts are swapped, with the result (also returned by <see cref="Reload"/>).</summary>
    public event EventHandler<ReloadResult>? Reloaded;

    public ReloadResult Reload(IReadOnlyList<ScriptSource> sources, IEnumerable<Scene> liveScenes) =>
        Apply(ScriptCompiler.Compile(sources), liveScenes);

    public ReloadResult ReloadFolder(string folder, IEnumerable<Scene> liveScenes) =>
        Apply(ScriptCompiler.CompileFolder(folder), liveScenes);

    /// <summary>Unregisters script types and turns their components into MissingComponents (data kept).</summary>
    public void Unload(IEnumerable<Scene> liveScenes)
    {
        var serializer = new SceneSerializer(registry);
        var snapshot = ComponentMigrator.Capture(liveScenes.ToList(), serializer, IsFromScripts);
        UnregisterCurrent();
        ComponentMigrator.Restore(snapshot, serializer);
    }

    private ReloadResult Apply(CompileResult compiled, IEnumerable<Scene> liveScenes)
    {
        if (!compiled.Success)
            return new ReloadResult { Success = false, Diagnostics = compiled.Diagnostics };

        var context = new AssemblyLoadContext("GriffScripts", isCollectible: true);
        var assembly = context.LoadFromStream(new MemoryStream(compiled.Assembly!), new MemoryStream(compiled.Symbols!));
        var newTypes = assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Component)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .ToList();

        // Capture with the old types still registered, swap, then rebuild with the new ones.
        var scenes = liveScenes.ToList();
        var serializer = new SceneSerializer(registry);
        var snapshot = ComponentMigrator.Capture(scenes, serializer, IsFromScripts);
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

        var replaced = ComponentMigrator.Restore(snapshot, serializer, warnings);

        var old = _context;
        _context = context;
        _types = registered;
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
