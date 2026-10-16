using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// A part of Griff that lives in a place of its own: a folder under src/Griff/Modules with a namespace to match
/// (Griff.Modules.Match3), holding everything of a genre such as Match-3 or Strategy: its components, systems, drawing,
/// network state, templates and the entities the editor's Entity menu makes. Griff's core names no module. Most of a
/// module is found by type: its components (and how they draw and what they send), its templates
/// (<see cref="Projects.ProjectTemplate"/>) and its Entity menu presets (<see cref="Editing.EntityPreset"/>). What can't
/// be found is how it runs, which the module registers once, in <see cref="AddTo"/>: its systems, where their steps go in
/// the frame, and what players' machines may ask its host to do. Modules are found rather than listed (see
/// <see cref="All"/>), so adding one is adding a folder with a module class in it.
/// </summary>
/// <example>
/// <code>
/// public sealed class Match3Module : GameModule
/// {
///     public override string Name => "Match-3";
///
///     protected internal override void AddTo(GameSetup game)
///     {
///         var boards = game.AddSystem(new Match3System());
///         game.StepAfter(Steps.Dragging, "Match Boards", boards, boards.Run);
///     }
/// }
/// </code>
/// </example>
public abstract class GameModule
{
    private static readonly Lazy<IReadOnlyList<GameModule>> Found = new(() => typeof(GameModule).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(GameModule)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
        .Select(t => (GameModule)Activator.CreateInstance(t)!)
        .OrderBy(m => m.Name, StringComparer.Ordinal)
        .ToList());

    /// <summary>Every module in Griff, by name. A game adds each one's systems in this order (see <see cref="AddTo"/>).</summary>
    public static IReadOnlyList<GameModule> All => Found.Value;

    /// <summary>What it's called, e.g. "Match-3".</summary>
    public abstract string Name { get; }

    /// <summary>The namespace its types are in: the module class's own. Types in it, or in one inside it, belong to the module.</summary>
    public string Namespace => GetType().Namespace ?? "";

    /// <summary>The module <paramref name="type"/> belongs to (by its namespace), or null for one of the core's.</summary>
    public static GameModule? Of(Type type) => Of(type.Namespace);

    /// <summary>The module whose namespace is <paramref name="ns"/> or holds it, or null.</summary>
    public static GameModule? Of(string? ns) =>
        ns is null ? null : All.FirstOrDefault(m => ns == m.Namespace || ns.StartsWith(m.Namespace + ".", StringComparison.Ordinal));

    /// <summary>
    /// Adds what the module runs to a game as it's made: its systems (<see cref="GameSetup.AddSystem{T}"/>), each step of
    /// theirs next to the step of the frame it has to follow or go before (<see cref="GameSetup.StepAfter"/>), and the
    /// commands players' machines send the host (<see cref="GameSetup.OnNetCommand"/>) and the host sends a player's
    /// machine (<see cref="GameSetup.OnHostCommand"/>).
    /// </summary>
    protected internal abstract void AddTo(GameSetup game);

    /// <summary>
    /// Adds the module's part to a scene a host plays out of sight before a game starts, so the code a game of it runs is
    /// ready when the game begins (the web player's warm-up, whose interpreter prepares each method the first time it's
    /// called): things of the module's that a game's first moments set going. <paramref name="turning"/> is true for a game
    /// with bodies that turn, whose first collapse the warm-up plays (Griff adds a bin of tumbling shapes for it). Most
    /// modules add nothing.
    /// </summary>
    public virtual void AddToWarmUp(Scene scene, bool turning)
    {
    }

    public override string ToString() => Name;
}

/// <summary>
/// A game being made, as each <see cref="GameModule"/> sees it in <see cref="GameModule.AddTo"/>: where it adds its
/// systems, their steps and its network commands.
/// </summary>
public sealed class GameSetup
{
    private readonly List<GameSystem> _systems = [];
    private readonly List<(StepId Anchor, bool Before, StepId Id, Action Run, GameSystem System, GameModule Module)> _steps = [];

    internal GameSetup(Game game) => Game = game;

    /// <summary>The game being made.</summary>
    public Game Game { get; }

    // The module whose AddTo is running.
    internal GameModule? Module { get; set; }

    internal IReadOnlyList<GameSystem> Systems => _systems;

    /// <summary>
    /// Runs <paramref name="system"/> for the whole game as one of Griff's own: it can't be removed, it forgets what it kept
    /// of a scene in its SceneChanged and draws in its Draw. Its work each frame is done by the steps the module adds for
    /// it (<see cref="StepAfter"/>, <see cref="StepBefore"/>), not its Update.
    /// </summary>
    public T AddSystem<T>(T system) where T : GameSystem
    {
        ArgumentNullException.ThrowIfNull(system);
        if (_systems.Any(s => s.GetType() == system.GetType()))
            throw new InvalidOperationException($"{system.Name} has already been added.");
        if (system.GetType().GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.DeclaringType != typeof(GameSystem))
            throw new InvalidOperationException($"{system.Name} overrides Update, but a module's system is scheduled by its steps alone (StepAfter, StepBefore).");
        _systems.Add(system);
        return system;
    }

