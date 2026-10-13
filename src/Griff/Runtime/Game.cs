using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Runs a scene: starts and updates behaviors each <see cref="Tick"/>. Spawning and destroying during a
/// frame are safe; destroys take effect at the end of the frame.
/// </summary>
public sealed partial class Game
{
    private readonly List<Entity> _pendingDestroy = [];
    private readonly List<GameTimer> _timers = [];
    private readonly List<Tween> _tweens = [];
    private readonly List<Coroutine> _coroutines = [];
    private readonly List<Entity> _kept = [];
    private readonly MessageBoard _messages = new();
    private readonly HashSet<(Entity Source, Entity Target)> _touches = [];
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
        (input as InputState)?.UseSaveData(_saveData);
        LoadSettings();
        SyncPanels();
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

    /// <summary>The view's width divided by its height. Set by the host each frame; <see cref="ScreenAnchor"/> uses it.</summary>
    public float ViewAspect { get; set; } = 16f / 9f;

    /// <summary>
    /// How much of the screen the scene change's <see cref="FadeColor"/> covers, from 0 (none) to 1 (all of it).
    /// See <see cref="LoadScene"/>.
    /// </summary>
    public float FadeAmount { get; private set; }

    /// <summary>The color the screen fades through between scenes.</summary>
    public Color FadeColor { get; private set; } = Color.Black;

    /// <summary>True while a fade is clearing to show the new scene (or the screen after <see cref="FadeThrough"/>).</summary>
    public bool IsUncovering => _fadingOut is null && FadeAmount > 0;

    /// <summary>True from <see cref="LoadScene"/> until the scene has switched (including the fade out).</summary>
    public bool IsChangingScene => _fadingOut is { Scene: not null } || _pendingScene is not null;

    /// <summary>True once <see cref="Quit"/> has been called.</summary>
    public bool IsQuitting { get; private set; }

    /// <summary>
    /// This machine's controls. (While the host of a network game runs another player's tools for them, theirs; see
    /// <see cref="InputOf"/> for whoever plays a particular entity.)
    /// </summary>
    public IInput Input => _actingInput ?? _input;

    private readonly IInput _input;

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

