using System.Reflection;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

// The frame: every step the game takes in a Tick, in phases, and the systems they belong to. Griff's own steps are
// listed in BuildFrame in the order they run, with each module's (see GameModule) put in next to the steps they name;
// scripts' systems run after them in their phase.
public sealed partial class Game
{
    private static readonly FramePhase[] Phases = Enum.GetValues<FramePhase>();

    // Griff's own systems, each owning its state and its steps: gameplay with no code first, then the rest of what a game
    // does. Every step of the frame is one of theirs; Game's members for them only forward to them.
    private readonly LevelSystem _levels = new();
    private readonly MovementSystem _movement = new();
    private readonly GridSystem _gridMoves = new();
    private readonly TurnSystem _turns = new();
    private readonly CombatSystem _combat = new();
    private readonly TriggerSystem _triggers = new();
    private readonly SwitchSystem _switches = new();
    private readonly ItemSystem _itemUses = new();
    private readonly DragSystem _drags = new();
    private readonly ClockSystem _clock = new();
    private readonly InputSystem _inputSystem = new();
    private readonly NetworkSystem _network = new();
    private readonly ScriptSystem _scriptSystem = new();
    private readonly TimerSystem _timerSystem = new();
    private readonly MessageSystem _messageSystem = new();
    private readonly LifetimeSystem _lifetimes = new();
    private readonly EffectSystem _effects = new();
    private readonly TilemapSystem _tilemaps = new();
    private readonly HudSystem _hud = new();
    private readonly PhysicsSystem _physicsSystem = new();
    private readonly SceneSystem _scenes = new();
    private readonly MenuSystem _menuSystem = new();
    private readonly FocusSystem _focusSystem = new();
    private readonly PanelSystem _panels = new();
    private readonly PauseSystem _pauses = new();
    private readonly ReadingSystem _reading = new();
    private readonly BeatSystem _beat = new();
    private readonly CalibrationSystem _calibration = new();
    private readonly AtmosphereSystem _atmosphere = new();
    private readonly WeatherSystem _weather = new();
    private readonly LobbySystem _lobby = new();
    private readonly ViewSystem _views = new();
    private readonly SaveSystem _saves = new();
    private readonly SettingsSystem _settings = new();
    private readonly TableSystem _tableSystem = new();
    private readonly MinigameSystem _minigames = new();

    internal LevelSystem LevelSystem => _levels;

    internal MovementSystem MovementSystem => _movement;

    internal GridSystem GridSystem => _gridMoves;

    internal TurnSystem TurnSystem => _turns;

    internal CombatSystem CombatSystem => _combat;

    internal TriggerSystem TriggerSystem => _triggers;

    internal SwitchSystem SwitchSystem => _switches;

    internal ItemSystem ItemSystem => _itemUses;

    internal DragSystem DragSystem => _drags;

    internal ClockSystem ClockSystem => _clock;

    internal InputSystem InputSystem => _inputSystem;

    internal NetworkSystem NetworkSystem => _network;

    internal ScriptSystem ScriptSystem => _scriptSystem;

    internal TimerSystem TimerSystem => _timerSystem;

    internal MessageSystem MessageSystem => _messageSystem;

    internal LifetimeSystem LifetimeSystem => _lifetimes;

    internal EffectSystem EffectSystem => _effects;

    internal TilemapSystem TilemapSystem => _tilemaps;

    internal HudSystem HudSystem => _hud;

    internal PhysicsSystem PhysicsSystem => _physicsSystem;

    internal SceneSystem SceneSystem => _scenes;

    internal MenuSystem MenuSystem => _menuSystem;

    internal FocusSystem FocusSystem => _focusSystem;

    internal PanelSystem PanelSystem => _panels;

    internal PauseSystem PauseSystem => _pauses;

    internal ReadingSystem ReadingSystem => _reading;

    internal BeatSystem BeatSystem => _beat;

    internal CalibrationSystem CalibrationSystem => _calibration;

    internal AtmosphereSystem AtmosphereSystem => _atmosphere;

    internal WeatherSystem WeatherSystem => _weather;

    internal LobbySystem LobbySystem => _lobby;

    internal ViewSystem ViewSystem => _views;

    internal SaveSystem SaveSystem => _saves;

    internal SettingsSystem SettingsSystem => _settings;

    internal TableSystem TableSystem => _tableSystem;

    internal MinigameSystem MinigameSystem => _minigames;

    [NotState("the frame's steps: the same for the game's life")]
    private readonly List<FrameStep> _builtInSteps = [];
    [NotState("kept first, on its own (see CaptureState)")]
    private readonly List<GameSystem> _systems = [];
    [NotState("the frame's steps: the same for the game's life")]
    private GameSystem[] _builtInSystems = [];

