using Griff.Scenes;

namespace Griff.Runtime;

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
