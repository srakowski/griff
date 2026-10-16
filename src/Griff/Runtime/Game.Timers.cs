using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for timers: each member forwards to TimerSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>
    /// Calls <paramref name="action"/> once, <paramref name="seconds"/> of game time from now.
    /// With an <paramref name="owner"/>, the timer is cancelled if that entity leaves the scene first.
    /// </summary>
    public GameTimer After(float seconds, Action action, Entity? owner = null) =>
        _timerSystem.After(seconds, action, owner);

    /// <summary>Calls <paramref name="action"/> every <paramref name="seconds"/> of game time until cancelled.</summary>
    public GameTimer Every(float seconds, Action action, Entity? owner = null) =>
        _timerSystem.Every(seconds, action, owner);

    /// <summary>
    /// Runs <paramref name="routine"/> over several frames: see <see cref="Coroutine"/> for what it can wait for. It runs
    /// straight away up to its first <c>yield return</c>. With an <paramref name="owner"/>, it stops if that entity leaves
    /// the scene first.
    /// </summary>
    public Coroutine StartCoroutine(System.Collections.IEnumerator routine, Entity? owner = null) =>
        _timerSystem.StartCoroutine(routine, owner);

    /// <summary>
    /// Calls <paramref name="apply"/> every frame for <paramref name="seconds"/> of game time with progress from 0 to 1,
    /// shaped by <paramref name="ease"/> (<see cref="Ease.OutQuad"/> by default). The last call is always exactly 1.
    /// With an <paramref name="owner"/>, the tween is cancelled if that entity leaves the scene first.
    /// </summary>
    /// <example><code>Game.Tween(0.5f, t => text.Size = 1 + t, Ease.OutBack);</code></example>
    public Tween Tween(float seconds, Action<float> apply, Func<float, float>? ease = null, Entity? owner = null) =>
        _timerSystem.Tween(seconds, apply, ease, owner);

    /// <summary>Glides <paramref name="entity"/> to <paramref name="position"/> (in its parent's space), keeping its Z.</summary>
    public Tween MoveTo(Entity entity, Vector2 position, float seconds, Func<float, float>? ease = null) =>
        _timerSystem.MoveTo(entity, position, seconds, ease);

    /// <summary>Grows or shrinks <paramref name="entity"/> to <paramref name="scale"/> on X and Y, keeping its Z scale.</summary>
    public Tween ScaleTo(Entity entity, Vector2 scale, float seconds, Func<float, float>? ease = null) =>
        _timerSystem.ScaleTo(entity, scale, seconds, ease);

    /// <summary>Turns <paramref name="entity"/> to <paramref name="degrees"/> around Z (360 or more spins it round).</summary>
    public Tween RotateTo(Entity entity, float degrees, float seconds, Func<float, float>? ease = null) =>
        _timerSystem.RotateTo(entity, degrees, seconds, ease);

    /// <summary>
    /// Fades <paramref name="entity"/>'s sprite, text and tilemap to <paramref name="alpha"/> (0 invisible, 1 opaque),
    /// e.g. <c>FadeTo(0, 0.3f).Then(() => Destroy(Entity))</c>.
    /// </summary>
    public Tween FadeTo(Entity entity, float alpha, float seconds, Func<float, float>? ease = null) =>
        _timerSystem.FadeTo(entity, alpha, seconds, ease);
}
