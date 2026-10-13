using System.Collections;
using System.Globalization;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// A sequence that runs over several frames, started with a behavior's <c>StartCoroutine</c> or
/// <see cref="Game.StartCoroutine"/>. It runs right away up to its first <c>yield return</c>, then picks up again once
/// what it yielded is done:
/// <list type="bullet">
/// <item><c>null</c>: the next frame.</item>
/// <item><c>Wait(seconds)</c> (or just a number of seconds): that much game time.</item>
/// <item><c>WaitUntil(() => ...)</c> and <c>WaitWhile(() => ...)</c>: a condition, checked every frame.</item>
/// <item>A <see cref="Tween"/>, <see cref="GameTimer"/> or another <see cref="Coroutine"/>: when it finishes.</item>
/// <item>Another <see cref="IEnumerator"/> method: runs it to the end first, like calling it.</item>
/// </list>
/// Like timers, coroutines run on game time, so they slow down with <see cref="Time.Scale"/> and stop while the game
/// is paused.
/// </summary>
/// <example>
/// <code>
/// IEnumerator Waves()
/// {
///     for (var wave = 1; wave &lt;= 3; wave++)
///     {
///         for (var i = 0; i &lt; wave * 4; i++)
///         {
///             Game.Spawn("Enemy", Rand.InsideCircle(6));
///             yield return Wait(0.3f);
///         }
///         yield return WaitUntil(() => Game.FindAll&lt;Enemy&gt;().Count == 0);
///     }
///     Game.LoadScene("Win");
/// }
/// </code>
/// </example>
public sealed class Coroutine
{
    private readonly Stack<IEnumerator> _stack = new();
    private Func<bool>? _resumeWhen;
    private long _yieldedOnFrame = -1;

    internal Coroutine(IEnumerator routine, Entity? ownerEntity, Behavior? ownerBehavior)
    {
        _stack.Push(routine ?? throw new ArgumentNullException(nameof(routine)));
        OwnerEntity = ownerEntity;
        OwnerBehavior = ownerBehavior;
    }

    /// <summary>False once the coroutine has finished or been stopped.</summary>
    public bool IsRunning { get; private set; } = true;

    /// <summary>The entity whose removal stops this coroutine, if any.</summary>
    public Entity? OwnerEntity { get; }

    // Behaviors own their coroutines, so a script reload (which swaps in a new behavior) stops the old version's.
    internal Behavior? OwnerBehavior { get; }

    public void Stop() => IsRunning = false;

    /// <summary>Resumes the coroutine if what it's waiting for is done. Every wait lasts at least until the next frame.</summary>
    internal void Run(Game game)
    {
        if (!IsRunning)
            return;
        if (!IsOwnerAlive(game.Scene))
        {
            Stop();
            return;
        }

        if (game.Time.Frame <= _yieldedOnFrame || (_resumeWhen is not null && !_resumeWhen()))
            return;
        _resumeWhen = null;

        try
        {
            Advance(game);
        }
        catch
        {
            Stop();
            throw;
        }
    }

    private bool IsOwnerAlive(Scene scene) =>
        (OwnerEntity is null || OwnerEntity.Scene == scene) &&
        (OwnerBehavior is null || OwnerBehavior.Entity?.Scene == scene);

    // Runs the innermost routine to its next wait. A finished nested routine hands back to its caller straight away.
    private void Advance(Game game)
    {
        while (IsRunning)
        {
            var routine = _stack.Peek();
            if (!routine.MoveNext())
            {
                _stack.Pop();
                if (_stack.Count == 0)
                {
                    Stop();
                    return;
                }

                continue;
            }

            if (routine.Current is IEnumerator nested)
            {
                _stack.Push(nested);
                continue;
            }

            _resumeWhen = WaitFor(routine.Current, game);
            _yieldedOnFrame = game.Time.Frame;
            return;
        }
    }

    private static Func<bool>? WaitFor(object? yielded, Game game)
    {
        switch (yielded)
        {
            case null:
                return null;
            case WaitSeconds wait:
                return SecondsFrom(game, wait.Seconds);
            case float or double or int:
                return SecondsFrom(game, Convert.ToDouble(yielded, CultureInfo.InvariantCulture));
            case WaitCondition condition:
                return condition.IsDone;
            case Tween tween:
                return () => !tween.IsActive;
            case GameTimer timer:
                return () => !timer.IsActive;
            case Coroutine coroutine:
                return () => !coroutine.IsRunning;
            default:
                throw new InvalidOperationException(
                    $"A coroutine yielded {yielded.GetType().Name}, which it can't wait for. Yield null (next frame), " +
                    "Wait(seconds), WaitUntil(...), a tween, a timer, another coroutine or an IEnumerator.");
        }
    }

    private static Func<bool> SecondsFrom(Game game, double seconds)
    {
        var due = game.Time.Total + Math.Max(0, seconds);
        return () => game.Time.Total >= due;
    }
}

/// <summary>Yield from a coroutine to wait this many seconds of game time. Made by a behavior's <c>Wait</c>.</summary>
public sealed record WaitSeconds(float Seconds);

/// <summary>Yield from a coroutine to wait until <see cref="IsDone"/> is true. Made by <c>WaitUntil</c> and <c>WaitWhile</c>.</summary>
public sealed record WaitCondition(Func<bool> IsDone);