    // Each phase's steps as they stand; replaced (never changed) when a system is added or taken away, so a frame can go
    // on through the ones it started with.
    [NotState("the frame's steps, worked out again from the systems")]
    private FrameStep[][] _phases = [];
    [NotState("the frame's steps, worked out again from the systems")]
    private FrameStep[]? _frame;
    [NotState("the steps taken, which never goes back (lists kept to use again count by it)")]
    private long _steps;
    private double _tickSeconds;

    // How many steps the game has taken, and how long each is: what the systems that pool or keep time go by.
    internal long StepCount => _steps;

    internal double TickSeconds => _tickSeconds;

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
    [field: NotState("the host's")]
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
        var previous = Here();
        try
        {
            AdvanceHere(seconds);
        }
        finally
        {
            Rand.EnterGame(previous);
        }
    }

    private void AdvanceHere(double seconds)
    {
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
                Scene.HeldStill = Time.FrozenLeft;
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
        var previous = Here();
        try
        {
            Time.CountFrame(seconds);
            Step(seconds);
            Scene.HeldStill = Time.FrozenLeft;
            Time.Blend = 1;
            Stepped?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Rand.EnterGame(previous);
        }
    }

    /// <summary>
    /// Draws things where they are now, rather than between the last two steps, until the game next takes a step: for a
    /// host holding the game still (the editor paused, or rewound), so what's on the screen is what the Inspector shows.
    /// </summary>
    public void ShowAsIs() => Time.Blend = 1;

    // How much real time the game owes in steps: what's left of the frames so far, less than a step.
    [NotState("real time owed by the host's frames")]
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
        if (!ReferenceEquals(_namesChecked, Scene))
            CheckNames();
        // What moves in it remembers where it was as it began, to be drawn between the two.
        Scene.Step = Time.Frame + 1;
        Scene.Steps++;
        _steps++;
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
        GameSystem[] core = [_levels, _movement, _gridMoves, _turns, _combat, _triggers, _switches, _itemUses, _drags, _panels, _clock, _inputSystem,
            _network, _scriptSystem, _timerSystem, _messageSystem, _lifetimes, _effects, _tilemaps, _hud, _physicsSystem, _scenes, _menuSystem,
            _focusSystem, _pauses, _reading, _beat, _calibration, _atmosphere, _weather, _lobby, _views, _saves, _settings, _tableSystem, _minigames];

        // Begin. Panels shown or hidden since last frame open or close first, so a pause starts straight away.
        Step(Steps.Panels, _panels.SyncPanels);
        Step(Steps.Pauses, _pauses.SyncPauses);
        Step(Steps.Time, _clock.AdvanceTime);
        Step(Steps.Input, _inputSystem.ReadInput);
        Step(Steps.DebugDrawing, _clock.BeginDebugDrawing);
        Step(Steps.BeginNetwork, _network.BeginNetwork);
        Step(Steps.Tables, _tableSystem.ApplyTables);
        Step(Steps.CarriedValues, _scenes.RunCarried);
        Step(Steps.ScriptStart, _scriptSystem.StartScripts);

        Step(Steps.Menus, _menuSystem.UpdateMenus);
        Step(Steps.Lobby, _lobby.RunLobby);
        Step(Steps.Grids, _tilemaps.LineUpGrids);
        Step(Steps.SwitchTargets, _switches.ApplySwitches);
        Step(Steps.Turns, _turns.RunTurns);
        Step(Steps.BeatClocks, _beat.RunBeatClocks);
        Step(Steps.Calibrations, _calibration.RunCalibrations);

        // No-code enemies and players go before scripts, so a script can still take over a body this frame.
        Step(Steps.Enemies, _levels.RunEnemies);
        Step(Steps.Movement, _movement.RunMovement);
        Step(Steps.ItemUses, _itemUses.RunItemUses);

        Step(Steps.Scripts, _scriptSystem.UpdateScripts, timesItself: true);

        // Whatever systems show on the screen is laid out first, so a click on it isn't taken for one in the world.
        Step(Steps.HUD, _hud.LayOutHuds);
        Step(Steps.OtherPlayersTools, _network.RunOtherPlayersTools);
        Step(Steps.Dragging, _drags.RunDragging);
        Step(Steps.Attacks, _combat.RunAttacks);
        Step(Steps.IdleGridMovers, _turns.PassIdleGridMovers);
        Step(Steps.Timers, _timerSystem.RunTimers);
        Step(Steps.Coroutines, _timerSystem.RunCoroutines);
        Step(Steps.Tweens, _timerSystem.RunTweens);
        Step(Steps.Lifetimes, _lifetimes.RunLifetimes);
        Step(Steps.Spawners, _lifetimes.RunSpawners);
        Step(Steps.Shooters, _levels.RunShooters);
        Step(Steps.Movers, _levels.RunMovers);
        Step(Steps.Animations, _effects.RunAnimations);
        Step(Steps.AnimatedTiles, _tilemaps.RunAnimatedTiles);
        Step(Steps.Particles, _effects.RunParticles);
        Step(Steps.CameraShake, _effects.RunCameraShake);
        Step(Steps.SpriteEffects, _effects.RunSpriteEffects);
        Step(Steps.SimulatePrompts, _panels.FillPrompts);
        Step(Steps.Typewriters, _hud.RunTypewriters);
        Step(Steps.PickupSpin, _itemUses.RunPickupSpin);

        // Scripts push bodies just before they move.
        Step(Steps.FixedScripts, _scriptSystem.FixedUpdateScripts, timesItself: true);
        // What crates and players are to physics, which knows only bodies and colliders, as they are just before it steps.
        Step(Steps.BodyRoles, _movement.ReadyBodies);
        Step(Steps.Physics, _physicsSystem.StepPhysics);
        Step(Steps.Carrying, _drags.CarryHeld);

        Step(Steps.Pickups, _itemUses.RunPickups);
        Step(Steps.Checkpoints, _levels.RunCheckpoints);
        Step(Steps.Doors, _levels.RunDoors);
        Step(Steps.Dialogues, _levels.RunDialogues);
        Step(Steps.Switches, _switches.RunSwitches);
        Step(Steps.Bouncers, _movement.RunBouncers);
        Step(Steps.Crumbles, _movement.RunCrumbles);
        Step(Steps.TouchDamage, _combat.RunTouchDamage);
        Step(Steps.Health, _combat.RunHealth);
        Step(Steps.Respawns, _levels.RunRespawns);
        Step(Steps.Bars, _hud.RunBars);
        Step(Steps.Stars, _hud.RunStars);
        Step(Steps.Counters, _hud.RunCounters);
        Step(Steps.TextOutputs, _hud.ShowOutputs);
        Step(Steps.Fade, _scenes.AdvanceFade);
        Step(Steps.ScreenFlash, _effects.RunScreenFlash);
        Step(Steps.Destroys, _lifetimes.FlushDestroys);
        Step(Steps.SceneChange, _scenes.SwitchSceneIfRequested);

        Step(Steps.Rooms, _atmosphere.RunRooms);
        Step(Steps.Followers, _effects.RunFollowers);
        Step(Steps.LateScripts, _scriptSystem.LateUpdateScripts, timesItself: true);
        Step(Steps.ScreenAnchors, _hud.LayOutScreen);
        Step(Steps.Trails, _effects.RunTrails);
        Step(Steps.Lights, _atmosphere.RunLights);
        Step(Steps.Weather, _weather.RunWeather);
        Step(Steps.PresentPrompts, _panels.FillPrompts);
        Step(Steps.SoundSources, _atmosphere.RunSoundSources);
        Step(Steps.SoundPlayers, _atmosphere.RunSoundPlayers);
        Step(Steps.PatrolWalks, _levels.CountPatrolWalks);

        Step(Steps.Audio, _atmosphere.AdvanceAudio);
        Step(Steps.Settings, _settings.SaveSettings);
        Step(Steps.EndNetwork, _network.EndNetwork);

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

    // Each step is its system's method: the system it belongs to is the one that runs it.
    private void Step(StepId id, Action run, bool timesItself = false) =>
        _builtInSteps.Add(new FrameStep(id, run, run.Target as GameSystem) { TimesItself = timesItself });

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

    // The scene is about to go (for another, or the game is stopping): systems keep what it leaves behind.
    internal void SystemsSceneEnding()
    {
        foreach (var system in _systems.ToList())
            system.RunSceneEnding();
    }

    // A new scene: systems whose script stayed behind go, and the rest forget the last scene, Griff's first.
    internal void SystemsSceneChanged()
    {
        // The pointer starts each scene as the arrow; what sets it there sets it again.
        Input.Cursor = Griff.Input.MouseCursor.Arrow;
        foreach (var system in _systems.Where(s => s.Owner is { } owner && owner.Entity?.Scene != Scene).ToList())
            RemoveSystem(system);
        foreach (var system in _systems.ToList())
        {
            if (!system.IsRemoved)
                system.RunSceneChanged();
        }
    }
}