    /// <summary>
    /// Adds a step called <paramref name="name"/> to the frame, run as part of <paramref name="system"/>, just after the
    /// step <paramref name="after"/> (one of Griff's, from <see cref="Steps"/>, or one this or another module added
    /// before) and after the steps added there before it, in that step's phase. Returns the step's id, for another step
    /// to follow it.
    /// </summary>
    public StepId StepAfter(StepId after, string name, GameSystem system, Action run) => AddStep(after, before: false, name, system, run);

    /// <summary>
    /// Adds a step called <paramref name="name"/> to the frame, run as part of <paramref name="system"/>, just before the
    /// step <paramref name="before"/> and after the steps added before it there, in that step's phase. Returns the step's
    /// id, for another step to go next to it.
    /// </summary>
    public StepId StepBefore(StepId before, string name, GameSystem system, Action run) => AddStep(before, before: true, name, system, run);

    /// <summary>
    /// Does one of the module's dialogue tags (see <see cref="DialogueTagsAttribute"/>) as the line it's on is said:
    /// <paramref name="run"/> is called with the dialogue and what follows the word in the brackets ("Ava left happy" for
    /// "[show Ava left happy]"). A dialogue is said on the machine of the player reading it.
    /// </summary>
    public void OnDialogueTag(string word, Action<Dialogue, string> run) => Game.LevelSystem.OnDialogueTag(word, run);

    /// <summary>
    /// Something players' machines in a network game can ask the host to do, by a name unique to the module (e.g.
    /// "griff.rts"): <paramref name="run"/> is called on the host with the player who asked and what they sent with it.
    /// </summary>
    public void OnNetCommand(string name, Action<NetPlayer?, object?> run) => Game.NetworkSystem.OnNetCommand(name, run);

    /// <summary>
    /// Something the host of a network game can tell one player's machine (what its run of that player's tools wants shown
    /// there), by a name unique to the module: <paramref name="run"/> is called on that machine with what the host sent.
    /// The host sends it with <c>Game.Net.SendCommandTo</c>.
    /// </summary>
    public void OnHostCommand(string name, Action<object?> run) => Game.NetworkSystem.OnHostCommand(name, run);

    private StepId AddStep(StepId anchor, bool before, string name, GameSystem system, Action run)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_systems.Contains(system))
            throw new InvalidOperationException($"Add {system.Name} with AddSystem before giving it steps.");
        var id = new StepId(anchor.Phase, name);
        _steps.Add((anchor, before, id, run, system, Module!));
        return id;
    }

    // Griff's steps with the modules' put in next to the steps they name, in the order they were added: those before a
    // step, the step, then those after it (each of them with the steps that name it in turn).
    internal List<FrameStep> Place(IReadOnlyList<FrameStep> core)
    {
        var known = core.Select(s => s.Id).ToHashSet();
        var ids = new HashSet<(FramePhase, string)>(core.Select(s => (s.Phase, s.Name)));
        var anchors = new Dictionary<(StepId Anchor, bool Before), List<FrameStep>>();
        var pending = _steps.ToList();

        // Resolve anchors among Griff's steps and the modules' already placed, until nothing more can be.
        while (pending.Count > 0)
        {
            var progress = false;
            foreach (var step in pending.ToList())
            {
                if (!known.Contains(step.Anchor))
                    continue;
                if (!ids.Add((step.Id.Phase, step.Id.Name)))
                    throw new InvalidOperationException(
                        $"The {step.Module.Name} module adds a step \"{step.Id.Name}\" to the {step.Id.Phase} phase, which already has one called that.");
                var frameStep = new FrameStep(step.Id, step.Run, step.System);
                known.Add(step.Id);
                var key = (step.Anchor, step.Before);
                if (!anchors.TryGetValue(key, out var list))
                    anchors[key] = list = [];
                list.Add(frameStep);
                pending.Remove(step);
                progress = true;
            }

            if (!progress)
            {
                var step = pending[0];
                throw new InvalidOperationException(
                    $"The {step.Module.Name} module puts its step \"{step.Id.Name}\" {(step.Before ? "before" : "after")} \"{step.Anchor}\", which isn't a step of this frame (use a Steps member, or the id of a step added earlier).");
            }
        }

        var frame = new List<FrameStep>();
        foreach (var step in core)
            Expand(step);
        return frame;

        void Expand(FrameStep step)
        {
            foreach (var before in anchors.GetValueOrDefault((step.Id, true)) ?? [])
                Expand(before);
            frame.Add(step);
            foreach (var after in anchors.GetValueOrDefault((step.Id, false)) ?? [])
                Expand(after);
        }
    }
}
