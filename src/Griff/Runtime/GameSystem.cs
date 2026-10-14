namespace Griff.Runtime;

/// <summary>The parts of a frame, in the order they run. See <see cref="Game.Frame"/> for every step in each.</summary>
public enum FramePhase
{
    /// <summary>Time moves on, input is read and new scripts start.</summary>
    Begin,

    /// <summary>Griff's no-code players, enemies, menus, turns and the like decide what to do.</summary>
    Logic,

    /// <summary>Every script's <c>Update</c>.</summary>
    Scripts,

    /// <summary>Everything else moves on: boards, attacks, timers, tweens, spawners, animations, particles.</summary>
    Simulate,

    /// <summary>Bodies move and collide.</summary>
    Physics,

    /// <summary>What the physics step caused (pickups, damage, doors, switches), then destroys and scene changes.</summary>
    Resolve,

    /// <summary>The camera and what's pinned to it, then lights, weather and sound: what's seen and heard. Scripts'
    /// <c>LateUpdate</c> runs here, after the camera follows, before screen-pinned things are put in place.</summary>
    Present,

    /// <summary>Sound plays, settings are saved and a network game sends what changed.</summary>
    End,
}

/// <summary>
/// One part of what the game does each frame, run in its <see cref="Phase"/> after Griff's own steps there. A script adds
/// one with <c>AddSystem</c> (it stops when that script's entity goes) or <see cref="Game.AddSystem"/> (for the rest of
/// the game). A game has one system of each type: adding another replaces it. Override <see cref="Update"/> to do
/// something every frame, for a whole kind of thing at once; something the system keeps track of in a scene is
/// forgotten in <see cref="SceneChanged"/>. Override <see cref="Draw"/> to draw what the system knows of that no one
/// entity draws (a HUD, markers over things); an entity's own look is a component's (see <see cref="Scenes.IDrawable"/>).
/// </summary>
/// <example>
/// <code>
/// // Pulls every Body towards each Magnet, whatever script or component moves it.
/// public class Magnets : GameSystem
/// {
///     public override FramePhase Phase => FramePhase.Simulate;
///
///     protected override void Update()
///     {
///         foreach (var magnet in Game.FindAll&lt;Magnet&gt;())
///         {
///             foreach (var body in Game.FindAll&lt;Body&gt;())
///                 body.Velocity += magnet.PullOn(body) * Time.Delta;
///         }
///     }
/// }
///
/// // In a script on the level: protected override void Start() => AddSystem(new Magnets());
/// </code>
/// </example>
public abstract class GameSystem
{
    private Game? _game;

    /// <summary>The phase <see cref="Update"/> runs in; <see cref="FramePhase.Logic"/> unless the system says.</summary>
    public virtual FramePhase Phase => FramePhase.Logic;

    /// <summary>What the frame calls the system's step (see <see cref="Game.Frame"/>); its type's name unless it says.</summary>
    public virtual string Name => GetType().Name;

    /// <summary>The game the system runs in. Available once it's added.</summary>
    protected Game Game => _game ?? throw new InvalidOperationException("The system hasn't been added to a game yet.");

    protected Time Time => Game.Time;

    /// <summary>Called every frame in <see cref="Phase"/>.</summary>
    protected virtual void Update()
    {
    }

    /// <summary>
    /// Called when the game has switched to another scene (before its scripts start), to forget what the system kept
    /// track of in the last one.
    /// </summary>
    protected virtual void SceneChanged()
    {
    }

    /// <summary>
    /// Called as each frame is drawn, after every entity has drawn itself and the systems before this one have drawn, to
    /// add to what's drawn or change it: see <see cref="FrameDrawing"/>. Griff's own systems draw a strategy
    /// game's fog and HUD, a tactics game's reachable cells and a first-person view this way.
    /// </summary>
    protected virtual void Draw(FrameDrawing frame)
    {
    }

    /// <summary>The script whose entity the system goes away with, if a script added it.</summary>
    internal Behavior? Owner { get; set; }

    internal bool IsRemoved { get; set; }

    internal void Attach(Game game) => _game = game;

    internal void RunUpdate() => Update();

    internal void RunSceneChanged() => SceneChanged();

    internal void RunDraw(FrameDrawing frame)
    {
        if (!IsRemoved)
            Draw(frame);
    }
}

/// <summary>
/// One step of the frame: a named piece of work in a <see cref="FramePhase"/>, belonging to a <see cref="GameSystem"/>
/// (a script's, or one of Griff's that keeps track of something) or to the game itself.
/// </summary>
public sealed class FrameStep
{
    internal FrameStep(FramePhase phase, string name, Action run, GameSystem? system)
    {
        Phase = phase;
        Name = name;
        Run = run;
        System = system;
    }

    public FramePhase Phase { get; }

    public string Name { get; }

    /// <summary>The system the step belongs to, or null for the game's own bookkeeping.</summary>
    public GameSystem? System { get; }

    internal Action Run { get; }

    public override string ToString() => $"{Phase}: {Name}";
}
