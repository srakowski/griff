using Griff.Scenes;

namespace Griff.Runtime;

// The frame: every step the game takes in a Tick, in phases, and the systems they belong to. Griff's own steps are
// listed in BuildFrame in the order they run; scripts' systems run after them in their phase.
public sealed partial class Game
{
    private static readonly FramePhase[] Phases = Enum.GetValues<FramePhase>();

    private readonly List<FrameStep> _builtInSteps = [];
    private readonly List<GameSystem> _systems = [];
    private GameSystem[] _builtInSystems = [];

    // Each phase's steps as they stand; replaced (never changed) when a system is added or taken away, so a frame can go
    // on through the ones it started with.
    private FrameStep[][] _phases = [];
    private FrameStep[]? _frame;

    // The scripts running this frame, taken as it starts: spawned scripts wait for the next one.
    private ComponentList<Behavior> _running;
    private double _tickSeconds;

    // What physics calls back, made once rather than every frame.
    private Action<Entity, Entity>? _onCollision;
    private Action<Entity, Entity, bool>? _onTrigger;

    /// <summary>
    /// Every step of a frame in the order <see cref="Tick"/> takes them: Griff's own, then in each phase the
    /// <see cref="Systems"/> scripts added.
    /// </summary>
    public IReadOnlyList<FrameStep> Frame => _frame ??= _phases.SelectMany(p => p).ToArray();

    /// <summary>The systems running: Griff's that keep track of something while a scene plays, then those scripts added.</summary>
    public IReadOnlyList<GameSystem> Systems => _systems;

    /// <summary>
    /// Runs <paramref name="system"/> every frame from now on (from this frame, if its phase hasn't started), after
    /// Griff's own steps in its <see cref="GameSystem.Phase"/>. A system of the same type is replaced. A script's
    /// <c>AddSystem</c> ties it to the script's entity instead, so it goes when that does.
    /// </summary>
    public T AddSystem<T>(T system) where T : GameSystem => AddSystem(system, null);

    internal T AddSystem<T>(T system, Behavior? owner) where T : GameSystem
    {
        ArgumentNullException.ThrowIfNull(system);
        if (_systems.Contains(system))
            throw new InvalidOperationException($"{system.Name} is already running.");
        if (IsBuiltIn(system))
            throw new InvalidOperationException($"{system.Name} is one of Griff's own systems.");

        // Matched by name, so a script reloaded in the editor replaces the system its last version added.
        var type = system.GetType().FullName;
        foreach (var other in _systems.Where(s => !IsBuiltIn(s) && s.GetType().FullName == type).ToList())
            RemoveSystem(other);

        system.Attach(this);
        system.Owner = owner;
        system.IsRemoved = false;
        _systems.Add(system);
        BuildPhases();
        return system;
    }

    /// <summary>Stops running <paramref name="system"/> (one a script added). Returns whether it was running.</summary>
    public bool RemoveSystem(GameSystem system)
    {
        if (IsBuiltIn(system) || !_systems.Remove(system))
            return false;
        system.IsRemoved = true;
        BuildPhases();
        return true;
    }

    // The systems scripts added, in the order they were.
    internal IEnumerable<GameSystem> AddedSystems => _systems.Where(s => !IsBuiltIn(s));

    /// <summary>The running system of type <typeparamref name="T"/>, if there is one.</summary>
    public T? GetSystem<T>() where T : GameSystem => _systems.OfType<T>().FirstOrDefault();

    /// <summary>Advances the game by <paramref name="seconds"/>, one <see cref="Frame"/> step after another.</summary>
    public void Tick(double seconds)
    {
        if (_stopped)
            throw new InvalidOperationException("The game has stopped.");

        _tickSeconds = seconds;
        foreach (var phase in Phases)
        {
            // Read again each phase: a system added in an earlier one (by a script's Start) runs this frame.
            foreach (var step in _phases[(int)phase])
                step.Run();
        }
    }

    private bool IsBuiltIn(GameSystem system) => _builtInSystems.Contains(system);

