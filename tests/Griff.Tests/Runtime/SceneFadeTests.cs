using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class SceneFadeTests
{
    private const double Step = 0.1;
    private readonly Scene _menu = new();
    private readonly Scene _level = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public SceneFadeTests()
    {
        _game = new Game(_menu, _input)
        {
            SceneName = "Menu",
            SceneLoader = name => name switch
            {
                "Level" => _level,
                "Menu" => _menu,
                _ => throw new FileNotFoundException(name),
            },
        };
    }

    private void Run(int frames)
    {
        for (var i = 0; i < frames; i++)
            _game.Tick(Step);
    }

    [Fact]
    public void FadesOut_Switches_ThenFadesBackIn()
    {
        _game.LoadScene("Level", fade: 0.3f);
        Assert.True(_game.IsChangingScene);

        Run(1);
        Assert.Same(_menu, _game.Scene);
        Assert.Equal(1 / 3f, _game.FadeAmount, 3);

        Run(2);
        Assert.Same(_level, _game.Scene);
        Assert.Equal(1, _game.FadeAmount);
        Assert.False(_game.IsChangingScene);

        Run(1);
        Assert.Equal(2 / 3f, _game.FadeAmount, 3);
        Run(2);
        Assert.Equal(0, _game.FadeAmount);
    }

    [Fact]
    public void KeepsRunningTheOldSceneWhileFadingOut()
    {
        var entity = new Entity("Counter");
        var counter = entity.AddComponent<Counter>();
        _menu.Add(entity);

        _game.LoadScene("Level", fade: 0.5f);
        Run(3);

        Assert.Equal(3, counter.Updates);
        Assert.Same(_menu, _game.Scene);
    }

    [Fact]
    public void UsesRealTime_SoItWorksWhilePaused()
    {
        _game.Time.Scale = 0;
        _game.LoadScene("Level", fade: 0.2f);

        Run(2);

        Assert.Same(_level, _game.Scene);
    }

    [Fact]
    public void LoadingAgainMidFade_ChangesTheDestination()
    {
        var other = new Scene();
        _game.SceneLoader = name => name == "Other" ? other : _level;

        _game.LoadScene("Level", fade: 0.4f);
        Run(2);
        _game.LoadScene("Other", fade: 0.4f);
        Run(2);

        Assert.Same(other, _game.Scene);
    }

    [Fact]
    public void CuttingStraightThereMidFade_ClearsTheFade()
    {
        _game.LoadScene("Level", fade: 0.4f);
        Run(2);
        _game.LoadScene("Level");
        Run(1);

        Assert.Same(_level, _game.Scene);
        Run(5);
        Assert.Equal(0, _game.FadeAmount);
    }

    [Fact]
    public void ReloadScene_CanFade_InAnyColor()
    {
        var red = new Color(1, 0, 0);
        _game.ReloadScene(fade: 0.2f, color: red);
        Run(1);

        Assert.Equal(red, _game.FadeColor);
        Assert.Equal(0.5f, _game.FadeAmount, 3);
    }

    [Fact]
    public void Frame_CoversTheWholeViewWithTheFade()
    {
        var camera = new Entity("Camera");
        camera.Transform.Position = new Vector3(10, 4, 0);
        camera.AddComponent<Camera>();
        _menu.Add(camera);
        _game.LoadScene("Level", fade: 0.2f);
        Run(1);

        var frame = SceneRenderer.Build(_game, 16f / 9f);

        var overlay = frame.Sprites[^1];
        Assert.Equal(0.5f, overlay.Tint.A, 3);
        var toClip = overlay.World * frame.ViewProjection;
        var topLeft = Vector4.Transform(new Vector4(-0.5f, 0.5f, 0, 1), toClip);
        var bottomRight = Vector4.Transform(new Vector4(0.5f, -0.5f, 0, 1), toClip);
        Assert.Equal(-1, topLeft.X / topLeft.W, 3);
        Assert.Equal(1, topLeft.Y / topLeft.W, 3);
        Assert.Equal(1, bottomRight.X / bottomRight.W, 3);
        Assert.Equal(-1, bottomRight.Y / bottomRight.W, 3);
    }

    [Fact]
    public void Frame_HasNoOverlayWithoutAFade()
    {
        var box = new Entity("Box");
        box.AddComponent<Sprite>();
        _menu.Add(box);

        var frame = SceneRenderer.Build(_game, 1);

        Assert.Single(frame.Sprites);
    }

    [Fact]
    public void Button_CanFade()
    {
        var entity = new Entity("Play");
        var button = entity.AddComponent<Button>();
        button.Action = ButtonAction.LoadScene;
        button.Scene = "Level";
        button.Fade = 0.2f;
        button.Shortcut = Key.Enter;
        _menu.Add(entity);

        _input.SetKey(Key.Enter, true);
        Run(1);
        _input.EndFrame();

        Assert.Same(_menu, _game.Scene);
        Assert.True(_game.IsChangingScene);
        Run(2);
        Assert.Same(_level, _game.Scene);
    }

    private sealed class Counter : Behavior
    {
        public int Updates { get; private set; }

        protected override void Update() => Updates++;
    }
}
