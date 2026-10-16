using System.Numerics;
using Griff.Data;
using Griff.Editing;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Runs a scene in steps of the same length (see <see cref="Runtime.Time.FixedDelta"/>): each frame the screen shows,
/// <see cref="Advance"/> takes as many as the real time since the last one holds, each going through the parts of the
/// game's <see cref="Frame"/>, phase by phase, from starting new behaviors through physics to sound. Spawning and
/// destroying during a step are safe; destroys take effect at the end of the step.
/// </summary>
public sealed partial class Game
{
    private readonly EntityPairSet _touches = new();

    /// <summary>
    /// Who touched what in this frame's physics, both ways round (triggers and what's in them too), in the order physics
    /// found them, for systems that look for themselves without making garbage.
    /// </summary>
    public EntityPairSet Touches => _touches;

    [NotState("whether the host has stopped it")]
    private bool _stopped;

    public Game(Scene scene, IInput input)
    {
        _input = input;
        _services = new GameServices(this);
        Scene = scene;
        Debug = new DebugDraw(Time);
        BuildFrame();
        _network.ListenForCommands();
        (input as InputState)?.UseSaveData(_saveData);
        _settings.LoadSettings();
        _panels.SyncPanels();
        // A scene put back into a hit stop (a rewind) finishes it.
        if (scene.HeldStill > 0)
            Time.Freeze(scene.HeldStill);
    }

    // Makes this the game whose numbers Rand draws on on this thread, returning the numbers that were, to put back once done.
    private GameRandom? Here() => Rand.EnterGame(Random);

    /// <summary>
    /// The game's random numbers (what <see cref="Rand"/> draws on during its steps), starting from where the numbers of
    /// the thread it first runs a step on were then: after <c>Rand.Seed(5)</c>, a new game plays the same every time.
    /// </summary>
    internal GameRandom Random => _random ??= Rand.Outside.Copy();

    [NotState("a part of its own, put back in place")]
    private GameRandom? _random;

    [field: NotState("put back with the snapshot's scene")]
    public Scene Scene
    {
        get;
        internal set
        {
            // The scene played reaches the game's services (see Scene.Services); the one it leaves no longer does.
            if (field is { } left && left.Services == _services)
                left.Services = null;
            field = value;
            value.Services = _services;
        }
    }

    // What the game's systems provide its components, asked for by type (see IGameService).
    [NotState("the systems' own, the same for the game's life")]
    private readonly GameServices _services;

    /// <summary>
    /// The services the game's systems provide its components and scripts, asked for by type (see
    /// <see cref="IGameService"/>): <c>Game.Services.Get&lt;IFlags&gt;()</c>.
    /// </summary>
    public IGameServices Services => _services;

    /// <summary>Loads scenes by name for <see cref="LoadScene"/>. Set by the host (editor or player).</summary>
    [field: NotState("the host's")]
    public Func<string, Scene>? SceneLoader { get; set; }

    /// <summary>Creates entities from prefabs by name for <see cref="Spawn(string, Entity?)"/>. Set by the host (editor or player).</summary>
    [field: NotState("the host's")]
    public Func<string, Entity>? PrefabLoader { get; set; }

    /// <summary>Raised after <see cref="LoadScene"/> switches to a new scene.</summary>
    [field: NotState("the host's")]
    public event EventHandler<Scene>? SceneChanged;

    // The scene system tells everyone the scene has changed, once every system has caught up with it.
    internal void RaiseSceneChanged(Scene next) => SceneChanged?.Invoke(this, next);

    /// <summary>Raised when gameplay code calls <see cref="Quit"/>. The host closes the game (the editor stops playing).</summary>
    [field: NotState("the host's")]
    public event EventHandler? QuitRequested;

    /// <summary>
    /// The current scene's name, e.g. "Level1", used by <see cref="ReloadScene"/>. The host sets it for the first scene;
    /// <see cref="LoadScene"/> keeps it up to date.
    /// </summary>
    public string? SceneName { get; set; }

    /// <summary>
    /// Where the frames the game shows are timed, for a profiler (the editor's Profiler panel sets one while it's on
    /// screen); null, as it starts, times nothing. The host that runs the game adds each frame it shows. While it's set,
    /// each step the game takes also times each of its steps, each kind of script and each system a script added, with
    /// the garbage each makes (see <see cref="FrameProfiler.Costs"/>).
    /// </summary>
    [field: NotState("the host's")]
    public FrameProfiler? Profiler { get; set; }

