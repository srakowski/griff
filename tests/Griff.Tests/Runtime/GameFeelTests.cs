using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Coyote time, slow motion and screen flashes.</summary>
public class GameFeelTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;

    public GameFeelTests()
    {
        _game = new Game(_scene, new InputState());
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Step)
            _game.Tick(Step);
    }

    // A body standing at the right-hand end of a ledge that ends at x = 0.
    private Body AddRunnerOnLedge()
    {
        var ledge = new Entity("Ledge");
        ledge.Transform.Position = new Vector3(-5, -1, 0);
        ledge.AddComponent<BoxCollider>().Size = new Vector2(10, 1);
        _scene.Add(ledge);

        var runner = new Entity("Runner");
        runner.Transform.Position = new Vector3(-1, 0, 0);
        runner.AddComponent<BoxCollider>().Size = new Vector2(0.8f, 1);
        var body = runner.AddComponent<Body>();
        _scene.Add(runner);
        Run(0.2f);
        Assert.True(body.IsGrounded);
        return body;
    }

    [Fact]
    public void WasGrounded_StaysTrueBriefly_AfterWalkingOffAnEdge()
    {
        var body = AddRunnerOnLedge();
        Assert.Equal(0, body.AirTime);

        body.Velocity = new Vector2(6, 0);
        while (body.IsGrounded)
        {
            body.Velocity = body.Velocity with { X = 6 };
            _game.Tick(Step);
        }

        Assert.True(body.WasGrounded(0.1f), "just off the edge, a jump should still count");
        Run(0.15f);
        Assert.False(body.WasGrounded(0.1f));
        Assert.True(body.AirTime > 0.1f);
    }

    [Fact]
    public void WasGrounded_IsFalseOnceTheBodyHasJumped()
    {
        var body = AddRunnerOnLedge();

        body.Velocity = new Vector2(0, 10);
        _game.Tick(Step);

        Assert.False(body.IsGrounded);
        Assert.True(body.AirTime < 0.1f);
        Assert.False(body.WasGrounded(0.1f), "coyote time mustn't allow a second jump");
    }

    [Fact]
    public void WasGrounded_IsFalseForABodyThatNeverLanded()
    {
        var entity = new Entity("Falling");
        entity.AddComponent<BoxCollider>();
        var body = entity.AddComponent<Body>();
        _scene.Add(entity);

        Run(0.05f);

        Assert.False(body.WasGrounded(1));
    }

    [Fact]
    public void SlowMotion_SlowsGameTime_ThenEasesBackToFullSpeed()
    {
        _game.SlowMotion(0.25f, 1);
        _game.Tick(Step);
        Assert.Equal(Step * 0.25f, _game.Time.Delta, 5);

        // Into the last quarter: speeding back up.
        Run(0.85f);
        Assert.InRange(_game.Time.SlowMotionScale, 0.3f, 0.99f);

        Run(0.2f);
        Assert.Equal(1, _game.Time.SlowMotionScale);
        Assert.Equal(Step, _game.Time.Delta, 5);
    }

    [Fact]
    public void SlowMotion_WorksOnTopOfTimeScale_AndAWeakerOneDoesntInterrupt()
    {
        _game.Time.Scale = 0.5f;
        _game.SlowMotion(0.2f, 1);
        _game.SlowMotion(0.8f, 3);
        _game.Tick(Step);

        Assert.Equal(Step * 0.5f * 0.2f, _game.Time.Delta, 6);
    }

    [Fact]
    public void SlowMotion_DuringAHitStop_StillFreezes()
    {
        _game.HitStop(0.1f);
        _game.SlowMotion(0.5f, 1);
        _game.Tick(Step);

        Assert.Equal(0, _game.Time.Delta);
    }

    [Fact]
    public void FlashScreen_CoversTheView_AndFadesOnRealTime()
    {
        _game.Time.Scale = 0;
        _game.FlashScreen(Color.Red, 0.2f);
        Assert.Equal(Color.Red, _game.ScreenFlash);

        var frame = SceneRenderer.Build(_game, 1);
        var cover = Assert.Single(frame.Sprites);
        Assert.Equal(Color.Red, cover.Tint);

        Run(0.1f);
        Assert.InRange(_game.ScreenFlash.A, 0.4f, 0.6f);
        Run(0.11f);
        Assert.Equal(Color.Transparent, _game.ScreenFlash);
        Assert.Empty(SceneRenderer.Build(_game, 1).Sprites);
    }

    [Fact]
    public void FlashScreen_DrawsUnderASceneFade()
    {
        _game.SceneLoader = _ => new Scene();
        _game.LoadScene("Next", fade: 1);
        _game.Tick(0.05);
        _game.FlashScreen(Color.White with { A = 0.5f });

        var draws = SceneRenderer.Build(_game, 1).Sprites;

        Assert.Equal(2, draws.Count);
        Assert.Equal(Color.White with { A = 0.5f }, draws[0].Tint);
        Assert.Equal(0, draws[1].Tint.R);
    }
}
