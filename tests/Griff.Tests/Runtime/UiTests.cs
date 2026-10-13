using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class UiTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Entity _camera;

    public UiTests()
    {
        _camera = new Entity("Camera");
        _camera.AddComponent<Camera>().Size = 5;
        _scene.Add(_camera);
    }

    private Game CreateGame() => new(_scene, _input) { ViewAspect = 2 };

    private void Tick(Game game)
    {
        game.Tick(Frame);
        _input.EndFrame();
    }

    private Entity AddAnchored(Anchor anchor, Vector2 offset, Entity? parent = null)
    {
        var entity = new Entity("HUD");
        entity.Transform.Position = new Vector3(100, 100, 3);
        var screen = entity.AddComponent<ScreenAnchor>();
        screen.Anchor = anchor;
        screen.Offset = offset;
        _scene.Add(entity, parent);
        return entity;
    }

    private Entity AddButton(Vector2 position, ButtonAction action = ButtonAction.None, string? scene = null)
    {
        var entity = new Entity("Button");
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<Sprite>().Size = new Vector2(2, 1);
        var button = entity.AddComponent<Button>();
        button.Size = new Vector2(2, 1);
        button.Action = action;
        button.Scene = scene;
        _scene.Add(entity);
        return entity;
    }

    private void Click(Game game, Vector2 at)
    {
        _input.MouseWorld = at;
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
    }

    [Theory]
    [InlineData(Anchor.TopLeft, -10, 5)]
    [InlineData(Anchor.Top, 0, 5)]
    [InlineData(Anchor.BottomRight, 10, -5)]
    [InlineData(Anchor.Center, 0, 0)]
    [InlineData(Anchor.Left, -10, 0)]
    public void ScreenAnchor_PinsToTheEdgeOfTheView(Anchor anchor, float x, float y)
    {
        var hud = AddAnchored(anchor, Vector2.Zero);

        Tick(CreateGame());

        // Camera size 5 at aspect 2: the view spans x -10..10, y -5..5.
        Assert.Equal(new Vector3(x, y, 3), hud.Transform.Position);
    }

    [Fact]
    public void ScreenAnchor_FollowsTheCamera_AndAddsItsOffset()
    {
        var hud = AddAnchored(Anchor.TopLeft, new Vector2(1.5f, -0.5f));
        var game = CreateGame();

        _camera.Transform.Position = new Vector3(20, 3, -10);
        Tick(game);

        Assert.Equal(new Vector3(20 - 10 + 1.5f, 3 + 5 - 0.5f, 3), hud.Transform.Position);
    }

    [Fact]
    public void ScreenAnchor_FollowsTheWindowsShape()
    {
        var hud = AddAnchored(Anchor.Right, Vector2.Zero);
        var game = CreateGame();

        Tick(game);
        Assert.Equal(10, hud.Transform.Position.X, 4);

        game.ViewAspect = 1;
        Tick(game);
        Assert.Equal(5, hud.Transform.Position.X, 4);
    }

    [Fact]
    public void ScreenAnchor_UnderAParent_IsPlacedInWorldSpace()
    {
        var parent = new Entity("Parent");
        parent.Transform.Position = new Vector3(4, 4, 0);
        parent.Transform.Scale = new Vector3(2, 2, 1);
        _scene.Add(parent);
        var hud = AddAnchored(Anchor.BottomLeft, Vector2.Zero, parent);

        Tick(CreateGame());

        var world = hud.Transform.WorldPosition;
        Assert.Equal(-10, world.X, 4);
        Assert.Equal(-5, world.Y, 4);
    }

    [Fact]
    public void ScreenAnchor_HoldsStillWhileTheCameraShakes()
    {
        var hud = AddAnchored(Anchor.Top, Vector2.Zero);
        var game = CreateGame();
        Tick(game);

        game.ShakeCamera(1, 1);
        Tick(game);

        // On screen it hasn't moved: the shake offset moves the view and the HUD together.
        var shake = _camera.GetComponent<Camera>()!.ShakeOffset;
        Assert.NotEqual(Vector2.Zero, shake);
        Assert.Equal(shake.X, hud.Transform.Position.X, 4);
        Assert.Equal(5 + shake.Y, hud.Transform.Position.Y, 4);
    }

    [Fact]
    public void Button_ClicksOnRelease_OverTheButton()
    {
        var entity = AddButton(new Vector2(3, 0));
        var clicks = entity.AddComponent<ClickCounter>();
        var raised = 0;
        entity.GetComponent<Button>()!.Clicked += _ => raised++;
        var game = CreateGame();
        Tick(game);

        _input.MouseWorld = new Vector2(3.9f, 0.4f);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        Assert.Equal(0, clicks.Count); // not on press
        Assert.True(entity.GetComponent<Button>()!.IsPressed);

        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
        Assert.Equal(1, clicks.Count);
        Assert.Equal(1, raised);
        Assert.False(entity.GetComponent<Button>()!.IsPressed);
    }

    [Fact]
    public void Button_DraggingOffBeforeReleasing_DoesNotClick()
    {
        var entity = AddButton(Vector2.Zero);
        var clicks = entity.AddComponent<ClickCounter>();
        var game = CreateGame();
        Tick(game);

        _input.MouseWorld = Vector2.Zero;
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.MouseWorld = new Vector2(5, 0);
        Tick(game);
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);

        Assert.Equal(0, clicks.Count);
    }

    [Fact]
    public void Button_PressStartedElsewhere_DoesNotClick()
    {
        var entity = AddButton(Vector2.Zero);
        var clicks = entity.AddComponent<ClickCounter>();
        var game = CreateGame();
        Tick(game);

        _input.MouseWorld = new Vector2(5, 0);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.MouseWorld = Vector2.Zero;
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);

        Assert.Equal(0, clicks.Count);
    }

    [Fact]
    public void Button_Shortcut_ClicksIt()
    {
        var entity = AddButton(Vector2.Zero);
        entity.GetComponent<Button>()!.Shortcut = Key.Enter;
        var clicks = entity.AddComponent<ClickCounter>();
        var game = CreateGame();
        Tick(game);

        _input.SetKey(Key.Enter, true);
        Tick(game);

        Assert.Equal(1, clicks.Count);
    }

    [Fact]
    public void Button_OnlyTheTopmostOverlappingButtonIsHoveredAndClicked()
    {
        var below = AddButton(Vector2.Zero);
        var above = AddButton(new Vector2(0.5f, 0));
        below.GetComponent<Sprite>()!.Order = 5;
        above.GetComponent<Sprite>()!.Order = 1;
        var belowClicks = below.AddComponent<ClickCounter>();
        var aboveClicks = above.AddComponent<ClickCounter>();
        var game = CreateGame();
        Tick(game);

        Click(game, new Vector2(0.4f, 0));

        Assert.Equal(1, belowClicks.Count); // higher order draws on top, so it wins
        Assert.Equal(0, aboveClicks.Count);
    }

    [Fact]
    public void Button_DisabledEntity_CannotBeClicked()
    {
        var entity = AddButton(Vector2.Zero);
        var clicks = entity.AddComponent<ClickCounter>();
        var game = CreateGame();
        Tick(game);

        entity.IsEnabled = false;
        Click(game, Vector2.Zero);

        Assert.Equal(0, clicks.Count);
    }

    [Fact]
    public void Button_MouseOutsideTheView_DoesNotHoverOrClick()
    {
        var entity = AddButton(Vector2.Zero);
        var clicks = entity.AddComponent<ClickCounter>();
        var game = CreateGame();
        _input.IsMouseInView = false;

        Click(game, Vector2.Zero);

        Assert.False(entity.GetComponent<Button>()!.IsHovered);
        Assert.Equal(0, clicks.Count);
    }

    [Fact]
    public void Button_LoadSceneAction_SwitchesScene()
    {
        AddButton(Vector2.Zero, ButtonAction.LoadScene, "Level1");
        var level = new Scene();
        var game = CreateGame();
        game.SceneLoader = name => name == "Level1" ? level : throw new FileNotFoundException();
        Tick(game);

        Click(game, Vector2.Zero);

        Assert.Same(level, game.Scene);
        Assert.Equal("Level1", game.SceneName);
    }

    [Fact]
    public void Button_LoadSceneAction_WithoutAScene_ExplainsTheProblem()
    {
        AddButton(Vector2.Zero, ButtonAction.LoadScene);
        var game = CreateGame();
        game.SceneLoader = _ => new Scene();
        Tick(game);

        _input.MouseWorld = Vector2.Zero;
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.SetMouseButton(MouseButton.Left, false);

        var error = Assert.Throws<InvalidOperationException>(() => game.Tick(Frame));
        Assert.Contains("Scene isn't set", error.Message);
    }

    [Fact]
    public void Button_QuitAction_AsksTheHostToQuit()
    {
        AddButton(Vector2.Zero, ButtonAction.Quit);
        var game = CreateGame();
        var quits = 0;
        game.QuitRequested += (_, _) => quits++;
        Tick(game);

        Click(game, Vector2.Zero);
        game.Quit();

        Assert.True(game.IsQuitting);
        Assert.Equal(1, quits);
    }

    [Fact]
    public void Button_IsShadedWhileHoveredAndPressed()
    {
        var entity = AddButton(Vector2.Zero);
        var sprite = entity.GetComponent<Sprite>()!;
        sprite.Tint = new Color(0.5f, 0.5f, 0.5f);
        var game = CreateGame();
        Color Drawn() => SceneRenderer.Build(_scene, 2).Sprites.Single(s => s.Entity == entity).Tint;

        _input.MouseWorld = new Vector2(10, 10);
        Tick(game);
        Assert.Equal(sprite.Tint, Drawn());

        _input.MouseWorld = Vector2.Zero;
        Tick(game);
        Assert.True(Drawn().R > 0.5f, "hovered buttons get lighter");

        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        Assert.True(Drawn().R < 0.5f, "pressed buttons get darker");
        Assert.Equal(new Color(0.5f, 0.5f, 0.5f), sprite.Tint); // the sprite itself is untouched
    }

    public class ClickCounter : Behavior
    {
        [DoNotSerialize]
        public int Count { get; private set; }

        protected override void OnClick() => Count++;
    }
}
