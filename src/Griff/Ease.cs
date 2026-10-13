namespace Griff;

/// <summary>
/// Easing curves for tweens: each maps progress 0–1 to an eased 0–1 (some briefly overshoot, like
/// <see cref="OutBack"/>). Pass one to <see cref="Runtime.Game.Tween"/>, <c>MoveTo</c> and friends, or write your own.
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
