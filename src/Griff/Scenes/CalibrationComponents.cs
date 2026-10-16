using System.Numerics;

namespace Griff.Scenes;

/// <summary>What a <see cref="Calibration"/> measures.</summary>
public enum CalibrationKind
{
    /// <summary>How late the player hears the sound: they tap along to clicks (<c>Game.AudioOffset</c>).</summary>
    Audio,

    /// <summary>How late the player sees the screen: they tap along to a light flashing in silence (<c>Game.VisualOffset</c>).</summary>
    Visual,
}

/// <summary>Where a <see cref="Calibration"/> has got to.</summary>
public enum CalibrationState
{
    /// <summary>Not started (or stopped): its Start action starts it.</summary>
    Waiting,

    /// <summary>Counting in: the first bar of clicks or flashes, before the taps count.</summary>
    Listening,

    /// <summary>The player taps along; each tap on a beat counts.</summary>
    Tapping,

    /// <summary>Done, with a <see cref="Calibration.Result"/> to offer: its Apply action sets the player's offset to it.</summary>
    Measured,

    /// <summary>Done, but too few taps were steady enough to tell: start it again.</summary>
    Failed,
}

/// <summary>
/// Measures how late this player hears the game's sound (speakers with a delay of their own, like Bluetooth headphones) or
/// sees its screen, and offers to set their offset to it: it plays clicks (or, measuring Visual, flashes its light in
/// silence) on a steady beat, the player taps along with its <see cref="TapAction"/>, a click or a touch for a few bars,
/// and it works out how long after each beat the taps came, leaves out the odd ones and offers their average as the
/// <see cref="Result"/>, which its Apply action makes the player's <c>Game.AudioOffset</c> (or VisualOffset), kept in
/// their save data. Beat clocks allow for the offset from then on.
/// </summary>
/// <remarks>
/// It starts as it's shown (a panel with it opens; see <see cref="StartOnShow"/>), playing its clicks in place of the music
/// (which comes back when it's done), and hides the buttons inside it while the player taps, showing them again when it's
/// done: Entity &gt; Audio &gt; Create Calibration Screen makes a panel with one, its prompt, and Apply, Again and Back
/// buttons, for an options menu's button to open. It draws its light and a dot for each beat to tap, lit as each tap
/// lands. Its Start, Stop and Apply actions are there for buttons and scripts, its <see cref="Prompt"/> for a text to say
/// what to do, and its <see cref="Result"/>, <see cref="Taps"/> and <see cref="Current"/> outputs for anything else. It runs
/// in real time, under a pause menu too, and each machine keeps its own in a network game: it measures and sets its own
/// player's offset, which no other player's game ever sees.
/// </remarks>
[Category("Audio")]
[ComponentName("Griff.Calibration")]
[WorksWith(typeof(Panel), Note = "to open it from an options menu")]
[WorksWith(typeof(Text), Note = "showing its Prompt")]
[WorksWith(typeof(BeatClock), Note = "which allows for the offset it sets")]
[NetLocal]
public sealed class Calibration : Component, IDrawable, IActionReady
{
    /// <summary>What it measures: Audio taps along to clicks, Visual to a light flashing in silence.</summary>
    public CalibrationKind Measures { get; set; }

    /// <summary>Beats a minute of the clicks or flashes: slow enough that a late tap isn't taken for the next beat's.</summary>
    public float Tempo { get; set; } = 90;

    /// <summary>Beats to listen to (or watch) before the taps count.</summary>
    public int CountIn { get; set; } = 4;

    /// <summary>Beats to tap along to. Half of them need steady taps for a result.</summary>
    public int Beats { get; set; } = 16;

    /// <summary>Starts as soon as it's shown (as its panel opens, or the scene starts); off, it waits for its Start action.</summary>
    public bool StartOnShow { get; set; } = true;

    /// <summary>How loud the clicks are, 0 to 1, on top of the player's sound volume.</summary>
    public float Volume { get; set; } = 1;

    /// <summary>
    /// An input action that taps: Jump is Space and the gamepad's A. Empty leaves just clicks and touches. Like a menu's
    /// controls, it listens while it's on the top panel (or no panel is open).
    /// </summary>
    [Group("Controls")]
    [Names(NameKind.Action)]
    public string? TapAction { get; set; } = "Jump";

    /// <summary>A click of the left mouse button, or a touch, taps too.</summary>
    public bool Clicks { get; set; } = true;

    /// <summary>The color of its light and of the dots for the beats.</summary>
    [Group("Look")]
    public Color Color { get; set; } = Color.FromBytes(0xF0, 0x8C, 0x2E);