    // Griff's own steps, in the order a frame runs them. FrameTests pins this order: change it on purpose.
    private void BuildFrame()
    {
        _builtInSystems = [_levels, _puzzles, _drags, _panels, _strategy, _firstPerson, _sandboxes];
        foreach (var system in _builtInSystems)
        {
            system.Attach(this);
            _systems.Add(system);
        }

        // Begin. Panels shown or hidden since last frame open or close first, so a pause starts straight away.
        Step(FramePhase.Begin, "Panels", SyncPanels, _panels);
        Step(FramePhase.Begin, "Time", AdvanceTime);
        Step(FramePhase.Begin, "Input", () => (Input as InputState)?.BeginFrame(Time.UnscaledDelta));
        Step(FramePhase.Begin, "Debug Drawing", Debug.BeginFrame);
        Step(FramePhase.Begin, "Network", () => Net.BeginFrame(Time.UnscaledDelta));

        // Dungeons are made first, so what starts this frame starts on the floor (spawns included).
        Step(FramePhase.Begin, "Dungeons", RunDungeons);
        Step(FramePhase.Begin, "Script Start", StartScripts);

        Step(FramePhase.Logic, "Worlds", StartWorlds, _sandboxes);
        Step(FramePhase.Logic, "Menus", UpdateMenus);
        Step(FramePhase.Logic, "Lobby", RunLobby);
        Step(FramePhase.Logic, "Switch Targets", ApplySwitches, _puzzles);
        Step(FramePhase.Logic, "Turns", RunTurns);
        Step(FramePhase.Logic, "Tactics", RunTactics);
        Step(FramePhase.Logic, "Beat Clocks", RunBeatClocks);

        // Before anything moves, so a move taken back this frame isn't stepped on from where it was.
        Step(FramePhase.Logic, "Move History", RunMoveHistory);

        // No-code enemies and players go before scripts, so a script can still take over a body this frame.
        Step(FramePhase.Logic, "Enemies", RunEnemies);
        Step(FramePhase.Logic, "Movement", RunMovement);
        Step(FramePhase.Logic, "First Person", RunFirstPerson, _firstPerson);
        Step(FramePhase.Logic, "Item Uses", RunItemUses);

        Step(FramePhase.Scripts, "Scripts", UpdateScripts);

        Step(FramePhase.Simulate, "Sandbox HUD", RunSandboxHud, _sandboxes);
        Step(FramePhase.Simulate, "Blocks", RunBlocks, _sandboxes);
        Step(FramePhase.Simulate, "Tools", RunTools, _sandboxes);
        Step(FramePhase.Simulate, "Other Players' Tools", RunOtherPlayersTools, _sandboxes);
        Step(FramePhase.Simulate, "Dragging", RunDragging, _drags);
        Step(FramePhase.Simulate, "Match Boards", RunMatchBoards);
        Step(FramePhase.Simulate, "Falling Boards", RunFallingBoards);
        Step(FramePhase.Simulate, "Strategy", RunStrategy, _strategy);
        Step(FramePhase.Simulate, "Attacks", RunAttacks);
        Step(FramePhase.Simulate, "Idle Grid Movers", PassIdleGridMovers);
        Step(FramePhase.Simulate, "Timers", RunTimers);
        Step(FramePhase.Simulate, "Coroutines", RunCoroutines);
        Step(FramePhase.Simulate, "Tweens", RunTweens);
        Step(FramePhase.Simulate, "Lifetimes", RunLifetimes);
        Step(FramePhase.Simulate, "Spawners", RunSpawners);
        Step(FramePhase.Simulate, "Shooters", RunShooters);
        Step(FramePhase.Simulate, "Movers", RunMovers);
        Step(FramePhase.Simulate, "Animations", RunAnimations);
        Step(FramePhase.Simulate, "Animated Tiles", RunAnimatedTiles);
        Step(FramePhase.Simulate, "Particles", RunParticles);
        Step(FramePhase.Simulate, "Camera Shake", RunCameraShake);
        Step(FramePhase.Simulate, "Sprite Effects", RunSpriteEffects);
        Step(FramePhase.Simulate, "Prompts", FillPrompts);
        Step(FramePhase.Simulate, "Typewriters", RunTypewriters);
        Step(FramePhase.Simulate, "Pickup Spin", RunPickupSpin);

        Step(FramePhase.Physics, "Thrusters", RunThrusters, _sandboxes);
        Step(FramePhase.Physics, "Welds", GroupWelds, _sandboxes);
        Step(FramePhase.Physics, "Physics", StepPhysics);
        Step(FramePhase.Physics, "Ropes", RunRopes, _sandboxes);
        Step(FramePhase.Physics, "Carrying", CarryHeld, _drags);

        Step(FramePhase.Resolve, "Pickups", RunPickups);
        Step(FramePhase.Resolve, "Checkpoints", RunCheckpoints);
        Step(FramePhase.Resolve, "Doors", RunDoors, _levels);
        Step(FramePhase.Resolve, "Dialogues", RunDialogues, _levels);
        Step(FramePhase.Resolve, "Switches", RunSwitches, _puzzles);
        Step(FramePhase.Resolve, "Goals", RunGoals, _puzzles);
        Step(FramePhase.Resolve, "Wiring", RunWiring);
        Step(FramePhase.Resolve, "Bouncers", RunBouncers);
        Step(FramePhase.Resolve, "Crumbles", RunCrumbles);
        Step(FramePhase.Resolve, "Touch Damage", RunTouchDamage);
        Step(FramePhase.Resolve, "Health", RunHealth);
        Step(FramePhase.Resolve, "Respawns", RunRespawns);
        Step(FramePhase.Resolve, "Bars", RunBars);
        Step(FramePhase.Resolve, "Counters", RunCounters);
        Step(FramePhase.Resolve, "Fade", AdvanceFade);
        Step(FramePhase.Resolve, "Screen Flash", () => _screenFlashLeft = Math.Max(0, _screenFlashLeft - Time.UnscaledDelta));
        Step(FramePhase.Resolve, "Destroys", FlushDestroys);
        Step(FramePhase.Resolve, "Scene Change", SwitchSceneIfRequested);

        Step(FramePhase.Present, "Rooms", RunRooms);
        Step(FramePhase.Present, "Followers", RunFollowers);
        Step(FramePhase.Present, "Late Scripts", LateUpdateScripts);
        Step(FramePhase.Present, "Screen Anchors", LayOutScreen);
        Step(FramePhase.Present, "Trails", RunTrails);
        Step(FramePhase.Present, "Lights", RunLights);
        Step(FramePhase.Present, "Weather", RunWeather);
        Step(FramePhase.Present, "Prompts", FillPrompts);
        Step(FramePhase.Present, "Sound Sources", RunSoundSources);
        Step(FramePhase.Present, "Sound Players", RunSoundPlayers);
        Step(FramePhase.Present, "Patrol Walks", CountPatrolWalks);

        Step(FramePhase.End, "Audio", () => Audio.Advance(Time.UnscaledDelta));
        Step(FramePhase.End, "Settings", SaveSettings);
        Step(FramePhase.End, "Network", () => Net.EndFrame(Time.UnscaledDelta));

        BuildPhases();
    }

