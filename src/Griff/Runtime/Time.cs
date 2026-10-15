namespace Griff.Runtime;

/// <summary>
/// Game time. The game moves in steps of <see cref="FixedDelta"/> (a sixtieth of a second unless <see cref="StepRate"/>
/// says otherwise), whatever the screen's frame rate, so the same input gives the same game on any machine; the screen
/// is drawn between the last two steps (see <see cref="Blend"/>). <see cref="Delta"/> is a step's game time: multiply
/// speeds by it.
/// </summary>
public sealed class Time
{
    /// <summary>
    /// The most real time a single frame may cover, so a stall (breakpoint, window drag) doesn't teleport things: a frame
    /// takes at most this much in steps (6 at 60 a second), and the game slows down rather than falling further behind.
    /// </summary>
    public const float MaxDelta = 0.1f;

    /// <summary>How many steps a second a game takes unless its project or a script says otherwise.</summary>
    public const int DefaultStepRate = 60;

    /// <summary>The fewest steps a second <see cref="StepRate"/> takes.</summary>
    public const int MinStepRate = 10;

    /// <summary>The most steps a second <see cref="StepRate"/> takes.</summary>
    public const int MaxStepRate = 240;

    /// <summary>Game seconds this step covers, after <see cref="Scale"/>: <see cref="FixedDelta"/> at normal speed.</summary>
    public float Delta { get; private set; }

    /// <summary>Real seconds this step covers, ignoring <see cref="Scale"/>: <see cref="FixedDelta"/> as the game runs.</summary>
    public float UnscaledDelta { get; private set; }

    /// <summary>Scaled seconds since the game started.</summary>
    public double Total { get; private set; }

    /// <summary>How many steps the game has taken since it started.</summary>
    public long Frame { get; private set; }

    /// <summary>1 is normal speed, 0.5 is slow motion, 0 pauses gameplay.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>
    /// How many steps the game takes a second: the project's Step Rate (Game &gt; Step Rate in the editor, 60 unless it's
    /// changed), and a script can change it as the game runs (say, 120 for a level whose fast bodies need finer steps).
    /// Each step is <see cref="FixedDelta"/> long. From 10 to 240.
    /// </summary>
    public int StepRate
    {
        get => _stepRate;
        set => _stepRate = Math.Clamp(value, MinStepRate, MaxStepRate);
    }

    private int _stepRate = DefaultStepRate;

    /// <summary>Real seconds each step covers: 1 / <see cref="StepRate"/>.</summary>
    public float FixedDelta => (float)StepSeconds;

    internal double StepSeconds => 1.0 / _stepRate;

    /// <summary>
    /// How far the screen being drawn is between the game's last two steps, from 0 (the one before) to 1 (the last):
    /// what's drawn is placed that far from where it was to where it is, so it moves smoothly on any screen. 1 when the
    /// game is stepped by hand or held still.
    /// </summary>
    public float Blend { get; internal set; } = 1;

    /// <summary>Frames the screen has shown (one each <see cref="Game.Advance"/> or <see cref="Game.Tick"/>), where <see cref="Frame"/> counts steps.</summary>
    public long DrawnFrames { get; private set; }

    /// <summary>Real seconds since the game started, counted frame by frame as the screen shows them, stalls included.</summary>
    public double RealTotal { get; private set; }

    /// <summary>
    /// True while a <see cref="Scenes.Panel"/> that pauses the game is open (a pause menu): <see cref="Delta"/> is 0
    /// whatever <see cref="Scale"/> is, and scripts outside open panels don't update. Set by the game.
    /// </summary>
    public bool IsPaused { get; internal set; }

    /// <summary>True during a <see cref="Game.HitStop"/>: <see cref="Delta"/> is 0 whatever <see cref="Scale"/> is.</summary>
    public bool IsFrozen => _frozenFor > 0;

    /// <summary>
    /// How much <see cref="Game.SlowMotion"/> is slowing the game right now: 1 at normal speed. It multiplies
    /// <see cref="Scale"/>, so a game paused with Scale 0 stays paused.
    /// </summary>
    public float SlowMotionScale => _slowLeft > 0 ? SlowMotionAt(_slowLeft) : 1;

    private float _frozenFor;

    /// <summary>Seconds left of the hit stop under way (see <see cref="IsFrozen"/>).</summary>
    internal float FrozenLeft => _frozenFor;
    private float _slowScale = 1;
    private float _slowSeconds;
    private float _slowLeft;

    // The last quarter of a slow motion eases back to full speed, rather than snapping.
    private const float SlowMotionRecovery = 0.25f;

    /// <summary>Stops game time for <paramref name="seconds"/> of real time, or longer if a longer freeze is already going.</summary>
    internal void Freeze(float seconds) => _frozenFor = Math.Max(_frozenFor, seconds);

    /// <summary>
    /// Runs game time at <paramref name="scale"/> for <paramref name="seconds"/> of real time. One already going is replaced
    /// if this one is slower, or lasts longer at the same speed.
    /// </summary>
    internal void SlowDown(float scale, float seconds)
    {
        if (_slowLeft > 0 && SlowMotionScale < scale)
            return;
        (_slowScale, _slowSeconds, _slowLeft) = (scale, seconds, Math.Max(seconds, _slowScale == scale ? _slowLeft : 0));
    }

    // One step, covering `seconds` of real time.
    internal void Advance(double seconds)
    {
        UnscaledDelta = Math.Clamp((float)seconds, 0f, MaxDelta);
        var frozen = _frozenFor > 0;
        _frozenFor = Math.Max(0, _frozenFor - UnscaledDelta);
        var slow = SlowMotionScale;
        _slowLeft = Math.Max(0, _slowLeft - UnscaledDelta);
        Delta = frozen || IsPaused ? 0 : UnscaledDelta * Scale * slow;
        Total += Delta;
        Frame++;
    }

    // A frame the screen shows, `seconds` of real time after the last.
    internal void CountFrame(double seconds)
    {
        DrawnFrames++;
        RealTotal += seconds;
    }

    private float SlowMotionAt(float left)
    {
        var recovery = _slowSeconds * SlowMotionRecovery;
        return left >= recovery || recovery <= 0 ? _slowScale : _slowScale + (1 - _slowScale) * (1 - left / recovery);
    }
}