    /// <summary>How big its light is, in world units (the dots go under it); 0 draws nothing.</summary>
    public float Size { get; set; } = 1.2f;

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/> (an entity's sprite says for it).</summary>
    public int Order { get; set; } = 310;

    /// <summary>Where it has got to.</summary>
    [Group("State")]
    [DoNotSerialize]
    public CalibrationState State { get; internal set; }

    /// <summary>True while the clicks or flashes are going.</summary>
    [DoNotSerialize]
    [Output]
    public bool IsRunning => State is CalibrationState.Listening or CalibrationState.Tapping;

    /// <summary>The taps counted so far, on the beats to tap along to.</summary>
    [DoNotSerialize]
    [Output(OutOf = nameof(Beats))]
    public int Taps { get; internal set; }

    /// <summary>
    /// What it measured, in milliseconds: how long after the beats the steady taps came, on average. Below 0 when they came
    /// before. 0 until it's measured.
    /// </summary>
    [DoNotSerialize]
    [Output]
    public float Result { get; internal set; }

    /// <summary>How far the steady taps were from their average, in milliseconds: how much to trust the result.</summary>
    [DoNotSerialize]
    [Output]
    public float Spread { get; internal set; }

    /// <summary>True once its result has been applied, until it starts again.</summary>
    [DoNotSerialize]
    [Output]
    public bool Applied { get; internal set; }

    /// <summary>The player's offset it measures, as it's set now, in milliseconds (<c>Game.AudioOffset</c> or VisualOffset).</summary>
    [DoNotSerialize]
    [Output]
    public float Current { get; internal set; }

    /// <summary>What it sets, for a heading: "Audio Offset" or "Visual Offset".</summary>
    [DoNotSerialize]
    [Output]
    public string Title => Measures == CalibrationKind.Visual ? "Visual Offset" : "Audio Offset";

    /// <summary>
    /// What to tell the player now, for a <see cref="Text"/> to show (its Output): how to tap, how many taps so far, what it
    /// measured and whether to apply it. It names the tap action in braces, so the text shows its key or button.
    /// </summary>
    [DoNotSerialize]
    [Output]
    public string Prompt { get; internal set; } = "";

    /// <summary>Raised when it's done measuring, with a result or without.</summary>
    public event Action<Calibration>? Finished;

    /// <summary>Starts it (again): the clicks or flashes begin, and earlier taps are forgotten.</summary>
    [Action]
    public void Start() => Wanted = CalibrationRequest.Start;

    /// <summary>Stops it without a result, bringing the music back.</summary>
    [Action]
    public void Stop() => Wanted = CalibrationRequest.Stop;

    /// <summary>Sets the player's offset to what it measured (kept in their save data), once it has measured something.</summary>
    [Action]
    private void Apply(IPlayerOffsets offsets)
    {
        if (State != CalibrationState.Measured)
            return;
        if (Measures == CalibrationKind.Visual)
            offsets.VisualOffset = Result;
        else
            offsets.AudioOffset = Result;
        (Applied, Current) = (true, Result);
    }

    // Apply only has something to apply once it has measured; a button doing it is faded until then.
    bool IActionReady.IsReady(string action) => action != nameof(Apply) || (State == CalibrationState.Measured && !Applied);

    // Asked for by an action, done by the game on its next step.
    internal CalibrationRequest Wanted { get; set; }

    // Where the clicks are, in beats from the first (as the sound card says they're heard, before any offset), and when (in
    // the same beats) the last tap was.
    internal double Position { get; set; } = -1;

    internal double LastTap { get; set; } = double.NegativeInfinity;

    // How late each tap on a beat came, in seconds, and which of the beats to tap along to have had one.
    internal List<double> Lags { get; } = [];

    internal bool[] Tapped { get; set; } = [];

    // The music that was playing when it started, to bring back; the buttons it hid; what the prompt was made from.
    internal string? Resume { get; set; }

    internal List<Entity> Hidden { get; } = [];

    internal (CalibrationState, CalibrationKind, int, int, float, float, bool, string?, bool)? PromptMadeFrom { get; set; }

    internal void RaiseFinished() => Finished?.Invoke(this);

    int? IDrawable.DrawOrder => Order;

    int IDrawable.DrawStep => DrawSteps.Shapes;

    // The light (flashing on the beat when measuring what's seen, on each tap when measuring what's heard, so nothing on
    // the screen gives away when the click comes), and under it a dot for each beat to tap, lit once it's been tapped.
    void IDrawable.Draw(Drawing drawing)
    {
        var size = Size;
        if (!(size > 0))
            return;
        var lit = 0.25f;
        if (IsRunning)
        {
            var since = Measures == CalibrationKind.Visual ? Position - Math.Floor(Position) : Position - LastTap;
            if (Position >= 0 && since >= 0 && since * 60 / Math.Max(1, Tempo) < 0.1)
                lit = 1;
        }

        drawing.Rect(Vector2.Zero, new Vector2(size), Color with { A = Color.A * lit });

        var beats = Tapped.Length > 0 ? Tapped.Length : Math.Max(0, Beats);
        if (beats == 0)
            return;
        var gap = Math.Min(size / 4, size * 5 / beats);
        var dot = gap * 0.55f;
        var y = -size / 2 - gap * 1.5f;
        for (var i = 0; i < beats; i++)
        {
            var on = i < Tapped.Length && Tapped[i];
            drawing.Rect(new Vector2((i - (beats - 1) / 2f) * gap, y), new Vector2(dot), Color with { A = Color.A * (on ? 1 : 0.25f) });
        }
    }
}

/// <summary>What a calibration's action asked for, done on the game's next step.</summary>
internal enum CalibrationRequest
{
    None,
    Start,
    Stop,
}