// Rewind: what the game keeps besides its scene (see IKeepsState), taken with each snapshot and put back with it.
public sealed partial class Game : IKeepsState
{
    [NotState("reused to write each snapshot's state")]
    private readonly StateWriter _stateWriter = new();

    // The parts whose fields are kept, in order: the game, its clock and its physics system's world, then each system
    // running. (Its random numbers are kept apart: they start as the game first takes a step.)
    private IEnumerable<object> KeptParts()
    {
        yield return this;
        yield return Time;
        yield return _physicsSystem.World;
        foreach (var system in _systems)
            yield return system;
    }

    /// <summary>
    /// What the game keeps besides its scene, as it is between two steps (see <see cref="IKeepsState"/>): which systems
    /// run and every part's fields, its random numbers, what the save data holds, then for each component what the scene
    /// file doesn't hold of it (a script that hasn't started yet, a script's own fields, a body's time in the air).
    /// Written into <paramref name="into"/> if given (reusing its memory).
    /// </summary>
    internal GameState CaptureState(GameState? into = null)
    {
        var writer = BeginState();
        foreach (var entity in Scene.Entities)
        {
            var components = entity.Components;
            for (var i = 0; i < components.Count; i++)
            {
                if (components[i] is not MissingComponent)
                    writer.Component(entity, i, components[i], KeptFields.OfComponent(components[i].GetType()));
            }
        }

        return EndState(into);
    }

