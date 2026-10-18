using System.Numerics;

namespace Griff.Scenes;

/// <summary>One live particle, in world space.</summary>
public readonly record struct Particle(Vector2 Position, Vector2 Velocity, float Age, float Lifetime)
{
    /// <summary>0 when born, 1 when it disappears.</summary>
    public float Progress => Lifetime > 0 ? Math.Clamp(Age / Lifetime, 0, 1) : 1;

    /// <summary>Where it was before it last moved (where it was born, until it moves), for drawing it between steps.</summary>
    public Vector2 From { get; init; } = Position;

    /// <summary>Its age before it last moved, for drawing it between steps.</summary>
    public float AgeFrom { get; init; } = Age;

    /// <summary>The frame of the emitter's sprite sheet it was born with, when the emitter picks one at random.</summary>
    public int Frame { get; init; }
}

/// <summary>
/// Sprays small fading quads: smoke, sparks, dust, explosions. Particles live in world space, so a moving emitter
/// leaves a trail. Call <see cref="Burst"/> from a script for one-off effects, or use <see cref="BurstOnStart"/> with
/// <see cref="DestroyWhenFinished"/> in a prefab and spawn it.
/// </summary>
/// <remarks>Live particles aren't saved, so rewinding during play clears them.</remarks>
[Category("Rendering")]
[WorksWith(typeof(Lifetime), Note = "for one-off effects")]
[ComponentName("ParticleEmitter")]
public sealed class ParticleEmitter : Component, IDrawable
{
    int? IDrawable.DrawOrder => Order;

    int IDrawable.DrawStep => DrawSteps.Particles;

    void IDrawable.Draw(Drawing drawing)
    {
        foreach (var draw in ParticleDraws(drawing.Blend))
            drawing.Add(draw);
    }

    /// <summary>One quad per live particle, in world space at the emitter's depth, where they are now.</summary>
    public IEnumerable<SpriteDraw> ParticleDraws() => ParticleDraws(StepBlend.AsIs);

    /// <summary>
    /// One quad per live particle, in world space at the emitter's depth, in a frame drawn as <paramref name="blend"/>
    /// says: each between where it was and is (and how old it was and is), if they moved in the last step.
    /// </summary>
    public IEnumerable<SpriteDraw> ParticleDraws(StepBlend blend)
    {
        blend = blend.For(_movedIn);
        var entity = Entity!;
        var z = entity.Transform.WorldPosition.Z;
        var image = string.IsNullOrEmpty(Image) ? null : Image;
        var sheet = Columns > 1 || Rows > 1;
        foreach (var particle in _particles)
        {
            var progress = particle.Lifetime > 0 ? Math.Clamp(blend.Between(particle.AgeFrom, particle.Age) / particle.Lifetime, 0, 1) : 1;
            var size = SizeAt(progress);
            if (size <= 0)
                continue;
            var at = blend.Between(particle.From, particle.Position);
            var world = Matrix4x4.CreateScale(size, size, 1) * Matrix4x4.CreateTranslation(at.X, at.Y, z);
            yield return sheet
                ? new SpriteDraw(world, image, ColorAt(progress), entity) { Source = FrameRect(FrameOf(particle, progress)) }
                : new SpriteDraw(world, image, ColorAt(progress), entity);
        }
    }

    private readonly List<Particle> _particles = [];
    private float _pending;
    private bool _started;

    // The step the particles last moved in: they're drawn between where they were and are only just after it.
    private long _movedIn = -1;

    /// <summary>Image for each particle, relative to Assets. Without one, particles are solid squares.</summary>
    public string? Image { get; set; }

    /// <summary>For sprite sheets: frames across the image, as on a <see cref="Sprite"/>. 1 for a single image.</summary>
    [Group("Sprite Sheet")]
    public int Columns { get; set; } = 1;

    /// <summary>For sprite sheets: frames down the image.</summary>
    public int Rows { get; set; } = 1;

