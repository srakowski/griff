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

    /// <summary>The game's tables (see <see cref="Runtime.Game.Tables"/>).</summary>
    protected Griff.Data.TableSet Tables => Game.Tables;

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
    /// game's fog and HUD, a tactics game's reachable cells and a block world's hotbar this way. It's called for each view:
    /// the main one and each <see cref="Viewport"/>'s (<see cref="FrameDrawing.Viewport"/> says which), so a HUD of the
    /// whole screen goes in the main view only.
    /// </summary>
    protected virtual void Draw(FrameDrawing frame)
    {
    }

    /// <summary>The script whose entity the system goes away with, if a script added it.</summary>
    internal Behavior? Owner { get; set; }

    internal bool IsRemoved { get; set; }

    // What the system opts into (see the interfaces below), found once as it's added.
    private IViewDrawer? _viewDrawer;
    private ISeesThroughEyes? _eyes;
    private IHudLayout? _hud;
    private IMouseOnHud? _mouse;
    private INoticeSource? _notice;
    private IPlayerTools? _tools;
    private IGridStepListener? _gridSteps;
    private ISceneEndListener? _sceneEnd;
    private IClaimsBack? _claimsBack;
    private IPlayersListener? _players;

    internal void Attach(Game game)
    {
        _game = game;
        (_viewDrawer, _eyes, _hud, _mouse, _notice, _tools) = (this as IViewDrawer, this as ISeesThroughEyes, this as IHudLayout,
            this as IMouseOnHud, this as INoticeSource, this as IPlayerTools);
        (_gridSteps, _sceneEnd, _claimsBack, _players) = (this as IGridStepListener, this as ISceneEndListener,
            this as IClaimsBack, this as IPlayersListener);
    }

    internal void RunUpdate() => Update();

    internal void RunSceneChanged() => SceneChanged();

    internal bool HasMouse => !IsRemoved && _mouse is { MouseOnHud: true };

    internal bool HasBack => !IsRemoved && _claimsBack is { ClaimsBack: true };

    internal void RunPlayersChanged()
    {
        if (!IsRemoved)
            _players?.PlayersChanged();
    }

    internal void RunDraw(FrameDrawing frame)
    {
        if (!IsRemoved)
            Draw(frame);
    }

    internal void RunDrawView(FrameDrawing frame)
    {
        if (!IsRemoved)
            _viewDrawer?.DrawView(frame);
    }

    internal Entity? SeenThrough => IsRemoved ? null : _eyes?.Eyes;

    internal bool SeesThrough(Entity entity) => !IsRemoved && _eyes is { } eyes && eyes.LooksThrough(entity);

    internal void RunLayOutHud()
    {
        if (!IsRemoved)
            _hud?.LayOutHud();
    }

    internal IReadOnlyList<HudButton> Buttons => IsRemoved || _hud is null ? [] : _hud.HudButtons;

    internal string? ShownNotice => IsRemoved ? null : _notice?.Notice;

    internal void RunForOtherPlayer(NetPlayer player)
    {
        if (!IsRemoved)
            _tools?.RunForPlayer(player);
    }

    internal void RunGridStepStarting(GridMovement mover)
    {
        if (!IsRemoved)
            _gridSteps?.GridStepStarting(mover);
    }

    internal void RunSceneEnding()
    {
        if (!IsRemoved)
            _sceneEnd?.SceneEnding();
    }
}

/// <summary>
/// A system that draws the world its own way (a first-person view) in place of the camera's view from above: called as
/// each frame is drawn, before any system's <see cref="GameSystem.Draw"/>, to set <see cref="FrameDrawing.Sprites"/>,
/// <see cref="FrameDrawing.View"/> and <see cref="FrameDrawing.ViewProjection"/> (and <see cref="FrameDrawing.SeenThrough"/>).
/// Every system then draws over it. Called for each view too, with the view's own camera: a first-person camera can be
/// the main one or a viewport's.
/// </summary>
public interface IViewDrawer
{
    /// <summary>Draws the world the system's way into <paramref name="frame"/>.</summary>
    void DrawView(FrameDrawing frame);
}

/// <summary>A system that has the game seen through someone's eyes (a first-person view's player).</summary>
public interface ISeesThroughEyes
{
    /// <summary>
    /// The entity the game is seen through while the system draws the world through its eyes, or null: sounds are then
    /// heard from it, facing the way it faces, and <see cref="Game.Eyes"/> is it.
    /// </summary>
    Entity? Eyes { get; }

    /// <summary>
    /// True if the game is seen through <paramref name="entity"/>'s eyes on the screen of whoever plays it (in a network
    /// game, each player's own character): their controls aim the way it faces rather than with the mouse.
    /// </summary>
    bool LooksThrough(Entity entity);
}

