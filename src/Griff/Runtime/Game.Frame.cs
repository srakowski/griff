using System.Diagnostics;
using Griff.Scenes;

namespace Griff.Runtime;

// The frame: every step the game takes in a Tick, in phases, and the systems they belong to. Griff's own steps are
// listed in BuildFrame in the order they run, with each module's (see GameModule) put in next to the steps they name;
// scripts' systems run after them in their phase.
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

    // The scripts running this frame, taken as it starts: spawned scripts wait for the next one. Then those of them with
    // an Update, a FixedUpdate and a LateUpdate, kept with the snapshot they were taken from (unchanged while nothing is
    // added, removed, enabled or disabled), and the last snapshot every script of had started.
    private ComponentList<Behavior> _running;
    private Behavior[]? _hooksFor;
    private Behavior[] _updating = [];
    private Behavior[] _fixedUpdating = [];
    private Behavior[] _lateUpdating = [];
    private Behavior[]? _startedFor;
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
        Crowds.ThrowIfRunning("add systems");
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
        Crowds.ThrowIfRunning("remove systems");
        if (IsBuiltIn(system) || !_systems.Remove(system))
            return false;
        system.IsRemoved = true;
        BuildPhases();
        return true;
    }

    // The systems scripts added, in the order they were.
    internal IEnumerable<GameSystem> AddedSystems => _systems.Where(s => !IsBuiltIn(s));

    /// <summary>The running system of type <typeparamref name="T"/>, if there is one.</summary>
    public T? GetSystem<T>() where T : GameSystem
    {
        foreach (var system in _systems)
        {
            if (system is T found)
                return found;
        }

        return null;
    }

    /// <summary>
    /// The entity the game is seen through, when a system draws the world through someone's eyes (a first-person view's
    /// player); null when the camera looks down on it. Sounds are heard from it, facing the way it faces.
    /// </summary>
    public Entity? Eyes
    {
        get
        {
            for (var i = 0; i < _systems.Count; i++)
            {
                if (_systems[i].SeenThrough is { } eyes)
                    return eyes;
            }

            return null;
        }
    }

    /// <summary>The buttons the systems show on the screen this frame (a hotbar's slots, building tools), each with what it does.</summary>
    public IReadOnlyList<HudButton> HudButtons
    {
        get
        {
            List<HudButton>? all = null;
            IReadOnlyList<HudButton> only = [];
            foreach (var system in _systems)
            {
                var buttons = system.Buttons;
                if (buttons.Count == 0)
                    continue;
                if (only.Count == 0)
                {
                    only = buttons;
                    continue;
                }

                all ??= [.. only];
                all.AddRange(buttons);
            }

            return all ?? only;
        }
    }

    /// <summary>A short message a system's HUD is showing (something made, a tool's next step), or null.</summary>
    public string? Notice
    {
        get
        {
            foreach (var system in _systems)
            {
                if (system.ShownNotice is { } notice)
                    return notice;
            }

            return null;
        }
    }

    /// <summary>
    /// Raised after each step the game takes (see <see cref="Advance"/>), once its input is done with: controls set here
    /// count for the next step. The editor keeps its rewind snapshots here.
    /// </summary>
    public event EventHandler? Stepped;

    /// <summary>
    /// A frame of the screen, <paramref name="seconds"/> of real time after the last: takes as many steps of
    /// <see cref="Time.FixedDelta"/> as that time holds (none on a fast screen's frame, two on a slow one's), keeping the
    /// rest for the next frame, so the game moves the same on any machine. <see cref="Time.Blend"/> then says how far
    /// between its last two steps to draw it. Presses (and typing, scrolling, mouse movement) count in the next step taken,
    /// once: a frame with no step keeps them for the next. A frame covers at most <see cref="Time.MaxDelta"/>; a longer
    /// one (a stall) slows the game down rather than running a burst of steps.
    /// </summary>
    public void Advance(double seconds)
    {
        ThrowIfStopped();
        if (!(seconds > 0))
            seconds = 0;
        Time.CountFrame(seconds);

        // A frame within a hair of a whole number of steps (a 60 Hz screen's, give or take the timer's jitter) counts as
        // exactly that: a steady screen then takes the same steps every frame, rather than now and then none and then two
        // (which looks uneven, and doubles the work of a frame that may already be full). The game may run a touch slow
        // for it, on a screen a touch slower than its step rate, but never skips.
        var step = Time.StepSeconds;
        var whole = Math.Round(seconds / step);
        if (whole is >= 1 and <= MaxSnappedSteps && Math.Abs(seconds - whole * step) < step * FrameSnap)
            seconds = whole * step;
        _owed = Math.Min(_owed + seconds, Math.Max(Time.MaxDelta, step));
        while (_owed >= step - StepTolerance && !_stopped)
        {
            _owed -= step;
            try
            {
                Step(step);
            }
            finally
            {
                // What was pressed counted in this step, and doesn't again in the next.
                (LocalInput as InputState)?.EndFrame();
            }

            Stepped?.Invoke(this, EventArgs.Empty);
        }

        Time.Blend = (float)Math.Clamp(_owed / step, 0, 1);
    }

    /// <summary>
    /// Takes one step of exactly <paramref name="seconds"/> (at most <see cref="Time.MaxDelta"/>), as a frame of its own
    /// drawn as it ends (<see cref="Time.Blend"/> 1): stepping a game by hand, for tests and tools, or one step at a time
    /// while paused. The caller ends the input's frame (<see cref="InputState.EndFrame"/>) after. A running game uses
    /// <see cref="Advance"/>.
    /// </summary>
    public void Tick(double seconds)
    {
        ThrowIfStopped();
        Time.CountFrame(seconds);
        Step(seconds);
        Time.Blend = 1;
        Stepped?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Draws things where they are now, rather than between the last two steps, until the game next takes a step: for a
    /// host holding the game still (the editor paused, or rewound), so what's on the screen is what the Inspector shows.
    /// </summary>
    public void ShowAsIs() => Time.Blend = 1;

    // How much real time the game owes in steps: what's left of the frames so far, less than a step.
    private double _owed;

    // Frame times measured in floating point land a hair either side of a step; within this, it's a step.
    private const double StepTolerance = 1e-7;

    // How close to a whole number of steps (as a share of a step, 2 ms at 60 a second) a frame counts as exactly that many,
    // for frames of up to this many steps.
    private const double FrameSnap = 0.12;
    private const int MaxSnappedSteps = 4;

    private void ThrowIfStopped()
    {
        if (_stopped)
            throw new InvalidOperationException("The game has stopped.");
    }

    // One step: each phase's steps in turn.
    private void Step(double seconds)
    {
        _tickSeconds = seconds;
        // What moves in it remembers where it was as it began, to be drawn between the two.
        Scene.Step = Time.Frame + 1;
        if (Profiler is { } profiler)
        {
            StepTimed(profiler);
            return;
        }

        foreach (var phase in Phases)
        {
            // Read again each phase: a system added in an earlier one (by a script's Start) runs this step.
            foreach (var step in _phases[(int)phase])
                step.Run();
        }
    }

    // The same, timing each step and the garbage it makes for a profiler. Scripts are timed a kind at a time, as they run.
    private void StepTimed(FrameProfiler profiler)
    {
        foreach (var phase in Phases)
        {
            foreach (var step in _phases[(int)phase])
            {
                if (step.TimesItself)
                {
                    step.Run();
                    continue;
                }

                var (started, allocated) = (Stopwatch.GetTimestamp(), Workers.AllocatedBytes());
                step.Run();
                profiler.AddCost(step.Name, step.CostKind, Stopwatch.GetTimestamp() - started, Workers.AllocatedBytes() - allocated);
            }
        }
    }

    private bool IsBuiltIn(GameSystem system) => _builtInSystems.Contains(system);

    // Griff's own steps, in the order a frame runs them. FrameTests pins this order: change it on purpose.
    private void BuildFrame()
    {
        GameSystem[] core = [_levels, _switches, _drags, _panels];

        // Begin. Panels shown or hidden since last frame open or close first, so a pause starts straight away.
        Step(FramePhase.Begin, "Panels", SyncPanels, _panels);
        Step(FramePhase.Begin, "Time", AdvanceTime);
        Step(FramePhase.Begin, "Input", () => (Input as InputState)?.BeginFrame(Time.UnscaledDelta));
        Step(FramePhase.Begin, "Debug Drawing", Debug.BeginFrame);
        Step(FramePhase.Begin, "Network", () => Net.BeginFrame(Time.UnscaledDelta));
        Step(FramePhase.Begin, "Tables", ApplyTables);
        Step(FramePhase.Begin, "Script Start", StartScripts);

        Step(FramePhase.Logic, "Menus", UpdateMenus);
        Step(FramePhase.Logic, "Lobby", RunLobby);
        Step(FramePhase.Logic, "Switch Targets", ApplySwitches, _switches);
        Step(FramePhase.Logic, "Turns", RunTurns);
        Step(FramePhase.Logic, "Beat Clocks", RunBeatClocks);

        // No-code enemies and players go before scripts, so a script can still take over a body this frame.
        Step(FramePhase.Logic, "Enemies", RunEnemies);
        Step(FramePhase.Logic, "Movement", RunMovement);
        Step(FramePhase.Logic, "Item Uses", RunItemUses);

        Step(FramePhase.Scripts, "Scripts", UpdateScripts, timesItself: true);

        // Whatever systems show on the screen is laid out first, so a click on it isn't taken for one in the world.
        Step(FramePhase.Simulate, "HUD", LayOutHuds);
        Step(FramePhase.Simulate, "Other Players' Tools", RunOtherPlayersTools);
        Step(FramePhase.Simulate, "Dragging", RunDragging, _drags);
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

        // Scripts push bodies just before they move.
        Step(FramePhase.Physics, "Fixed Scripts", FixedUpdateScripts, timesItself: true);
        Step(FramePhase.Physics, "Physics", StepPhysics);
        Step(FramePhase.Physics, "Carrying", CarryHeld, _drags);

        Step(FramePhase.Resolve, "Pickups", RunPickups);
        Step(FramePhase.Resolve, "Checkpoints", RunCheckpoints);
        Step(FramePhase.Resolve, "Doors", RunDoors, _levels);
        Step(FramePhase.Resolve, "Dialogues", RunDialogues, _levels);
        Step(FramePhase.Resolve, "Switches", RunSwitches, _switches);
        Step(FramePhase.Resolve, "Bouncers", RunBouncers);
        Step(FramePhase.Resolve, "Crumbles", RunCrumbles);
        Step(FramePhase.Resolve, "Touch Damage", RunTouchDamage);
        Step(FramePhase.Resolve, "Health", RunHealth);
        Step(FramePhase.Resolve, "Respawns", RunRespawns);
        Step(FramePhase.Resolve, "Bars", RunBars);
        Step(FramePhase.Resolve, "Counters", RunCounters);
        Step(FramePhase.Resolve, "Fade", AdvanceFade);
        Step(FramePhase.Resolve, "Screen Flash", RunScreenFlash);
        Step(FramePhase.Resolve, "Destroys", FlushDestroys);
        Step(FramePhase.Resolve, "Scene Change", SwitchSceneIfRequested);

        Step(FramePhase.Present, "Rooms", RunRooms);
        Step(FramePhase.Present, "Followers", RunFollowers);
        Step(FramePhase.Present, "Late Scripts", LateUpdateScripts, timesItself: true);
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

        // Then each module's systems, their steps put in next to the steps they follow or go before.
        var setup = new GameSetup(this);
        foreach (var module in GameModule.All)
        {
            setup.Module = module;
            module.AddTo(setup);
        }

        var steps = setup.Place(_builtInSteps);
        _builtInSteps.Clear();
        _builtInSteps.AddRange(steps);
        _builtInSystems = [.. core, .. setup.Systems];
        foreach (var system in _builtInSystems)
        {
            system.Attach(this);
            _systems.Add(system);
        }

        BuildPhases();
    }

    private void Step(FramePhase phase, string name, Action run, GameSystem? system = null, bool timesItself = false) =>
        _builtInSteps.Add(new FrameStep(phase, name, run, system) { TimesItself = timesItself });

    // Griff's steps, then each added system's Update, phase by phase.
    private void BuildPhases()
    {
        var added = AddedSystems.ToList();
        _phases = Phases.Select(phase => _builtInSteps.Where(s => s.Phase == phase)
            .Concat(added.Where(s => s.Phase == phase).Select(s => new FrameStep(phase, s.Name, () => RunAdded(s), s) { CostKind = FrameCostKind.System }))
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
        var running = _running.Items;
        if (running != _hooksFor)
        {
            (_updating, _fixedUpdating, _lateUpdating) = (With(running, BehaviorHooks.Update), With(running, BehaviorHooks.FixedUpdate),
                With(running, BehaviorHooks.LateUpdate));
            (_updatingRuns, _fixedUpdatingRuns, _lateUpdatingRuns) = (RunsOf(_updating), RunsOf(_fixedUpdating), RunsOf(_lateUpdating));
            _hooksFor = running;
        }

        // A big scene's scripts started long ago: unless it's changed, there's nothing to look for.
        if (running == _startedFor)
            return;
        _startedFor = null;
        foreach (var behavior in running)
        {
            if (!behavior.IsStarted)
                behavior.RunStart(this);
        }

        _startedFor = running;
    }

    // Those of `running` whose type has `hook`, in the same order.
    private static Behavior[] With(Behavior[] running, BehaviorHooks hook)
    {
        var count = 0;
        foreach (var behavior in running)
        {
            if ((behavior.Hooks & hook) != 0)
                count++;
        }

        if (count == running.Length)
            return running;
        var with = new Behavior[count];
        count = 0;
        foreach (var behavior in running)
        {
            if ((behavior.Hooks & hook) != 0)
                with[count++] = behavior;
        }

        return with;
    }

    // While a panel pauses the game, only the scripts on open panels run.
    private void UpdateScripts() => RunScripts(_updating, _updatingRuns, BehaviorHooks.Update);

    // Just before bodies move, in steps where game time moves: not while paused (by a panel, or Time.Scale 0) or frozen in
    // a hit stop, when physics doesn't step either.
    private void FixedUpdateScripts()
    {
        if (Time.Delta > 0)
            RunScripts(_fixedUpdating, _fixedUpdatingRuns, BehaviorHooks.FixedUpdate);
    }

    // After everything has moved and the camera has followed, before what's pinned to the screen is put in place.
    private void LateUpdateScripts() => RunScripts(_lateUpdating, _lateUpdatingRuns, BehaviorHooks.LateUpdate);

    private void LayOutHuds()
    {
        // By index: a button pressed while laying out may do anything.
        for (var i = 0; i < _systems.Count; i++)
            _systems[i].RunLayOutHud();
    }

    // The host runs each other player's tools for them, as them: each system lays out its HUD for their screen, then runs
    // their controls.
    private void RunOtherPlayersTools()
    {
        if (!Net.IsHost)
            return;
        ForEachRemotePlayer(player =>
        {
            LayOutHuds();
            for (var i = 0; i < _systems.Count; i++)
                _systems[i].RunForOtherPlayer(player);
        });
    }

    // Trails go after everything has moved (followers too), so their copies are left where things were drawn.
    private void RunTrails()
    {
        foreach (var trail in Scene.Active<Trail>())
            trail.Advance(Time.Delta);
    }

    private void RunAnimatedTiles()
    {
        foreach (var tilemap in Scene.Active<Tilemap>())
        {
            if (!string.IsNullOrWhiteSpace(tilemap.AnimatedTiles))
                tilemap.AnimationTime += Time.Delta;
        }
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

    // Only the sprites flashing or blinking, each dropped from the list once it's done, or gone. Each moves on by itself,
    // so the order they're in doesn't matter.
    private void RunSpriteEffects()
    {
        var sprites = Scene.SpriteEffects;
        for (var i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            if (sprite.Entity is { } entity && entity.Scene == Scene)
            {
                if (!entity.IsActive)
                    continue;
                sprite.AdvanceEffects(Time.Delta);
                if (sprite.HasEffects)
                    continue;
            }

            if (sprite.EffectsListedIn == Scene)
                sprite.EffectsListedIn = null;
            sprites[i--] = sprites[^1];
            sprites.RemoveAt(sprites.Count - 1);
        }
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

    // The scene is about to go (for another, or the game is stopping): systems keep what it leaves behind.
    private void SystemsSceneEnding()
    {
        foreach (var system in _systems.ToList())
            system.RunSceneEnding();
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
