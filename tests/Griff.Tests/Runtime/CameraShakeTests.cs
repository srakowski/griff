using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class CameraShakeTests
{
    private readonly Scene _scene = new();
    private readonly Game _game;
    private readonly Camera _camera;

    public CameraShakeTests()
    {
        var entity = new Entity("Main Camera");
        _camera = entity.AddComponent<Camera>();
        _scene.Add(entity);
        _game = new Game(_scene, new InputState());
    }

    // Frames are clamped to Time.MaxDelta, so advance in small steps.
    private void Run(double seconds)
    {
        for (var t = 0.0; t < seconds - 1e-9; t += 0.05)
            _game.Tick(0.05);
    }

    [Fact]
    public void Shake_OffsetsTheView_ThenSettles()
    {
        _game.ShakeCamera(0.5f, 0.2f);

        var maxOffset = 0f;
        for (var i = 0; i < 5; i++)
        {
            _game.Tick(0.02);
            maxOffset = Math.Max(maxOffset, _camera.ShakeOffset.Length());
            Assert.True(_camera.ShakeOffset.Length() <= 0.5f);
        }

        Assert.True(maxOffset > 0);

        for (var i = 0; i < 10; i++)
            _game.Tick(0.02);
        Assert.Equal(Vector2.Zero, _camera.ShakeOffset);
    }

    [Fact]
    public void Shake_KeepsGoingWhileTimeIsFrozen()
    {
        _game.Time.Scale = 0;
        _camera.Shake(1, 1);

        Run(0.5);

        Assert.True(_camera.ShakeOffset.Length() <= 0.5f + 1e-4f);
        Run(0.6);
        Assert.Equal(Vector2.Zero, _camera.ShakeOffset);
    }

    [Fact]
    public void WeakerShake_DoesNotCutAStrongOneShort()
    {
        _camera.Shake(1, 1);
        _camera.Shake(0.1f, 5);

        Run(1.05);

        // Still the strong 1 s shake, which has now ended; the weak 5 s one was ignored.
        Assert.Equal(Vector2.Zero, _camera.ShakeOffset);
    }

    [Fact]
    public void Renderer_AppliesTheShake()
    {
        var still = SceneRenderer.Build(_scene, 1).ViewProjection;
        _camera.Shake(1, 1);
        while (_camera.ShakeOffset == Vector2.Zero)
            _game.Tick(0.01);

        var shaken = SceneRenderer.Build(_scene, 1).ViewProjection;

        Assert.NotEqual(still, shaken);
    }
}
