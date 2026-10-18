using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Built-in juice with no code: tween players, buttons that pop, numbers that count, layouts that slide, camera kicks.</summary>
public sealed class JuiceTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public JuiceTests()
    {
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        _scene.Add(camera);
        _game = new Game(_scene, _input) { ViewAspect = 2, PrefabLoader = name => new Entity(name) };
    }

    private void Run(int frames = 1)
    {
        for (var i = 0; i < frames; i++)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private Entity Add(string name, Entity? parent = null)
    {
        var entity = new Entity(name);
        _scene.Add(entity, parent);
        return entity;
    }

    // How many times its size an entity is drawn, by its draw effect.
    private static float DrawnScale(IDrawEffect effect) => effect.DrawTransform is { } m ? new Vector2(m.M11, m.M12).Length() : 1;

    // ----- Tween players -----

    [Fact]
    public void TweenPlayer_ByDefault_PopsItsEntityInFromNothingAsItAppears_LeavingItsTransformAlone()
    {
        var card = Add("Card");
        card.Transform.Position = new Vector3(2, 1, 0);
        var tween = card.AddComponent<TweenPlayer>();
        (tween.Fade, tween.Ease) = (1, Easing.Linear);

        Run();
        Assert.True(tween.IsPlaying);
        Assert.True(DrawnScale(tween) < 0.1f);
        Assert.True(((IDrawEffect)tween).DrawOpacity < 0.1f);

        Run(9);
        var half = DrawnScale(tween);
        Assert.InRange(half, 0.4f, 0.7f);
        Assert.InRange(((IDrawEffect)tween).DrawOpacity, 0.4f, 0.7f);

        // Grown about its own middle, which stays put.
        var middle = Vector3.Transform(new Vector3(2, 1, 0), ((IDrawEffect)tween).DrawTransform!.Value);
        Assert.Equal(2, middle.X, 3);
        Assert.Equal(1, middle.Y, 3);

        Run(15);
        Assert.False(tween.IsPlaying);
        Assert.Null(((IDrawEffect)tween).DrawTransform);
        Assert.Equal(1, ((IDrawEffect)tween).DrawOpacity);
        Assert.Equal(new Vector3(2, 1, 0), card.Transform.Position);
        Assert.Equal(Vector3.One, card.Transform.Scale);
    }

    [Fact]
    public void TweenPlayer_OnItsMessage_PlaysFromTheStart_AndNotBefore()
    {
        var stamp = Add("Stamp");
        var tween = stamp.AddComponent<TweenPlayer>();
        (tween.OnStart, tween.Message, tween.Scale, tween.Move) = (false, "Approved", 2, new Vector2(0, 1));

        Run(5);
        Assert.False(tween.IsPlaying);
        Assert.Null(((IDrawEffect)tween).DrawTransform);

        _game.Send("Approved");
        Run();
        Assert.True(tween.IsPlaying);
        Assert.True(DrawnScale(tween) > 1.5f);
        Assert.True(((IDrawEffect)tween).DrawTransform!.Value.M42 > 0.5f);
    }

    [Fact]
    public void TweenPlayer_To_EndsAtItsValues_AndStaysThere_UntilStopped()
    {
        var card = Add("Card");
        var tween = card.AddComponent<TweenPlayer>();
        (tween.Direction, tween.Scale, tween.Fade, tween.Duration) = (TweenDirection.To, 1, 1, 0.1f);

        Run(20);
        Assert.False(tween.IsPlaying);
        Assert.Equal(0, ((IDrawEffect)tween).DrawOpacity, 3);

        tween.Stop();
        Assert.Equal(1, ((IDrawEffect)tween).DrawOpacity);
    }

    [Fact]
    public void TweenPlayer_PingPong_GoesBackAndForthUntilStopped()
    {
        var coin = Add("Coin");
        var tween = coin.AddComponent<TweenPlayer>();
        (tween.Scale, tween.Move, tween.Loop, tween.Ease, tween.Duration) = (1, new Vector2(0, 1), TweenLoop.PingPong, Easing.Linear, 0.5f);

        Run(31);
        var top = tween.Amount;
        Run(30);
        var bottom = tween.Amount;
        Run(30);

        Assert.True(tween.IsPlaying);
        Assert.InRange(top, -0.05f, 0.05f);
        Assert.InRange(bottom, 0.95f, 1.05f);
        Assert.InRange(tween.Amount, -0.05f, 0.05f);
    }

    [Fact]
    public void TweenPlayer_ShownAgain_PlaysOnStartAgain()
    {
        var panel = Add("Popup");
        var tween = panel.AddComponent<TweenPlayer>();
        tween.Duration = 0.1f;
        Run(20);
        Assert.False(tween.IsPlaying);

        panel.IsEnabled = false;
        Run(2);
        panel.IsEnabled = true;
        Run();

        Assert.True(tween.IsPlaying);
    }

    // ----- Buttons that pop -----

    [Fact]
    public void Button_ByDefault_DrawsItsSizeAndShadesAtOnce()
    {
        var play = Add("Play");
        var button = play.AddComponent<Button>();
        _input.MouseWorld = Vector2.Zero;
        Run(2);

        Assert.True(button.IsHovered);
        Assert.Null(((IDrawEffect)button).DrawTransform);
        Assert.Equal(Color.Lerp(Color.Red, Color.White, 0.2f), button.Shade(Color.Red));
    }

    [Fact]
    public void Button_WithHoverGrowAndPopTime_EasesBiggerAndLighterUnderTheMouse_AndSmallerAsItsPressed()
    {
        var play = Add("Play");
        var button = play.AddComponent<Button>();
        (button.HoverGrow, button.PressShrink, button.PopTime) = (0.1f, 0.1f, 0.1f);
        _input.MouseWorld = new Vector2(20, 20);
        Run();
        Assert.Null(((IDrawEffect)button).DrawTransform);

        _input.MouseWorld = Vector2.Zero;
        Run(3);
        var growing = DrawnScale(button);
        Assert.InRange(growing, 1.001f, 1.099f);
        Assert.True(button.Shade(Color.Red).G is > 0 and < 0.2f);

        Run(10);
        Assert.Equal(1.1f, DrawnScale(button), 3);
        Assert.Equal(Color.Lerp(Color.Red, Color.White, 0.2f), button.Shade(Color.Red));

        _input.SetMouseButton(MouseButton.Left, true);
        Run(10);
        Assert.Equal(0.9f, DrawnScale(button), 3);
    }

    // ----- Numbers that count -----

    [Fact]
    public void TextShowingAnOutput_WithCountTime_CountsUpToANewNumber_AndJumpsWithout()
    {
        var score = Add("Score");
        score.AddComponent<Counter>();
        var counting = Add("Counting").AddComponent<Text>();
        (counting.Track, counting.Output, counting.Value, counting.CountTime) = ("Score", "Count", "{0}", 0.5f);
        var jumping = Add("Jumping").AddComponent<Text>();
        (jumping.Track, jumping.Output, jumping.Value) = ("Score", "Count", "{0}");
        Run();
        Assert.Equal("0", counting.Shown);

        _game.Send("Score", 100);
        Run(10);
        var partway = int.Parse(counting.Shown!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(partway, 1, 99);
        Assert.Equal("100", jumping.Shown);

        Run(30);
        Assert.Equal("100", counting.Shown);
    }

    // ----- Layouts that slide -----

    private (Layout Layout, List<Entity> Rows) List(bool slide)
    {
        var list = Add("List");
        var layout = list.AddComponent<Layout>();
        (layout.Size, layout.Spacing, layout.Slide, layout.SlideTime) = (new Vector2(6, 8), 0, slide, 0.2f);
        var rows = new List<Entity>();
        for (var i = 0; i < 3; i++)
        {
            var row = Add($"Row {i}", list);
            row.AddComponent<Sprite>().Size = new Vector2(2, 1);
            rows.Add(row);
        }

        Run();
        return (layout, rows);
    }

    [Fact]
    public void Layout_WithSlide_SlidesTheRestUpWhenOneIsHidden_AndPutsANewOneInPlaceAtOnce()
    {
        var (layout, rows) = List(slide: true);
        Assert.Equal(1.5f, rows[2].Transform.Position.Y, 3);

        rows[0].IsEnabled = false;
        Run();
        Assert.InRange(rows[2].Transform.Position.Y, 1.6f, 2.4f);
        Run(60);
        Assert.Equal(2.5f, rows[2].Transform.Position.Y, 3);

        var added = Add("Row 3", layout.Entity);
        added.AddComponent<Sprite>().Size = new Vector2(2, 1);
        Run();
        Assert.Equal(1.5f, added.Transform.Position.Y, 3);
    }

    [Fact]
    public void Layout_WithoutSlide_PutsTheRestInPlaceAtOnce()
    {
        var (_, rows) = List(slide: false);

        rows[0].IsEnabled = false;
        Run();

        Assert.Equal(2.5f, rows[2].Transform.Position.Y, 3);
    }

    // ----- Camera kicks -----

    [Fact]
    public void CameraKick_PushesTheViewItsWay_AndEasesBack()
    {
        var camera = _scene.FindNamed("Camera")!.GetComponent<Camera>()!;
        _game.KickCamera(new Vector2(0.5f, 0), 0.2f);
        Run(3);
        Assert.True(camera.ShakeOffset.X > 0.3f);
        Assert.Equal(0, camera.ShakeOffset.Y, 4);

        Run(12);
        Assert.Equal(Vector2.Zero, camera.ShakeOffset);

        // With no code: its Kick action pushes it by its Kick By.
        camera.KickBy = new Vector2(0, -0.4f);
        _game.Do(camera.Entity!, "Kick");
        Run(3);
        Assert.True(camera.ShakeOffset.Y < -0.2f);
    }
}