    // The first part of CaptureState, before the components: a rewind snapshot writes each component's state
    // (StateWriter.Component) as it goes through the entities for their records, then ends it (EndState).
    internal StateWriter BeginState()
    {
        var writer = _stateWriter;
        writer.Begin(Scene);
        writer.Write(_systems.Count);
        foreach (var system in _systems)
            writer.Ref(system);
        foreach (var part in KeptParts())
            KeptFields.OfPart(part.GetType()).Write(part, writer);
        writer.Write(_random is null ? (byte)0 : (byte)1);
        if (_random is not null)
            KeptFields.OfPart(typeof(GameRandom)).Write(_random, writer);
        writer.Ref(_saveData.Held());
        return writer;
    }

    internal GameState EndState(GameState? into)
    {
        _stateWriter.Write(StateWriter.ComponentsEnd);
        return _stateWriter.Finish(into);
    }

    /// <summary>
    /// Puts the game back as <paramref name="state"/> (from <see cref="CaptureState"/>) had it, in <paramref name="scene"/>
    /// (the snapshot's scene, put back as a scene file loads): the same game goes on from there, its systems, clock, random
    /// numbers, timers, tweens, coroutines and save data as they were, and its scripts started as they were, with their own
    /// fields.
    /// </summary>
    internal void Restore(Scene scene, GameState state)
    {
        Scene = scene;
        var reader = new StateReader(state, scene);
        var systems = new GameSystem[reader.Read<int>()];
        for (var i = 0; i < systems.Length; i++)
            systems[i] = (GameSystem)reader.Ref()!;
        foreach (var system in _systems.Except(systems))
            system.IsRemoved = true;
        _systems.Clear();
        _systems.AddRange(systems);
        foreach (var part in KeptParts())
            reader.Fields(part, KeptFields.OfPart(part.GetType()).Fields);
        if (reader.Read<byte>() == 0)
            _random = null;
        else
            reader.Fields(_random ??= new GameRandom(0), KeptFields.OfPart(typeof(GameRandom)).Fields);
        _saveData.PutBack((string)reader.Ref()!);

        // Each component's own fields, and the scripts that hadn't started yet: every other script had.
        var waiting = new HashSet<Behavior>();
        for (var tag = reader.Read<byte>(); tag != StateWriter.ComponentsEnd; tag = reader.Read<byte>())
        {
            var entity = scene.Find(reader.Read<Guid>())!;
            var component = entity.Components[reader.Read<int>()];
            if (tag == StateWriter.Waiting)
            {
                waiting.Add((Behavior)component);
                continue;
            }

            reader.Fields(component, KeptFields.OfComponent(component.GetType()).Fields);
            // The scene lists the sprites flashing or blinking, for the game to move on.
            (component as Sprite)?.ListEffects();
        }

        foreach (var script in scene.AllNow<Behavior>())
        {
            if (!waiting.Contains(script))
                script.PutBack(this);
        }

        scene.Step = scene.FirstStep = Time.Frame;
        BuildPhases();
        foreach (var part in KeptParts())
            (part as IKeepsState)?.Restored();
    }
}
