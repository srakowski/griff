using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>Which way a <see cref="Flipper"/> swings up.</summary>
public enum FlipperSide
{
    /// <summary>A flipper on the left, pointing right: it swings up anticlockwise, on the Left Flipper action.</summary>
    Left,

    /// <summary>A flipper on the right, pointing left: it swings up clockwise, on the Right Flipper action.</summary>
    Right,
}

/// <summary>
/// A pinball flipper, with no code: a paddle on a <see cref="HingeJoint"/> that snaps up while its button is held and drops
/// back as it's let go, hitting the ball hard and the same every time. How far it swings is its hinge's limits (it rests
/// against one and swings up to the other; a hinge without limits is given its side's, resting as it starts and swinging 55
/// degrees up); how fast and how hard are its <see cref="Speed"/> and <see cref="Strength"/>. Its body rotates, and should be
/// heavier than the ball (Entity &gt; Genres &gt; Physics Games &gt; Flippers makes a pair).
/// </summary>
/// <remarks>
/// It's held up by its <see cref="Action"/> (Left Flipper: Left Shift, Z, the left shoulder or trigger; Right Flipper: Right
/// Shift, /, the right shoulder or trigger), by the mouse or a finger held down on its side of the flippers, by a switch or a
/// wire into its Power, or for a moment by its Flip action. A <see cref="Tilt"/> that has tilted drops it and keeps it down
/// until the ball drains. A fast ball doesn't go through it: the game works out where the two will meet within each step.
/// </remarks>
[Category("Physics Games")]
[Requires(typeof(HingeJoint), Note = "what it swings on: its limits are how far")]
[WorksWith(typeof(Tilt), Note = "tilted, it drops and stays down until the ball drains")]
[WorksWith(typeof(TurnTaker), Note = "its side: flipped on its side's turn", Back = "a Flipper flipped on its side's turn")]
[ComponentName("Griff.Flipper")]
public sealed class Flipper : Component, IPlaytestReport
{
    /// <summary>How long its Flip action holds it up, in seconds.</summary>
    internal const float FlipSeconds = 0.18f;

    /// <summary>Which way it swings up: Left (a flipper pointing right, swinging up anticlockwise) or Right.</summary>
    public FlipperSide Side { get; set; }

    /// <summary>
    /// How fast it swings, in degrees a second, up as it's pressed and down as it's let go: 2,000 snaps it up in a few
    /// hundredths of a second, as a real flipper does.
    /// </summary>
    public float Speed { get; set; } = 2000;

    /// <summary>
    /// How hard it swings (the most its hinge's motor turns it with): enough to hold the ball cradled on it, and to hit it as
    /// hard each time whatever it was doing. Lower, a heavy ball slows it down.
    /// </summary>
    public float Strength { get; set; } = 4000;

    // ----- Controls -----

    /// <summary>
    /// The input action that holds it up. Empty: Left Flipper or Right Flipper by its <see cref="Side"/> (Left or Right Shift,
    /// Z or /, a shoulder or trigger of the gamepad).
    /// </summary>
    [Group("Controls")]
    [Names(NameKind.Action)]
    public string? Action { get; set; }

    /// <summary>
    /// The mouse button or a finger held down on its side of the flippers (left of the middle between them, for one on the
    /// left) holds it up too: on a phone, a finger on the left of the screen flips the left flipper, and a finger on each side
    /// holds both up.
    /// </summary>
    public bool Touch { get; set; } = true;

    /// <summary>
    /// Untick it to have it ignore the keys, mouse and gamepad (its actions, a wire and scripts still work): another part of
    /// the scene has the player's input. Its Focus action ticks it and unticks it on everything else the player plays.
    /// </summary>
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>Held up by a switch or a wire into its Power (or a script), whatever the buttons say.</summary>
    [Powered]
    public bool Raised { get; set; }

    // ----- Sound and messages -----

    /// <summary>A sound played as it swings up, e.g. "Sounds/flip.wav".</summary>
    [Group("Sound and Messages")]
    public string? Sound { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> as it swings up, with the flipper as its data, e.g. "Flipped".</summary>
    [Names(NameKind.Message)]
    public string? Message { get; set; }

    // ----- State, kept with the scene -----

    /// <summary>True while its button (the keys, the mouse, a finger) is held down. Set by the game.</summary>
    [Group("State")]
    public bool Pressed { get; set; }

    /// <summary>Seconds its Flip action still holds it up. Set by the game.</summary>
    public float FlipFor { get; set; }

    /// <summary>True while it's swinging up or held up: pressed, powered or flipped, and not tilted. Set by the game.</summary>
    [Output]
    public bool Up { get; set; }

    /// <summary>How many times it has swung up.</summary>
    [Output]
    public int Flips { get; set; }

    // Hidden in the Inspector: only the game sets it.
    public override bool Uses(string property) => property is not nameof(FlipFor);

    /// <summary>The input action that holds it up: its <see cref="Action"/>, or Left Flipper or Right Flipper by its side.</summary>
    public string InputAction => string.IsNullOrWhiteSpace(Action) ? Side == FlipperSide.Left ? "LeftFlipper" : "RightFlipper" : Action.Trim();

    /// <summary>Which way it swings up: 1 anticlockwise (a left flipper), -1 clockwise.</summary>
    internal int UpWay => Side == FlipperSide.Left ? 1 : -1;

    /// <summary>Swings it up for a moment and lets it drop: a flipper a Trigger, a wire or the beat flips.</summary>
    [Action]
    public void Flip() => FlipFor = FlipSeconds;

    /// <summary>Holds it up until its Lower action (or until it's tilted).</summary>
    [Action]
    public void Raise() => Raised = true;

    /// <summary>Lets it drop, held up by its Raise action or a wire no longer.</summary>
    [Action]
    public void Lower() => (Raised, FlipFor) = (false, 0);

    /// <summary>Raised as it swings up.</summary>
    public event Action<Flipper>? Flipped;

    internal void RaiseFlipped() => Flipped?.Invoke(this);

    string IPlaytestReport.Report() => Up ? ", up" : ", down";
}
