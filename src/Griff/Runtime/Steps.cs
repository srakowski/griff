namespace Griff.Runtime;

/// <summary>
/// Names one step of the frame (see <see cref="Game.Frame"/>) for another step to go next to: Griff's own are the
/// members of <see cref="Steps"/>, and a module's come back from <see cref="GameSetup.StepAfter"/> and
/// <see cref="GameSetup.StepBefore"/>. A step is placed by its id rather than its label, so a step that is renamed, or
/// whose label another step shares, is still found, and a step that isn't there can't be named at all.
/// </summary>
public sealed class StepId
{
    internal StepId(FramePhase phase, string name)
    {
        Phase = phase;
        Name = name;
    }

    /// <summary>The phase the step runs in.</summary>
    public FramePhase Phase { get; }

    /// <summary>What the step is called in the frame and a profiler; two steps may share a label, never an id.</summary>
    public string Name { get; }

    public override string ToString() => $"{Phase}: {Name}";
}

/// <summary>
/// Griff's own steps of the frame, in the order they run (see <see cref="Game.Frame"/>), for a module to put its steps
/// next to (<see cref="GameSetup.StepAfter"/>).
/// </summary>
public static class Steps
{
    /// <summary>The "Panels" step of the Begin phase.</summary>
    public static readonly StepId Panels = new(FramePhase.Begin, "Panels");

    /// <summary>The "Pauses" step of the Begin phase.</summary>
    public static readonly StepId Pauses = new(FramePhase.Begin, "Pauses");

    /// <summary>The "Time" step of the Begin phase.</summary>
    public static readonly StepId Time = new(FramePhase.Begin, "Time");

    /// <summary>The "Input" step of the Begin phase.</summary>
    public static readonly StepId Input = new(FramePhase.Begin, "Input");

    /// <summary>The "Debug Drawing" step of the Begin phase.</summary>
    public static readonly StepId DebugDrawing = new(FramePhase.Begin, "Debug Drawing");

    /// <summary>The "Network" step of the Begin phase.</summary>
    public static readonly StepId BeginNetwork = new(FramePhase.Begin, "Network");

    /// <summary>The "Tables" step of the Begin phase.</summary>
    public static readonly StepId Tables = new(FramePhase.Begin, "Tables");

    /// <summary>The "Carried Values" step of the Begin phase.</summary>
    public static readonly StepId CarriedValues = new(FramePhase.Begin, "Carried Values");

    /// <summary>The "Show When" step of the Begin phase.</summary>
    public static readonly StepId ShowWhen = new(FramePhase.Begin, "Show When");

    /// <summary>The "Script Start" step of the Begin phase.</summary>
    public static readonly StepId ScriptStart = new(FramePhase.Begin, "Script Start");

    /// <summary>The "Menus" step of the Logic phase.</summary>
    public static readonly StepId Menus = new(FramePhase.Logic, "Menus");

    /// <summary>The "Lobby" step of the Logic phase.</summary>
    public static readonly StepId Lobby = new(FramePhase.Logic, "Lobby");

    /// <summary>The "Grids" step of the Logic phase.</summary>
    public static readonly StepId Grids = new(FramePhase.Logic, "Grids");

    /// <summary>The "Switch Targets" step of the Logic phase.</summary>
    public static readonly StepId SwitchTargets = new(FramePhase.Logic, "Switch Targets");

    /// <summary>The "Turns" step of the Logic phase.</summary>
    public static readonly StepId Turns = new(FramePhase.Logic, "Turns");

    /// <summary>The "Beat Clocks" step of the Logic phase.</summary>
    public static readonly StepId BeatClocks = new(FramePhase.Logic, "Beat Clocks");

    /// <summary>The "Calibrations" step of the Logic phase.</summary>
    public static readonly StepId Calibrations = new(FramePhase.Logic, "Calibrations");

    /// <summary>The "Enemies" step of the Logic phase.</summary>
    public static readonly StepId Enemies = new(FramePhase.Logic, "Enemies");

    /// <summary>The "Movement" step of the Logic phase.</summary>
    public static readonly StepId Movement = new(FramePhase.Logic, "Movement");

    /// <summary>The "Item Uses" step of the Logic phase.</summary>
    public static readonly StepId ItemUses = new(FramePhase.Logic, "Item Uses");

    /// <summary>The "Scripts" step of the Scripts phase.</summary>
    public static readonly StepId Scripts = new(FramePhase.Scripts, "Scripts");

    /// <summary>The "HUD" step of the Simulate phase.</summary>
    public static readonly StepId HUD = new(FramePhase.Simulate, "HUD");

    /// <summary>The "Other Players' Tools" step of the Simulate phase.</summary>
    public static readonly StepId OtherPlayersTools = new(FramePhase.Simulate, "Other Players' Tools");

