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
///         game.StepAfter("Dragging", "Match Boards", boards, boards.Run);
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

    public override string ToString() => Name;
}

/// <summary>
/// A game being made, as each <see cref="GameModule"/> sees it in <see cref="GameModule.AddTo"/>: where it adds its
/// systems, their steps and its network commands.
/// </summary>
public sealed class GameSetup
{
    private readonly List<GameSystem> _systems = [];
    private readonly List<(string Anchor, bool Before, string Name, Action Run, GameSystem System, GameModule Module)> _steps = [];

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
        _systems.Add(system);
        return system;
    }

    /// <summary>
    /// Adds a step called <paramref name="name"/> to the frame, run as part of <paramref name="system"/>, just after the
    /// step <paramref name="after"/> (one of Griff's or another module's, e.g. "Dragging", or "Present: Prompts" for one
    /// whose name comes up in two phases) and after the steps added there before it, in that step's phase.
    /// </summary>
    public void StepAfter(string after, string name, GameSystem system, Action run) => AddStep(after, before: false, name, system, run);

    /// <summary>
    /// Adds a step called <paramref name="name"/> to the frame, run as part of <paramref name="system"/>, just before the
    /// step <paramref name="before"/> and after the steps added before it there, in that step's phase.
    /// </summary>
    public void StepBefore(string before, string name, GameSystem system, Action run) => AddStep(before, before: true, name, system, run);

    /// <summary>
    /// Does one of the module's dialogue tags (see <see cref="DialogueTagsAttribute"/>) as the line it's on is said:
    /// <paramref name="run"/> is called with the dialogue and what follows the word in the brackets ("Ava left happy" for
    /// "[show Ava left happy]"). A dialogue is said on the machine of the player reading it.
    /// </summary>
    public void OnDialogueTag(string word, Action<Dialogue, string> run) => Game.OnDialogueTag(word, run);

    /// <summary>
    /// Something players' machines in a network game can ask the host to do, by a name unique to the module (e.g.
    /// "griff.rts"): <paramref name="run"/> is called on the host with the player who asked and what they sent with it.
    /// </summary>
    public void OnNetCommand(string name, Action<NetPlayer?, object?> run) => Game.OnNetCommand(name, run);

    /// <summary>
    /// Something the host of a network game can tell one player's machine (what its run of that player's tools wants shown
    /// there), by a name unique to the module: <paramref name="run"/> is called on that machine with what the host sent.
    /// The host sends it with <c>Game.Net.SendCommandTo</c>.
    /// </summary>
    public void OnHostCommand(string name, Action<object?> run) => Game.OnHostCommand(name, run);

    private void AddStep(string anchor, bool before, string name, GameSystem system, Action run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(anchor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_systems.Contains(system))
            throw new InvalidOperationException($"Add {system.Name} with AddSystem before giving it steps.");
        _steps.Add((anchor, before, name, run, system, Module!));
    }

    // Griff's steps with the modules' put in next to the steps they name, in the order they were added: those before a
    // step, the step, then those after it (each of them with the steps that name it in turn).
    internal List<FrameStep> Place(IReadOnlyList<FrameStep> core)
    {
        var all = core.ToList();
        var pending = _steps.Select((s, i) => (Step: s, Index: i)).ToList();

        // Resolve anchors among Griff's steps and the modules' already placed, until nothing more can be.
        var anchors = new Dictionary<(string Anchor, bool Before), List<FrameStep>>();
        var resolved = new Dictionary<int, FrameStep>();
        while (pending.Count > 0)
        {
            var progress = false;
            foreach (var (step, index) in pending.ToList())
            {
                var known = all.Concat(resolved.Values).ToList();
                if (Find(known, step.Anchor, step.Module) is not { } anchor)
                    continue;
                var frameStep = new FrameStep(anchor.Phase, step.Name, step.Run, step.System);
                resolved[index] = frameStep;
                var key = (anchor.ToString(), step.Before);
                if (!anchors.TryGetValue(key, out var list))
                    anchors[key] = list = [];
                list.Add(frameStep);
                pending.Remove((step, index));
                progress = true;
            }

            if (!progress)
            {
                var (step, _) = pending[0];
                throw new InvalidOperationException(
                    $"The {step.Module.Name} module puts its step \"{step.Name}\" {(step.Before ? "before" : "after")} \"{step.Anchor}\", but the frame has no step called that.");
            }
        }

        var frame = new List<FrameStep>();
        foreach (var step in core)
            Expand(step);
        return frame;

        void Expand(FrameStep step)
        {
            foreach (var before in anchors.GetValueOrDefault((step.ToString(), true)) ?? [])
                Expand(before);
            frame.Add(step);
            foreach (var after in anchors.GetValueOrDefault((step.ToString(), false)) ?? [])
                Expand(after);
        }
    }

    // A step by its name, or by "Phase: Name" when the name comes up in more than one phase.
    private static FrameStep? Find(List<FrameStep> steps, string anchor, GameModule module)
    {
        var matches = steps.Where(s => s.Name == anchor || s.ToString() == anchor).ToList();
        if (matches.Count > 1 && matches.Select(s => s.Phase).Distinct().Count() > 1)
            throw new InvalidOperationException(
                $"The {module.Name} module names the step \"{anchor}\", which comes up in more than one phase: say which, e.g. \"{matches[0]}\".");
        return matches.FirstOrDefault();
    }
}
