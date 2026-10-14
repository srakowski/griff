using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// How far the frame being drawn is between the game's last two steps, for drawing what changes once a step from a
/// component's own state rather than its transform (a particle's place, a fade, a gauge's needle), so it moves smoothly
/// on a screen faster than the game steps. Ask a <see cref="Drawing"/> for its <see cref="Drawing.Blend"/> (a system,
/// <see cref="Runtime.FrameDrawing.Blend"/>), and draw what something was as the last step began and what it is now
/// <see cref="Between(float, float)"/>; a value kept in a <see cref="Smoothed{T}"/> remembers what it was by itself
/// (see <see cref="Of(in Smoothed{float})"/>).
/// </summary>
/// <remarks>
/// It follows the rules transforms are drawn by. Everything is drawn as it is (an <see cref="Amount"/> of 1) in the
/// editor, while the game is paused, rewound or stepped by hand, on a scene's first step, and for an entity that jumped
/// in the last step (added, moved to another parent, teleported, or inside one that did). A point that moved further than
/// a quarter of the view in one step, or a turn of more than 90 degrees, is drawn where it ends up.
/// </remarks>
/// <example>
/// <code>
/// // A gauge's needle that swings smoothly, whatever the screen's frame rate.
/// public class Needle : Behavior, IDrawable
/// {
///     private Smoothed&lt;float&gt; _angle;
///
///     protected override void Update() => _angle.Set(this, _angle.Value + 90 * Time.Delta);
///
///     public void Draw(Drawing drawing)
///     {
///         var angle = drawing.Blend.TurnOf(_angle) * MathF.PI / 180;
///         var at = drawing.Entity.Transform.WorldPosition;
///         var middle = new Vector2(at.X, at.Y);
///         drawing.Line(middle, middle + new Vector2(MathF.Cos(angle), MathF.Sin(angle)), drawing.LineWidth * 2, Color.Red);
///     }
/// }
/// </code>
/// </example>
public readonly struct StepBlend
{
    // A turn bigger than this in one step is drawn where it ends up rather than swung through.
    internal const float MaxGlideDegrees = 90;

    // How far back from now towards the last step but one: 0 draws things as they are, so the default blend does.
    private readonly float _behind;
    private readonly float _glideLimit;

    internal StepBlend(float amount, long step, float glideLimit)
    {
        _behind = 1 - Math.Clamp(amount, 0, 1);
        Step = step;
        _glideLimit = glideLimit;
    }

    /// <summary>Things drawn as they are now: in the editor, or a game held still.</summary>
    public static StepBlend AsIs => default;

    /// <summary>
    /// How far the frame is between the last step but one (0) and the last step (1). 1 when things are drawn as they are.
    /// </summary>
    public float Amount => 1 - _behind;

    /// <summary>True when this frame is drawn between two steps, false when things are drawn as they are.</summary>
    public bool IsBetween => _behind > 0;

    /// <summary>
    /// The game's last step (its <see cref="Runtime.Time.Frame"/> as it ended), which this frame is drawn after: state a
    /// component keeps for itself counts as changed in it, as <see cref="For"/> says.
    /// </summary>
    public long Step { get; }

    /// <summary>
    /// This blend for state last stepped in <paramref name="step"/> (a component that keeps what it was itself, the way
    /// particles do, notes the step it moved them in): as it is, unless that was the last step.
    /// </summary>
    public StepBlend For(long step) => step == Step ? this : AsIs;

    /// <summary>A number between what it was as the last step began and what it is now.</summary>
    public float Between(float was, float now) => _behind > 0 ? now + (was - now) * _behind : now;

    /// <summary>A point between where it was as the last step began and where it is, or where it is if that's a jump (see the remarks).</summary>
    public Vector2 Between(Vector2 was, Vector2 now) =>
        _behind > 0 && Vector2.DistanceSquared(was, now) <= _glideLimit * _glideLimit ? Vector2.Lerp(now, was, _behind) : now;

    /// <summary>A point between where it was as the last step began and where it is, or where it is if that's a jump (see the remarks).</summary>
    public Vector3 Between(Vector3 was, Vector3 now) =>
        _behind > 0 && Vector3.DistanceSquared(was, now) <= _glideLimit * _glideLimit ? Vector3.Lerp(now, was, _behind) : now;

    /// <summary>A color between what it was as the last step began and what it is now.</summary>
    public Color Between(Color was, Color now) => _behind > 0 ? Color.Lerp(now, was, _behind) : now;

    /// <summary>
    /// An angle in degrees between what it was as the last step began and what it is now, turned the short way round (so
    /// one kept between -180 and 180 doesn't spin back as it crosses over); a turn of more than 90 degrees is drawn where it
    /// ends up.
    /// </summary>
    public float Turn(float wasDegrees, float nowDegrees) => _behind > 0 ? TurnBetween(wasDegrees, nowDegrees, Amount) : nowDegrees;

    /// <summary>The number <paramref name="value"/> holds, drawn between what it was and is if it changed in the last step.</summary>
    public float Of(in Smoothed<float> value) => value.ChangedIn == Step ? Between(value.Was, value.Value) : value.Value;

    /// <summary>The point <paramref name="value"/> holds, drawn between where it was and is if it moved in the last step.</summary>
    public Vector2 Of(in Smoothed<Vector2> value) => value.ChangedIn == Step ? Between(value.Was, value.Value) : value.Value;

    /// <summary>The point <paramref name="value"/> holds, drawn between where it was and is if it moved in the last step.</summary>
    public Vector3 Of(in Smoothed<Vector3> value) => value.ChangedIn == Step ? Between(value.Was, value.Value) : value.Value;

    /// <summary>The color <paramref name="value"/> holds, drawn between what it was and is if it changed in the last step.</summary>
    public Color Of(in Smoothed<Color> value) => value.ChangedIn == Step ? Between(value.Was, value.Value) : value.Value;

    /// <summary>The angle in degrees <paramref name="value"/> holds, turned as <see cref="Turn"/> does if it changed in the last step.</summary>
    public float TurnOf(in Smoothed<float> value) => value.ChangedIn == Step ? Turn(value.Was, value.Value) : value.Value;

    // The blend for what `entity` draws in `scene`: as it is unless a frame of the game is being drawn between steps, and
    // for an entity that jumped (or is inside one that did) in the last step.
    internal static StepBlend For(Scene? scene, Entity? entity)
    {
        if (scene is not { DrawStamp: not 0 } drawn)
            return AsIs;
        var step = drawn.Step;
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.Transform.JumpedIn(step))
                return AsIs;
        }

        return new StepBlend(drawn.DrawBlend, step, drawn.GlideLimit);
    }

    // `t` of the way from one angle to another the short way round, or where it ends up for a turn over the limit.
    internal static float TurnBetween(float from, float to, float t)
    {
        var delta = ((to - from) % 360 + 540) % 360 - 180;
        return MathF.Abs(delta) > MaxGlideDegrees ? to : from + delta * t;
    }
}

