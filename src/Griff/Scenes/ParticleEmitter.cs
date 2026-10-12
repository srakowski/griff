using System.Numerics;

namespace Griff.Scenes;

/// <summary>One live particle, in world space.</summary>
public readonly record struct Particle(Vector2 Position, Vector2 Velocity, float Age, float Lifetime)
{
    /// <summary>0 when born, 1 when it disappears.</summary>
    public float Progress => Lifetime > 0 ? Math.Clamp(Age / Lifetime, 0, 1) : 1;
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
public sealed class ParticleEmitter : Component
{
    private readonly List<Particle> _particles = [];
    private float _pending;
    private bool _started;

    /// <summary>Image for each particle, relative to Assets. Without one, particles are solid squares.</summary>
    public string? Image { get; set; }

    /// <summary>While set, emits <see cref="Rate"/> particles per second.</summary>
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
            _particles[i] = p with { Age = age, Velocity = velocity, Position = p.Position + velocity * seconds };
        }

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
        var angle = Direction * (MathF.PI / 180) + spin + (Random.Shared.NextSingle() - 0.5f) * Spread * (MathF.PI / 180);
        var speed = Speed * (1 + (Random.Shared.NextSingle() * 2 - 1) * Math.Clamp(SpeedVariation, 0, 1));
        var velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
        _particles.Add(new Particle(new Vector2(world.M41, world.M42), velocity, 0, Lifetime));
    }
}