    /// <summary>Which frame of the sheet each particle shows, counting left to right, top to bottom from 0. <see cref="Frames"/> takes over when it's set.</summary>
    public int Frame { get; set; }

    /// <summary>
    /// Frames to use instead of <see cref="Frame"/>, written like an <see cref="Animation"/> clip's: <c>0-1</c> is frames 0 and 1,
    /// <c>3-4, 7</c> is 3, 4 and 7, and <c>3-0</c> runs backward. Each particle picks one at random as it's born (from the
    /// game's random, so a rewound or networked game repeats), or with <see cref="AnimateFrames"/> steps through them.
    /// </summary>
    [PlainText("frame numbers and ranges, as an Animation clip writes them")]
    public string? Frames
    {
        get => _frames;
        set
        {
            _frames = value;
            _frameList = ParseFrames(value);
        }
    }

    private string? _frames;
    private int[] _frameList = [];

    /// <summary>With <see cref="Frames"/>: each particle shows them in order, one after another across its life, instead of one picked at birth.</summary>
    [ShownWhen(nameof(Frames))]
    public bool AnimateFrames { get; set; }

    /// <summary>The frames <see cref="Frames"/> names, in order; empty when it names none.</summary>
    public IReadOnlyList<int> FrameList => _frameList;

    /// <summary>Reads frames written like <see cref="Frames"/>; entries that don't make sense are skipped.</summary>
    public static int[] ParseFrames(string? text)
    {
        var frames = new List<int>();
        foreach (var entry in (text ?? "").Split([',', ';', ' ', '\t', '\n'],StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Animation.TryParseRange(entry, out var first, out var count, out var backward) || count > 4096)
                continue;
            for (var i = 0; i < count; i++)
                frames.Add(backward ? first + count - 1 - i : first + i);
        }

        return [.. frames];
    }

    /// <summary>The frame a particle shows at <paramref name="progress"/> through its life.</summary>
    public int FrameOf(Particle particle, float progress)
    {
        if (_frameList.Length == 0)
            return Frame;
        return AnimateFrames ? _frameList[Math.Min(_frameList.Length - 1, (int)(progress * _frameList.Length))] : particle.Frame;
    }

    /// <summary>The part of the image to show for <paramref name="frame"/>: (u, v, width, height) in 0–1 image space, as <see cref="Sprite.FrameRect"/>.</summary>
    public Vector4 FrameRect(int frame)
    {
        var columns = Math.Max(1, Columns);
        var rows = Math.Max(1, Rows);
        if (columns == 1 && rows == 1)
            return new Vector4(0, 0, 1, 1);
        frame = ((frame % (columns * rows)) + columns * rows) % (columns * rows);
        return new Vector4((float)(frame % columns) / columns, (float)(frame / columns) / rows, 1f / columns, 1f / rows);
    }

    /// <summary>While set, emits <see cref="Rate"/> particles per second.</summary>
    [Powered]
    public bool Emitting { get; set; } = true;

    /// <summary>Particles per second while <see cref="Emitting"/>.</summary>
    [ShownWhen(nameof(Emitting))]
    public float Rate { get; set; } = 20;

    /// <summary>Particles emitted at once when the game first runs this emitter (e.g. for an explosion prefab).</summary>
    public int BurstOnStart { get; set; }

    /// <summary>Seconds each particle lives.</summary>
    [Group("Motion")]
    public float Lifetime { get; set; } = 1;

    /// <summary>Starting speed in world units per second.</summary>
    public float Speed { get; set; } = 3;

    /// <summary>How much each particle's speed may differ, from 0 (all the same) to 1 (anywhere from 0 to double).</summary>
    public float SpeedVariation { get; set; } = 0.5f;

    /// <summary>Degrees: 0 is right, 90 is up. The entity's rotation is added.</summary>
    public float Direction { get; set; } = 90;

    /// <summary>Width of the spray in degrees around <see cref="Direction"/>; 360 sprays every way.</summary>
    public float Spread { get; set; } = 30;

