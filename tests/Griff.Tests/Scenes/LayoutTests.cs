using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Scenes;

public class LayoutTests
{
    private readonly Scene _scene = new();
    private readonly InputState _input = new();

    // A layout 6 by 4 at (10, 0): its top left corner is (7, 2).
    private (Game Game, Entity Board, Layout Layout) Board(LayoutDirection direction = LayoutDirection.Vertical)
    {
        var board = new Entity("Board");
        board.Transform.Position = new Vector3(10, 0, 0);
        var layout = board.AddComponent<Layout>();
        layout.Direction = direction;
        layout.Spacing = 0.5f;
        _scene.Add(board);
        var game = new Game(_scene, _input)
        {
            PrefabLoader = _ =>
            {
                var row = new Entity("Row");
                row.AddComponent<Sprite>().Size = new Vector2(2, 1);
                return row;
            },
        };
        return (game, board, layout);
    }

    // A frame, as the host takes it: the wheel turned in it is spent.
    private void Tick(Game game)
    {
        game.Tick(0.016);
        _input.EndFrame();
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.Position.X, entity.Transform.Position.Y);

    [Fact]
    public void Spawned_TheyTakeTheirPlacesInOrder_WithNoPositionSet()
    {
        var (game, board, _) = Board();

        var rows = Enumerable.Range(0, 3).Select(_ => game.Spawn("Row", board)).ToList();
        Tick(game);

        // From the top left, one under another: each 1 high with 0.5 between, its left edge on the layout's.
        Assert.Equal(new Vector2(-2, 1.5f), At(rows[0]));
        Assert.Equal(new Vector2(-2, 0), At(rows[1]));
        Assert.Equal(new Vector2(-2, -1.5f), At(rows[2]));
        Assert.Equal(new Vector2(8, 1.5f), Game.Flat(rows[0].Transform.WorldPosition));
    }

    [Fact]
    public void Destroyed_TheRestCloseTheGap_AndHiddenOnesTakeNoRoom()
    {
        var (game, board, _) = Board();
        var rows = Enumerable.Range(0, 3).Select(_ => game.Spawn("Row", board)).ToList();
        Tick(game);

        game.Destroy(rows[0]);
        Tick(game);

        Assert.Equal(new Vector2(-2, 1.5f), At(rows[1]));
        Assert.Equal(new Vector2(-2, 0), At(rows[2]));

        rows[1].IsEnabled = false;
        Tick(game);
        Assert.Equal(new Vector2(-2, 1.5f), At(rows[2]));
    }

    [Fact]
    public void Grid_FillsRowsOfItsColumns_InCellsTheSizeOfTheBiggest()
    {
        var (game, board, layout) = Board(LayoutDirection.Grid);
        layout.Columns = 2;
        layout.Padding = 0.25f;
        var cards = Enumerable.Range(0, 5).Select(_ => game.Spawn("Row", board)).ToList();
        // A bigger one makes every cell its size: 2 by 1.5.
        cards[1].GetComponent<Sprite>()!.Size = new Vector2(1, 1.5f);
        Tick(game);

        // Inside the padding from the top left corner (-2.75, 1.75), cells 2.5 apart across and 2 down.
        Assert.Equal(new Vector2(-1.75f, 1), At(cards[0]));
        Assert.Equal(new Vector2(0.75f, 1), At(cards[1]));
        Assert.Equal(new Vector2(-1.75f, -1), At(cards[2]));
        Assert.Equal(new Vector2(0.75f, -1), At(cards[3]));
        Assert.Equal(new Vector2(-1.75f, -3), At(cards[4]));
    }

    [Fact]
    public void Horizontal_GoesAcross_AndCellSizeGivesEveryChildTheSameRoom()
    {
        var (game, board, layout) = Board(LayoutDirection.Horizontal);
        var icons = Enumerable.Range(0, 3).Select(_ => game.Spawn("Row", board)).ToList();
        Tick(game);
        Assert.Equal([new Vector2(-2, 1.5f), new Vector2(0.5f, 1.5f), new Vector2(3, 1.5f)], icons.Select(At));

        layout.CellSize = new Vector2(1, 0);
        layout.Arrange();
        Assert.Equal([new Vector2(-2.5f, 1.5f), new Vector2(-1, 1.5f), new Vector2(0.5f, 1.5f)], icons.Select(At));
    }

