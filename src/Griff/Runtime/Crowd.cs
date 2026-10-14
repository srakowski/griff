namespace Griff.Runtime;

/// <summary>
/// Marks a script that changes nothing but its own entity in its Update, FixedUpdate and LateUpdate: its own values, its
/// entity's transform and components. The game then runs the scripts of a crowd of them (thousands of bunnies, boids,
/// bullets) together, in one pass spread across the computer's cores (see <see cref="Workers"/>), rather than one call
/// after another on the game's thread. What comes out is the same on any number of cores, one included.
/// </summary>
/// <remarks>
/// <para>
/// A crowd script reads its own entity, the game's time and controls, and anything else nothing changes during the pass
/// (not another of the crowd: it may be moving at that very moment). <see cref="Rand"/> gives each script of the crowd
/// numbers of its own, the same however the crowd is split. What would change something else (spawning, destroying,
/// messages, timers, coroutines, tweens, sounds, flashes, debug drawing, saving, adding or removing components, changing
/// scenes) throws instead: do that from a script that isn't a crowd one, or in Start, which runs as any script's does.
/// </para>
/// <para>
/// Scripts run in hierarchy order as ever, a crowd's all at once at the place of its first: what comes before it in the
/// hierarchy has run, and what comes after hasn't. A crowd is a run of scripts of the same type next to each other in
/// that order; one inside another entity runs on the game's thread after the rest of its crowd.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Crowd]
/// public class Drifter : Behavior
/// {
///     public Vector2 Velocity { get; set; } = new(1, 0);
///
///     protected override void Update() => Transform.Position += new Vector3(Velocity * Time.Delta, 0);
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class CrowdAttribute : Attribute;
