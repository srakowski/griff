using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// Leaves fading copies of its entity's <see cref="Sprite"/> behind as it moves: dashes, speed, ghosts, bullets with
/// streaks. Copies are left every <see cref="Interval"/> seconds while it moves at least <see cref="MinSpeed"/>.
/// </summary>
[Category("Rendering")]
[Requires(typeof(Sprite), Note = "the copies left behind")]
[ComponentName("Griff.Trail")]
public sealed class Trail : Component, IDrawable
{
    // Drawn with its sprite, just behind it.
    int? IDrawable.DrawOrder => null;

    int IDrawable.DrawStep => DrawSteps.Trail;

    // Each copy fades between how faded it was and is, if they aged in the last step. One left in the last step is where
    // the sprite is now, ahead of where it's drawn between steps, so it shows once the sprite is drawn there.
    void IDrawable.Draw(Drawing drawing)
    {
        var blend = drawing.Blend.For(_agedIn);
        foreach (var copy in _copies)
        {
            if (copy.Age <= 0 && blend.IsBetween)
                continue;
            var opacity = OpacityAt(blend.Between(Math.Max(0, copy.Age - _agedBy), copy.Age));
            if (opacity > 0)
                drawing.Add(new SpriteDraw(copy.World, copy.Image, copy.Tint with { A = copy.Tint.A * opacity }, Entity!) { Source = copy.Source, Flash = Color });
        }
    }

    private readonly List<TrailCopy> _copies = [];
    private Vector2? _last;
    private float _sinceCopy;

    // The step the copies last aged in, and by how much, for drawing them between steps.
    private long _agedIn = -1;
    private float _agedBy;

    /// <summary>Seconds between copies.</summary>
    public float Interval { get; set; } = 0.04f;

    /// <summary>Seconds each copy takes to fade away.</summary>
    public float Fade { get; set; } = 0.25f;

    /// <summary>How see-through a fresh copy is, from 0 (invisible) to 1 (as solid as the sprite).</summary>
    public float Opacity { get; set; } = 0.5f;

    /// <summary>A color painted over the copies, e.g. a cyan dash. Transparent keeps the sprite's own look.</summary>
    public Color Color { get; set; } = Color.Transparent;

    /// <summary>Only leave copies while moving at least this fast, in world units per second: just the dash, not the walk.</summary>
    public float MinSpeed { get; set; } = 1;

    /// <summary>Untick it (from a script, say at the end of a dash) to stop leaving copies; those already left still fade.</summary>
    public bool Emitting { get; set; } = true;

    /// <summary>The copies on screen, oldest first.</summary>
    public IReadOnlyList<TrailCopy> Copies => _copies;

    /// <summary>Ages the copies and leaves a new one when it's due. Called by the game.</summary>
    internal void Advance(float seconds)
    {
        if (seconds <= 0)
            return;
        foreach (var i in Enumerable.Range(0, _copies.Count))
            _copies[i] = _copies[i] with { Age = _copies[i].Age + seconds };
        _copies.RemoveAll(c => c.Age >= Fade);
        (_agedIn, _agedBy) = (Entity?.Scene?.Step ?? -1, seconds);

        var position = Entity!.Transform.WorldPosition;
        var here = new Vector2(position.X, position.Y);
        var speed = _last is { } last ? Vector2.Distance(here, last) / seconds : 0;
        _last = here;
        _sinceCopy += seconds;

        if (!Emitting || speed < MinSpeed || _sinceCopy < Math.Max(Interval, 0.005f) || !(Fade > 0) ||
            Entity.GetComponent<Sprite>() is not { } sprite)
            return;
        _sinceCopy = 0;
        foreach (var (world, source) in sprite.Pieces())
            _copies.Add(new TrailCopy(world, source, sprite.Image, sprite.Tint, 0));
    }

    /// <summary>How solid a copy is now: <see cref="Opacity"/> fading to nothing over <see cref="Fade"/>.</summary>
    public float OpacityOf(TrailCopy copy) => OpacityAt(copy.Age);

    private float OpacityAt(float age) => Fade > 0 ? Math.Clamp(Opacity, 0, 1) * Math.Clamp(1 - age / Fade, 0, 1) : 0;
}

/// <summary>One copy left by a <see cref="Trail"/>: where it was drawn, what it showed, and how many seconds ago.</summary>
public readonly record struct TrailCopy(Matrix4x4 World, Vector4 Source, string? Image, Color Tint, float Age);