    /// <summary>
    /// How many entities a scene of this game has before its frames are built in chunks across the cores (see
    /// <see cref="Workers"/>), or null for the renderer's own choice. Tests set it to compare the two ways.
    /// </summary>
    [field: NotState("the host's")]
    internal int? ChunkedFrom { get; set; }

    /// <summary>The view's width divided by its height. Set by the host each frame; <see cref="ScreenAnchor"/> uses it.</summary>
    [field: NotState("the host's")]
    public float ViewAspect { get; set; } = 16f / 9f;

    /// <summary>True once <see cref="Quit"/> has been called.</summary>
    public bool IsQuitting { get; private set; }

    /// <summary>
    /// This machine's controls, always: what runs for another player on the host of a network game is handed theirs (see
    /// <see cref="IPlayerTools"/>, and <see cref="InputOf"/> for whoever plays a particular entity).
    /// </summary>
    public IInput Input => _input;

    [NotState("the host's")]
    private readonly IInput _input;

    public Time Time { get; } = new();

    /// <summary>
    /// The physics world of the scene the game is playing: what its bodies touched, knocked and shoved, gravity, and
    /// questions about the scene (<see cref="Physics.Raycast"/>, <see cref="Physics.OverlapBox"/>...). Owned and stepped by
    /// the game's physics system, which starts it afresh for each scene.
    /// </summary>
    public Physics Physics => _physicsSystem.World;

    /// <summary>Lines, boxes, circles and text drawn over the game for a frame, to see what code is doing.</summary>
    [field: NotState("what code draws to debug, for a while")]
    public DebugDraw Debug { get; }

    /// <summary>Values kept between runs (high scores, progress). Set by the host; in memory only by default.</summary>
    /// <remarks>
    /// Players' own input bindings (<see cref="IInput.Rebind"/>) and settings (volumes, sound on or off, fullscreen) are
    /// kept here too, and come back when it's set.
    /// </remarks>
    public SaveData SaveData
    {
        get => _saveData;
        set
        {
            _saveData = value;
            (Input as InputState)?.UseSaveData(value);
            _settings.LoadSettings();
        }
    }

    [NotState("kept as its text (see CaptureState)")]
    private SaveData _saveData = new();

    /// <summary>Set by the platform; silent by default (tests, headless). The player's saved volumes are applied to it.</summary>
    public IAudio Audio
    {
        get => _audio;
        set
        {
            _audio = value;
            _settings.LoadSettings();
        }
    }

    [NotState("the host's")]
    private IAudio _audio = new SilentAudio();

    /// <summary>Asks the host to close the game; in the editor, play mode stops.</summary>
    public void Quit()
    {
        Crowds.ThrowIfRunning("quit the game");
        if (IsQuitting)
            return;
        IsQuitting = true;
        _settings.SaveKept(now: true);
        QuitRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds <paramref name="entity"/> to the scene. Its behaviors start on the next frame; its <see cref="TableValues"/>
    /// set their values straight away. One spawned with no parent where a minigame is being played (see
    /// <see cref="PlayMinigame"/>), such as a bullet or a falling piece, goes into the minigame, staying where it is.
    /// </summary>
    public Entity Spawn(Entity entity, Entity? parent = null)
    {
        parent ??= _minigames.MinigameAt(entity);
        Scene.Add(entity, parent);
        _tableSystem.ApplyTables(entity);
        return entity;
    }

    /// <summary>Adds a new copy of a prefab ("Bullet" or "Prefabs/Bullet.prefab") to the scene.</summary>
    public Entity Spawn(string prefab, Entity? parent = null)
    {
        Crowds.ThrowIfRunning("spawn");
        if (PrefabLoader is null)
            throw new InvalidOperationException("This game can't spawn prefabs (no prefab loader is set).");
        return Spawn(PrefabLoader(prefab), parent);
    }

    /// <summary>
    /// Adds a new copy of a prefab at <paramref name="position"/> (in the parent's space, or the world without one).
    /// The prefab's own Z is kept.
    /// </summary>
    public Entity Spawn(string prefab, Vector2 position, Entity? parent = null)
    {
        Crowds.ThrowIfRunning("spawn");
        if (PrefabLoader is null)
            throw new InvalidOperationException("This game can't spawn prefabs (no prefab loader is set).");
        var entity = PrefabLoader(prefab);
        entity.Transform.Position = new Vector3(position, entity.Transform.Position.Z);
        return Spawn(entity, parent);
    }

    /// <summary>The entity called <paramref name="name"/>; in a network game, this machine's player's own first (see <see cref="Scene.FindNamed"/>).</summary>
    public Entity? Find(string name) => Scene.FindNamed(name);

    /// <summary>Every entity called <paramref name="name"/>, e.g. all the copies of the "Enemy" prefab.</summary>
    public IReadOnlyList<Entity> FindAll(string name) => EntityNames.Every(Scene, name);

    /// <summary>The first component of type <typeparamref name="T"/> in the scene, e.g. <c>Game.Find&lt;ScoreCounter&gt;()</c>.</summary>
    public T? Find<T>() where T : Component => Scene.AllNow<T>().FirstOrDefault();

    /// <summary>Every component of type <typeparamref name="T"/> in the scene, e.g. <c>Game.FindAll&lt;Enemy&gt;()</c>.</summary>
    public IReadOnlyList<T> FindAll<T>() where T : Component => Scene.All<T>();

    /// <summary>Calls OnDestroy on every started behavior. The game can't be ticked afterwards.</summary>
    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;

        foreach (var behavior in Scene.AllNow<Behavior>())
            behavior.RunDestroy();
        _network.Leave("The game closed.");
        _settings.SaveSettings();
        SystemsSceneEnding();
        _atmosphere.StopSoundSources();
        Audio.StopAll();
    }