    /// <summary>The "Dragging" step of the Simulate phase.</summary>
    public static readonly StepId Dragging = new(FramePhase.Simulate, "Dragging");

    /// <summary>The "Selection" step of the Simulate phase.</summary>
    public static readonly StepId Selection = new(FramePhase.Simulate, "Selection");

    /// <summary>The "Attacks" step of the Simulate phase.</summary>
    public static readonly StepId Attacks = new(FramePhase.Simulate, "Attacks");

    /// <summary>The "Idle Grid Movers" step of the Simulate phase.</summary>
    public static readonly StepId IdleGridMovers = new(FramePhase.Simulate, "Idle Grid Movers");

    /// <summary>The "Timers" step of the Simulate phase.</summary>
    public static readonly StepId Timers = new(FramePhase.Simulate, "Timers");

    /// <summary>The "Coroutines" step of the Simulate phase.</summary>
    public static readonly StepId Coroutines = new(FramePhase.Simulate, "Coroutines");

    /// <summary>The "Tweens" step of the Simulate phase.</summary>
    public static readonly StepId Tweens = new(FramePhase.Simulate, "Tweens");

    /// <summary>The "Lifetimes" step of the Simulate phase.</summary>
    public static readonly StepId Lifetimes = new(FramePhase.Simulate, "Lifetimes");

    /// <summary>The "Spawners" step of the Simulate phase.</summary>
    public static readonly StepId Spawners = new(FramePhase.Simulate, "Spawners");

    /// <summary>The "Shooters" step of the Simulate phase.</summary>
    public static readonly StepId Shooters = new(FramePhase.Simulate, "Shooters");

    /// <summary>The "Movers" step of the Simulate phase.</summary>
    public static readonly StepId Movers = new(FramePhase.Simulate, "Movers");

    /// <summary>The "Animations" step of the Simulate phase.</summary>
    public static readonly StepId Animations = new(FramePhase.Simulate, "Animations");

    /// <summary>The "Animated Tiles" step of the Simulate phase.</summary>
    public static readonly StepId AnimatedTiles = new(FramePhase.Simulate, "Animated Tiles");

    /// <summary>The "Particles" step of the Simulate phase.</summary>
    public static readonly StepId Particles = new(FramePhase.Simulate, "Particles");

    /// <summary>The "Camera Shake" step of the Simulate phase.</summary>
    public static readonly StepId CameraShake = new(FramePhase.Simulate, "Camera Shake");

    /// <summary>The "Sprite Effects" step of the Simulate phase.</summary>
    public static readonly StepId SpriteEffects = new(FramePhase.Simulate, "Sprite Effects");

    /// <summary>The "Prompts" step of the Simulate phase.</summary>
    public static readonly StepId SimulatePrompts = new(FramePhase.Simulate, "Prompts");

    /// <summary>The "Typewriters" step of the Simulate phase.</summary>
    public static readonly StepId Typewriters = new(FramePhase.Simulate, "Typewriters");

    /// <summary>The "Pickup Spin" step of the Simulate phase.</summary>
    public static readonly StepId PickupSpin = new(FramePhase.Simulate, "Pickup Spin");

    /// <summary>The "Fixed Scripts" step of the Physics phase.</summary>
    public static readonly StepId FixedScripts = new(FramePhase.Physics, "Fixed Scripts");

    /// <summary>The "Body Roles" step of the Physics phase.</summary>
    public static readonly StepId BodyRoles = new(FramePhase.Physics, "Body Roles");

    /// <summary>The "Physics" step of the Physics phase.</summary>
    public static readonly StepId Physics = new(FramePhase.Physics, "Physics");

    /// <summary>The "Carrying" step of the Physics phase.</summary>
    public static readonly StepId Carrying = new(FramePhase.Physics, "Carrying");

    /// <summary>The "Pickups" step of the Resolve phase.</summary>
    public static readonly StepId Pickups = new(FramePhase.Resolve, "Pickups");

    /// <summary>The "Checkpoints" step of the Resolve phase.</summary>
    public static readonly StepId Checkpoints = new(FramePhase.Resolve, "Checkpoints");

    /// <summary>The "Doors" step of the Resolve phase.</summary>
    public static readonly StepId Doors = new(FramePhase.Resolve, "Doors");

    /// <summary>The "Dialogues" step of the Resolve phase.</summary>
    public static readonly StepId Dialogues = new(FramePhase.Resolve, "Dialogues");

    /// <summary>The "Switches" step of the Resolve phase.</summary>
    public static readonly StepId Switches = new(FramePhase.Resolve, "Switches");

    /// <summary>The "Bouncers" step of the Resolve phase.</summary>
    public static readonly StepId Bouncers = new(FramePhase.Resolve, "Bouncers");

