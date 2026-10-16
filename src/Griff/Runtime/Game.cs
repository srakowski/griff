using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Runs a scene in steps of the same length (see <see cref="Runtime.Time.FixedDelta"/>): each frame the screen shows,
/// <see cref="Advance"/> takes as many as the real time since the last one holds, each going through the parts of the
/// game's <see cref="Frame"/>, phase by phase, from starting new behaviors through physics to sound. Spawning and
/// destroying during a step are safe; destroys take effect at the end of the step.
/// </summary>
public sealed partial class Game : IGameContext
{
    private readonly List<Entity> _pendingDestroy = [];
    private readonly List<GameTimer> _timers = new(16);
    private readonly List<Tween> _tweens = new(16);
    private readonly List<Coroutine> _coroutines = [];
    private readonly MessageBoard _messages = new();
    private readonly HashSet<(Entity Source, Entity Target)> _touches = [];

    /// <summary>
    /// Who touched what in this frame's physics, both ways round (triggers and what's in them too), for systems that look
    /// for themselves without making garbage.
    /// </summary>
    public HashSet<(Entity Source, Entity Target)> Touches => _touches;

    private string? _pendingScene;
    // A fade out on its way to a scene, or to a call (FadeThrough) with no scene change.
    private (string? Scene, Action? Then, float Seconds)? _fadingOut;
    private float _fadeInSeconds;

    // How close to fully covered (or clear) a fade has to get to count as there.
    private const float FadeSnap = 1e-4f;
    private bool _stopped;

    public Game(Scene scene, IInput input)
    {
        Scene = scene;
        _input = input;
        Net = new NetSession(this);
        ListenForCommands();
        Debug = new DebugDraw(Time);
        BuildFrame();
        (input as InputState)?.UseSaveData(_saveData);
        LoadSettings();
        SyncPanels();
        // A scene put back into a hit stop (a rewind) finishes it.
        if (scene.HeldStill > 0)
            Time.Freeze(scene.HeldStill);
    }

    public Scene Scene { get; private set; }

    /// <summary>Loads scenes by name for <see cref="LoadScene"/>. Set by the host (editor or player).</summary>
    public Func<string, Scene>? SceneLoader { get; set; }

    /// <summary>Creates entities from prefabs by name for <see cref="Spawn(string, Entity?)"/>. Set by the host (editor or player).</summary>
    public Func<string, Entity>? PrefabLoader { get; set; }

    /// <summary>Raised after <see cref="LoadScene"/> switches to a new scene.</summary>
    public event EventHandler<Scene>? SceneChanged;

    /// <summary>Raised when gameplay code calls <see cref="Quit"/>. The host closes the game (the editor stops playing).</summary>
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
    public FrameProfiler? Profiler { get; set; }

    /// <summary>
    /// How many entities a scene of this game has before its frames are built in chunks across the cores (see
    /// <see cref="Workers"/>), or null for the renderer's own choice. Tests set it to compare the two ways.
    /// </summary>
    internal int? ChunkedFrom { get; set; }

    /// <summary>The view's width divided by its height. Set by the host each frame; <see cref="ScreenAnchor"/> uses it.</summary>
    public float ViewAspect { get; set; } = 16f / 9f;

    /// <summary>
    /// How much of the screen the scene change's <see cref="FadeColor"/> covers, from 0 (none) to 1 (all of it).
    /// See <see cref="LoadScene"/>.
    /// </summary>
    public float FadeAmount
    {
        get => _fade.Value;
        private set => _fade.Set(Scene, value);
    }

    // Kept as it was when the step began, too, to draw the screen between steps.
    private Smoothed<float> _fade;

    // How much of the screen the fade covers in a frame drawn as `blend` says.
    internal float FadeDrawn(StepBlend blend) => blend.Of(_fade);

    /// <summary>The color the screen fades through between scenes.</summary>
    public Color FadeColor { get; private set; } = Color.Black;

    /// <summary>True while a fade is clearing to show the new scene (or the screen after <see cref="FadeThrough"/>).</summary>
    public bool IsUncovering => _fadingOut is null && FadeAmount > 0;

    /// <summary>True from <see cref="LoadScene"/> until the scene has switched (including the fade out).</summary>
    public bool IsChangingScene => _fadingOut is { Scene: not null } || _pendingScene is not null;

    // A fade or a scene change under way: a door waits for it rather than starting another trip.
    internal bool IsFadingOrChangingScene => _fadingOut is not null || _pendingScene is not null;

    /// <summary>True once <see cref="Quit"/> has been called.</summary>
    public bool IsQuitting { get; private set; }

    /// <summary>
    /// This machine's controls. (While the host of a network game runs another player's tools for them, theirs; see
    /// <see cref="InputOf"/> for whoever plays a particular entity.)
    /// </summary>
    public IInput Input => _actingInput ?? _input;

    private readonly IInput _input;

    /// <summary>This machine's own controls, even while the host runs another player's tools for them (see <see cref="Input"/>).</summary>
    public IInput LocalInput => _input;

    /// <summary>True if <paramref name="entity"/> goes at the end of this frame (see <see cref="Destroy"/>).</summary>
    public bool IsDestroying(Entity entity) => _pendingDestroy.Contains(entity);

    public Time Time { get; } = new();

    public Physics Physics { get; } = new();