/// <summary>A system that shows buttons and the like on the screen (a hotbar, a tool picker).</summary>
public interface IHudLayout
{
    /// <summary>
    /// Called early in each frame's Simulate phase (and on the host of a network game, again for each other player, as
    /// them: see <see cref="IPlayerTools"/>) to lay out what the system shows on the screen for the view as it is now,
    /// with its <see cref="HudButtons"/>, so whether the mouse is on it is known before anything acts on a click. Draw what
    /// it lays out through <see cref="FrameDrawing.PinToView"/>, so it keeps still on the screen as the camera is drawn
    /// moving between steps.
    /// </summary>
    void LayOutHud();

    /// <summary>The buttons the system shows on the screen this frame (a hotbar's slots, a tool picker), with what each does.</summary>
    IReadOnlyList<HudButton> HudButtons { get; }
}

/// <summary>A system that draws something on the screen the mouse can be over (a strategy game's HUD, say).</summary>
public interface IMouseOnHud
{
    /// <summary>
    /// True while the mouse is over something the system draws on the screen, so a click there isn't meant for the
    /// world: Griff's own controls leave it alone, and in a network game the host is told.
    /// </summary>
    bool MouseOnHud { get; }
}

/// <summary>A system whose HUD shows a short message.</summary>
public interface INoticeSource
{
    /// <summary>A short message the system's HUD is showing (something made, a tool's next step), or null.</summary>
    string? Notice { get; }
}

/// <summary>A system with controls that are one player's own (a hotbar, building tools), which the host of a network game runs for each player.</summary>
public interface IPlayerTools
{
    /// <summary>
    /// On the host of a network game, runs the system's controls that are one player's own for <paramref name="player"/>,
    /// as them: <see cref="Game.Input"/> is their keys and mouse, the view is their screen, and what the system keeps for
    /// a player should be theirs (see <see cref="Game.ActingFor"/>). Called once a frame for each other player with a
    /// character, after the system's <see cref="IHudLayout.LayOutHud"/> for them.
    /// </summary>
    void RunForPlayer(NetPlayer player);
}

/// <summary>A system that hears of a grid mover setting off.</summary>
public interface IGridStepListener
{
    /// <summary>
    /// Called as a grid mover sets off on a step it chose (a player's, with the crates it pushes), before anything moves: a
    /// puzzle's move history remembers where everything was.
    /// </summary>
    void GridStepStarting(GridMovement mover);
}

/// <summary>A system that keeps something when the game leaves its scene.</summary>
public interface ISceneEndListener
{
    /// <summary>
    /// Called as the game is about to leave its scene, for another or because it's stopping, while the scene is still there:
    /// to keep what the scene leaves behind (a block world saved).
    /// </summary>
    void SceneEnding();
}

/// <summary>A system that has something under way that Back should cancel first.</summary>
public interface IClaimsBack
{
    /// <summary>
    /// True while Back (Esc, the gamepad's B) would cancel something the system has under way (a strategy game's building
    /// being placed, a target being picked), so it does that first: a panel that opens on a key (a pause menu) waits.
    /// </summary>
    bool ClaimsBack { get; }
}

/// <summary>A system that settles who plays what in a network game.</summary>
public interface IPlayersListener
{
    /// <summary>
    /// Called in a network game as each scene starts and whenever the players change (someone joins, leaves or changes
    /// team), to settle who plays what (a strategy player commands their own team).
    /// </summary>
    void PlayersChanged();
}

/// <summary>
/// One step of the frame: a named piece of work in a <see cref="FramePhase"/>, belonging to a <see cref="GameSystem"/>
/// (a script's, or one of Griff's that keeps track of something) or to the game itself.
/// </summary>
public sealed class FrameStep
{
    internal FrameStep(StepId id, Action run, GameSystem? system)
    {
        Id = id;
        Run = run;
        System = system;
    }

    internal FrameStep(FramePhase phase, string name, Action run, GameSystem? system) : this(new StepId(phase, name), run, system)
    {
    }

    /// <summary>The step's id, which a module's step names to go next to it (see <see cref="Steps"/>).</summary>
    public StepId Id { get; }

    public FramePhase Phase => Id.Phase;

    public string Name => Id.Name;

    /// <summary>The system the step belongs to, or null for the game's own bookkeeping.</summary>
    public GameSystem? System { get; }

    internal Action Run { get; }

    // How a profiler counts it: as one of Griff's steps or a script's system; a step that runs scripts times each kind itself.
    internal FrameCostKind CostKind { get; init; } = FrameCostKind.Step;

    internal bool TimesItself { get; init; }

    public override string ToString() => Id.ToString();
}
