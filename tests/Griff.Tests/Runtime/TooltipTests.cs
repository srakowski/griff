using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class TooltipTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();

    public TooltipTests()
    {
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        _scene.Add(camera);
    }

    private Game CreateGame() => new(_scene, _input) { ViewAspect = 2 };

    private void Tick(Game game, int steps = 1)
    {
        for (var i = 0; i < steps; i++)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    // A sprite in the world with a tooltip and nothing else: no button, no collider.
    private Tooltip AddDeck(string name, Vector2 at, string says = "Deck", float delay = 0.25f)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        entity.AddComponent<Sprite>().Size = new Vector2(2, 2);
        var tip = entity.AddComponent<Tooltip>();
        tip.Value = says;
        tip.Delay = delay;
        _scene.Add(entity);
        return tip;
    }

    private void MouseAt(Vector2 at)
    {
        _input.MouseWorld = at;
        _input.IsMouseInView = true;
    }

    [Fact]
    public void Tooltip_ShowsOnAWorldSpriteOnlyAfterItsDelay()
    {
        var tip = AddDeck("Deck", Vector2.Zero, delay: 0.25f);
        var game = CreateGame();
        MouseAt(new Vector2(0.5f, 0.5f));

        Tick(game, 6);
        Assert.True(tip.IsHovered);
        Assert.False(tip.IsShowing);

        Tick(game, 12);
        Assert.True(tip.IsShowing);
        Assert.Equal("Deck", tip.Shown);
    }

    [Fact]
    public void Tooltip_HidesWhenTheMouseLeaves_AndWaitsItsDelayAgain()
    {
        var tip = AddDeck("Deck", Vector2.Zero);
        var game = CreateGame();
        MouseAt(Vector2.Zero);
        Tick(game, 30);
        Assert.True(tip.IsShowing);

        MouseAt(new Vector2(5, 0));
        Tick(game);
        Assert.False(tip.IsHovered);
        Assert.False(tip.IsShowing);

        MouseAt(Vector2.Zero);
        Tick(game);
        Assert.False(tip.IsShowing);
    }

    [Fact]
    public void Tooltip_MouseOutsideTheView_HidesIt()
    {
        var tip = AddDeck("Deck", Vector2.Zero);
        var game = CreateGame();
        MouseAt(Vector2.Zero);
        Tick(game, 30);

        _input.IsMouseInView = false;
        Tick(game);

        Assert.False(tip.IsShowing);
    }

    [Fact]
    public void Tooltip_MovingStraightToAnotherShowsItAtOnce()
    {
        var first = AddDeck("A", Vector2.Zero, "A");
        var second = AddDeck("B", new Vector2(2, 0), "B");
        var game = CreateGame();
        MouseAt(Vector2.Zero);
        Tick(game, 30);

        MouseAt(new Vector2(2, 0));
        Tick(game);

        Assert.False(first.IsShowing);
        Assert.True(second.IsShowing);
    }

    [Fact]
    public void Tooltip_OnlyTheTopmostShows()
    {
        var below = AddDeck("Below", Vector2.Zero, "Below");
        var above = AddDeck("Above", new Vector2(0.5f, 0), "Above");
        above.Entity!.GetComponent<Sprite>()!.Order = 5;
        var game = CreateGame();
        MouseAt(new Vector2(0.3f, 0));

        Tick(game, 30);

        Assert.True(above.IsShowing);
        Assert.False(below.IsHovered);
    }

    [Fact]
    public void Tooltip_OnTheScreenComesBeforeOneInTheWorld()
    {
        var world = AddDeck("World", Vector2.Zero, "World");
        world.Entity!.GetComponent<Sprite>()!.Order = 50;
        var hud = AddDeck("Hud", Vector2.Zero, "Hud");
        var anchor = hud.Entity!.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Center;
        var game = CreateGame();
        MouseAt(Vector2.Zero);

        Tick(game, 30);

        Assert.True(hud.IsShowing);
        Assert.False(world.IsHovered);
    }

    [Fact]
    public void Tooltip_OnAButtonShows_AndOneInTheWorldUnderAButtonDoesNot()
    {
        var world = AddDeck("World", Vector2.Zero, "World");
        var buttonEntity = new Entity("Button");
        buttonEntity.AddComponent<Button>().Size = new Vector2(1, 1);
        _scene.Add(buttonEntity);
        var game = CreateGame();
        MouseAt(Vector2.Zero);

        Tick(game, 30);
        Assert.False(world.IsHovered); // the button has the mouse

        var onButton = buttonEntity.AddComponent<Tooltip>();
        onButton.Delay = 0;
        Tick(game, 2);
        Assert.True(onButton.IsShowing);
    }

    [Fact]
    public void Tooltip_AreaSetsThePartHovered()
    {
        var tip = AddDeck("Deck", Vector2.Zero);
        tip.Area = new Vector2(6, 1);
        var game = CreateGame();
        MouseAt(new Vector2(2.5f, 0));

        Tick(game, 30);

        Assert.True(tip.IsShowing);
    }

    [Fact]
    public void Tooltip_SaysWhatAScriptSets()
    {
        var tip = AddDeck("Deck", Vector2.Zero);
        var game = CreateGame();
        MouseAt(Vector2.Zero);
        Tick(game, 30);

        tip.Value = "Contract: 3 crates\nDue in 40 s";
        Tick(game);

        Assert.Equal("Contract: 3 crates\nDue in 40 s", tip.Shown);
    }

    private sealed class Stock : Component
    {
        [Output(OutOf = nameof(Capacity))]
        public int Cards { get; set; }

        public int Capacity { get; set; } = 10;
    }

    [Fact]
    public void Tooltip_TracksAnOutput()
    {
        var tip = AddDeck("Deck", Vector2.Zero);
        var stock = tip.Entity!.AddComponent<Stock>();
        stock.Cards = 3;
        tip.Output = "Cards";
        tip.Value = "{0} / {1} cards";
        var game = CreateGame();
        MouseAt(Vector2.Zero);
        Tick(game, 30);
        Assert.Equal("3 / 10 cards", tip.Shown);

        stock.Cards = 7;
        Tick(game);

        Assert.Equal("7 / 10 cards", tip.Shown);
    }

    [Fact]
    public void Tooltip_TracksAnotherEntitysOutput()
    {
        var board = new Entity("Board");
        board.AddComponent<Stock>().Cards = 4;
        _scene.Add(board);
        var tip = AddDeck("Deck", Vector2.Zero);
        tip.Track = "Board";
        tip.Output = "Cards";
        tip.Value = "Left: {0}";
        var game = CreateGame();

        Tick(game);

        Assert.Equal("Left: 4", tip.Shown);
    }

    [Fact]
    public void Tooltip_InspectorPreviewReadsTheTrackedEntity_NotTheTextItWritesInto()
    {
        var board = new Entity("Board");
        board.AddComponent<Stock>().Cards = 4;
        _scene.Add(board);
        var info = new Entity("Hover Info");
        info.AddComponent<Text>();
        _scene.Add(info);
        var tip = AddDeck("Deck", Vector2.Zero);
        (tip.Into, tip.Track, tip.Output) = ("Hover Info", "Board", "Cards");

        Assert.Equal("4 cards", new OutputFormatAttribute(nameof(Tooltip.Output)).Preview(tip, "{0} cards"));
    }

    [Fact]
    public void Tooltip_WritesIntoANamedText_AndPutsBackWhatItSaid()
    {
        var info = new Entity("Hover Info");
        var text = info.AddComponent<Text>();
        text.Value = "Hover a deck";
        info.AddComponent<ScreenAnchor>().Anchor = Anchor.BottomLeft;
        _scene.Add(info);
        var first = AddDeck("A", Vector2.Zero, "Deck A");
        var second = AddDeck("B", new Vector2(4, 0), "Deck B");
        first.Into = second.Into = "Hover Info";
        var game = CreateGame();
        MouseAt(Vector2.Zero);

        Tick(game, 5);
        Assert.Equal("Hover a deck", text.Value); // not before its delay

        Tick(game, 20);
        Assert.Equal("Deck A", text.Value);

        first.Value = "Deck A, 2 cards";
        Tick(game);
        Assert.Equal("Deck A, 2 cards", text.Value);

        MouseAt(new Vector2(4, 0));
        Tick(game, 30);
        Assert.Equal("Deck B", text.Value);

        MouseAt(new Vector2(8, 4));
        Tick(game);
        Assert.Equal("Hover a deck", text.Value);
    }

    [Fact]
    public void Tooltip_WritingIntoAText_DrawsNoBox()
    {
        var info = new Entity("Hover Info");
        info.AddComponent<Text>().Value = "";
        _scene.Add(info);
        var tip = AddDeck("Deck", Vector2.Zero, "Deck A");
        tip.Into = "Hover Info";
        var game = CreateGame();
        MouseAt(Vector2.Zero);
        Tick(game, 30);

        var frame = SceneRenderer.Build(game, 2);

        Assert.DoesNotContain(frame.Sprites, d => d.Entity == Views.ScreenEntity && d.Text == "Deck A");
    }

    [Fact]
    public void Tooltip_DrawsABoxByTheCursor_PinnedToTheScreen()
    {
        var tip = AddDeck("Deck", Vector2.Zero, "Deck A");
        var game = CreateGame();
        MouseAt(new Vector2(0.5f, 0.5f));

        var before = SceneRenderer.Build(game, 2);
        Assert.DoesNotContain(before.Sprites, d => d.Text == "Deck A");
        Tick(game, 30);
        var frame = SceneRenderer.Build(game, 2);

        var label = Assert.Single(frame.Sprites, d => d.Text == "Deck A");
        Assert.Equal(Views.ScreenEntity, label.Entity);
        // To the right of the cursor and below it, over the box behind it, drawn last.
        Assert.True(label.World.M41 > 0.5f && label.World.M42 < 0.5f);
        Assert.Equal(label, frame.Sprites[^1]);
        Assert.Equal(tip.Background, frame.Sprites[^2].Tint);
    }

    [Fact]
    public void Tooltip_BoxNearTheRightEdge_GoesLeftOfTheCursor()
    {
        AddDeck("Deck", new Vector2(9, 0), "A long tooltip that would run off the screen");
        var game = CreateGame();
        MouseAt(new Vector2(9.5f, 0));
        Tick(game, 30);

        var frame = SceneRenderer.Build(game, 2);

        var label = Assert.Single(frame.Sprites, d => d.Text?.StartsWith("A long", StringComparison.Ordinal) == true);
        Assert.True(label.World.M41 < 9.5f);
        // Camera size 5 at aspect 2: the view's left edge is at -10.
        Assert.True(label.World.M41 >= -10);
    }
}
