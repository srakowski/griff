using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Base class for gameplay scripts. Override <see cref="Start"/> and <see cref="Update"/>;
/// public read/write properties are saved with the scene and editable in the inspector.
/// </summary>
/// <example>
/// <code>
/// public class Mover : Behavior
/// {
///     public float Speed { get; set; } = 5;
///
///     protected override void Update()
///     {
///         Transform.Position += new Vector3(Input.Horizontal, Input.Vertical, 0) * Speed * Time.Delta;
///     }
/// }
/// </code>
/// </example>
public abstract class Behavior : Component, IHealthListener
{
    private Game? _game;

    /// <summary>The running game. Only available from <see cref="Start"/> onwards.</summary>
    protected Game Game => _game ?? throw new InvalidOperationException("The behavior isn't running in a game yet.");

    protected Transform Transform => Entity!.Transform;

    protected Time Time => Game.Time;

    /// <summary>
    /// The controls of whoever plays this entity: this machine's, or in a network game, those of the player it belongs
    /// to (see <see cref="Runtime.Game.InputOf"/>).
    /// </summary>
    protected IInput Input => Game.InputOf(Entity);

    protected IAudio Audio => Game.Audio;

    /// <summary>Draws lines, boxes, circles and text over the game for a frame; see <see cref="DebugDraw"/>.</summary>
    protected DebugDraw Debug => Game.Debug;

    internal bool IsStarted { get; private set; }

    /// <summary>Called once, before the first <see cref="Update"/>, after every behavior in the scene exists.</summary>
    protected virtual void Start()
    {
    }

    /// <summary>Called every frame while the behavior and its entity are enabled.</summary>
    protected virtual void Update()
    {
    }

    /// <summary>
    /// Called every frame after <see cref="Update"/>, once everything else has moved: physics, pickups and damage, and
    /// cameras following their targets. Good for a camera of your own, or something that stays by what moved. Screen
    /// anchors are placed afterwards, so they stay pinned to wherever the camera ends up.
    /// </summary>
    protected virtual void LateUpdate()
    {
    }

    /// <summary>Called when this entity's body bumps into a solid collider, or something bumps into it.</summary>
    protected virtual void OnCollision(Entity other)
    {
    }

    /// <summary>Called when a trigger and another collider start overlapping (on both entities).</summary>
    protected virtual void OnTriggerEnter(Entity other)
    {
    }

    /// <summary>Called when a trigger and another collider stop overlapping (on both entities).</summary>
    protected virtual void OnTriggerExit(Entity other)
    {
    }

    /// <summary>Called when this entity's <see cref="Button"/> is clicked (or its shortcut key pressed).</summary>
    protected virtual void OnClick()
    {
    }

    /// <summary>
    /// Called when this entity's <see cref="Health"/> takes damage, with how much and what dealt it (null if a script
    /// didn't say).
    /// </summary>
    protected virtual void OnDamage(float amount, Entity? source)
    {
    }

    /// <summary>Called once when this entity's <see cref="Health"/> reaches zero, before it's destroyed.</summary>
    protected virtual void OnDeath()
    {
    }

    /// <summary>
    /// Called when this entity collects a <see cref="Pickup"/>, e.g. to heal on a heart: <c>if (pickup.Name == "Heart")</c>.
    /// The pickup disappears at the end of the frame.
    /// </summary>
    protected virtual void OnPickup(Entity pickup)
    {
    }

    /// <summary>Called when the entity is destroyed or the game stops.</summary>
    protected virtual void OnDestroy()
    {
    }

    protected T? GetComponent<T>() where T : Component => Entity!.GetComponent<T>();

    /// <summary>Calls <paramref name="action"/> once after <paramref name="seconds"/>, unless this entity is destroyed first.</summary>
    protected GameTimer After(float seconds, Action action) => Game.After(seconds, action, Entity);

    /// <summary>Calls <paramref name="action"/> every <paramref name="seconds"/> while this entity exists.</summary>
    protected GameTimer Every(float seconds, Action action) => Game.Every(seconds, action, Entity);

    /// <summary>
    /// Runs <paramref name="routine"/> over several frames, e.g. <c>StartCoroutine(Waves())</c>; see <see cref="Coroutine"/>.
    /// It stops if this entity is destroyed, and when the script is reloaded (Start then runs again on the new version).
    /// </summary>
    protected Coroutine StartCoroutine(System.Collections.IEnumerator routine) => Game.StartCoroutine(routine, this);

    /// <summary>Stops every coroutine this behavior started.</summary>
    protected void StopCoroutines() => Game.StopCoroutines(this);

