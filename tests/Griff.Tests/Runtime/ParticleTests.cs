using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

public class ParticleTests
{
    private const double Step = 0.05;
    private readonly Scene _scene = new();
    private readonly Game _game;

    public ParticleTests()
    {
        _game = new Game(_scene, new InputState());
    }

    private ParticleEmitter AddEmitter(Action<ParticleEmitter>? configure = null, Vector2 position = default)
    {
        var entity = new Entity("Sparks");
        entity.Transform.Position = new Vector3(position, 0);
        var emitter = entity.AddComponent<ParticleEmitter>();
        configure?.Invoke(emitter);
        _scene.Add(entity);
        return emitter;
    }

    private void Run(int frames)
    {
        for (var i = 0; i < frames; i++)
            _game.Tick(Step);
    }

    [Fact]
    public void Emits_AtTheRate_AndParticlesDieAfterTheirLifetime()
    {
        var emitter = AddEmitter(e =>
        {
            e.Rate = 20;
            e.Lifetime = 0.5f;
        });

        Run(4); // 0.2 s at 20/s
        Assert.Equal(4, emitter.Particles.Count);

        emitter.Emitting = false;
        Run(11);
        Assert.Empty(emitter.Particles);
    }

    [Fact]
    public void Particles_StartAtTheEmitter_AndMoveInTheDirection()
    {
        var emitter = AddEmitter(e =>
        {
            e.Emitting = false;
            e.Direction = 0;
            e.Spread = 0;
            e.Speed = 4;
            e.SpeedVariation = 0;
        }, new Vector2(2, 3));

        Run(1);
        emitter.Burst(1);
        Run(5); // 0.25 s

        var particle = Assert.Single(emitter.Particles);
        Assert.Equal(3f, particle.Position.X, 3);
        Assert.Equal(3f, particle.Position.Y, 3);
    }

    [Fact]
    public void Gravity_PullsParticlesDown()
    {
        var emitter = AddEmitter(e =>
        {
            e.Emitting = false;
            e.Speed = 0;
            e.GravityScale = 1;
            e.BurstOnStart = 1;
        });

        Run(10);

        Assert.True(emitter.Particles[0].Velocity.Y < -10);
    }

    [Fact]
    public void BurstOnStart_WithDestroyWhenFinished_CleansUpTheEntity()
    {
        var emitter = AddEmitter(e =>
        {
            e.Emitting = false;
            e.BurstOnStart = 12;
            e.Lifetime = 0.3f;
            e.DestroyWhenFinished = true;
        });

        Run(1);
        Assert.Equal(12, emitter.Particles.Count);
        Assert.Same(_scene, emitter.Entity!.Scene);

        Run(10);
        Assert.Null(emitter.Entity!.Scene);
    }

    [Fact]
    public void MaxParticles_DropsTheOldest()
    {
        var emitter = AddEmitter(e =>
        {
            e.Emitting = false;
            e.MaxParticles = 5;
        });
        Run(1);

        emitter.Burst(8);

        Assert.Equal(5, emitter.Particles.Count);
    }

    [Fact]
    public void SizeAndColor_FollowTheParticlesLife()
    {
        var emitter = new ParticleEmitter
        {
            StartSize = 1,
            EndSize = 0,
            StartColor = Color.White,
            EndColor = Color.Black,
        };

        Assert.Equal(0.25f, emitter.SizeAt(0.75f));
        Assert.Equal(new Color(0.5f, 0.5f, 0.5f), emitter.ColorAt(0.5f));
    }

    [Fact]
    public void Renderer_DrawsLiveParticles()
    {
        var emitter = AddEmitter(e =>
        {
            e.Emitting = false;
            e.BurstOnStart = 3;
            e.Image = "spark.png";
            e.StartSize = 0.5f;
        });
        Run(1);

        var draws = SceneRenderer.Build(_scene, 1).Sprites;

        Assert.Equal(3, draws.Count);
        Assert.All(draws, d => Assert.Equal("spark.png", d.Image));
        Assert.All(draws, d => Assert.Same(emitter.Entity, d.Entity));
    }

    [Fact]
    public void Settings_RoundTripThroughSceneFiles()
    {
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        AddEmitter(e =>
        {
            e.Rate = 7;
            e.EndColor = Color.FromBytes(0xFF, 0x80, 0x00, 0x00);
            e.DestroyWhenFinished = true;
        });

        var copy = serializer.LoadFromString(serializer.SaveToString(_scene)).Roots[0].GetComponent<ParticleEmitter>()!;

        Assert.Equal(7, copy.Rate);
        Assert.Equal(Color.FromBytes(0xFF, 0x80, 0x00, 0x00), copy.EndColor);
        Assert.True(copy.DestroyWhenFinished);
    }
}