    /// <summary>
    /// What collects for a collider: its own entity's body, or the nearest ancestor's (a player's hitbox child).
    /// </summary>
    public static Entity? BodyOwner(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Body>() is not null)
                return e;
        }

        return null;
    }

    /// <summary>The layer an entity's Collider (or else its Tilemap) is on, or null.</summary>
    public static string? LayerOf(Entity entity) =>
        entity.GetComponent<Collider>()?.Layer ?? entity.GetComponent<Tilemap>()?.Layer;

    /// <summary>
    /// Where a mistake in the game's data goes, once each: a prefab that won't load, a name that finds nothing, a Table
    /// Values naming a row that isn't there. The editor shows it in Output and the Inspector, agents read it, and the game
    /// goes on; a mistake in code throws instead (see <see cref="ProblemLog"/>).
    /// </summary>
    [field: NotState("the host's, and a rewound game doesn't say a problem twice: each is said once a game")]
    public ProblemLog Problems { get; } = new();

    // The scene whose names (actions, outputs, wires' inputs) have been checked: each scene says once, as it starts, what
    // it names that nothing answers to.
    [NotState("a rewound game doesn't say it again: each problem is said once a game")]
    private Scene? _namesChecked;

    /// <summary>Problems the host has already said (the loader's warnings), so the game doesn't say them again.</summary>
    internal void AlreadySaid(IEnumerable<string> messages) => Problems.MarkSaid(messages);

    private void CheckNames()
    {
        _namesChecked = Scene;
        if (Components is not { } components)
            return;
        foreach (var (component, label, problem) in NameChecks.In(Scene, components))
            Warn(component, $"{component.Entity!.Name}: {label}: {problem}");
    }

    /// <summary>
    /// Each problem once a game, so one that comes up every step isn't said every step.
    /// </summary>
    public void Warn(string message) => Problems.Report(message);

    /// <summary>
    /// Says a mistake in the data of <paramref name="component"/>, once, naming it and its entity so the Inspector can
    /// flag it. The game goes on.
    /// </summary>
    public void Warn(Component component, string message) => Problems.Report(message, component);

    /// <summary>
    /// A new copy of a prefab for <paramref name="by"/>'s field, or null, with a warning (once), if it won't load: no such
    /// prefab, a file that doesn't read, or a game with no prefabs. A prefab named in the game's data is data, so a
    /// missing one never stops the game. <paramref name="what"/> says what the prefab is for in the warning ("its Effect").
    /// </summary>
    public Entity? TryLoadPrefab(string prefab, Component? by, string what)
    {
        var name = prefab.Trim();
        try
        {
            if (PrefabLoader is null)
                throw new InvalidOperationException("this game can't load prefabs");
            return PrefabLoader(name);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or KeyNotFoundException or ArgumentException or System.Text.Json.JsonException)
        {
            var label = by?.Entity is { } entity ? $"{by.GetType().Name} {entity.Name}: " : "";
            Problems.Report(new Problem($"{label}{what} \"{name}\" won't load ({ex.Message}).", by?.Entity?.Name, by?.GetType().Name));
            return null;
        }
    }

    /// <summary>Adds a new copy of a prefab to the scene as <see cref="Spawn(string, Entity?)"/> does, or warns and returns null if it won't load (see <see cref="TryLoadPrefab"/>).</summary>
    public Entity? TrySpawn(string prefab, Component? by, string what, Entity? parent = null)
    {
        Crowds.ThrowIfRunning("spawn");
        return TryLoadPrefab(prefab, by, what) is { } entity ? Spawn(entity, parent) : null;
    }

    /// <summary>Adds a new copy of a prefab at <paramref name="position"/> as <see cref="Spawn(string, Vector2, Entity?)"/> does, or warns and returns null if it won't load.</summary>
    public Entity? TrySpawn(string prefab, Component? by, string what, Vector2 position, Entity? parent = null)
    {
        Crowds.ThrowIfRunning("spawn");
        if (TryLoadPrefab(prefab, by, what) is not { } entity)
            return null;
        entity.Transform.Position = new Vector3(position, entity.Transform.Position.Z);
        return Spawn(entity, parent);
    }

    internal void Notify(Entity entity, Action<Behavior> action)
    {
        if (entity.Scene != Scene)
            return;
        foreach (var behavior in entity.GetComponents<Behavior>().Where(b => b.IsStarted).ToList())
            action(behavior);
    }

    /// <summary>
    /// What this machine runs: everything offline and on the host; on a player's machine, only its own things (HUD, menus).
    /// Not what's paused (see Entity.IsPaused): it's held still.
    /// </summary>
    public ComponentList<T> Active<T>() where T : Component => Scene.NetFlagged == 0 ? Scene.RunningNow<T>() : Where(Scene.RunningNow<T>(), Runs);

    /// <summary>
    /// What this machine runs, paused or not: for what reads or shows how things are rather than moving them on.
    /// </summary>
    public ComponentList<T> Present<T>() where T : Component => Scene.NetFlagged == 0 ? Scene.ActiveNow<T>() : Where(Scene.ActiveNow<T>(), Runs);

    /// <summary>
    /// What this machine moves: what it runs, less other players' own characters, plus this player's own; not what's paused.
    /// </summary>
    public ComponentList<T> Moving<T>() where T : Component => Scene.NetFlagged == 0 ? Scene.RunningNow<T>() : Where(Scene.RunningNow<T>(), Moves);

    /// <summary>
    /// Everything showing, wherever it's run: for what only changes how things look.
    /// </summary>
    public ComponentList<T> Showing<T>() where T : Component => Scene.ActiveNow<T>();

    /// <summary>
    /// Everything this machine runs, active or not.
    /// </summary>
    public ComponentList<T> RunHere<T>() where T : Component => Scene.NetFlagged == 0 ? Scene.AllNow<T>() : Where(Scene.AllNow<T>(), Runs);

    // Those whose entity passes: the same list when they all do (always, offline), so a frame allocates nothing.
    internal static ComponentList<T> Where<T>(ComponentList<T> components, Func<Entity, bool> test) where T : Component
    {
        var all = components.AsSpan();
        var first = 0;
        while (first < all.Length && test(all[first].Entity!))
            first++;
        if (first == all.Length)
            return components;

        var kept = new List<T>(all.Length);
        for (var i = 0; i < all.Length; i++)
        {
            if (i < first || (i > first && test(all[i].Entity!)))
                kept.Add(all[i]);
        }

        return new ComponentList<T>([.. kept]);
    }

    /// <summary>A position in 3D without its height: the 2D point the world draws it at.</summary>
    public static Vector2 Flat(Vector3 v) => new(v.X, v.Y);

    /// <summary>Plays the sound called <paramref name="sound"/> if it names one (a blank or null name plays nothing).</summary>
    public void PlayIfSet(string? sound)
    {
        if (!string.IsNullOrWhiteSpace(sound))
            PlayShared(sound.Trim());
    }

    /// <summary>Moves <paramref name="from"/> towards <paramref name="to"/> by at most <paramref name="step"/>, stopping on it.</summary>
    public static float MoveTowards(float from, float to, float step) =>
        MathF.Abs(to - from) <= step ? to : from + MathF.Sign(to - from) * step;

    /// <summary>Moves <paramref name="from"/> towards <paramref name="to"/> by at most <paramref name="step"/>, stopping on it.</summary>
    public static Vector2 MoveTowards(Vector2 from, Vector2 to, float step)
    {
        var gap = to - from;
        var length = gap.Length();
        return length <= step || length < 1e-6f ? to : from + gap / length * step;
    }
}