    /// <summary>
    /// Calls <paramref name="action"/> whenever someone sends <paramref name="message"/> with <c>Game.Send</c>, until this
    /// entity is destroyed or the script is reloaded (Start then runs again on the new version, so listen in Start).
    /// </summary>
    /// <example><code>protected override void Start() => On("PlayerDied", () => Game.ReloadScene(fade: 0.5f));</code></example>
    protected MessageListener On(string message, Action action) => Game.On(message, _ => action(), this);

    /// <summary>
    /// Like <see cref="On(string, Action)"/>, with the data sent along, e.g. <c>On&lt;int&gt;("ScoreChanged", score =&gt; ...)</c>.
    /// </summary>
    protected MessageListener On<T>(string message, Action<T> action) => Game.On(message, data => action(data switch
    {
        T value => value,
        null when default(T) is null => default!,
        _ => throw new InvalidOperationException(
            $"\"{message}\" was sent with {(data is null ? "no data" : $"a {data.GetType().Name}")}, but {GetType().Name} listens for a {typeof(T).Name}."),
    }), this);

    /// <summary>Stops every message listener this behavior started.</summary>
    protected void StopListening() => Game.StopListening(this);

    /// <summary>
    /// Runs <paramref name="system"/> every frame until this entity is destroyed or left behind by a scene change, or
    /// the script is reloaded; see <see cref="GameSystem"/>. Add it in <see cref="Start"/>. A system of the same type is
    /// replaced, so a scene that adds one each time it starts still runs just one.
    /// </summary>
    /// <example><code>protected override void Start() => AddSystem(new Magnets());</code></example>
    protected T AddSystem<T>(T system) where T : GameSystem => Game.AddSystem(system, this);

    /// <summary>Yield this from a coroutine to wait <paramref name="seconds"/> of game time.</summary>
    protected static WaitSeconds Wait(float seconds) => new(seconds);

    /// <summary>Yield this from a coroutine to wait until <paramref name="condition"/> is true (checked every frame).</summary>
    protected static WaitCondition WaitUntil(Func<bool> condition) => new(condition);

    /// <summary>Yield this from a coroutine to wait as long as <paramref name="condition"/> is true.</summary>
    protected static WaitCondition WaitWhile(Func<bool> condition) => new(() => !condition());

    /// <summary>Animates a value over <paramref name="seconds"/>; see <see cref="Runtime.Game.Tween"/>. Stops if this entity is destroyed.</summary>
    protected Tween Tween(float seconds, Action<float> apply, Func<float, float>? ease = null) =>
        Game.Tween(seconds, apply, ease, Entity);

    /// <summary>Glides this entity to <paramref name="position"/> (in its parent's space).</summary>
    protected Tween MoveTo(System.Numerics.Vector2 position, float seconds, Func<float, float>? ease = null) =>
        Game.MoveTo(Entity!, position, seconds, ease);

    /// <summary>Grows or shrinks this entity to <paramref name="scale"/> on X and Y.</summary>
    protected Tween ScaleTo(System.Numerics.Vector2 scale, float seconds, Func<float, float>? ease = null) =>
        Game.ScaleTo(Entity!, scale, seconds, ease);

    /// <summary>Turns this entity to <paramref name="degrees"/> around Z.</summary>
    protected Tween RotateTo(float degrees, float seconds, Func<float, float>? ease = null) =>
        Game.RotateTo(Entity!, degrees, seconds, ease);

    /// <summary>Paints this entity's sprites (and its children's) <paramref name="color"/>, white by default, for a moment.</summary>
    protected void Flash(Color? color = null, float seconds = 0.1f) => Game.Flash(Entity!, color, seconds);

    /// <summary>Fades this entity's sprite, text and tilemap to <paramref name="alpha"/>.</summary>
    protected Tween FadeTo(float alpha, float seconds, Func<float, float>? ease = null) =>
        Game.FadeTo(Entity!, alpha, seconds, ease);

    internal void RunStart(Game game)
    {
        _game = game;
        IsStarted = true;
        Start();
    }

    internal void RunUpdate() => Update();

    internal void RunLateUpdate() => LateUpdate();

    internal void RunCollision(Entity other) => OnCollision(other);

    internal void RunTrigger(Entity other, bool entered)
    {
        if (entered)
            OnTriggerEnter(other);
        else
            OnTriggerExit(other);
    }

    internal void RunClick() => OnClick();

    void IHealthListener.Damaged(float amount, Entity? source)
    {
        if (IsStarted)
            OnDamage(amount, source);
    }

    void IHealthListener.Died()
    {
        if (IsStarted)
            OnDeath();
    }

    internal void RunPickup(Entity pickup) => OnPickup(pickup);

    internal void RunDestroy()
    {
        if (IsStarted)
            OnDestroy();
    }
}