    /// <summary>The "Crumbles" step of the Resolve phase.</summary>
    public static readonly StepId Crumbles = new(FramePhase.Resolve, "Crumbles");

    /// <summary>The "Touch Damage" step of the Resolve phase.</summary>
    public static readonly StepId TouchDamage = new(FramePhase.Resolve, "Touch Damage");

    /// <summary>The "Health" step of the Resolve phase.</summary>
    public static readonly StepId Health = new(FramePhase.Resolve, "Health");

    /// <summary>The "Respawns" step of the Resolve phase.</summary>
    public static readonly StepId Respawns = new(FramePhase.Resolve, "Respawns");

    /// <summary>The "Bars" step of the Resolve phase.</summary>
    public static readonly StepId Bars = new(FramePhase.Resolve, "Bars");

    /// <summary>The "Stars" step of the Resolve phase.</summary>
    public static readonly StepId Stars = new(FramePhase.Resolve, "Stars");

    /// <summary>The "Counters" step of the Resolve phase.</summary>
    public static readonly StepId Counters = new(FramePhase.Resolve, "Counters");

    /// <summary>The "Tooltips" step of the Resolve phase.</summary>
    public static readonly StepId Tooltips = new(FramePhase.Resolve, "Tooltips");

    /// <summary>The "Text Outputs" step of the Resolve phase.</summary>
    public static readonly StepId TextOutputs = new(FramePhase.Resolve, "Text Outputs");

    /// <summary>The "Fade" step of the Resolve phase.</summary>
    public static readonly StepId Fade = new(FramePhase.Resolve, "Fade");

    /// <summary>The "Screen Flash" step of the Resolve phase.</summary>
    public static readonly StepId ScreenFlash = new(FramePhase.Resolve, "Screen Flash");

    /// <summary>The "Destroys" step of the Resolve phase.</summary>
    public static readonly StepId Destroys = new(FramePhase.Resolve, "Destroys");

    /// <summary>The "Wall Rooms" step of the Resolve phase.</summary>
    public static readonly StepId WallRooms = new(FramePhase.Resolve, "Wall Rooms");

    /// <summary>The "Scene Change" step of the Resolve phase.</summary>
    public static readonly StepId SceneChange = new(FramePhase.Resolve, "Scene Change");

    /// <summary>The "Rooms" step of the Present phase.</summary>
    public static readonly StepId Rooms = new(FramePhase.Present, "Rooms");

    /// <summary>The "Camera Controls" step of the Present phase.</summary>
    public static readonly StepId CameraControls = new(FramePhase.Present, "Camera Controls");

    /// <summary>The "Followers" step of the Present phase.</summary>
    public static readonly StepId Followers = new(FramePhase.Present, "Followers");

    /// <summary>The "Late Scripts" step of the Present phase.</summary>
    public static readonly StepId LateScripts = new(FramePhase.Present, "Late Scripts");

    /// <summary>The "Layouts" step of the Present phase.</summary>
    public static readonly StepId Layouts = new(FramePhase.Present, "Layouts");

    /// <summary>The "Screen Anchors" step of the Present phase.</summary>
    public static readonly StepId ScreenAnchors = new(FramePhase.Present, "Screen Anchors");

    /// <summary>The "Trails" step of the Present phase.</summary>
    public static readonly StepId Trails = new(FramePhase.Present, "Trails");

    /// <summary>The "Lights" step of the Present phase.</summary>
    public static readonly StepId Lights = new(FramePhase.Present, "Lights");

    /// <summary>The "Weather" step of the Present phase.</summary>
    public static readonly StepId Weather = new(FramePhase.Present, "Weather");

    /// <summary>The "Prompts" step of the Present phase.</summary>
    public static readonly StepId PresentPrompts = new(FramePhase.Present, "Prompts");

    /// <summary>The "Sound Sources" step of the Present phase.</summary>
    public static readonly StepId SoundSources = new(FramePhase.Present, "Sound Sources");

    /// <summary>The "Sound Players" step of the Present phase.</summary>
    public static readonly StepId SoundPlayers = new(FramePhase.Present, "Sound Players");

    /// <summary>The "Patrol Walks" step of the Present phase.</summary>
    public static readonly StepId PatrolWalks = new(FramePhase.Present, "Patrol Walks");

    /// <summary>The "Audio" step of the End phase.</summary>
    public static readonly StepId Audio = new(FramePhase.End, "Audio");

    /// <summary>The "Settings" step of the End phase.</summary>
    public static readonly StepId Settings = new(FramePhase.End, "Settings");

    /// <summary>The "Network" step of the End phase.</summary>
    public static readonly StepId EndNetwork = new(FramePhase.End, "Network");
}
