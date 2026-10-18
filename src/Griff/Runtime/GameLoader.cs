using Griff.Input;
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

    /// <summary>
    /// The modules a game's project uses (see <see cref="GameModule"/>), their running assemblies only, inside the game folder:
    /// its host loads them (<see cref="ModuleAssemblies.Load(string)"/>) before it reads the game.
    /// </summary>
    public const string ModulesFolder = "Modules";

    /// <summary>Marks a folder as created by export, so re-exporting may safely replace it.</summary>
    public const string Marker = ".griff-build";

    /// <summary>
    /// For a game exported for the web: the list of the game folder's files (paths relative to it, '/' between folders,
    /// one a line), inside it. A browser can't list a folder, so the web player fetches what this names.
    /// </summary>
    public const string WebFileList = "files.txt";
}

/// <summary>
/// Everything needed to start a game: its component types, its start scene, how it loads its other scenes and prefabs,
/// and the project it came from. Every host makes its games from one (<see cref="NewGame"/>): the player, windowed and as
/// a server, the web player, the editor's Play and Play with Players, and a balance simulation's fights.
/// </summary>
/// <param name="Project">
/// The project (or exported game folder) it was loaded from, whose settings, scenes and prefabs it plays with. Null for a
/// game made of a running one's content (a simulation's arena), which says how it loads scenes and prefabs itself.
/// </param>
/// <param name="Components">The component types its scenes are read with (its scripts' too).</param>
/// <param name="Scene">The scene it starts in.</param>
/// <param name="Problems">What loading it found wrong, said once (a game doesn't say these again).</param>
public sealed record LoadedGame(Project? Project, ComponentRegistry Components, Scene Scene, ProblemLog Problems)
{
    private PrefabLibrary? _prefabs;
    private readonly int? _stepRate;

    /// <summary>The start scene's name, for <see cref="Game.SceneName"/>.</summary>
    public string? SceneName { get; init; }

    /// <summary>
    /// The modules whose systems its games run (<see cref="Game.Modules"/>): those its project uses
    /// (<see cref="ProjectModules"/>), or every one this program has if null.
    /// </summary>
    public IReadOnlyList<GameModule>? Modules { get; init; }

    /// <summary>
    /// How another scene loads by name, if not from the project's files: the editor plays the open scene as it is there,
    /// unsaved changes included.
    /// </summary>
    public Func<string, Scene>? Scenes { get; init; }

    /// <summary>How an entity is made from a prefab by name, if not from the project's prefabs.</summary>
    public Func<string, Entity>? Prefabs { get; init; }

    /// <summary>
    /// The project's prefabs, read once each and copied for every spawn: the host's own, kept across games (the editor's,
    /// which reads a prefab again when it's saved), or else one made for this game, whose prefabs never change.
    /// </summary>
    public PrefabLibrary? Library
    {
        get => _prefabs;
        init => _prefabs = value;
    }

    /// <summary>Steps a second of game time: the project's, unless set.</summary>
    public int StepRate
    {
        get => _stepRate ?? Project?.StepRate ?? Time.DefaultStepRate;
        init => _stepRate = value;
    }

    /// <summary>Loads another scene of this game by name, for <see cref="Game.SceneLoader"/>.</summary>
    public Scene LoadScene(string nameOrPath) =>
        Scenes is { } scenes ? scenes(nameOrPath)
        : Project is { } project ? GameLoader.LoadScene(project, Components, nameOrPath)
        : throw new FileNotFoundException($"No scene named \"{nameOrPath}\".");

    /// <summary>Creates an entity from one of this game's prefabs, for <see cref="Game.PrefabLoader"/>.</summary>
    public Entity LoadPrefab(string nameOrPath) =>
        Prefabs is { } prefabs ? prefabs(nameOrPath)
        : Project is { } project ? (_prefabs ?? MadeLibrary(project)).Instantiate(nameOrPath)
        : throw new FileNotFoundException($"No prefab named \"{nameOrPath}\".");

    // Made once, though games spawn on many threads at once (a simulation's, each on a core of its own).
    private PrefabLibrary MadeLibrary(Project project)
    {
        Interlocked.CompareExchange(ref _prefabs, new PrefabLibrary(project, Components), null);
        return _prefabs!;
    }

    /// <summary>
    /// The game at its start scene, set up for the host that plays it (<paramref name="host"/>: its input, sound, saves,
    /// network and where its problems are said): its other scenes and prefabs loaded from here, its title and components,
    /// and the project's settings (<see cref="GameLoader.UseProjectSettings"/>). The one way a host makes a game, so a game
    /// starts the same in the editor, exported, in a browser and in a simulation.
    /// </summary>
    public Game NewGame(GameHostOptions? host = null)
    {
        host ??= new GameHostOptions();
        var game = new Game(Scene, host.Input ?? new InputState(), Modules)
        {
            // A game with nowhere to load scenes or prefabs from has no loader, which it says when asked for one.
            SceneLoader = Scenes is not null || Project is not null ? LoadScene : null,
            PrefabLoader = Prefabs is not null || Project is not null ? LoadPrefab : null,
            SceneName = SceneName,
            Title = Project?.Name,
            Components = Components,
        };
        if (host.Problems is { } problems)
            game.Problems.Reported += problems;
        if (Project is { } project)
            game.UseProjectSettings(project, host.Tables);
        else if (host.Tables is { } tables)
            game.Tables = tables;
        // A host without a project (a simulation's) hands over what the project says of its controls and saves.
        if (host.Controls is { } controls)
            game.HostInput.UseControls(controls);
        if (host.SaveVersion is { } saveVersion)
            game.SaveVersion = saveVersion;
        game.Time.StepRate = StepRate;
        game.AlreadySaid(Problems.Lines);
        if (host.Audio is { } audio)
            game.Audio = audio;
        if (host.SaveData is { } saves)
            game.SaveData = saves;
        game.Network.OverNetwork = host.OverNetwork;
        if (host.Transport is { } transport)
            game.Network.Transport = transport;
        return game;
    }
}