    /// <summary>Advances the game by <paramref name="seconds"/>: start new behaviors, update all, apply destroys.</summary>
    public void Tick(double seconds)
    {
        if (_stopped)
            throw new InvalidOperationException("The game has stopped.");

        // Panels shown or hidden since last frame open or close first, so a pause starts straight away.
        SyncPanels();
        _pausedAtStart = Time.IsPaused;
        Time.Advance(seconds);
        (Input as InputState)?.BeginFrame(Time.UnscaledDelta);
        Debug.BeginFrame();
        Net.BeginFrame(Time.UnscaledDelta);

        // Dungeons are made first, so what starts this frame starts on the floor (spawns included).
        RunDungeons();

        // Snapshot first: behaviors may spawn or destroy while we iterate. In a network game, the host runs scripts.
        var behaviors = ActiveBehaviors().Where(b => Runs(b.Entity!)).ToList();
        foreach (var behavior in behaviors.Where(b => !b.IsStarted))
            behavior.RunStart(this);

        StartWorlds();
        UpdateMenus();
        RunLobby();
        ApplySwitches();
        RunTurns();
        RunTactics();
        RunBeatClocks();

        // Before anything moves, so a move taken back this frame isn't stepped on from where it was.
        RunMoveHistory();

        // No-code enemies and players go before scripts, so a script can still take over a body this frame.
        RunEnemies();
        RunMovement();
        RunFirstPerson();
        RunItemUses();

        // While a panel pauses the game, only the scripts on open panels run.
        foreach (var behavior in behaviors)
        {
            if (behavior.Entity?.Scene == Scene && (!PausedThisFrame || InOpenPanel(behavior.Entity)))
                behavior.RunUpdate();
        }

        RunSandboxHud();
        RunBlocks();
        RunTools();
        ForEachRemotePlayer(() =>
        {
            RunSandboxHud();
            RunBlockPlayers();
            RunTools();
        });
        RunDragging();
        RunMatchBoards();
        RunFallingBoards();
        RunStrategy();
        RunAttacks();
        PassIdleGridMovers();
        RunTimers();
        RunCoroutines();
        RunTweens();
        RunLifetimes();
        RunSpawners();
        RunShooters();
        RunMovers();

        foreach (var animation in Scene.Entities.Where(Rendering.SceneRenderer.IsActive).SelectMany(e => e.GetComponents<Animation>()).ToList())
            animation.Advance(Time.Delta);
        RunAnimatedTiles();

        foreach (var emitter in Scene.Entities.Where(Rendering.SceneRenderer.IsActive).SelectMany(e => e.GetComponents<ParticleEmitter>()).ToList())
        {
            emitter.Advance(Time.Delta, Physics.Gravity);
            if (emitter.DestroyWhenFinished && emitter.IsFinished && Runs(emitter.Entity!))
                Destroy(emitter.Entity!);
        }

        foreach (var camera in Scene.Entities.Where(Rendering.SceneRenderer.IsActive).SelectMany(e => e.GetComponents<Camera>()))
        {
            camera.AdvanceShake(Time.UnscaledDelta);
            camera.AdvanceAmbient(Time.UnscaledDelta);
        }

        foreach (var sprite in Scene.Entities.Where(Rendering.SceneRenderer.IsActive).SelectMany(e => e.GetComponents<Sprite>()))
            sprite.AdvanceEffects(Time.Delta);

        FillPrompts();
        RunTypewriters();
        RunPickupSpin();

        RunThrusters();
        GroupWelds();
        if (Time.Delta > 0)
        {
            Physics.Step(Scene, Time.Delta, OnCollision, OnTrigger);
            foreach (var (trigger, other) in Physics.TriggerPairs)
            {
                _touches.Add((trigger, other));
                _touches.Add((other, trigger));
            }
        }

        RunRopes();
        CarryHeld();

        RunPickups();
        RunCheckpoints();
        RunDoors();
        RunDialogues();
        RunSwitches();
        RunGoals();
        RunWiring();
        RunBouncers();
        RunCrumbles();
        RunTouchDamage();
        RunHealth();
        RunRespawns();
        RunBars();
        RunCounters();
        AdvanceFade();
        _screenFlashLeft = Math.Max(0, _screenFlashLeft - Time.UnscaledDelta);
        FlushDestroys();
        SwitchSceneIfRequested();
        RunRooms();
        RunFollowers();
        LayOutScreen();
        RunTrails();
        RunLights();
        RunWeather();
        FillPrompts();
        RunSoundSources();
        RunSoundPlayers();
        CountPatrolWalks();
        Audio.Advance(Time.UnscaledDelta);
        SaveSettings();
        Net.EndFrame(Time.UnscaledDelta);
    }

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
    public void LoadScene(string nameOrPath, float fade, Color? color = null)
    {
        if (SceneLoader is null)
            throw new InvalidOperationException("This game can't load scenes (no scene loader is set).");

        // On a player's machine, going to another scene (a menu's Quit to Title) leaves the host's game.
        if (Net.IsClient || Net.IsConnecting)
            Net.Leave();

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

    /// <summary>Starts the current scene over from its saved state at the end of this frame, e.g. after the player dies.</summary>
    public void ReloadScene() => ReloadScene(0);

    /// <summary>Like <see cref="ReloadScene()"/>, fading out and back in as <see cref="LoadScene(string, float, Color?)"/> does.</summary>
    public void ReloadScene(float fade, Color? color = null) =>
        LoadScene(SceneName ?? throw new InvalidOperationException("This game doesn't know its scene's name, so it can't reload it."),
            fade, color);

    /// <summary>
    /// Carries <paramref name="entity"/> (and its children) into the next scenes <see cref="LoadScene"/> switches to, still
    /// running: music players, a score kept across levels. It becomes a top-level entity there. If the next scene has the
    /// very same entity (say, back on the scene it came from), the kept one replaces it, so there's never two.
    /// </summary>
    public void KeepBetweenScenes(Entity entity)
    {
        if (!_kept.Contains(entity))
            _kept.Add(entity);
    }

    /// <summary>Asks the host to close the game; in the editor, play mode stops.</summary>
    public void Quit()
    {
        if (IsQuitting)
            return;
        IsQuitting = true;
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
    /// Tells everyone listening for <paramref name="message"/> (with <c>On</c>) straight away, passing along
    /// <paramref name="data"/>. Returns how many listeners heard it; nobody listening is fine.
    /// </summary>
    /// <example><code>Game.Send("PlayerDied"); Game.Send("ScoreChanged", 10);</code></example>
    public int Send(string message, object? data = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        // On the host, everyone else's machine hears it too (their HUD counts it, their sound players play).
        if (Net.IsHost && !_netMessage)
            Net.Relay(message, data);
        var heard = _messages.Send(message, data, Scene);

        // Counters add up their message with no listener of their own: the number sent, or 1 (unless they count something else).
        var counters = Scene.Entities.Where(e => Rendering.SceneRenderer.IsActive(e) && Runs(e)).SelectMany(e => e.GetComponents<Counter>())
            .Where(c => c.Message?.Trim() == message && !c.CountsOwn).ToList();
        foreach (var counter in counters)
            counter.Add(CountOf(data));

        // So do sound players.
        var players = Scene.Entities.Where(Rendering.SceneRenderer.IsActive).SelectMany(e => e.GetComponents<SoundPlayer>())
            .Where(p => p.Message?.Trim() == message).ToList();
        foreach (var player in players)
            Play(player);

        // And panels open on theirs (a puzzle's "Solved" screen), if what they're in is showing.
        var panels = Scene.Entities.SelectMany(e => e.GetComponents<Panel>())
            .Where(p => p.OpenMessage?.Trim() == message && !p.Entity!.IsEnabled && (p.Entity.Parent is null || Rendering.SceneRenderer.IsActive(p.Entity.Parent)))
            .ToList();
        foreach (var panel in panels)
            OpenPanel(panel);

        // And dungeons go down to their next floor on theirs, at the start of the next frame.
        var dungeons = Active<DungeonGenerator>().Where(d => d.NextFloorMessage?.Trim() == message).ToList();
        foreach (var dungeon in dungeons)
            dungeon.NextFloor();
        return heard + counters.Count + players.Count + panels.Count + dungeons.Count;
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
    public MessageListener On(string message, Action<object?> action, Entity? owner = null) =>
        _messages.Add(new MessageListener(message, action, owner, null));

    internal MessageListener On(string message, Action<object?> action, Behavior owner) =>
        _messages.Add(new MessageListener(message, action, null, owner));

    /// <summary>Stops every message listener <paramref name="owner"/> started.</summary>
    internal void StopListening(Behavior owner) => _messages.StopAll(owner);

    /// <summary>Adds <paramref name="entity"/> to the scene. Its behaviors start on the next frame.</summary>
    public Entity Spawn(Entity entity, Entity? parent = null)
    {
        Scene.Add(entity, parent);
        return entity;
    }

    /// <summary>Adds a new copy of a prefab ("Bullet" or "Prefabs/Bullet.prefab") to the scene.</summary>
    public Entity Spawn(string prefab, Entity? parent = null)
    {
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
        if (PrefabLoader is null)
            throw new InvalidOperationException("This game can't spawn prefabs (no prefab loader is set).");
        var entity = PrefabLoader(prefab);
        entity.Transform.Position = new Vector3(position, entity.Transform.Position.Z);
        return Spawn(entity, parent);
    }

    /// <summary>Removes <paramref name="entity"/> and its children at the end of the frame.</summary>
    public void Destroy(Entity entity)
    {
        if (!_pendingDestroy.Contains(entity))
            _pendingDestroy.Add(entity);
    }

    /// <summary>The entity called <paramref name="name"/>; in a network game, this machine's player's own first (see <see cref="Scene.FindNamed"/>).</summary>
    public Entity? Find(string name) => Scene.FindNamed(name);

    /// <summary>Every entity called <paramref name="name"/>, e.g. all the copies of the "Enemy" prefab.</summary>
    public IReadOnlyList<Entity> FindAll(string name) => Scene.Entities.Where(e => e.Name == name).ToList();

    /// <summary>The first component of type <typeparamref name="T"/> in the scene, e.g. <c>Game.Find&lt;ScoreCounter&gt;()</c>.</summary>
    public T? Find<T>() where T : Component => Scene.Entities.SelectMany(e => e.GetComponents<T>()).FirstOrDefault();

    /// <summary>Every component of type <typeparamref name="T"/> in the scene, e.g. <c>Game.FindAll&lt;Enemy&gt;()</c>.</summary>
    public IReadOnlyList<T> FindAll<T>() where T : Component => Scene.Entities.SelectMany(e => e.GetComponents<T>()).ToList();

    /// <summary>
    /// Freezes the game for <paramref name="seconds"/> of real time (a tenth of a second or less is plenty) so a big hit
    /// lands with a thud. Everything on game time stops; camera shake, fades and the screen keep going.
    /// </summary>
    public void HitStop(float seconds = 0.08f)
    {
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
        if (seconds > 0)
            (_screenFlash, _screenFlashSeconds, _screenFlashLeft) = (color ?? Color.White, seconds, seconds);
    }

    /// <summary>The color <see cref="FlashScreen"/> is covering the screen with right now; transparent when there's no flash.</summary>
    public Color ScreenFlash => _screenFlashLeft > 0
        ? _screenFlash with { A = _screenFlash.A * (_screenFlashLeft / _screenFlashSeconds) }
        : Color.Transparent;

    private Color _screenFlash;
    private float _screenFlashSeconds;
    private float _screenFlashLeft;

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
    public void ShakeCamera(float strength = 0.3f, float duration = 0.25f) =>
        Rendering.SceneRenderer.FindCamera(Scene)?.GetComponent<Camera>()?.Shake(strength, duration);

    /// <summary>Calls OnDestroy on every started behavior. The game can't be ticked afterwards.</summary>
    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;

        foreach (var behavior in Scene.Entities.SelectMany(e => e.GetComponents<Behavior>()))
            behavior.RunDestroy();
        Net.Leave("The game closed.");
        SaveSettings();
        SaveWorlds();
        StopSoundSources();
        Audio.StopAll();
    }

    private Coroutine StartCoroutine(Coroutine coroutine)
    {
        _coroutines.Add(coroutine);
        coroutine.Run(this);
        return coroutine;
    }

    // Coroutines started by other coroutines have already taken their first step, so they wait for the next frame.
    private void RunCoroutines()
    {
        foreach (var coroutine in _coroutines.ToList())
            coroutine.Run(this);
        _coroutines.RemoveAll(c => !c.IsRunning);
    }

    private GameTimer AddTimer(GameTimer timer)
    {
        _timers.Add(timer);
        return timer;
    }

    // A new tween replaces an unfinished one with the same key (the same kind of tween on the same entity).
    private Tween AddTween(Tween tween)
    {
        if (tween.Key is not null)
        {
            foreach (var other in _tweens.Where(t => Equals(t.Key, tween.Key)))
                other.Cancel();
        }

        _tweens.Add(tween);
        return tween;
    }

    // Tweens added by callbacks (e.g. chained with Then) start on the next frame.
    private void RunTweens()
    {
        foreach (var tween in _tweens.ToList())
            tween.Run(Time.Total, Scene);
        _tweens.RemoveAll(t => !t.IsActive);
    }

    // Timers added by callbacks wait for the next frame, like spawned behaviors.
    private void RunTimers()
    {
        foreach (var timer in _timers.ToList())
            timer.Run(Time.Total, Scene);

        foreach (var timer in _timers.Where(t => t.Owner is not null && t.Owner.Scene != Scene))
            timer.Cancel();
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
        if (_pendingScene is not { } name)
            return;
        _pendingScene = null;
        SaveWorlds();

        var next = SceneLoader!(name);

        // Kept entities move across still running; a kept child goes along with a kept ancestor.
        _kept.RemoveAll(e => e.Scene != Scene);
        var moving = _kept.Where(e => !_kept.Any(other => other != e && e.IsSelfOrDescendantOf(other))).ToList();
        var staying = moving.SelectMany(e => e.SelfAndDescendants()).ToHashSet();

        foreach (var behavior in Scene.Entities.Where(e => !staying.Contains(e)).SelectMany(e => e.GetComponents<Behavior>()))
            behavior.RunDestroy();
        _pendingDestroy.Clear();
        Physics.Reset();

        foreach (var entity in moving)
        {
            Scene.Remove(entity);
            foreach (var e in entity.SelfAndDescendants())
            {
                if (next.Find(e.Id) is { } twin && twin.Scene == next)
                    next.Remove(twin);
            }

            next.Add(entity);
        }

        Scene = next;
        SceneName = SceneNameOf(name);
        _messages.Prune(next);
        _controlsSeen.RemoveWhere(c => c.Entity?.Scene != next);
        ForgetLevelState();
        ForgetPuzzleState();
        ForgetDragState();
        ForgetPanelState();
        Rts = new StrategyState();
        Fps = new FirstPersonState();
        Sandbox = new SandboxState();
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
        foreach (var follow in Scene.Entities.Where(e => Rendering.SceneRenderer.IsActive(e) && Moves(e)).SelectMany(e => e.GetComponents<Follow>()).ToList())
            follow.Advance(Time.Delta, ViewAspect, Time.UnscaledDelta);
    }

    // Pins ScreenAnchor entities to their point on the camera's view, parents before children.
    private void LayOutScreen()
    {
        var anchors = Scene.Entities.Where(Rendering.SceneRenderer.IsActive)
            .Select(e => e.GetComponent<ScreenAnchor>()).OfType<ScreenAnchor>().ToList();
        if (anchors.Count == 0)
            return;

        var camera = Rendering.SceneRenderer.FindCamera(Scene);
        var component = camera?.GetComponent<Camera>();
        var center = camera is null ? Vector2.Zero : new Vector2(camera.Transform.WorldPosition.X, camera.Transform.WorldPosition.Y);
        center += component?.ShakeOffset ?? Vector2.Zero;
        var halfHeight = component?.Size ?? Rendering.SceneRenderer.DefaultCameraSize;
        var half = new Vector2(halfHeight * (ViewAspect > 0 ? ViewAspect : 1), halfHeight);

        foreach (var anchor in anchors)
        {
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
        Notify(a, behavior => behavior.RunCollision(b));
        Notify(b, behavior => behavior.RunCollision(a));
    }

    private void OnTrigger(Entity trigger, Entity other, bool entered)
    {
        Notify(trigger, behavior => behavior.RunTrigger(other, entered));
        Notify(other, behavior => behavior.RunTrigger(trigger, entered));
    }

    // Lifetimes run on game time; the fade out is an ordinary FadeTo, so it shows on sprites, text and tilemaps alike.
    private void RunLifetimes()
    {
        foreach (var lifetime in Scene.Entities.Where(e => Rendering.SceneRenderer.IsActive(e) && Runs(e)).SelectMany(e => e.GetComponents<Lifetime>()).ToList())
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
        foreach (var spawner in Scene.Entities.Where(e => Rendering.SceneRenderer.IsActive(e) && Runs(e)).SelectMany(e => e.GetComponents<Spawner>()).ToList())
            spawner.Advance(Time.Delta, this);
    }

    // Typewriters run on real time, so dialogue still types out while the game is paused.
    private void RunTypewriters()
    {
        foreach (var typewriter in Scene.Entities.Where(Rendering.SceneRenderer.IsActive).SelectMany(e => e.GetComponents<Typewriter>()).ToList())
        {
            if (typewriter.Advance(Time.UnscaledDelta))
                Audio.Play(typewriter.Sound!);
        }
    }

    private void RunPickupSpin()
    {
        foreach (var pickup in Scene.Entities.Where(e => Rendering.SceneRenderer.IsActive(e) && Moves(e)).SelectMany(e => e.GetComponents<Pickup>()))
        {
            if (pickup.Spin != 0)
                pickup.Entity!.Transform.Rotation += new Vector3(0, 0, pickup.Spin * Time.Delta);
        }
    }

    // Touches from this frame's physics; each pickup goes to the first thing that reaches it.
    private void RunPickups()
    {
        foreach (var (source, target) in _touches.ToList())
        {
            if (source.Scene != Scene || target.Scene != Scene || !Runs(source) || source.GetComponent<Pickup>() is not { IsCollected: false } pickup)
                continue;
            if (BodyOwner(target) is not { } collector || !pickup.AcceptsLayer(LayerOf(target)) || source.IsSelfOrDescendantOf(collector))
                continue;
            if (pickup.Heal > 0 && string.IsNullOrWhiteSpace(pickup.Item) && Health.Of(collector) is { } health && health.Current >= health.Max)
                continue;
            Collect(pickup, collector);
        }
    }

    private void Collect(Pickup pickup, Entity collector)
    {
        pickup.IsCollected = true;
        var entity = pickup.Entity!;
        var position = entity.Transform.WorldPosition;
        if (!string.IsNullOrWhiteSpace(pickup.Sound))
            PlayShared(pickup.Sound);

        if (!string.IsNullOrWhiteSpace(pickup.Effect))
        {
            var effect = Spawn(pickup.Effect.Trim(), new Vector2(position.X, position.Y));
            if (pickup.TintEffect && entity.GetComponent<Sprite>() is { } sprite)
            {
                foreach (var emitter in effect.SelfAndDescendants().SelectMany(e => e.GetComponents<ParticleEmitter>()))
                {
                    emitter.StartColor = sprite.Tint;
                    emitter.EndColor = sprite.Tint with { A = 0 };
                }
            }
        }

        GiveItem(pickup, collector);
        if (pickup.Heal > 0)
            Health.Of(collector)?.Heal(pickup.Heal);
        Notify(collector, behavior => behavior.RunPickup(entity));
        pickup.RaiseCollected(collector);
        if (!string.IsNullOrWhiteSpace(pickup.Message))
            Send(pickup.Message.Trim(), pickup.Amount);
        Destroy(entity);
    }

    // What collects for a collider: its own entity's body, or the nearest ancestor's (a player's hitbox child).
    private static Entity? BodyOwner(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Body>() is not null)
                return e;
        }

        return null;
    }

    private void RunBars()
    {
        foreach (var bar in Scene.Entities.Where(e => Rendering.SceneRenderer.IsActive(e) && Runs(e)).SelectMany(e => e.GetComponents<Bar>()).ToList())
            bar.Advance(Time.Delta, Scene);
    }

    // Counters show their count, keep their best, and pop when the count changes.
    private void RunCounters()
    {
        foreach (var counter in Scene.Entities.Where(e => Rendering.SceneRenderer.IsActive(e) && Runs(e)).SelectMany(e => e.GetComponents<Counter>()).ToList())
        {
            var transform = counter.Entity!.Transform;
            var key = counter.SaveBestAs?.Trim();
            if (!string.IsNullOrWhiteSpace(counter.Item))
                counter.Add(ItemCount(counter.Item) - counter.Value);
            if (counter.CountGoals)
            {
                var goals = Showing<Goal>().ToList();
                counter.Total = goals.Count;
                counter.Add(goals.Count(g => g.IsCovered) - counter.Value);
            }

            if (counter.CountFloor && Showing<DungeonGenerator>().FirstOrDefault() is { } dungeon)
                counter.Add(dungeon.Floor - counter.Value);
            if (counter.CountMoves && Showing<MoveHistory>().FirstOrDefault() is { } history)
                counter.Add(history.Moves - counter.Value);
            else if (counter.CountMoves || counter.CountScore || counter.CountGem >= 0)
                CountBoard(counter);

            if (counter.Shown is null)
            {
                counter.RestingScale = transform.Scale;
                counter.Best = string.IsNullOrEmpty(key) ? counter.Value : SaveData.GetInt(key);
            }

            if (counter.Value > counter.Best)
            {
                counter.Best = counter.Value;
                if (!string.IsNullOrEmpty(key))
                    SaveData.Set(key, counter.Best);
            }

            if (counter.Entity.GetComponent<Text>() is { } text && counter.Display() is var display && text.Value != display)
                text.Value = display;

            if (counter.Shown is { } shown && shown != counter.Value && counter.Pop > 0 && counter.Pop != 1 && counter.RestingScale is { } rest)
            {
                transform.Scale = new Vector3(rest.X * counter.Pop, rest.Y * counter.Pop, rest.Z);
                ScaleTo(counter.Entity, new Vector2(rest.X, rest.Y), 0.3f, Ease.OutBack);
            }

            counter.Shown = counter.Value;
        }
    }

    // A counter showing the scene's match board: its swaps out of its Moves, its score out of its Target Score, or the
    // gems of a kind cleared out of how many it asks for. Or a falling-block board's score.
    private void CountBoard(Counter counter)
    {
        if (Showing<MatchBoard>().FirstOrDefault() is not { } board)
        {
            if (counter.CountScore && Showing<FallingBoard>().FirstOrDefault() is { } falling)
            {
                counter.Total = 0;
                counter.Add(falling.Score - counter.Value);
            }

            return;
        }

        var (value, total) = counter.CountMoves ? (board.Swaps, board.Moves)
            : counter.CountScore ? (board.Score, board.TargetScore)
            : (board.ClearedOf(counter.CountGem), board.ToCollect(counter.CountGem));
        counter.Total = total;
        counter.Add(value - counter.Value);
    }

    // Each pair that touched this frame (bumped, or overlapping as a trigger) counts once, however many times physics saw it.
    private void RunTouchDamage()
    {
        foreach (var (source, target) in _touches)
        {
            if (source.Scene != Scene || target.Scene != Scene || !Runs(source) || source.GetComponent<DamageOnTouch>() is not { } touch)
                continue;
            if (touch.DestroyOnWall && IsWall(target) && !(Health.Of(target) is { } struck && struck != Health.Of(source) && touch.Hurts(LayerOf(target))))
            {
                Destroy(source);
                continue;
            }

            if (Health.Of(target) is not { } health || health == Health.Of(source) || !touch.Hurts(LayerOf(target)) || !CanHurt(source, health.Entity!))
                continue;

            var hurt = health.Entity!;
            if (AlreadyStruck(source, hurt, out var swing))
                continue;
            var landed = touch.Kill ? !health.IsDead && KillNow(health) : health.Damage(touch.Amount, source);
            if (landed && touch.Knockback > 0 && hurt.GetComponent<Body>() is { } body)
            {
                var away = hurt.Transform.WorldPosition - source.Transform.WorldPosition;
                var direction = new Vector2(away.X, away.Y);
                body.Velocity = (direction.LengthSquared() > 1e-8f ? Vector2.Normalize(direction) : Vector2.UnitY) * touch.Knockback;
            }

            if (landed && swing is not null)
                Landed(swing, hurt);
            if (touch.DestroyOnHit)
                Destroy(source);
        }

        _touches.Clear();

        static bool KillNow(Health health)
        {
            health.Kill();
            return true;
        }
    }

    // Something solid to run into: a solid tilemap, or a collider that isn't a trigger.
    private static bool IsWall(Entity entity) =>
        entity.GetComponent<Tilemap>() is { Solid: true } || entity.GetComponents<Collider>().Any(c => !c.IsTrigger);

    private static string? LayerOf(Entity entity) =>
        entity.GetComponent<Collider>()?.Layer ?? entity.GetComponent<Tilemap>()?.Layer;

    // Deaths from this frame (touches, scripts, triggers) send their message and are destroyed along with everything else.
    private void RunHealth()
    {
        foreach (var health in Scene.Entities.Where(e => Rendering.SceneRenderer.IsActive(e) && Runs(e)).SelectMany(e => e.GetComponents<Health>()).ToList())
        {
            health.Home ??= new Vector2(health.Entity!.Transform.WorldPosition.X, health.Entity.Transform.WorldPosition.Y);
            health.Advance(Time.Delta);
            var at = new Vector2(health.Entity!.Transform.WorldPosition.X, health.Entity.Transform.WorldPosition.Y);
            if (health.Hurt)
            {
                health.Hurt = false;
                if (!string.IsNullOrWhiteSpace(health.HurtSound))
                    PlaySoundAt(health.HurtSound.Trim(), at);
            }

            if (!health.TakeDeath())
                continue;
            Net.CountDefeat(health.Entity!, health.LastHitBy);
            if (!string.IsNullOrWhiteSpace(health.DeathSound))
                PlaySoundAt(health.DeathSound.Trim(), at);
            if (!string.IsNullOrWhiteSpace(health.DeathMessage))
                Send(health.DeathMessage, health.Entity);
            if (health.Respawn)
                health.Entity!.IsEnabled = false;
            else if (health.DestroyOnDeath)
                Destroy(health.Entity!);
        }
    }

    private void Notify(Entity entity, Action<Behavior> action)
    {
        if (entity.Scene != Scene)
            return;
        foreach (var behavior in entity.GetComponents<Behavior>().Where(b => b.IsStarted).ToList())
            action(behavior);
    }

    private IEnumerable<Behavior> ActiveBehaviors()
    {
        foreach (var root in Scene.Roots)
        {
            foreach (var behavior in ActiveBehaviors(root))
                yield return behavior;
        }
    }

    // Disabled entities skip themselves and their children.
    private static IEnumerable<Behavior> ActiveBehaviors(Entity entity)
    {
        if (!entity.IsEnabled)
            yield break;

        foreach (var behavior in entity.GetComponents<Behavior>())
            yield return behavior;

        foreach (var child in entity.Children)
        {
            foreach (var behavior in ActiveBehaviors(child))
                yield return behavior;
        }
    }

    private void FlushDestroys()
    {
        while (_pendingDestroy.Count > 0)
        {
            var batch = _pendingDestroy.ToList();
            _pendingDestroy.Clear();

            foreach (var entity in batch.Where(e => e.Scene == Scene && Runs(e)))
            {
                foreach (var behavior in entity.SelfAndDescendants().SelectMany(e => e.GetComponents<Behavior>()))
                    behavior.RunDestroy();
                if (entity.Scene == Scene)
                    Scene.Remove(entity);
            }
        }
    }
}
