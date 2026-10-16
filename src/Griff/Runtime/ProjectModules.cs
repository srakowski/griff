using System.Reflection;
using System.Text.Json;
using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>
/// The modules a game uses (see <see cref="GameModule"/>): those a component of which is in one of its scenes or prefabs,
/// and those its scripts were built against. A game builds the systems of these alone (<see cref="LoadedGame.Modules"/>),
/// its scripts get a <c>using</c> for each, and its export carries their assemblies and no other module's.
/// </summary>
public static class ProjectModules
{
    /// <summary>
    /// The modules <paramref name="project"/> uses: those its scenes and prefabs name a component of, those
    /// <paramref name="scripts"/> (its scripts, built) were built against, and those of <paramref name="open"/> (scenes the
    /// editor has open, unsaved changes and all).
    /// </summary>
    public static IReadOnlyList<GameModule> Of(Project project, Assembly? scripts = null, IEnumerable<Scene>? open = null) =>
        Of(project.AssetsPath, scripts, open);

    /// <inheritdoc cref="Of(Project, Assembly?, IEnumerable{Scene}?)"/>
    /// <param name="assetsFolder">The project's Assets folder, which holds its scenes, prefabs and scripts.</param>
    public static IReadOnlyList<GameModule> Of(string assetsFolder, Assembly? scripts = null, IEnumerable<Scene>? open = null)
    {
        var used = new HashSet<GameModule>(InFiles(assetsFolder));
        if (scripts is not null)
            used.UnionWith(BuiltAgainst(scripts));
        if (open is not null)
            used.UnionWith(open.SelectMany(InScene));
        return Sorted(used);
    }

    /// <summary>The modules the scenes and prefabs under <paramref name="assetsFolder"/> name a component of.</summary>
    public static IReadOnlyList<GameModule> InFiles(string assetsFolder)
    {
        if (!Directory.Exists(assetsFolder))
            return [];
        var byName = Named.Value;
        var used = new HashSet<GameModule>();
        foreach (var path in Directory.EnumerateFiles(assetsFolder, "*", SearchOption.AllDirectories)
                     .Where(p => p.EndsWith(Project.SceneExtension, StringComparison.OrdinalIgnoreCase) ||
                                 p.EndsWith(Project.PrefabExtension, StringComparison.OrdinalIgnoreCase)))
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var type in ComponentNames(bytes))
            {
                if (byName.TryGetValue(type, out var module))
                    used.Add(module);
            }
        }

        return Sorted(used);
    }

    /// <summary>The modules whose components are in <paramref name="scene"/>.</summary>
    public static IReadOnlyList<GameModule> InScene(Scene scene) =>
        Sorted(scene.Entities.SelectMany(e => e.Components).Select(c => GameModule.Of(c.GetType())).OfType<GameModule>());

    /// <summary>The modules <paramref name="scripts"/> (a project's scripts, built) were built against: whose types they name.</summary>
    public static IReadOnlyList<GameModule> BuiltAgainst(Assembly scripts)
    {
        var names = scripts.GetReferencedAssemblies().Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        return Sorted(GameModule.All.Where(m => names.Contains(m.GetType().Assembly.GetName().Name)));
    }

    private static IReadOnlyList<GameModule> Sorted(IEnumerable<GameModule> modules) =>
        [.. modules.Distinct().OrderBy(m => m.Name, StringComparer.Ordinal)];

    // Each module component's saved names (its own and those it was saved as before) and its module.
    private static readonly ModuleAssemblies.Cached<Dictionary<string, GameModule>> Named = new(() =>
    {
        var registry = ComponentRegistry.CreateDefault();
        var named = new Dictionary<string, GameModule>(StringComparer.Ordinal);
        foreach (var descriptor in registry.All)
        {
            if (GameModule.Of(descriptor.Type) is not { } module)
                continue;
            named[descriptor.Name] = module;
            foreach (var former in descriptor.FormerNames)
                named[former] = module;
        }

        return named;
    });

    // The "type" of every component in a scene or prefab file, its entities' children's too.
    private static List<string> ComponentNames(byte[] json)
    {
        var names = new List<string>();
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("type"u8))
                    continue;
                if (reader.Read() && reader.TokenType == JsonTokenType.String && reader.GetString() is { } name)
                    names.Add(name);
            }
        }
        catch (JsonException)
        {
            // A file that doesn't read is said when it's loaded; what was read of it still counts.
        }

        return names;
    }
}