    /// <summary>Lines, boxes, circles and text drawn over the game for a frame, to see what code is doing.</summary>
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
            LoadSettings();
        }
    }

    private SaveData _saveData = new();

    /// <summary>Set by the platform; silent by default (tests, headless). The player's saved volumes are applied to it.</summary>
    public IAudio Audio
    {
        get => _audio;
        set
        {
            _audio = value;
            LoadSettings();
        }
    }

    private IAudio _audio = new SilentAudio();

    /// <summary>
    /// Switches to another scene ("Level2" or "Scenes/Level2.scene") at the end of this frame.
    /// The current scene's behaviors get OnDestroy; music keeps playing.
    /// </summary>
    public void LoadScene(string nameOrPath) => LoadScene(nameOrPath, 0);

    /// <summary>
    /// Like <see cref="LoadScene(string)"/>, first fading the screen to <paramref name="color"/> (black by default) over
    /// <paramref name="fade"/> seconds while the game keeps running, then fading back in just as long once it's switched.
    /// Fades use real time, so they work from a paused menu. Loading again mid-fade just changes where it's headed.
    /// </summary>
    public void LoadScene(string nameOrPath, float fade, Color? color = null) => LoadScene(nameOrPath, fade, color, arriveAt: null);

    /// <summary>
    /// Like <see cref="LoadScene(string, float, Color?)"/>, with the players arriving at the entity called
    /// <paramref name="arriveAt"/> in the next scene (a door there, the way back), as a door's Arrive At does: the
    /// characters the players move stand where it is, and don't go straight back through a door there. A scene that
    /// remembers how it was left (see <see cref="RememberScene"/>) comes back so.
    /// </summary>
    /// <example><code>Game.LoadScene("Cave", fade: 0.4f, arriveAt: "Cave Mouth");</code></example>
    public void LoadScene(string nameOrPath, float fade, Color? color, string? arriveAt) =>
        LoadScene(nameOrPath, fade, color, arriveAt, traveller: null, fresh: false);

    /// <summary>
    /// Where a door sends whoever used it: they arrive at the spot, even if no player moves them.
    /// </summary>
    public void LoadScene(string nameOrPath, float fade, Color? color, string? arriveAt, Entity? traveller, bool fresh)
    {
        Crowds.ThrowIfRunning("change scenes");
        if (SceneLoader is null)
            throw new InvalidOperationException("This game can't load scenes (no scene loader is set).");

        // On a player's machine, going to another scene (a menu's Quit to Title) leaves the host's game.
        if (Net.IsClient || Net.IsConnecting)
            Net.Leave();

        _arriving = string.IsNullOrWhiteSpace(arriveAt) ? null : (arriveAt.Trim(), traveller?.Name);
        (_freshScene, _startingOver) = (fresh, false);

        if (fade > 0)
        {
            _fadingOut = (nameOrPath, null, fade);
            _fadeInSeconds = 0;
            FadeColor = color ?? Color.Black;
            return;
        }

        // Cut straight there; a fade on its way out clears over the time it would have taken.
        if (_fadingOut is { } fading)
            _fadeInSeconds = fading.Seconds;
        _fadingOut = null;
        _pendingScene = nameOrPath;
    }

    /// <summary>
    /// Starts the current scene over from its saved state at the end of this frame, e.g. after the player dies. A scene
    /// that remembers how it was left (see <see cref="RememberScene"/>) forgets it, and starts as its file has it.
    /// </summary>
    public void ReloadScene() => ReloadScene(0);

    /// <summary>Like <see cref="ReloadScene()"/>, fading out and back in as <see cref="LoadScene(string, float, Color?)"/> does.</summary>
    public void ReloadScene(float fade, Color? color = null) =>
        LoadScene(SceneName ?? throw new InvalidOperationException("This game doesn't know its scene's name, so it can't reload it."),
            fade, color, arriveAt: null, traveller: null, fresh: true);

    // The scene being gone to starts as its file has it, even if it's remembered (a restart).
    private bool _freshScene;

    /// <summary>
    /// Carries <paramref name="entity"/> (and its children) into the next scenes <see cref="LoadScene"/> switches to, still
    /// running: music players, a score kept across levels. It gets a <see cref="Scenes.KeepBetweenScenes"/> (if it hasn't
    /// one), which does it: it becomes a top-level entity there, in place of the very same entity there (say, back on the
    /// scene it came from) or one of the same name, so there's never two.
    /// </summary>
    public void KeepBetweenScenes(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Crowds.ThrowIfRunning("keep entities between scenes");
        if (entity.GetComponent<Scenes.KeepBetweenScenes>() is null)
            entity.AddComponent<Scenes.KeepBetweenScenes>();
    }

    /// <summary>Asks the host to close the game; in the editor, play mode stops.</summary>
    public void Quit()
    {
        Crowds.ThrowIfRunning("quit the game");
        if (IsQuitting)
            return;
        IsQuitting = true;
        SaveReadLines(now: true);
        QuitRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Calls <paramref name="action"/> once, <paramref name="seconds"/> of game time from now.
    /// With an <paramref name="owner"/>, the timer is cancelled if that entity leaves the scene first.
    /// </summary>
    public GameTimer After(float seconds, Action action, Entity? owner = null) =>
        AddTimer(new GameTimer(Time.Total + Math.Max(0, seconds), 0, action, owner));

    /// <summary>Calls <paramref name="action"/> every <paramref name="seconds"/> of game time until cancelled.</summary>
    public GameTimer Every(float seconds, Action action, Entity? owner = null)
    {
        if (!(seconds > 0))
            throw new ArgumentOutOfRangeException(nameof(seconds), "The interval must be more than zero.");
        return AddTimer(new GameTimer(Time.Total + seconds, seconds, action, owner));
    }

    /// <summary>
    /// Runs <paramref name="routine"/> over several frames: see <see cref="Coroutine"/> for what it can wait for. It runs
    /// straight away up to its first <c>yield return</c>. With an <paramref name="owner"/>, it stops if that entity leaves
    /// the scene first.
    /// </summary>
    public Coroutine StartCoroutine(System.Collections.IEnumerator routine, Entity? owner = null) =>
        StartCoroutine(new Coroutine(routine, owner, null));

    internal Coroutine StartCoroutine(System.Collections.IEnumerator routine, Behavior owner) =>
        StartCoroutine(new Coroutine(routine, null, owner));

    /// <summary>Stops every coroutine <paramref name="owner"/> started.</summary>
    internal void StopCoroutines(Behavior owner)
    {
        foreach (var coroutine in _coroutines.Where(c => c.OwnerBehavior == owner))
            coroutine.Stop();
    }

    /// <summary>
    /// Calls <paramref name="apply"/> every frame for <paramref name="seconds"/> of game time with progress from 0 to 1,
    /// shaped by <paramref name="ease"/> (<see cref="Ease.OutQuad"/> by default). The last call is always exactly 1.
    /// With an <paramref name="owner"/>, the tween is cancelled if that entity leaves the scene first.
    /// </summary>
    /// <example><code>Game.Tween(0.5f, t => text.Size = 1 + t, Ease.OutBack);</code></example>
    public Tween Tween(float seconds, Action<float> apply, Func<float, float>? ease = null, Entity? owner = null) =>
        AddTween(new Tween(Time.Total, seconds, apply, ease, owner, null));

    /// <summary>Glides <paramref name="entity"/> to <paramref name="position"/> (in its parent's space), keeping its Z.</summary>
    public Tween MoveTo(Entity entity, Vector2 position, float seconds, Func<float, float>? ease = null)
    {
        var transform = entity.Transform;
        var from = new Vector2(transform.Position.X, transform.Position.Y);
        return AddTween(new Tween(Time.Total, seconds,
            t => transform.Position = new Vector3(Vector2.Lerp(from, position, t), transform.Position.Z),
            ease, entity, (entity, nameof(MoveTo))));
    }

    /// <summary>Grows or shrinks <paramref name="entity"/> to <paramref name="scale"/> on X and Y, keeping its Z scale.</summary>
    public Tween ScaleTo(Entity entity, Vector2 scale, float seconds, Func<float, float>? ease = null)
    {
        var transform = entity.Transform;
        var from = new Vector2(transform.Scale.X, transform.Scale.Y);
        return AddTween(new Tween(Time.Total, seconds,
            t => transform.Scale = new Vector3(Vector2.Lerp(from, scale, t), transform.Scale.Z),
            ease, entity, (entity, nameof(ScaleTo))));
    }

    /// <summary>Turns <paramref name="entity"/> to <paramref name="degrees"/> around Z (360 or more spins it round).</summary>
    public Tween RotateTo(Entity entity, float degrees, float seconds, Func<float, float>? ease = null)
    {
        var transform = entity.Transform;
        var from = transform.Rotation.Z;
        return AddTween(new Tween(Time.Total, seconds,
            t => transform.Rotation = transform.Rotation with { Z = from + (degrees - from) * t },
            ease, entity, (entity, nameof(RotateTo))));
    }

    /// <summary>
    /// Fades <paramref name="entity"/>'s sprite, text and tilemap to <paramref name="alpha"/> (0 invisible, 1 opaque),
    /// e.g. <c>FadeTo(0, 0.3f).Then(() => Destroy(Entity))</c>.
    /// </summary>
    public Tween FadeTo(Entity entity, float alpha, float seconds, Func<float, float>? ease = null)
    {
        var sprite = entity.GetComponent<Sprite>();
        var text = entity.GetComponent<Text>();
        var tilemap = entity.GetComponent<Tilemap>();
        var (spriteFrom, textFrom, tilemapFrom) = (sprite?.Tint.A ?? 0, text?.Color.A ?? 0, tilemap?.Tint.A ?? 0);
        return AddTween(new Tween(Time.Total, seconds, t =>
        {
            if (sprite is not null)
                sprite.Tint = sprite.Tint with { A = spriteFrom + (alpha - spriteFrom) * t };
            if (text is not null)
                text.Color = text.Color with { A = textFrom + (alpha - textFrom) * t };
            if (tilemap is not null)
                tilemap.Tint = tilemap.Tint with { A = tilemapFrom + (alpha - tilemapFrom) * t };
        }, ease, entity, (entity, nameof(FadeTo))));
    }

    /// <summary>
    /// Tells everyone listening for <paramref name="message"/> (with <c>On</c>, a <see cref="Trigger"/>, a
    /// <see cref="Counter"/>...) straight away, passing along <paramref name="data"/>. Returns how many heard it; nobody
    /// listening is fine.
    /// </summary>
    /// <example><code>Game.Send("PlayerDied"); Game.Send("ScoreChanged", 10);</code></example>
    public int Send(string message, object? data = null) => Deliver(message, data, default);

    /// <summary>
    /// Like <see cref="Send(string, object?)"/>, saying who sent it: a <see cref="Trigger"/> whose From names
    /// <paramref name="from"/> (or something it's inside) hears it, and if <paramref name="from"/> or something it's inside
    /// has a <see cref="MessageScope"/>, only the part of the scene the scope says does. Griff's own components send their
    /// messages this way.
    /// </summary>
    /// <example><code>Game.Send("Opened", data: null, from: Entity);</code></example>
    public int Send(string message, object? data, Entity? from) => Deliver(message, data, TriggerSystem.ScopeOf(from));

    /// <summary>
    /// Sends <paramref name="message"/> only to <paramref name="to"/> (and with <paramref name="children"/>, everything
    /// inside it): its counters, triggers, sound players, panels and scripts listening with <c>On</c>, so one of two boards
    /// can be told something. <paramref name="from"/> says who sent it, for a <see cref="Trigger"/>'s From.
    /// </summary>
    /// <example><code>Game.SendTo(Game.Find("Board A")!, "Bonus", 50, children: true);</code></example>
    public int SendTo(Entity to, string message, object? data = null, bool children = false, Entity? from = null)
    {
        ArgumentNullException.ThrowIfNull(to);
        return Deliver(message, data, new MessageRoute(from, to, children));
    }

    private int Deliver(string message, object? data, MessageRoute route)
    {
        Crowds.ThrowIfRunning("send messages");
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        // On the host, everyone else's machine hears it too (their HUD counts it, their sound players play).
        if (Net.IsHost && !_netMessage)
            Net.Relay(message, data, route);
        var heard = _messages.Send(message, data, Scene, route);

        // Each kind that hears it is picked out first (what hearing it does can change the scene), into lists kept for the
        // next message, so sending one makes no garbage.
        var picked = PickList();
        try
        {
            // Counters add up their message with no listener of their own: the number sent, or 1 (unless they count
            // something else); or 1 a message, whatever it sends.
            foreach (var counter in Active<Counter>())
            {
                if (Named(counter.Message, message) && !counter.CountsOwn && route.Reaches(counter.Entity))
                    picked.Add(counter);
            }

            var counters = picked.Count;
            foreach (var component in picked)
            {
                var counter = (Counter)component;
                counter.Add(counter.OnePerMessage ? 1 : CountOf(data));
            }

            // So do sound players.
            picked.Clear();
            foreach (var player in Scene.ActiveNow<SoundPlayer>())
            {
                if (Named(player.Message, message) && route.Reaches(player.Entity))
                    picked.Add(player);
            }

            var players = picked.Count;
            foreach (var player in picked)
                Play((SoundPlayer)player);

            // And components that hear messages themselves (a dungeon going down a floor on its message) where the game is run.
            var done = 0;
            picked.Clear();
            foreach (var hearer in Scene.ActiveNow<IHearsMessages>())
            {
                if (hearer is Component component)
                    picked.Add(component);
            }

            foreach (var component in picked)
            {
                if (component.Entity is { } entity && Runs(entity) && route.Reaches(entity) && ((IHearsMessages)component).Hear(message, data))
                    done++;
            }

            // And triggers do what they do.
            done += TriggerSystem.FireTriggers(message, data, route);

            // And panels open on theirs (a puzzle's "Solved" screen), if what they're in is showing and they need no flag that
            // isn't set: after the triggers, so one that sets the flag on this message lets it open.
            picked.Clear();
            foreach (var panel in Scene.AllNow<Panel>())
            {
                if (Named(panel.OpenMessage, message) && !panel.Entity!.IsEnabled && (panel.Entity.Parent is null || panel.Entity.Parent.IsActive) &&
                    route.Reaches(panel.Entity) && FlagHolds(panel.NeedsFlag))
                    picked.Add(panel);
            }

            foreach (var panel in picked)
                OpenPanel((Panel)panel);
            return heard + counters + players + picked.Count + done;
        }
        finally
        {
            picked.Clear();
            _pickLists.Push(picked);
        }

        static bool Named(string? name, string message) => name is not null && name.AsSpan().Trim().SequenceEqual(message);
    }

    // Lists Deliver picks out what hears a message into, one for each message being sent at once (one sent as another is
    // heard), kept for the next.
    private readonly Stack<List<Component>> _pickLists = new();

    private List<Component> PickList() => _pickLists.Count > 0 ? _pickLists.Pop() : [];

    /// <summary>The middle of an entity: its collider's, or its position.</summary>
    public static Vector2 CenterOf(Entity entity)
    {
        if (entity.GetComponent<Collider>() is { } collider)
            return collider.WorldBounds.Center;
        var position = entity.Transform.WorldPosition;
        return new Vector2(position.X, position.Y);
    }

    private static int CountOf(object? data) => data switch
    {
        null => 1,
        int value => value,
        float or double or decimal or long or short or byte => (int)Math.Round(Convert.ToDouble(data, System.Globalization.CultureInfo.InvariantCulture)),
        _ => 1,
    };

    /// <summary>
    /// Calls <paramref name="action"/> with the data of every <paramref name="message"/> sent from now on, until cancelled.
    /// With an <paramref name="owner"/>, it stops if that entity leaves the scene.
    /// </summary>
    public MessageListener On(string message, Action<object?> action, Entity? owner = null)
    {
        Crowds.ThrowIfRunning("listen for messages");
        return _messages.Add(new MessageListener(message, action, owner, null));
    }

    internal MessageListener On(string message, Action<object?> action, Behavior owner)
    {
        Crowds.ThrowIfRunning("listen for messages");
        return _messages.Add(new MessageListener(message, action, null, owner));
    }

    /// <summary>Stops every message listener <paramref name="owner"/> started.</summary>
    internal void StopListening(Behavior owner) => _messages.StopAll(owner);

    /// <summary>
    /// Adds <paramref name="entity"/> to the scene. Its behaviors start on the next frame; its <see cref="TableValues"/>
    /// set their values straight away. One spawned with no parent where a minigame is being played (see
    /// <see cref="PlayMinigame"/>), such as a bullet or a falling piece, goes into the minigame, staying where it is.
    /// </summary>
    public Entity Spawn(Entity entity, Entity? parent = null)
    {
        parent ??= MinigameAt(entity);
        Scene.Add(entity, parent);
        ApplyTables(entity);
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

    /// <summary>Removes <paramref name="entity"/> and its children at the end of the frame.</summary>
    public void Destroy(Entity entity)
    {
        Crowds.ThrowIfRunning("destroy entities");
        if (!_pendingDestroy.Contains(entity))
            _pendingDestroy.Add(entity);
    }

    /// <summary>The entity called <paramref name="name"/>; in a network game, this machine's player's own first (see <see cref="Scene.FindNamed"/>).</summary>
    public Entity? Find(string name) => Scene.FindNamed(name);

    /// <summary>Every entity called <paramref name="name"/>, e.g. all the copies of the "Enemy" prefab.</summary>
    public IReadOnlyList<Entity> FindAll(string name) => Scene.Entities.Where(e => e.Name == name).ToList();

    /// <summary>The first component of type <typeparamref name="T"/> in the scene, e.g. <c>Game.Find&lt;ScoreCounter&gt;()</c>.</summary>
    public T? Find<T>() where T : Component => Scene.AllNow<T>().FirstOrDefault();

    /// <summary>Every component of type <typeparamref name="T"/> in the scene, e.g. <c>Game.FindAll&lt;Enemy&gt;()</c>.</summary>
    public IReadOnlyList<T> FindAll<T>() where T : Component => Scene.All<T>();

    /// <summary>
    /// Freezes the game for <paramref name="seconds"/> of real time (a tenth of a second or less is plenty) so a big hit
    /// lands with a thud. Everything on game time stops; camera shake, fades and the screen keep going.
    /// </summary>
    public void HitStop(float seconds = 0.08f)
    {
        Crowds.ThrowIfRunning("stop time");
        // A network game is shared: one player's big hit can't freeze everyone's.
        if (seconds > 0 && !Net.IsOnline)
            Time.Freeze(seconds);
    }

    /// <summary>
    /// Slows the game to <paramref name="scale"/> (0.3 is a third of normal speed) for <paramref name="seconds"/> of real
    /// time, easing back to full speed at the end: the last enemy falling, a near miss. It works on top of
    /// <see cref="Time.Scale"/>; see <see cref="Time.SlowMotionScale"/>.
    /// </summary>
    public void SlowMotion(float scale = 0.3f, float seconds = 0.6f)
    {
        Crowds.ThrowIfRunning("slow time");
        if (seconds > 0 && float.IsFinite(scale) && !Net.IsOnline)
            Time.SlowDown(Math.Clamp(scale, 0, 1), seconds);
    }

    /// <summary>
    /// Covers the whole screen in <paramref name="color"/> (white by default), fading away over <paramref name="seconds"/>
    /// of real time: a flash of light, red for getting hurt. Give the color some transparency for a gentler flash,
    /// e.g. <c>Color.Red with { A = 0.3f }</c>. A new flash replaces one in progress.
    /// </summary>
    public void FlashScreen(Color? color = null, float seconds = 0.15f)
    {
        Crowds.ThrowIfRunning("flash the screen");
        if (!(seconds > 0))
            return;
        (_screenFlash, _screenFlashSeconds) = (color ?? Color.White, seconds);
        _screenFlashLeft.Set(Scene, seconds);
    }

    /// <summary>The color <see cref="FlashScreen"/> is covering the screen with right now; transparent when there's no flash.</summary>
    public Color ScreenFlash => ScreenFlashDrawn(StepBlend.AsIs);

    // The flash in a frame drawn as `blend` says.
    internal Color ScreenFlashDrawn(StepBlend blend)
    {
        var left = blend.Of(_screenFlashLeft);
        return left > 0 ? _screenFlash with { A = _screenFlash.A * Math.Min(1, left / _screenFlashSeconds) } : Color.Transparent;
    }

    private Color _screenFlash;
    private float _screenFlashSeconds;
    private Smoothed<float> _screenFlashLeft;

    private void RunScreenFlash() => _screenFlashLeft.Set(Scene, Math.Max(0, _screenFlashLeft.Value - Time.UnscaledDelta));

    /// <summary>
    /// Paints <paramref name="entity"/>'s sprites and its children's <paramref name="color"/> (white by default) for
    /// <paramref name="seconds"/>; see <see cref="Sprite.Flash"/>.
    /// </summary>
    public void Flash(Entity entity, Color? color = null, float seconds = 0.1f)
    {
        foreach (var sprite in entity.SelfAndDescendants().SelectMany(e => e.GetComponents<Sprite>()))
            sprite.Flash(color, seconds);
    }

    /// <summary>Shakes the camera the game is viewed through, if there is one. See <see cref="Camera.Shake"/>.</summary>
    public void ShakeCamera(float strength = 0.3f, float duration = 0.25f)
    {
        Crowds.ThrowIfRunning("shake the camera");
        Views.FindCamera(Scene)?.GetComponent<Camera>()?.Shake(strength, duration);
    }

    /// <summary>Calls OnDestroy on every started behavior. The game can't be ticked afterwards.</summary>
    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;

        foreach (var behavior in Scene.AllNow<Behavior>())
            behavior.RunDestroy();
        Net.Leave("The game closed.");
        SaveSettings();
        SystemsSceneEnding();
        StopSoundSources();
        Audio.StopAll();
    }

    private Coroutine StartCoroutine(Coroutine coroutine)
    {
        Crowds.ThrowIfRunning("start coroutines");
        _coroutines.Add(coroutine);
        coroutine.Run(this);
        return coroutine;
    }

    // Coroutines started by other coroutines have already taken their first step, so they wait for the next frame.
    private void RunCoroutines()
    {
        if (_coroutines.Count == 0)
            return;
        foreach (var coroutine in _coroutines.ToList())
        {
            // One whose entity (or script) is paused waits, its waits put off as long (see Entity.IsPaused).
            if (coroutine.IsPaused)
                coroutine.Hold(Time.Delta);
            else
                coroutine.Run(this);
        }
        _coroutines.RemoveAll(c => !c.IsRunning);
    }

    private GameTimer AddTimer(GameTimer timer)
    {
        Crowds.ThrowIfRunning("start timers");
        _timers.Add(timer);
        return timer;
    }

    // A new tween replaces an unfinished one with the same key (the same kind of tween on the same entity).
    private Tween AddTween(Tween tween)
    {
        Crowds.ThrowIfRunning("start tweens");
        if (tween.Key is not null)
        {
            foreach (var other in _tweens.Where(t => Equals(t.Key, tween.Key)))
                other.Cancel();
        }

        _tweens.Add(tween);
        return tween;
    }

    // The tweens and timers a frame runs, copied out (into lists kept for the next frame, so running them makes no garbage)
    // since what they call can start more.
    private readonly List<Tween> _runningTweens = new(16);
    private readonly List<GameTimer> _runningTimers = new(16);

    // Tweens added by callbacks (e.g. chained with Then) start on the next frame.
    private void RunTweens()
    {
        if (_tweens.Count == 0)
            return;
        _runningTweens.AddRange(_tweens);
        try
        {
            foreach (var tween in _runningTweens)
            {
                // One on a paused entity holds where it is, finishing as much later (see Entity.IsPaused).
                if (tween.Owner is { IsPaused: true })
                    tween.Hold(Time.Delta);
                else
                    tween.Run(Time.Total, Scene);
            }
        }
        finally
        {
            _runningTweens.Clear();
        }

        _tweens.RemoveAll(t => !t.IsActive);
    }

    // Timers added by callbacks wait for the next frame, like spawned behaviors.
    private void RunTimers()
    {
        if (_timers.Count == 0)
            return;
        _runningTimers.AddRange(_timers);
        try
        {
            foreach (var timer in _runningTimers)
            {
                // One whose entity is paused goes off as much later (see Entity.IsPaused).
                if (timer.Owner is { IsPaused: true })
                    timer.Hold(Time.Delta);
                else
                    timer.Run(Time.Total, Scene);
            }
        }
        finally
        {
            _runningTimers.Clear();
        }

        foreach (var timer in _timers)
        {
            if (timer.Owner is not null && timer.Owner.Scene != Scene)
                timer.Cancel();
        }

        _timers.RemoveAll(t => !t.IsActive);
    }

    // A fade out ends by requesting the scene, which switches at the end of this frame; the fade in starts on the next.
    private void AdvanceFade()
    {
        var seconds = Time.UnscaledDelta;
        if (_fadingOut is { } fading)
        {
            FadeAmount = Snap(FadeAmount + seconds / fading.Seconds);
            if (FadeAmount < 1)
                return;
            _fadingOut = null;
            _pendingScene = fading.Scene;
            _fadeInSeconds = fading.Seconds;
            fading.Then?.Invoke();
        }
        else if (_fadeInSeconds > 0 && _pendingScene is null)
        {
            FadeAmount = Snap(FadeAmount - seconds / _fadeInSeconds);
            if (FadeAmount <= 0)
                _fadeInSeconds = 0;
        }

        // Rounds the last sliver off, so 0.1 + 0.1 + 0.1 of a 0.3 second fade can't cost an extra frame.
        static float Snap(float amount) => amount >= 1 - FadeSnap ? 1 : amount <= FadeSnap ? 0 : amount;
    }

    private void SwitchSceneIfRequested()
    {
        if (SwitchToLoadedGame() || _pendingScene is not { } name)
            return;
        _pendingScene = null;
        SwitchScene(name);
    }

    // The lambdas here capture what's staying, so they're kept out of the step every frame takes. A saved game's scene
    // comes `loaded`; otherwise it's loaded by name.
    private void SwitchScene(string name, Scene? loaded = null)
    {
        // Minigames being played end with the scene, giving the input back first (so a remembered scene has it); what the
        // scene carries is saved as it is, and the scene remembered as it's left if it says so (not when it's starting over,
        // nor for a saved game being loaded, which brings its own).
        foreach (var root in Scene.AllNow<MinigameRoot>())
        {
            if (!root.Ended)
            {
                GiveFocusBack(root.FocusBefore);
                root.Ended = true;
            }
        }

        _endingMinigames.Clear();
        var target = SceneNameOf(name);
        var restarting = loaded is null && _freshScene;
        if (!_startingOver || loaded is not null)
            FlushCarried();
        _startingOver = false;
        if (restarting)
            _remembered.Remove(target);
        if (loaded is null && !restarting)
            RememberLeaving();
        _freshScene = false;

        SystemsSceneEnding();

        var next = loaded ?? (restarting ? null : Recalled(name)) ?? SceneLoader!(name);

        // Nothing glides into the new scene: after this step it's drawn as it is.
        next.Step = next.FirstStep = Scene.Step;

        // Kept entities (see KeepBetweenScenes) move across still running; a kept child goes along with a kept ancestor.
        var moving = KeptEntities();
        var staying = moving.SelectMany(e => e.SelfAndDescendants()).ToHashSet();

        foreach (var behavior in Scene.Entities.Where(e => !staying.Contains(e)).SelectMany(e => e.GetComponents<Behavior>()))
            behavior.RunDestroy();
        _pendingDestroy.Clear();
        Physics.Reset();

        foreach (var entity in moving)
        {
            Scene.Remove(entity);
            Arrive(entity, next);
        }

        Scene = next;
        if (name.Length > 0 || loaded is null)
            SceneName = SceneNameOf(name);
        _messages.Prune(next);
        _controlsSeen.RemoveWhere(c => c.Entity?.Scene != next);
        if (loaded is not null)
            _arriving = null;
        var arrived = PlaceArrivals();
        SystemsSceneChanged();
        if (arrived is not null)
            _levels.ArrivedThroughDoors(arrived);
        LoadCarried(null);
        Physics.Groups = [];
        StopSoundSources();
        _focus = null;
        SyncPanels();
        if (Net.IsHost)
            Net.StartHostScene();
        else if (_netLoading)
        {
            _netLoading = false;
            Net.HostSceneLoaded();
        }

        SceneChanged?.Invoke(this, next);
    }

    // "Scenes/Level2.scene" and "Level2" are both "Level2".
    private static string SceneNameOf(string nameOrPath)
    {
        var name = nameOrPath[(nameOrPath.LastIndexOfAny(['/', '\\']) + 1)..];
        return name.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) ? name[..^".scene".Length] : name;
    }

    // Followers move after physics (so they see where things ended up) and before screen anchors (which pin to the camera).
    private void RunFollowers()
    {
        // A viewport's camera keeps its own view (its part of the screen's shape) inside what it stays in.
        foreach (var follow in Moving<Follow>())
            follow.Advance(Time.Delta, AspectOf(follow.Entity!), Time.UnscaledDelta);
    }

    // Pins ScreenAnchor entities to their point on the camera's view, parents before children.
    private void LayOutScreen()
    {
        var anchors = Scene.ActiveNow<ScreenAnchor>();
        if (anchors.Count == 0)
            return;

        var camera = Views.FindCamera(Scene);
        // What's behind the minigame that has the screen isn't seen, so it isn't pinned to its camera either.
        var shown = Minigame;
        var component = camera?.GetComponent<Camera>();
        var center = camera is null ? Vector2.Zero : new Vector2(camera.Transform.WorldPosition.X, camera.Transform.WorldPosition.Y);
        center += component?.ShakeOffset ?? Vector2.Zero;
        var halfHeight = component?.Size ?? Views.DefaultCameraSize;
        var half = new Vector2(halfHeight * (ViewAspect > 0 ? ViewAspect : 1), halfHeight);

        foreach (var anchor in anchors)
        {
            // An entity's first anchor places it.
            if (anchor.Entity!.GetComponent<ScreenAnchor>() != anchor || MinigameRoot.IsBehind(anchor.Entity, shown))
                continue;
            var transform = anchor.Entity!.Transform;
            var world = center + ScreenAnchor.Direction(anchor.Anchor) * half + anchor.Offset;
            var target = new Vector3(world, transform.WorldPosition.Z);
            if (anchor.Entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
                target = Vector3.Transform(target, inverse);
            transform.Position = new Vector3(target.X, target.Y, transform.Position.Z);
        }
    }

    private void OnCollision(Entity a, Entity b)
    {
        _touches.Add((a, b));
        _touches.Add((b, a));
        // Most things that bump (crates resting on the floor every step) have no scripts to tell.
        if (HasBehaviors(a))
            NotifyCollision(a, b);
        if (HasBehaviors(b))
            NotifyCollision(b, a);
    }

    private void NotifyCollision(Entity entity, Entity other) => Notify(entity, behavior => behavior.RunCollision(other));

    private static bool HasBehaviors(Entity entity)
    {
        foreach (var component in entity.ComponentList)
        {
            if (component is Behavior)
                return true;
        }

        return false;
    }

    private void OnTrigger(Entity trigger, Entity other, bool entered)
    {
        // Most things going in and out of triggers (a ball through a pinball lane) have no scripts to tell (and telling them
        // is apart, so nothing is made for them unless there are).
        if (HasBehaviors(trigger))
            NotifyTrigger(trigger, other, entered);
        if (HasBehaviors(other))
            NotifyTrigger(other, trigger, entered);
    }

    private void NotifyTrigger(Entity entity, Entity other, bool entered) => Notify(entity, behavior => behavior.RunTrigger(other, entered));

    // Lifetimes run on game time; the fade out is an ordinary FadeTo, so it shows on sprites, text and tilemaps alike.
    private void RunLifetimes()
    {
        foreach (var lifetime in Active<Lifetime>())
        {
            lifetime.Age += Time.Delta;
            if (!lifetime.IsFading && lifetime.FadeOut > 0 && lifetime.Age >= lifetime.Seconds - lifetime.FadeOut)
            {
                lifetime.IsFading = true;
                FadeTo(lifetime.Entity!, 0, Math.Max(0, lifetime.Seconds - lifetime.Age));
            }

            if (lifetime.Age >= lifetime.Seconds)
                Destroy(lifetime.Entity!);
        }
    }

    private void RunSpawners()
    {
        foreach (var spawner in Active<Spawner>())
            spawner.Advance(Time.Delta, this);
    }

    // Typewriters run on real time, so dialogue still types out while a talk holds the game still; a pause menu holds them
    // still too, all but those on an open panel.
    private void RunTypewriters()
    {
        foreach (var typewriter in Scene.RunningNow<Typewriter>())
        {
            if (Time.IsPaused && !InOpenPanel(typewriter.Entity!))
                continue;
            if (typewriter.Advance(Time.UnscaledDelta))
                Audio.Play(typewriter.Sound!);
        }
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

    // Bars and texts show how things are, so they go on showing it while they're paused.
    private void RunBars()
    {
        foreach (var bar in Present<Bar>())
            bar.Advance(Time.Delta, Scene);
    }

    // Stars show how a level went on every machine, filling one after another in real time, so they go on under a panel
    // that pauses the game.
    private void RunStars()
    {
        foreach (var stars in Showing<Stars>())
        {
            var saved = string.IsNullOrWhiteSpace(stars.Saved) ? 0 : SaveData.GetInt(stars.Saved.Trim());
            if (stars.Advance(Time.UnscaledDelta, saved) && !string.IsNullOrWhiteSpace(stars.Sound))
                Audio.Play(stars.Sound.Trim());
        }
    }

    // Counters show their count, keep their best, and pop when the count changes.
    private void RunCounters()
    {
        foreach (var counter in Active<Counter>())
        {
            var transform = counter.Entity!.Transform;
            var key = counter.SaveBestAs?.Trim();
            if (!string.IsNullOrWhiteSpace(counter.Item))
                counter.Change(ItemSystem.ItemCount(counter.Item, counter.Entity) - counter.Value);
            if (counter.CountsFromSource)
                CountFromSource(counter);
            if (!string.IsNullOrWhiteSpace(counter.Output))
                CountOutput(counter);

            if (counter.Shown is null)
            {
                counter.RestingScale = transform.Scale;
                counter.Best = string.IsNullOrEmpty(key) ? counter.Value : SaveData.GetInt(key);
                if (counter.BestToBeat < 0 && !string.IsNullOrEmpty(key))
                    counter.BestToBeat = SaveData.Has(key) ? counter.Best : 0;
            }

            if (counter.Value > counter.Best)
            {
                // Beating the best kept as the scene started is a new best, said once (and not when nothing was kept).
                if (counter.BestToBeat > 0 && counter.Best <= counter.BestToBeat && !string.IsNullOrWhiteSpace(counter.NewBestMessage) &&
                    Runs(counter.Entity!))
                    Send(counter.NewBestMessage.Trim(), counter.Value, from: counter.Entity);
                counter.Best = counter.Value;
                if (!string.IsNullOrEmpty(key))
                    SaveData.Set(key, counter.Best);
            }

            // The text is only written again when what it shows has changed, so a steady count makes no garbage.
            if (counter.Entity.GetComponent<Text>() is { } text &&
                (counter.Displayed != (counter.Value, counter.Best, counter.Total, counter.Format) || text.Value != counter.DisplayedText))
            {
                var display = counter.Display();
                counter.Displayed = (counter.Value, counter.Best, counter.Total, counter.Format);
                counter.DisplayedText = display;
                if (text.Value != display)
                    text.Value = display;
            }

            ReachedOrNot(counter);

            // A change pops it bigger, springing back over a moment (in game time, held while it's paused), with no garbage:
            // a pinball score changes all the time.
            if (counter.RestingScale is { } rest)
            {
                if (counter.Shown is { } shown && shown != counter.Value && counter.Pop > 0 && counter.Pop != 1)
                {
                    transform.Scale = new Vector3(rest.X * counter.Pop, rest.Y * counter.Pop, rest.Z);
                    counter.Popping = (0, counter.Pop);
                }
                else if (counter.Popping is var (age, pop) && !counter.Entity.IsPaused)
                {
                    age += Time.Delta;
                    var size = age >= Counter.PopTime ? 1 : pop + (1 - pop) * Ease.OutBack(age / Counter.PopTime);
                    transform.Scale = new Vector3(rest.X * size, rest.Y * size, transform.Scale.Z);
                    counter.Popping = age >= Counter.PopTime ? null : (age, pop);
                }
            }

            counter.Shown = counter.Value;
        }
    }

    // A counter showing what a component in the scene counts (see ICounterSource): the goals covered out of how many there
    // are, a move history's moves, a match board's swaps out of its Moves, its score out of its Target Score or the gems of
    // a kind cleared out of how many it asks for, a falling-block board's score, a dungeon's floor. The first that counts
    // what the counter asks for.
    private void CountFromSource(Counter counter)
    {
        foreach (var source in Scene.ActiveNow<ICounterSource>())
        {
            if (source.CountFor(counter) is not { } count)
                continue;
            counter.Total = count.Total;
            counter.Change(count.Value - counter.Value);
            return;
        }
    }

    // Texts showing an output fill it into what they say, made again only when it (or what it's out of) changes.
    private void ShowOutputs()
    {
        foreach (var text in Present<Text>())
        {
            if (string.IsNullOrWhiteSpace(text.Output))
            {
                if (text.ReadFrom is not null)
                {
                    text.ReadFrom = null;
                    text.SetReadout(null);
                }

                continue;
            }

            var reader = text.Reader;
            var found = reader.Find(text.Entity!, text.Track, text.Output);
            var (number, name, outOf) = found ? (reader.Number, reader.Text, reader.OutOf) : (0f, "", 0f);
            if (text.ReadFrom is { } from && ReferenceEquals(from.Template, text.Value) && from.Number.Equals(number) && from.Name == name &&
                from.OutOf.Equals(outOf))
                continue;
            text.ReadFrom = (text.Value, number, name, outOf);
            text.SetReadout(Readout.Fill(text.Value, number, name, outOf, found ? reader.Output!.ValueType : typeof(string)));
        }
    }

    // A counter counting an output (a board's score, the dungeon's floor, a faction's gold), out of what it's out of.
    private static void CountOutput(Counter counter)
    {
        if (!counter.Reader.Find(counter.Entity!, counter.Track, counter.Output))
            return;
        counter.Total = (int)MathF.Round(counter.Reader.OutOf);
        counter.Change((int)MathF.Round(counter.Reader.Number) - counter.Value);
    }

    // As a counter reaches its value, it sends its Reach Message (where the game is run; a player's own counter, such as
    // one on their HUD, on their machine alone). Starting there, or being put back there by a rewind, isn't reaching it.
    private void ReachedOrNot(Counter counter)
    {
        var reached = counter.Reached;
        var was = counter.WasReached ?? reached;
        counter.WasReached = reached;
        if (!reached || was || string.IsNullOrWhiteSpace(counter.ReachMessage) || !Runs(counter.Entity!))
            return;
        var message = counter.ReachMessage.Trim();
        if (Net.IsOnline && NetLocalAttribute.IsOn(counter.Entity!))
            SendFromNetwork(message, counter.Value, relay: false, TriggerSystem.ScopeOf(counter.Entity));
        else
            Send(message, counter.Value, from: counter.Entity);
    }

    /// <summary>The layer an entity's Collider (or else its Tilemap) is on, or null.</summary>
    public static string? LayerOf(Entity entity) =>
        entity.GetComponent<Collider>()?.Layer ?? entity.GetComponent<Tilemap>()?.Layer;

    internal void Notify(Entity entity, Action<Behavior> action)
    {
        if (entity.Scene != Scene)
            return;
        foreach (var behavior in entity.GetComponents<Behavior>().Where(b => b.IsStarted).ToList())
            action(behavior);
    }

    private void FlushDestroys()
    {
        while (_pendingDestroy.Count > 0)
        {
            var batch = _destroying;
            batch.AddRange(_pendingDestroy);
            _pendingDestroy.Clear();
            foreach (var entity in batch)
            {
                if (entity.Scene != Scene || !Runs(entity))
                    continue;
                RunDestroys(entity);
                if (entity.Scene == Scene)
                    Scene.Remove(entity);
            }

            batch.Clear();
        }
    }

    // The batch of destroys being done (those asked for meanwhile wait for the next).
    private readonly List<Entity> _destroying = [];

    // Each script's OnDestroy on `entity` and inside it, depth first.
    private static void RunDestroys(Entity entity)
    {
        var components = entity.ComponentList;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i] is Behavior behavior)
                behavior.RunDestroy();
        }

        var children = entity.ChildList;
        for (var i = 0; i < children.Count; i++)
            RunDestroys(children[i]);
    }

    /// <summary>
    /// Fades the screen to <paramref name="color"/> (black by default) over <paramref name="seconds"/> of real time, calls
    /// <paramref name="then"/> while it's covered, and fades back in just as long: teleports, a jump to the next room, a
    /// cut in a cutscene. 0 seconds calls it straight away.
    /// </summary>
    public void FadeThrough(float seconds, Action then, Color? color = null)
    {
        ArgumentNullException.ThrowIfNull(then);
        if (!(seconds > 0))
        {
            then();
            return;
        }

        _fadingOut = (null, then, seconds);
        _fadeInSeconds = 0;
        FadeColor = color ?? Color.Black;
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
    private static ComponentList<T> Where<T>(ComponentList<T> components, Func<Entity, bool> test) where T : Component
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