    private void Step(FramePhase phase, string name, Action run, GameSystem? system = null) =>
        _builtInSteps.Add(new FrameStep(phase, name, run, system));

    // Griff's steps, then each added system's Update, phase by phase.
    private void BuildPhases()
    {
        var added = AddedSystems.ToList();
        _phases = Phases.Select(phase => _builtInSteps.Where(s => s.Phase == phase)
            .Concat(added.Where(s => s.Phase == phase).Select(s => new FrameStep(phase, s.Name, () => RunAdded(s), s)))
            .ToArray()).ToArray();
        _frame = null;
    }

    // A system a script added goes with the script's entity (destroyed, left behind by a scene change) or a reload.
    private void RunAdded(GameSystem system)
    {
        if (system.IsRemoved)
            return;
        if (system.Owner is { } owner && owner.Entity?.Scene != Scene)
        {
            RemoveSystem(system);
            return;
        }

        system.RunUpdate();
    }

    private void AdvanceTime()
    {
        _pausedAtStart = Time.IsPaused;
        Time.Advance(_tickSeconds);
    }

    // Snapshot first: behaviors may spawn or destroy while we iterate. In a network game, the host runs scripts.
    private void StartScripts()
    {
        _running = Active<Behavior>();
        foreach (var behavior in _running)
        {
            if (!behavior.IsStarted)
                behavior.RunStart(this);
        }
    }

    // While a panel pauses the game, only the scripts on open panels run.
    private void UpdateScripts()
    {
        foreach (var behavior in _running)
        {
            if (behavior.Entity?.Scene == Scene && (!PausedThisFrame || InOpenPanel(behavior.Entity)))
                behavior.RunUpdate();
        }
    }

    // After everything has moved and the camera has followed, before what's pinned to the screen is put in place.
    private void LateUpdateScripts()
    {
        foreach (var behavior in _running)
        {
            if (behavior.Entity?.Scene == Scene && (!PausedThisFrame || InOpenPanel(behavior.Entity)))
                behavior.RunLateUpdate();
        }
    }

    // The host runs each other player's tools for them, as them.
    private void RunOtherPlayersTools()
    {
        if (!Net.IsHost)
            return;
        ForEachRemotePlayer(() =>
        {
            RunSandboxHud();
            RunBlockPlayers();
            RunTools();
        });
    }

    private void RunAnimations()
    {
        foreach (var animation in Scene.Active<Animation>())
            animation.Advance(Time.Delta);
    }

    private void RunParticles()
    {
        foreach (var emitter in Scene.Active<ParticleEmitter>())
        {
            emitter.Advance(Time.Delta, Physics.Gravity);
            if (emitter.DestroyWhenFinished && emitter.IsFinished && Runs(emitter.Entity!))
                Destroy(emitter.Entity!);
        }
    }

    private void RunCameraShake()
    {
        foreach (var camera in Scene.Active<Camera>())
        {
            camera.AdvanceShake(Time.UnscaledDelta);
            camera.AdvanceAmbient(Time.UnscaledDelta);
        }
    }

    private void RunSpriteEffects()
    {
        foreach (var sprite in Scene.Active<Sprite>())
            sprite.AdvanceEffects(Time.Delta);
    }

    private void StepPhysics()
    {
        if (Time.Delta <= 0)
            return;
        Physics.Step(Scene, Time.Delta, _onCollision ??= OnCollision, _onTrigger ??= OnTrigger);
        foreach (var (trigger, other) in Physics.TriggerPairs)
        {
            _touches.Add((trigger, other));
            _touches.Add((other, trigger));
        }
    }

    // A new scene: systems whose script stayed behind go, and the rest forget the last scene, Griff's first.
    private void SystemsSceneChanged()
    {
        foreach (var system in _systems.Where(s => s.Owner is { } owner && owner.Entity?.Scene != Scene).ToList())
            RemoveSystem(system);
        foreach (var system in _systems.ToList())
        {
            if (!system.IsRemoved)
                system.RunSceneChanged();
        }
    }
}
