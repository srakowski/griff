using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// The parts of a step of the game (its frame), in the order they run. See <see cref="Game.Frame"/> for every step in
/// each.
/// </summary>
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

    /// <summary>Scripts' <c>FixedUpdate</c> pushes bodies, then they move and collide.</summary>
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

    /// <summary>
    /// Called every step of the game (60 a second, see <see cref="Runtime.Time.StepRate"/>) in <see cref="Phase"/>. A
    /// system that pushes bodies runs in <see cref="FramePhase.Simulate"/>, just before they move.
    /// </summary>
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
    /// game's fog and HUD, a tactics game's reachable cells and a block world's hotbar this way.
    /// </summary>
    protected virtual void Draw(FrameDrawing frame)
    {
    }

    /// <summary>
    /// Called as each frame is drawn, before any system's <see cref="Draw"/>, for a system that sees the world its own way
    /// (a first-person view) to draw it in place of the camera's view from above: set <see cref="FrameDrawing.Sprites"/>,
    /// <see cref="FrameDrawing.View"/> and <see cref="FrameDrawing.ViewProjection"/>. Every system then draws over it.
    /// </summary>
    protected virtual void DrawView(FrameDrawing frame)
    {
    }

    /// <summary>
    /// The entity the game is seen through while the system draws the world through someone's eyes (a first-person view's
    /// player), or null: sounds are then heard from it, facing the way it faces, and <see cref="Game.Eyes"/> is it.
    /// </summary>
    protected virtual Entity? Eyes => null;

    /// <summary>
    /// True if the game is seen through <paramref name="entity"/>'s eyes on the screen of whoever plays it (in a network
    /// game, each player's own character): their controls aim the way it faces rather than with the mouse.
    /// </summary>
    protected virtual bool LooksThrough(Entity entity) => false;

    /// <summary>
    /// Called early in each frame's Simulate phase (and on the host of a network game, again for each other player, as
    /// them: see <see cref="RunForPlayer"/>) to lay out what the system shows on the screen for the view as it is now,
    /// with its <see cref="HudButtons"/>, so whether the mouse is on it is known before anything acts on a click. Draw what
    /// it lays out through <see cref="FrameDrawing.PinToView"/>, so it keeps still on the screen as the camera is drawn
    /// moving between steps.
    /// </summary>
    protected virtual void LayOutHud()
    {
    }

    /// <summary>The buttons the system shows on the screen this frame (a hotbar's slots, a tool picker), with what each does.</summary>
    protected virtual IReadOnlyList<HudButton> HudButtons => [];

    /// <summary>A short message the system's HUD is showing (something made, a tool's next step), or null.</summary>
    protected virtual string? Notice => null;

    /// <summary>
    /// On the host of a network game, runs the system's controls that are one player's own (a hotbar, building tools) for
    /// <paramref name="player"/>, as them: <see cref="Game.Input"/> is their keys and mouse, the view is their screen, and
    /// what the system keeps for a player should be theirs (see <see cref="Game.ActingFor"/>). Called once a frame for each
    /// other player with a character, after the system's <see cref="LayOutHud"/> for them.
    /// </summary>
    protected virtual void RunForPlayer(NetPlayer player)
    {
    }

    /// <summary>
    /// Called as a grid mover sets off on a step it chose (a player's, with the crates it pushes), before anything moves: a
    /// puzzle's move history remembers where everything was.
    /// </summary>
    protected virtual void GridStepStarting(GridMovement mover)
    {
    }

    /// <summary>
    /// Called as the game is about to leave its scene, for another or because it's stopping, while the scene is still there:
    /// to keep what the scene leaves behind (a block world saved).
    /// </summary>
    protected virtual void SceneEnding()
    {
    }

    /// <summary>
    /// True while the mouse is over something the system draws on the screen (a strategy game's HUD, say), so a click there
    /// isn't meant for the world: Griff's own controls leave it alone, and in a network game the host is told.
    /// </summary>
    protected virtual bool MouseOnHud => false;

    /// <summary>
    /// True while Back (Esc, the gamepad's B) would cancel something the system has under way (a strategy game's building
    /// being placed, a target being picked), so it does that first: a panel that opens on a key (a pause menu) waits.
    /// </summary>
    protected virtual bool ClaimsBack => false;

    /// <summary>
    /// Called in a network game as each scene starts and whenever the players change (someone joins, leaves or changes
    /// team), to settle who plays what (a strategy player commands their own team).
    /// </summary>
    protected virtual void PlayersChanged()
    {
    }

    /// <summary>The script whose entity the system goes away with, if a script added it.</summary>
    internal Behavior? Owner { get; set; }

    internal bool IsRemoved { get; set; }

    internal void Attach(Game game) => _game = game;

    internal void RunUpdate() => Update();

    internal void RunSceneChanged() => SceneChanged();

    internal bool HasMouse => !IsRemoved && MouseOnHud;

    internal bool HasBack => !IsRemoved && ClaimsBack;

    internal void RunPlayersChanged()
    {
        if (!IsRemoved)
            PlayersChanged();
    }

    internal void RunDraw(FrameDrawing frame)
    {
        if (!IsRemoved)
            Draw(frame);
    }

    internal void RunDrawView(FrameDrawing frame)
    {
        if (!IsRemoved)
            DrawView(frame);
    }

    internal Entity? SeenThrough => IsRemoved ? null : Eyes;

    internal bool SeesThrough(Entity entity) => !IsRemoved && LooksThrough(entity);

    internal void RunLayOutHud()
    {
        if (!IsRemoved)
            LayOutHud();
    }

    internal IReadOnlyList<HudButton> Buttons => IsRemoved ? [] : HudButtons;

    internal string? ShownNotice => IsRemoved ? null : Notice;

    internal void RunForOtherPlayer(NetPlayer player)
    {
        if (!IsRemoved)
            RunForPlayer(player);
    }

    internal void RunGridStepStarting(GridMovement mover)
    {
        if (!IsRemoved)
            GridStepStarting(mover);
    }

    internal void RunSceneEnding()
    {
        if (!IsRemoved)
            SceneEnding();
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

    // How a profiler counts it: as one of Griff's steps or a script's system; a step that runs scripts times each kind itself.
    internal FrameCostKind CostKind { get; init; } = FrameCostKind.Step;

    internal bool TimesItself { get; init; }

    public override string ToString() => $"{Phase}: {Name}";
}
