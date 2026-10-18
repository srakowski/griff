using System.Numerics;

namespace Griff.Scenes;

/// <summary>Whether a <see cref="TweenPlayer"/> plays once or over and over.</summary>
public enum TweenLoop
{
    /// <summary>It plays once, each time it's set off.</summary>
    Once,

    /// <summary>It plays again from the start each time it ends: a spin, a pulse.</summary>
    Repeat,

    /// <summary>It plays forward, then back, over and over: a bob, a sway.</summary>
    PingPong,
}

/// <summary>Which way a <see cref="TweenPlayer"/> plays.</summary>
public enum TweenDirection
{
    /// <summary>From how its values say to how the entity is: popping in, landing, a pop that settles.</summary>
    From,

    /// <summary>From how the entity is to how its values say, staying there once it ends: fading out, shrinking away.</summary>
    To,
}

/// <summary>
/// Plays a little animation of how its entity is drawn, with no code: grown or shrunk (<see cref="Scale"/>), moved
/// (<see cref="Move"/>), turned (<see cref="Rotate"/>) and faded (<see cref="Fade"/>) at once, eased, as it appears
/// (<see cref="OnStart"/>), when its <see cref="Message"/> is sent, or from a script with <see cref="Play"/>. A card
/// popping into place, a stamp landing on a page, a score jumping as it changes, a coin bobbing. What's inside the entity
/// moves and fades with it.
/// </summary>
/// <remarks>
/// Only how it's drawn changes: its transform, collider and clickable area stay where they are, so it can't knock
/// anything over or walk off its place. It plays in real time, held while the game is paused unless it's on an open
/// panel, so a pause menu's buttons can pop in. It plays on every player's machine, from what's sent to it.
/// </remarks>
[Category("Rendering")]
[ComponentName("TweenPlayer")]
public sealed class TweenPlayer : Component, IDrawEffect
{
    /// <summary>
    /// How big it's drawn at the far end of the tween, times its own size: 0 grows out of nothing, 2 lands from twice its
    /// size, 1.3 a pop that settles, 1 keeps its size.
    /// </summary>
    public float Scale { get; set; }

    /// <summary>How far off its place it's drawn at the far end, in world units: (0, 1) drops in from a unit above.</summary>
    public Vector2 Move { get; set; }

    /// <summary>How far it's turned at the far end, in degrees (counterclockwise): 360 spins it once round.</summary>
    public float Rotate { get; set; }

    /// <summary>How faded it's drawn at the far end, from 0 (not at all) to 1 (gone): 1 fades it in, or out with To.</summary>
    public float Fade { get; set; }

    /// <summary>
    /// From (the default) starts at the far end and settles to how the entity is; To goes from how it is to the far end
    /// and stays there once it ends (until it's played again or stopped).
    /// </summary>
    public TweenDirection Direction { get; set; }

    /// <summary>Seconds it takes from one end to the other.</summary>
    public float Duration { get; set; } = 0.3f;

    /// <summary>Seconds it waits before it moves, once set off (held at its start meanwhile): cards appearing one after another.</summary>
    public float Delay { get; set; }

    /// <summary>The curve it moves along: Out Back overshoots a little and settles, Out Bounce lands like a dropped ball, Linear keeps one speed.</summary>
    public Easing Ease { get; set; } = Easing.OutBack;

    /// <summary>Once, or over and over: Repeat starts again from the start, Ping Pong goes back and forth.</summary>
    public TweenLoop Loop { get; set; }

    /// <summary>Plays it whenever the entity appears: as the scene starts, as it's spawned, and each time it's shown again.</summary>
    public bool OnStart { get; set; } = true;

    /// <summary>The message that plays it (from the start), e.g. "Scored". Empty plays it only on start, or from a script.</summary>
    [Names(NameKind.Message, Hears = true)]
    public string? Message { get; set; }

    // Seconds since it was set off, its delay too; kept as it was when the step began too, so it's drawn between steps.
    [NotState("only how it's drawn")]
    private Smoothed<float> _age;

    // Whether it's playing, and whether a To tween that ended is held at its far end.
    [NotState("only how it's drawn")]
    private bool _playing;

    [NotState("only how it's drawn")]
    private bool _held;

    // The pass of the game's tween players it was last seen showing in, so one shown again plays on start again.
    [NotState("only how it's drawn")]
    internal long SeenIn = -1;

    /// <summary>True while it's playing (a looping one, until it's stopped).</summary>
    [DoNotSerialize]
    public bool IsPlaying => _playing;

    /// <summary>Plays it from the start, even if it's playing now.</summary>
    [Action]
    public void Play()
    {
        _age = new Smoothed<float>(0);
        _playing = true;
        _held = false;
    }

    /// <summary>Stops it, and it's drawn as the entity is.</summary>
    [Action]
    public void Stop()
    {
        _playing = false;
        _held = false;
    }

    /// <summary>
    /// How far it's drawn towards the far end now, from 0 (as the entity is) to 1 (as its values say); an overshooting
    /// <see cref="Ease"/> takes it a little past either end.
    /// </summary>
    public float Amount => AmountAt(_age.Value);

    // Moves it on by `seconds`, ending it once a tween that doesn't loop has run its time.
    internal void Advance(float seconds)
    {
        if (!_playing)
            return;
        var age = _age.Value + Math.Max(0, seconds);
        _age.Set(this, age);
        if (Loop == TweenLoop.Once && age >= Math.Max(0, Delay) + Math.Max(0, Duration))
            (_playing, _held) = (false, Direction == TweenDirection.To);
    }

    private float AmountAt(float age)
    {
        if (!_playing)
            return _held ? 1 : 0;
        var duration = Math.Max(0, Duration);
        var t = age - Math.Max(0, Delay);
        var progress = t <= 0 ? 0 : duration <= 0 ? 1 : t / duration;
        progress = Loop switch
        {
            TweenLoop.Repeat => progress - MathF.Floor(progress),
            TweenLoop.PingPong => progress % 2 is var swing && swing > 1 ? 2 - swing : swing,
            _ => Math.Min(progress, 1),
        };
        var eased = Griff.Ease.Of(Ease)(progress);
        return Direction == TweenDirection.From ? 1 - eased : eased;
    }

    // Grown, turned and moved about its middle, with what's inside it, drawn between steps.
    Matrix4x4? IDrawEffect.DrawTransform
    {
        get
        {
            if (!_playing && !_held)
                return null;
            var amount = AmountAt(StepBlend.For(Entity!).Of(_age));
            // Never quite nothing, so what divides by its size (a nine-slice's edges) keeps to numbers.
            var scale = 1 + (Scale - 1) * amount;
            if (MathF.Abs(scale) < 0.001f)
                scale = 0.001f;
            var turn = Rotate * amount * MathF.PI / 180;
            var move = Move * amount;
            if (scale == 1 && turn == 0 && move == Vector2.Zero)
                return null;
            var at = Entity!.Transform.WorldPosition;
            var center = new Vector3(at.X, at.Y, 0);
            return Matrix4x4.CreateTranslation(-center) * Matrix4x4.CreateScale(scale, scale, 1) * Matrix4x4.CreateRotationZ(turn) *
                   Matrix4x4.CreateTranslation(center + new Vector3(move, 0));
        }
    }

    float IDrawEffect.DrawOpacity =>
        (!_playing && !_held) || Fade == 0 ? 1 : Math.Clamp(1 - Fade * AmountAt(StepBlend.For(Entity!).Of(_age)), 0, 1);
}
