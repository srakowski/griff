using System.Reflection;
using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>Layout of an exported game: the player executable sits next to a <c>game</c> folder.</summary>
public static class ExportLayout
{
    /// <summary>Folder next to the player holding project.griff and Assets.</summary>
    public const string GameFolder = "game";

    /// <summary>Precompiled scripts, inside the game folder.</summary>
    public const string ScriptsAssembly = "Scripts.dll";

    /// <summary>Marks a folder as created by export, so re-exporting may safely replace it.</summary>
    public const string Marker = ".griff-build";

    /// <summary>
    /// For a game exported for the web: the list of the game folder's files (paths relative to it, '/' between folders,
    /// one a line), inside it. A browser can't list a folder, so the web player fetches what this names.
    /// </summary>
    public const string WebFileList = "files.txt";
}

/// <summary>Everything needed to start a game: project, component types and the start scene.</summary>
public sealed record LoadedGame(Project Project, ComponentRegistry Components, Scene Scene, IReadOnlyList<string> Warnings)
{
    private PrefabLibrary? _prefabs;

    /// <summary>The start scene's name, for <see cref="Game.SceneName"/>.</summary>
    public string? SceneName { get; init; }

    /// <summary>Loads another scene of this game by name, for <see cref="Game.SceneLoader"/>.</summary>
    public Scene LoadScene(string nameOrPath) => GameLoader.LoadScene(Project, Components, nameOrPath);

    /// <summary>Creates an entity from one of this game's prefabs, for <see cref="Game.PrefabLoader"/>.</summary>
    public Entity LoadPrefab(string nameOrPath) => (_prefabs ??= new PrefabLibrary(Project, Components)).Instantiate(nameOrPath);

    /// <summary>
    /// The game at its start scene, reading <paramref name="input"/>: its other scenes and prefabs loaded from here, its
    /// title and components, and the project's settings (<see cref="GameLoader.UseProjectSettings"/>), as the web player
    /// starts it (so a check drawing it on the desktop starts it the same).
    /// </summary>
    public Game NewGame(InputState input)
    {
        var game = new Game(Scene, input)
        {
            SceneLoader = LoadScene,
            PrefabLoader = LoadPrefab,
            SceneName = SceneName,
            Title = Project.Name,
            Components = Components,
        };
        game.UseProjectSettings(Project);
        return game;
    }
}

/// <summary>Loads an exported game folder (or a project folder) for the player.</summary>
public static class GameLoader
{
    /// <summary>
    /// Sets <paramref name="game"/> up as <paramref name="project"/> says, before it starts: its controls (the game's own
    /// input actions), its step rate and its tables (every <c>.table</c> file in Assets, see <see cref="Game.Tables"/>).
    /// Every host does this, so a game plays the same in the editor, exported and in a browser.
    /// </summary>
    public static void UseProjectSettings(this Game game, Project project)
    {
        (game.Input as InputState)?.UseControls(project.Controls);
        game.Time.StepRate = project.StepRate;
        game.Tables = Griff.Data.TableSet.Load(project.AssetsPath);
    }

    public static async Task<LoadedGame> LoadAsync(string gameFolder)
    {
        var project = await Project.LoadAsync(gameFolder);
        var components = ComponentRegistry.CreateDefault();
        var warnings = new List<string>();

        var scripts = Path.Combine(project.RootPath, ExportLayout.ScriptsAssembly);
        // Loaded from bytes so the file isn't locked while the game runs.
        if (File.Exists(scripts))
            RegisterComponents(Assembly.Load(await File.ReadAllBytesAsync(scripts)), components, warnings);

        var scenePath = project.StartScenePath is { } start && File.Exists(start)
            ? start
            : project.FindScenes().FirstOrDefault()
              ?? throw new FileNotFoundException("The game has no scenes.");

        await using var stream = File.OpenRead(scenePath);
        var scene = new SceneSerializer(components).Load(stream, warnings);
        return new LoadedGame(project, components, scene, warnings) { SceneName = Path.GetFileNameWithoutExtension(scenePath) };
    }

    public static Scene LoadScene(Project project, ComponentRegistry components, string nameOrPath)
    {
        var path = project.FindScene(nameOrPath) ?? throw new FileNotFoundException($"No scene named \"{nameOrPath}\".");
        using var stream = File.OpenRead(path);
        return new SceneSerializer(components).Load(stream);
    }

    public static void RegisterComponents(Assembly assembly, ComponentRegistry registry, ICollection<string> warnings)
    {
        foreach (var type in assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Component)) && !t.IsAbstract))
        {
            try
            {
                registry.Register(type);
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
            {
                warnings.Add($"{type.Name}: {ex.Message}");
            }
        }
    }
}
