using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Easing curves for tweens: each maps progress 0–1 to an eased 0–1 (some briefly overshoot, like
/// <see cref="OutBack"/>). Pass one to <see cref="Game.Tween"/>, <c>MoveTo</c> and friends, or write your own.
/// </summary>
public static class Ease
{
    public static readonly Func<float, float> Linear = t => t;

    public static readonly Func<float, float> InQuad = t => t * t;
    public static readonly Func<float, float> OutQuad = t => 1 - (1 - t) * (1 - t);
    public static readonly Func<float, float> InOutQuad = t => t < 0.5f ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t);

    public static readonly Func<float, float> InCubic = t => t * t * t;
    public static readonly Func<float, float> OutCubic = t => 1 - MathF.Pow(1 - t, 3);
    public static readonly Func<float, float> InOutCubic = t => t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(2 - 2 * t, 3) / 2;

    public static readonly Func<float, float> InOutSine = t => (1 - MathF.Cos(t * MathF.PI)) / 2;

    /// <summary>Pulls back a little, then goes.</summary>
    public static readonly Func<float, float> InBack = t => 2.70158f * t * t * t - 1.70158f * t * t;

    /// <summary>Overshoots the target a little, then settles. Good for things popping into place.</summary>
    public static readonly Func<float, float> OutBack = t => 1 + 2.70158f * MathF.Pow(t - 1, 3) + 1.70158f * MathF.Pow(t - 1, 2);

    /// <summary>Springs past the target and wobbles to rest.</summary>
    public static readonly Func<float, float> OutElastic = t =>
        t <= 0 ? 0 : t >= 1 ? 1 : MathF.Pow(2, -10 * t) * MathF.Sin((t * 10 - 0.75f) * (2 * MathF.PI / 3)) + 1;

    /// <summary>Lands like a dropped ball.</summary>
    public static readonly Func<float, float> OutBounce = t =>
    {
        const float n = 7.5625f, d = 2.75f;
        if (t < 1 / d)
            return n * t * t;
        if (t < 2 / d)
            return n * (t -= 1.5f / d) * t + 0.75f;
        if (t < 2.5f / d)
            return n * (t -= 2.25f / d) * t + 0.9375f;
        return n * (t -= 2.625f / d) * t + 0.984375f;
    };
}

/// <summary>
/// A value animated over time, made with <see cref="Game.Tween"/> or a behavior's <c>MoveTo</c>, <c>ScaleTo</c>,
/// <c>RotateTo</c> and <c>FadeTo</c>. Tweens run on game time, so they slow down with <see cref="Time.Scale"/> and stop
/// while the game is paused.
/// </summary>
public sealed class Tween
{
    private readonly Action<float> _apply;
    private readonly Func<float, float> _ease;
    private Action? _then;

    internal Tween(double start, float duration, Action<float> apply, Func<float, float>? ease, Entity? owner, object? key)
    {
        Start = start;
        Duration = Math.Max(0, duration);
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _ease = ease ?? Ease.OutQuad;
        Owner = owner;
        Key = key;
    }

    /// <summary>Seconds from start to finish.</summary>
    public float Duration { get; }

    /// <summary>The entity whose removal cancels this tween, if any.</summary>
    public Entity? Owner { get; }

    /// <summary>False once the tween has finished or was cancelled.</summary>
    public bool IsActive { get; private set; } = true;

    internal double Start { get; }

    // Tweens with the same key replace each other, so a new MoveTo takes over from an unfinished one.
    internal object? Key { get; }

    /// <summary>Stops the tween where it is. Its <see cref="Then"/> action doesn't run.</summary>
    public void Cancel() => IsActive = false;

    /// <summary>
    /// Runs <paramref name="action"/> when the tween finishes (not if it's cancelled). Start another tween from it
    /// to chain them. Returns this tween.
    /// </summary>
    public Tween Then(Action action)
    {
        _then += action;
        return this;
    }

    /// <summary>Applies the tween's value at <paramref name="now"/>, finishing it once its time is up.</summary>
    internal void Run(double now, Scene scene)
    {
        if (!IsActive)
            return;
        if (Owner is not null && Owner.Scene != scene)
        {
            Cancel();
            return;
        }

        var progress = Duration > 0 ? (float)Math.Clamp((now - Start) / Duration, 0, 1) : 1;
        if (progress >= 1)
        {
            // Update state before calling back, so a throwing callback can't run forever.
            IsActive = false;
            _apply(1);
            _then?.Invoke();
            return;
        }

        _apply(_ease(progress));
    }
}