    [Fact]
    public void Clipped_TheWheelOverItScrolls_AndItsChildrensDrawsAreCutToIt()
    {
        var (game, board, layout) = Board();
        layout.Clip = true;
        board.AddComponent<Sprite>().Size = layout.Size;
        var rows = Enumerable.Range(0, 5).Select(_ => game.Spawn("Row", board)).ToList();
        Tick(game);

        // Five rows take 7 units of the 4 there's room for, a row (1.5) a turn of the wheel.
        Assert.Equal(3, layout.MostScroll, 4);
        Assert.Equal(1.5f, layout.ScrollStep, 4);

        _input.MouseWorld = new Vector2(10, 0);
        Assert.True(game.MouseOnUi);
        _input.AddScroll(-1);
        Tick(game);
        Assert.Equal(1.5f, layout.Scroll, 4);
        Assert.Equal(new Vector2(-2, 3), At(rows[0]));
        Assert.Equal(new Vector2(-2, 1.5f), At(rows[1]));

        // Not past the end, nor back past the start.
        _input.AddScroll(-5);
        Tick(game);
        Assert.Equal(3, layout.Scroll, 4);
        _input.AddScroll(5);
        Tick(game);
        Assert.Equal(0, layout.Scroll);

        // Away from it the wheel leaves it alone.
        _input.MouseWorld = new Vector2(0, 0);
        Assert.False(game.MouseOnUi);
        _input.AddScroll(-1);
        Tick(game);
        Assert.Equal(0, layout.Scroll);

        var frame = SceneRenderer.Build(game, 16f / 9);
        Assert.All(frame.Sprites.Where(d => rows.Contains(d.Entity)), d => Assert.Equal(new Box(new Vector2(10, 0), new Vector2(3, 2)), d.Clip!.Area));
        Assert.Null(frame.Sprites.Single(d => d.Entity == board).Clip);
    }

    [Fact]
    public void InsideTwoClippingLayouts_DrawsAreCutToWhereTheyOverlap()
    {
        var (game, board, layout) = Board();
        layout.Clip = true;
        var inner = new Entity("Inner");
        var shelf = inner.AddComponent<Layout>();
        (shelf.Size, shelf.Clip) = (new Vector2(2, 10), true);
        game.Spawn(inner, board);
        var item = new Entity("Item");
        item.AddComponent<Sprite>();
        game.Spawn(item, inner);
        Tick(game);

        // The inner one sits on the board's left edge (8, 0), 2 wide and 10 high; the board is 4 high.
        var draws = SceneRenderer.Build(game, 16f / 9).Sprites.Where(d => d.Entity == item).ToList();
        Assert.Equal(new Box(new Vector2(8, 0), new Vector2(1, 2)), draws.Single().Clip!.Area);
        Assert.Same(draws.Single().Clip, SceneRenderer.Build(game, 16f / 9).Sprites.Single(d => d.Entity == item).Clip);
    }

    [Fact]
    public void Unclipped_NothingIsCut_AndTheWheelIsTheWorlds()
    {
        var (game, board, _) = Board();
        var row = game.Spawn("Row", board);
        Tick(game);

        _input.MouseWorld = new Vector2(10, 0);
        Assert.False(game.MouseOnUi);
        Assert.Null(SceneRenderer.Build(game, 16f / 9).Sprites.Single(d => d.Entity == row).Clip);
    }

    [Fact]
    public void TextAlone_TakesTheRoomItsLettersAreGuessedTo()
    {
        var (game, board, _) = Board();
        var label = new Entity("Label");
        var text = label.AddComponent<Text>();
        (text.Value, text.Size, text.Align) = ("Gears", 0.5f, TextAlign.Left);
        game.Spawn(label, board);
        Tick(game);

        // Left-aligned text starts at its position: on the layout's left edge, half a line down from the top.
        Assert.Equal(new Vector2(-3, 1.75f), At(label));
    }
}
