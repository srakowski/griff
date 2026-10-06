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
public abstract class Behavior : Component
{
    private Game? _game;

    /// <summary>The running game. Only available from <see cref="Start"/> onwards.</summary>
    protected Game Game => _game ?? throw new InvalidOperationException("The behavior isn't running in a game yet.");

    protected Transform Transform => Entity!.Transform;

    protected Time Time => Game.Time;

    protected IInput Input => Game.Input;

    protected IAudio Audio => Game.Audio;

    internal bool IsStarted { get; private set; }

    /// <summary>Called once, before the first <see cref="Update"/>, after every behavior in the scene exists.</summary>
    protected virtual void Start()
    {
    }

    /// <summary>Called every frame while the behavior and its entity are enabled.</summary>
    protected virtual void Update()
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

    /// <summary>Called when the entity is destroyed or the game stops.</summary>
    protected virtual void OnDestroy()
    {
    }

    protected T? GetComponent<T>() where T : Component => Entity!.GetComponent<T>();

    internal void RunStart(Game game)
    {
        _game = game;
        IsStarted = true;
        Start();
    }

    internal void RunUpdate() => Update();

    internal void RunCollision(Entity other) => OnCollision(other);

    internal void RunTrigger(Entity other, bool entered)
    {
        if (entered)
            OnTriggerEnter(other);
        else
            OnTriggerExit(other);
    }

    internal void RunDestroy()
    {
        if (IsStarted)
            OnDestroy();
    }
}
