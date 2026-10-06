using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class TweenTests
{
    private const double Step = 0.1;
    private readonly Scene _scene = new();
    private readonly Game _game;

    public TweenTests()
    {
        _game = new Game(_scene, new InputState());
    }

    private void Run(int frames)
    {
        for (var i = 0; i < frames; i++)
            _game.Tick(Step);
    }

    private Entity Add(string name = "Thing")
    {
        var entity = new Entity(name);
        _scene.Add(entity);
        return entity;
    }

    [Fact]
    public void Tween_ReportsEasedProgress_AndEndsAtExactlyOne()
    {
        var values = new List<float>();
        var tween = _game.Tween(0.4f, values.Add, Ease.Linear);

        Run(6);

        Assert.Equal([0.25f, 0.5f, 0.75f, 1f], values.Skip(0).Select(v => MathF.Round(v, 3)));
        Assert.False(tween.IsActive);
    }

    [Fact]
    public void MoveTo_GlidesToTheTarget_KeepingZ()
    {
        var entity = Add();
        entity.Transform.Position = new Vector3(0, 0, 3);

        _game.MoveTo(entity, new Vector2(10, -4), 1, Ease.Linear);
        Run(5);
        Assert.Equal(5f, entity.Transform.Position.X, 3);

        Run(10);
        Assert.Equal(new Vector3(10, -4, 3), entity.Transform.Position);
    }

    [Fact]
    public void NewMoveTo_TakesOverFromAnUnfinishedOne()
    {
        var entity = Add();
        var first = _game.MoveTo(entity, new Vector2(10, 0), 1, Ease.Linear);
        Run(5);

        _game.MoveTo(entity, new Vector2(5, 5), 1, Ease.Linear);
        Run(20);

        Assert.False(first.IsActive);
        Assert.Equal(new Vector3(5, 5, 0), entity.Transform.Position);
    }

    [Fact]
    public void DifferentKindsOfTween_RunTogether()
    {
        var entity = Add();
        entity.AddComponent<Sprite>();

        _game.MoveTo(entity, new Vector2(1, 1), 0.5f);
        _game.ScaleTo(entity, new Vector2(2, 3), 0.5f);
        _game.RotateTo(entity, 90, 0.5f);
        _game.FadeTo(entity, 0, 0.5f);
        Run(10);

        Assert.Equal(new Vector3(1, 1, 0), entity.Transform.Position);
        Assert.Equal(new Vector3(2, 3, 1), entity.Transform.Scale);
        Assert.Equal(90f, entity.Transform.Rotation.Z);
        Assert.Equal(0f, entity.GetComponent<Sprite>()!.Tint.A);
    }

    [Fact]
    public void FadeTo_FadesTextAndTilemapToo()
    {
        var entity = Add();
        entity.AddComponent<Text>();
        entity.AddComponent<Tilemap>();

        _game.FadeTo(entity, 0.25f, 0.2f);
        Run(5);

        Assert.Equal(0.25f, entity.GetComponent<Text>()!.Color.A);
        Assert.Equal(0.25f, entity.GetComponent<Tilemap>()!.Tint.A);
    }

    [Fact]
    public void Then_RunsWhenFinished_AndCanChain()
    {
        var entity = Add();
        var steps = new List<string>();

        _game.MoveTo(entity, new Vector2(1, 0), 0.2f)
            .Then(() =>
            {
                steps.Add("first");
                _game.MoveTo(entity, new Vector2(1, 1), 0.2f).Then(() => steps.Add("second"));
            });
        Run(10);

        Assert.Equal(["first", "second"], steps);
        Assert.Equal(new Vector3(1, 1, 0), entity.Transform.Position);
    }

    [Fact]
    public void Cancel_StopsWhereItIs_AndSkipsThen()
    {
        var entity = Add();
        var finished = false;
        var tween = _game.MoveTo(entity, new Vector2(10, 0), 1, Ease.Linear).Then(() => finished = true);
        Run(3);

        tween.Cancel();
        Run(20);

        Assert.Equal(3f, entity.Transform.Position.X, 3);
        Assert.False(finished);
    }

    [Fact]
    public void TweensOfADestroyedEntity_Stop()
    {
        var entity = Add();
        var tween = _game.MoveTo(entity, new Vector2(10, 0), 1);
        _game.Destroy(entity);

        Run(2);

        Assert.False(tween.IsActive);
    }

    [Fact]
    public void Tweens_FollowTimeScale()
    {
        var entity = Add();
        _game.MoveTo(entity, new Vector2(10, 0), 1, Ease.Linear);

        _game.Time.Scale = 0;
        Run(20);
        Assert.Equal(0f, entity.Transform.Position.X);

        _game.Time.Scale = 1;
        Run(20);
        Assert.Equal(10f, entity.Transform.Position.X);
    }

    [Fact]
    public void ZeroLengthTween_AppliesTheEndRightAway()
    {
        var entity = Add();

        _game.ScaleTo(entity, new Vector2(4, 4), 0);
        Run(1);

        Assert.Equal(new Vector3(4, 4, 1), entity.Transform.Scale);
    }

    [Fact]
    public void Behaviors_CanTweenThemselves()
    {
        var entity = Add("Popper");
        entity.AddComponent<Popper>();

        Run(10);

        Assert.Equal(new Vector3(2, 2, 1), entity.Transform.Scale);
        Assert.Equal(new Vector3(0, 3, 0), entity.Transform.Position);
    }

    [Theory]
    [MemberData(nameof(Curves))]
    public void EaseCurves_StartAtZero_AndEndAtOne(string name, Func<float, float> ease)
    {
        Assert.True(MathF.Abs(ease(0)) < 1e-4f, $"{name}(0) = {ease(0)}");
        Assert.True(MathF.Abs(ease(1) - 1) < 1e-4f, $"{name}(1) = {ease(1)}");
    }

    public static TheoryData<string, Func<float, float>> Curves() => new()
    {
        { "Linear", Ease.Linear }, { "InQuad", Ease.InQuad }, { "OutQuad", Ease.OutQuad }, { "InOutQuad", Ease.InOutQuad },
        { "InCubic", Ease.InCubic }, { "OutCubic", Ease.OutCubic }, { "InOutCubic", Ease.InOutCubic },
        { "InOutSine", Ease.InOutSine }, { "InBack", Ease.InBack }, { "OutBack", Ease.OutBack },
        { "OutElastic", Ease.OutElastic }, { "OutBounce", Ease.OutBounce },
    };

    private sealed class Popper : Behavior
    {
        protected override void Start()
        {
            ScaleTo(new Vector2(2, 2), 0.3f, Ease.OutBack);
            MoveTo(new Vector2(0, 3), 0.3f);
        }
    }
}
