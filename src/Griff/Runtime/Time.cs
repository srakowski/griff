namespace Griff.Runtime;

/// <summary>Frame timing. <see cref="Delta"/> is scaled; multiply speeds by it for frame-rate independence.</summary>
public sealed class Time
{
    /// <summary>Longest step a single frame may take, so a stall (breakpoint, window drag) doesn't teleport things.</summary>
    public const float MaxDelta = 0.1f;

    /// <summary>Seconds since the previous frame, after <see cref="Scale"/>.</summary>
    public float Delta { get; private set; }

    /// <summary>Seconds since the previous frame, ignoring <see cref="Scale"/>.</summary>
    public float UnscaledDelta { get; private set; }

    /// <summary>Scaled seconds since the game started.</summary>
    public double Total { get; private set; }

    public long Frame { get; private set; }

    /// <summary>1 is normal speed, 0.5 is slow motion, 0 pauses gameplay.</summary>
    public float Scale { get; set; } = 1f;

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

    private float SlowMotionAt(float left)
    {
        var recovery = _slowSeconds * SlowMotionRecovery;
        return left >= recovery || recovery <= 0 ? _slowScale : _slowScale + (1 - _slowScale) * (1 - left / recovery);
    }
}
