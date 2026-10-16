using System.Runtime.CompilerServices;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Base class for gameplay scripts. Override <see cref="Start"/> and <see cref="Update"/> (every step of the game), and
/// <see cref="FixedUpdate"/> to push bodies just before they move; public read/write properties are saved with the scene
/// and editable in the inspector.
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
    [NotState("the game it runs in, set as it starts (or is put back by a rewind)")]
    private Game? _game;

    /// <summary>The running game. Only available from <see cref="Start"/> onwards.</summary>
    protected Game Game => _game ?? throw new InvalidOperationException("The behavior isn't running in a game yet.");

    protected Transform Transform => Entity!.Transform;

    protected Time Time => Game.Time;

    /// <summary>
    /// The game's tables (<see cref="Runtime.Game.Tables"/>): <c>float damage = Tables.Get("Spells", "Fireball")["Damage"];</c>,
    /// or a class of your own filled from a row, <c>Tables.Get&lt;Spell&gt;("Spells", "Fireball")</c>.
    /// </summary>
    protected Griff.Data.TableSet Tables => Game.Tables;

    /// <summary>
    /// The controls of whoever plays this entity: this machine's, or in a network game, those of the player it belongs
    /// to (see <see cref="Runtime.Game.InputOf"/>).
    /// </summary>
    protected IInput Input => Game.InputOf(Entity);

    protected IAudio Audio
    {
        get
        {
            Crowds.ThrowIfRunning("play sounds");
            return Game.Audio;
        }
    }

    /// <summary>Draws lines, boxes, circles and text over the game for a frame; see <see cref="DebugDraw"/>.</summary>
    protected DebugDraw Debug
    {
        get
        {
            Crowds.ThrowIfRunning("draw debug shapes");
            return Game.Debug;
        }
    }

    [field: NotState("a rewind snapshot says which scripts are still to start (see IsWaitingToStart)")]
    internal bool IsStarted { get; private set; }

    internal override bool IsWaitingToStart => !IsStarted;

    /// <summary>
    /// Which of Update, FixedUpdate and LateUpdate its type has, worked out once a type: the game calls only those. And
    /// whether it's a crowd script (see <see cref="CrowdAttribute"/>).
    /// </summary>
    [field: NotState("worked out from its type")]
    internal BehaviorHooks Hooks { get; }

    protected Behavior() => Hooks = HooksOf(GetType());

    // Held weakly, so a script reloaded in the editor doesn't keep its last version's types alive.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Type, StrongBox<BehaviorHooks>> s_hooks = new();

    private static BehaviorHooks HooksOf(Type type) => s_hooks.GetValue(type, static type => new StrongBox<BehaviorHooks>(
        (Overrides(type, nameof(Update)) ? BehaviorHooks.Update : 0) |
        (Overrides(type, nameof(FixedUpdate)) ? BehaviorHooks.FixedUpdate : 0) |
        (Overrides(type, nameof(LateUpdate)) ? BehaviorHooks.LateUpdate : 0) |
        (type.IsDefined(typeof(CrowdAttribute), inherit: true) ? BehaviorHooks.Crowd : 0))).Value;

    // Whether a type between `type` and Behavior overrides Behavior's method `name`.
    private static bool Overrides(Type type, string name)
    {
        const System.Reflection.BindingFlags declared = System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Instance |
                                                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        for (var t = type; t is not null && t != typeof(Behavior); t = t.BaseType)
        {
            if (t.GetMethod(name, declared, Type.EmptyTypes) is { } method && method.GetBaseDefinition().DeclaringType == typeof(Behavior))
                return true;
        }

        return false;
    }

    /// <summary>Called once, before the first <see cref="Update"/>, after every behavior in the scene exists.</summary>
    protected virtual void Start()
    {
    }

    /// <summary>
    /// Called every step of the game while the behavior and its entity are enabled: 60 times a second (see
    /// <see cref="Runtime.Time.StepRate"/>) whatever the screen's frame rate, with <see cref="Runtime.Time.Delta"/> the
    /// step's game time, so the same input plays the same everywhere. The screen is drawn between steps, so what moves
    /// here moves smoothly. Read the controls here: a press counts in one step. While a panel pauses the game, only the
    /// scripts on open panels update.
    /// </summary>
    protected virtual void Update()
    {
    }

    /// <summary>
    /// Called every step just before bodies move (after <see cref="Update"/> and everything else that runs before physics),
    /// and only while game time moves: not while the game is paused, at a <see cref="Runtime.Time.Scale"/> of 0, or frozen by
    /// a hit stop, when physics doesn't step either. <see cref="Runtime.Time.Delta"/> is the time the bodies are about to
    /// move by. The place to push bodies: set velocities, add forces.
    /// </summary>
    /// <example><code>protected override void FixedUpdate() => GetComponent&lt;Body&gt;()!.Velocity += new Vector2(3, 0) * Time.Delta;</code></example>
    protected virtual void FixedUpdate()
    {
    }

    /// <summary>
    /// Called every step after <see cref="Update"/>, once everything else has moved: physics, pickups and damage, and
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
    protected Coroutine StartCoroutine(System.Collections.IEnumerator routine) => Game.TimerSystem.StartCoroutine(routine, this);

    /// <summary>Stops every coroutine this behavior started.</summary>
    protected void StopCoroutines() => Game.TimerSystem.StopCoroutines(this);

    /// <summary>
    /// Calls <paramref name="action"/> whenever someone sends <paramref name="message"/> with <c>Game.Send</c>, until this
    /// entity is destroyed or the script is reloaded (Start then runs again on the new version, so listen in Start).
    /// </summary>
    /// <example><code>protected override void Start() => On("PlayerDied", () => Game.ReloadScene(fade: 0.5f));</code></example>
    protected MessageListener On(string message, Action action) => Game.MessageSystem.On(message, _ => action(), this);

    /// <summary>
    /// Like <see cref="On(string, Action)"/>, with the data sent along, e.g. <c>On&lt;int&gt;("ScoreChanged", score =&gt; ...)</c>.
    /// </summary>
    protected MessageListener On<T>(string message, Action<T> action) => Game.MessageSystem.On(message, data => action(data switch
    {
        T value => value,
        null when default(T) is null => default!,
        _ => throw new InvalidOperationException(
            $"\"{message}\" was sent with {(data is null ? "no data" : $"a {data.GetType().Name}")}, but {GetType().Name} listens for a {typeof(T).Name}."),
    }), this);

    /// <summary>Stops every message listener this behavior started.</summary>
    protected void StopListening() => Game.MessageSystem.StopListening(this);

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

    // Put back by a rewind as having started (it had, at the snapshot): it goes on without starting again.
    internal void PutBack(Game game)
    {
        _game = game;
        IsStarted = true;
    }

    internal void RunUpdate() => Update();

    internal void RunFixedUpdate() => FixedUpdate();

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

/// <summary>The steps a <see cref="Behavior"/>'s type has code for (see <see cref="Behavior.Hooks"/>).</summary>
[Flags]
internal enum BehaviorHooks : byte
{
    None = 0,
    Update = 1,
    FixedUpdate = 2,
    LateUpdate = 4,

    /// <summary>Not a step: a crowd script (see <see cref="CrowdAttribute"/>).</summary>
    Crowd = 8,
}