    /// <summary>Multiplier on world gravity; 0 floats, 1 falls like a body.</summary>
    public float GravityScale { get; set; }

    /// <summary>Width and height in world units at birth.</summary>
    [Group("Look")]
    public float StartSize { get; set; } = 0.3f;

    /// <summary>Width and height in world units at the end of its life.</summary>
    public float EndSize { get; set; }

    /// <summary>Each particle's color when it's made.</summary>
    public Color StartColor { get; set; } = Color.White;

    /// <summary>Each particle's color at the end of its life, blending from <see cref="StartColor"/>; a clear one fades it out.</summary>
    public Color EndColor { get; set; } = Color.White with { A = 0 };

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/>.</summary>
    public int Order { get; set; }

    /// <summary>Removes the entity once it has stopped emitting and its last particle is gone.</summary>
    [Group("Limits")]
    public bool DestroyWhenFinished { get; set; }

    /// <summary>The oldest particles make room for new ones beyond this many.</summary>
    public int MaxParticles { get; set; } = 500;

    /// <summary>The live particles, oldest first.</summary>
    public IReadOnlyList<Particle> Particles => _particles;

    /// <summary>
    /// True once there's nothing left to show: the start burst has happened, the emitter isn't emitting
    /// continuously, and every particle has died.
    /// </summary>
    public bool IsFinished => _started && _particles.Count == 0 && !(Emitting && Rate > 0);

    /// <summary>The size of a particle at <paramref name="progress"/> through its life.</summary>
    public float SizeAt(float progress) => StartSize + (EndSize - StartSize) * progress;

    /// <summary>The color of a particle at <paramref name="progress"/> through its life.</summary>
    public Color ColorAt(float progress) => Color.Lerp(StartColor, EndColor, progress);

    /// <summary>Emits <paramref name="count"/> particles right away.</summary>
    public void Burst(int count)
    {
        for (var i = 0; i < count; i++)
            Emit();
    }

    /// <summary>Removes every live particle.</summary>
    public void Clear() => _particles.Clear();

    /// <summary>Ages and moves particles and emits new ones. Called by the game every frame.</summary>
    public void Advance(float seconds, Vector2 gravity)
    {
        if (!_started)
        {
            _started = true;
            Burst(BurstOnStart);
        }

        var pull = gravity * GravityScale * seconds;
        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            var age = p.Age + seconds;
            if (age >= p.Lifetime)
            {
                _particles.RemoveAt(i);
                continue;
            }

            var velocity = p.Velocity + pull;
            _particles[i] = p with { Age = age, Velocity = velocity, Position = p.Position + velocity * seconds, From = p.Position, AgeFrom = p.Age };
        }

        if (seconds > 0 && Entity?.Scene is { } scene)
            _movedIn = scene.Step;

        if (!Emitting || !(Rate > 0))
        {
            _pending = 0;
            return;
        }

        _pending += Rate * seconds;
        while (_pending >= 1)
        {
            _pending--;
            Emit();
        }
    }

    private void Emit()
    {
        if (Entity is null || !(Lifetime > 0) || MaxParticles <= 0)
            return;
        if (_particles.Count >= MaxParticles)
            _particles.RemoveAt(0);

        var transform = Entity.Transform;
        var world = transform.WorldMatrix;
        var spin = MathF.Atan2(world.M12, world.M11);
        var angle = Direction * (MathF.PI / 180) + spin + (Rand.Value - 0.5f) * Spread * (MathF.PI / 180);
        var speed = Speed * (1 + (Rand.Value * 2 - 1) * Math.Clamp(SpeedVariation, 0, 1));
        var velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
        // Only an emitter with several frames to pick from asks the random, so the others draw what they always did.
        var frame = _frameList.Length > 1 && !AnimateFrames ? _frameList[Rand.Range(0, _frameList.Length)] : _frameList.Length == 1 ? _frameList[0] : 0;
        _particles.Add(new Particle(new Vector2(world.M41, world.M42), velocity, 0, Lifetime) { Frame = frame });
    }
}
