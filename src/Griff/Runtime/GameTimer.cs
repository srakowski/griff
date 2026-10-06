using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// A callback scheduled with <see cref="Game.After"/> or <see cref="Game.Every"/>. Timers run on game time,
/// so they slow down with <see cref="Time.Scale"/> and stop while the game is paused.
/// </summary>
public sealed class GameTimer
{
    private readonly Action _callback;

    internal GameTimer(double due, float interval, Action callback, Entity? owner)
    {
        Due = due;
        Interval = interval;
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        Owner = owner;
    }

    /// <summary>Seconds between repeats, or 0 for a one-shot timer.</summary>
    public float Interval { get; }

    /// <summary>The entity whose removal cancels this timer, if any.</summary>
    public Entity? Owner { get; }

    /// <summary>False once a one-shot timer has fired or the timer was cancelled.</summary>
    public bool IsActive { get; private set; } = true;

    internal double Due { get; private set; }

    public void Cancel() => IsActive = false;

    /// <summary>Fires every due occurrence up to <paramref name="now"/>.</summary>
    internal void Run(double now, Scene scene)
    {
        while (IsActive && Due <= now)
        {
            if (Owner is not null && Owner.Scene != scene)
            {
                Cancel();
                return;
            }

            // Update state before calling back, so a throwing callback can't fire forever.
            if (Interval > 0)
                Due += Interval;
            else
                IsActive = false;
            _callback();
        }
    }
}