/// <summary>
/// What a host hands a game it makes (<see cref="LoadedGame.NewGame"/>): the input its window or page feeds, the sound it
/// plays through, where it keeps its saves, whether it plays over the network and where its problems are said. Left out,
/// each is the game's own: fresh input, silence, saves kept in memory, the network on this machine only.
/// </summary>
public sealed record GameHostOptions
{
    /// <summary>The input the host feeds from its keyboard, mouse, touch and controllers; a fresh one if null.</summary>
    public InputState? Input { get; init; }

    /// <summary>What the game's sound plays through (a <see cref="Audio.MixerAudio"/> on the host's mixer); silent if null.</summary>
    public IAudio? Audio { get; init; }

    /// <summary>Where the game keeps its saves; in memory if null.</summary>
    public SaveData? SaveData { get; init; }

    /// <summary>
    /// Whether the game hosts and joins over the network (<see cref="INetwork.OverNetwork"/>): only the player's and the
    /// editor's Play in its window do.
    /// </summary>
    public bool OverNetwork { get; init; }

    /// <summary>
    /// What the game's network play goes over (<see cref="INetwork.Transport"/>): UDP if null. A platform with its own way to
    /// reach other machines hands its own.
    /// </summary>
    public Griff.Networking.INetTransport? Transport { get; init; }

    /// <summary>
    /// The tables it plays with, rather than the project's files read afresh: the editor's, kept across replays, or a
    /// simulation step's.
    /// </summary>
    public Griff.Data.TableSet? Tables { get; init; }

    /// <summary>
    /// The game's input actions with their bindings, as the project's Controls say, for a host that has no project to read
    /// them from (a simulation's); null leaves them as the project, or the built-in ones, have them.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<InputBinding>>? Controls { get; init; }

    /// <summary>The game's <see cref="Game.SaveVersion"/>, for a host with no project to read it from; null leaves it as it is.</summary>
    public int? SaveVersion { get; init; }

    /// <summary>Told each problem the game reports (something set up without code that can't do its job).</summary>
    public Action<Problem>? Problems { get; init; }
}

/// <summary>Loads an exported game folder (or a project folder) for the player.</summary>
public static class GameLoader
{
    /// <summary>
    /// Sets <paramref name="game"/> up as <paramref name="project"/> says, before it starts: its controls (the game's own
    /// input actions), its step rate, its save version, its tables (every <c>.table</c> file in Assets, see <see cref="Game.Tables"/>) and its
    /// balance simulations (every <c>.sim</c> file, see <c>Game.Simulate</c>).
    /// Every host does this, so a game plays the same in the editor, exported and in a browser.
    /// </summary>
    public static void UseProjectSettings(this Game game, Project project) => game.UseProjectSettings(project, null);

    // As above, playing with `tables` (when given) rather than the project's files read afresh.
    internal static void UseProjectSettings(this Game game, Project project, Griff.Data.TableSet? tables)
    {
        game.HostInput.UseControls(project.Controls);
        game.Time.StepRate = project.StepRate;
        game.SaveVersion = project.SaveVersion;
        // Opt-in: otherwise every game starts from the numbers its thread had (see Game.Random), the same each time.
        if (project.NewRandomEachGame)
            game.Random.Reseed(System.Random.Shared.Next());
        game.Tables = tables ?? Griff.Data.TableSet.Load(project.AssetsPath);
        game.Simulations = Griff.Data.SimulationFile.LoadAll(project.AssetsPath);
    }

    /// <summary>
    /// Loads a game folder (an export's, or a project's): the modules it carries first (<see cref="ExportLayout.ModulesFolder"/>,
    /// so its scenes read with their components), then its scripts and its start scene. Its games run the modules it uses.
    /// </summary>
    public static async Task<LoadedGame> LoadAsync(string gameFolder)
    {
        var project = await Project.LoadAsync(gameFolder);
        ModuleAssemblies.Load(Path.Combine(project.RootPath, ExportLayout.ModulesFolder));
        var components = ComponentRegistry.CreateDefault();
        var warnings = new ProblemLog();

        var scripts = Path.Combine(project.RootPath, ExportLayout.ScriptsAssembly);
        Assembly? scriptAssembly = null;
        // Loaded from bytes so the file isn't locked while the game runs.
        if (File.Exists(scripts))
            RegisterComponents(scriptAssembly = Assembly.Load(await File.ReadAllBytesAsync(scripts)), components, warnings);

        var scenePath = project.StartScenePath is { } start && File.Exists(start)
            ? start
            : project.FindScenes().FirstOrDefault()
              ?? throw new FileNotFoundException("The game has no scenes.");

        await using var stream = File.OpenRead(scenePath);
        var scene = new SceneSerializer(components).Load(stream, warnings);
        // A trigger, counter or wire naming an action or output nothing has: said now, not left to do nothing.
        foreach (var line in Griff.Scenes.NameChecks.Lines(scene, components))
            warnings.Report(line);
        return new LoadedGame(project, components, scene, warnings)
        {
            SceneName = Path.GetFileNameWithoutExtension(scenePath),
            Modules = ProjectModules.Of(project, scriptAssembly),
        };
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
