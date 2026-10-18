using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>A layout whose rows the player drags (or moves with keys) into a new order.</summary>
public class ReorderableLayoutTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly List<(Entity Row, int From, int To)> _moves = [];

    // A queue of five Button rows (2 by 1, 0.5 apart) in a layout 6 by 8 at the middle of a panel the game goes on
    // behind: the rows' middles are at y 3.5, 2, 0.5, -1 and -2.5, x -2.
    private (Game Game, Layout Layout, List<Entity> Rows) Queue(float height = 8)
    {
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        _scene.Add(camera);
        var panel = new Entity("Tray");
        var shown = panel.AddComponent<Panel>();
        (shown.Open, shown.PauseGame) = (true, false);
        var list = new Entity("Queue");
        var layout = list.AddComponent<Layout>();
        (layout.Size, layout.Spacing, layout.Reorderable, layout.SlideTime) = (new Vector2(6, height), 0.5f, true, 0);
        panel.AddChild(list);
        _scene.Add(panel);
        var game = new Game(_scene, _input)
        {
            ViewAspect = 2,
            PrefabLoader = _ =>
            {
                var row = new Entity("Batch");
                row.AddComponent<Button>().Size = new Vector2(2, 1);
                return row;
            },
        };
        var rows = Enumerable.Range(0, 5).Select(i => game.Spawn("Batch", list)).ToList();
        for (var i = 0; i < rows.Count; i++)
            rows[i].Name = $"Batch {i}";
        layout.Reordered += (row, from, to) => _moves.Add((row, from, to));
        Tick(game);
        return (game, layout, rows);
    }

    private void Tick(Game game, int frames = 1)
    {
        for (var i = 0; i < frames; i++)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void MouseTo(Game game, float x, float y)
    {
        _input.MouseWorld = new Vector2(x, y);
        Tick(game);
    }

    private static float Y(Entity row) => row.Transform.Position.Y;

    [Fact]
    public void FourthRowDraggedToTheTop_TheOthersShiftDownAsItPasses_AndReorderedFiresOnceOnLettingGo()
    {
        var (game, layout, rows) = Queue();
        var clicks = 0;
        rows[3].GetComponent<Button>()!.Clicked += _ => clicks++;

        MouseTo(game, -2, -1);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        MouseTo(game, -2, -0.5f);
        Assert.Equal(rows[3], layout.Held);

        // Over the third row's place: it moves down into the fourth's, the first two stay.
        MouseTo(game, -2, 0.5f);
        Assert.Equal(2, layout.HeldTo);
        Assert.Equal(-1, Y(rows[2]), 3);
        Assert.Equal(2, Y(rows[1]), 3);
        Assert.Equal(0.5f, Y(rows[3]), 3);

        // At the top: all three have shifted down a place.
        MouseTo(game, -2, 3.5f);
        Assert.Equal(0, layout.HeldTo);
        Assert.Equal([2f, 0.5f, -1f], rows.Take(3).Select(r => MathF.Round(Y(r), 3)));
        Assert.Empty(_moves);

        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game, 2);
        Assert.Equal([(rows[3], 3, 0)], _moves);
        Assert.Equal([rows[3], rows[0], rows[1], rows[2], rows[4]], layout.Rows);
        Assert.Equal(3.5f, Y(rows[3]), 3);
        Assert.Null(layout.Held);
        // Dragged away from where it was pressed, its button wasn't clicked.
        Assert.Equal(0, clicks);
    }

    [Fact]
    public void PlainClickOnARow_StillClicksItsButton_AndMovesNothing()
    {
        var (game, layout, rows) = Queue();
        var clicks = 0;
        rows[1].GetComponent<Button>()!.Clicked += _ => clicks++;

        MouseTo(game, -2, 2);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.MouseWorld = new Vector2(-1.95f, 2);
        Tick(game);
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);

        Assert.Equal(1, clicks);
        Assert.Empty(_moves);
        Assert.Null(layout.Held);
        Assert.True(game.MouseOnUi);
    }

    [Fact]
    public void EscOrLettingGoOffTheList_PutsTheRowBack_AndThePanelStaysOpen()
    {
        var (game, layout, rows) = Queue();

        MouseTo(game, -2, -2.5f);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        MouseTo(game, -2, 2);
        Assert.Equal(1, layout.HeldTo);
        _input.SetKey(Key.Escape, true);
        Tick(game);
        _input.SetKey(Key.Escape, false);
        Tick(game);
        Assert.Null(layout.Held);
        Assert.Equal(-2.5f, Y(rows[4]), 3);
        Assert.True(_scene.All<Panel>().Single().Entity!.IsEnabled);
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);

        // Dragged off the side, the gap goes back to where it came from, and letting go there moves nothing.
        MouseTo(game, -2, -2.5f);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        MouseTo(game, -2, 2);
        MouseTo(game, 8, 2);
        Assert.Equal(4, layout.HeldTo);
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);

        Assert.Empty(_moves);
        Assert.Equal(rows, layout.Rows);
    }

    [Fact]
    public void Clipped_DraggingNearItsEnd_ScrollsIt()
    {
        var (game, layout, rows) = Queue(height: 4);
        layout.Clip = true;
        Tick(game);
        Assert.Equal(3, layout.MostScroll, 3);

        MouseTo(game, -2, 1.5f);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        MouseTo(game, -2, 1);
        MouseTo(game, -2, -1.9f);
        Tick(game, 30);

        Assert.True(layout.Scroll > 1, $"scrolled {layout.Scroll}");
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
        Assert.Single(_moves);
        Assert.Equal(rows[0], _moves[0].Row);
        Assert.True(_moves[0].To >= 2);
    }

    [Fact]
    public void Keys_GrabLiftsThePickedRow_TheArrowsMoveIt_AndEnterPutsItDown()
    {
        var (game, layout, rows) = Queue();
        var clicks = 0;
        rows[0].GetComponent<Button>()!.Clicked += _ => clicks++;
        game.Focus(rows[0].GetComponent<Button>());
        Tick(game);

        Press(game, Key.E);
        Assert.Equal(rows[0], layout.Held);
        Press(game, Key.Down);
        Press(game, Key.Down);
        Assert.Equal(2, layout.HeldTo);
        Assert.Equal(3.5f, Y(rows[1]), 3);
        Assert.Equal(0.5f, Y(rows[0]), 3);
        Assert.Empty(_moves);

        Press(game, Key.Enter);
        Assert.Equal([(rows[0], 0, 2)], _moves);
        Assert.Equal(0, clicks);
        Assert.Null(layout.Held);
        Assert.Equal(rows[0], game.MenuSystem.Focused?.Entity);
    }

    [Fact]
    public void SaveOrderAs_KeepsTheOrderInSaveData_AndPutsRowsBackInIt()
    {
        var (game, layout, rows) = Queue();
        layout.SaveOrderAs = "queue";
        layout.ReorderMessage = "Requeued";
        var heard = new List<object?>();
        game.On("Requeued", data => heard.Add(data));

        layout.Move(rows[4], 0);
        Tick(game, 2);
        Assert.Equal("Batch 4, Batch 0, Batch 1, Batch 2, Batch 3", game.SaveData.GetString("queue"));
        Assert.Equal([rows[4]], heard);

        // Saved another way (a game loaded): the rows follow.
        game.SaveData.Set("queue", "Batch 2, Batch 1");
        Tick(game);
        Assert.Equal([rows[4], rows[0], rows[2], rows[1], rows[3]], layout.Rows);
    }

    [Fact]
    public void MoveIn_KeepsAScriptsListInTheRowsOrder()
    {
        var queue = new List<string> { "a", "b", "c", "d", "e" };
        Layout.MoveIn(queue, 3, 0);
        Assert.Equal(["d", "a", "b", "c", "e"], queue);
        Layout.MoveIn(queue, 0, 9);
        Assert.Equal(["a", "b", "c", "e", "d"], queue);
    }

    private void Press(Game game, Key key)
    {
        _input.SetKey(key, true);
        Tick(game);
        _input.SetKey(key, false);
        Tick(game);
    }
}