/// <summary>
/// A value a component (or a game's system) draws that changes as the game steps, which remembers what it was as the step
/// it last changed in began, so a frame drawn between two steps can show it between the two: draw it with
/// <see cref="StepBlend.Of(in Smoothed{float})"/> (or <see cref="StepBlend.TurnOf"/> for an angle). Keep it in a field (it's
/// a struct, so not a readonly one or an auto-property) and change it with <see cref="Set(Component, T)"/>.
/// </summary>
/// <typeparam name="T">float, <see cref="Vector2"/>, <see cref="Vector3"/> or <see cref="Color"/>.</typeparam>
public struct Smoothed<T> where T : struct
{
    private T _value;
    private T _was;
    private long _step;

    /// <summary>Starts out at <paramref name="value"/>.</summary>
    public Smoothed(T value) => _value = _was = value;

    /// <summary>What it is now.</summary>
    public readonly T Value => _value;

    /// <summary>What it was as the step it last changed in began.</summary>
    public readonly T Was => _was;

    /// <summary>The step it last changed in (see <see cref="StepBlend.Step"/>), or 0 if it hasn't changed while the game ran.</summary>
    public readonly long ChangedIn => _step;

    /// <summary>Changes it, for <paramref name="owner"/>'s entity: the first change in a step remembers what it was as the step began.</summary>
    public void Set(Component owner, T value) => Set(owner.Entity?.Scene, value);

    /// <summary>Changes it, in <paramref name="scene"/> (for a game's system): the first change in a step remembers what it was as the step began.</summary>
    public void Set(Scene? scene, T value)
    {
        if (EqualityComparer<T>.Default.Equals(_value, value))
            return;
        if (scene is { Step: > 0 and var step } && step != _step)
            (_was, _step) = (_value, step);
        _value = value;
    }

    /// <summary>Its <see cref="Value"/>.</summary>
    public static implicit operator T(Smoothed<T> smoothed) => smoothed._value;
}
