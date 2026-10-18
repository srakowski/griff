using System.Diagnostics;
using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// Conveyors: belts on a grid carrying items a cell at a time with back-pressure, merges taking turns, splitters going
/// round, ports into and out of machines, the same line at any game speed and after a rewind, belts as walls to grid
/// movers (crossings not), laying them from a script, and hundreds of items drawn and moved cheaply.
/// </summary>
public class ConveyorTests
{
    private const float Frame = 1 / 60f;

    // An empty floor 24 cells across and 12 down, cell (x, y) at (x + 0.5, -y - 0.5).
    private static (Game Game, Scene Scene, Conveyor Belts) Factory(Action<Scene, Conveyor>? build = null)
    {
        var scene = new Scene();
        var floor = new Entity("Floor");
        floor.AddComponent<Tilemap>().Tiles = [.. Enumerable.Repeat(new string('.', 24), 12)];
        scene.Add(floor);
        var line = new Entity("Line");
        var belts = line.AddComponent<Conveyor>();
        scene.Add(line);
        build?.Invoke(scene, belts);
        return (new Game(scene, new InputState()), scene, belts);
    }

    private static Entity Port(Scene scene, string name, int x, int y, ConveyorPortKind kind, string? items = null, Entity? inside = null)
    {
        var entity = new Entity(name);
        var parentAt = inside is null ? Vector2.Zero : new Vector2(inside.Transform.Position.X, inside.Transform.Position.Y);
        entity.Transform.Position = new Vector3(CellAt(x, y) - parentAt, 0);
        var port = entity.AddComponent<ConveyorPort>();
        (port.Kind, port.Items) = (kind, items);
        if (inside is null)
            scene.Add(entity);
        else
            inside.AddChild(entity);
        return entity;
    }

    private static Vector2 CellAt(int x, int y) => new(x + 0.5f, -y - 0.5f);

    private static void RunFor(Game game, double seconds)
    {
        while (game.Time.Total < seconds - 1e-6)
            game.Tick(Frame);
    }

    private static Conveyor Belts(Game game) => game.Scene.FindNamed("Line")!.GetComponent<Conveyor>()!;

    [Fact]
    public void An_item_glides_a_cell_at_a_time_at_its_speed_and_waits_at_the_end()
    {
        var (game, _, belts) = Factory((_, b) => b.LayPath([(0, 0), (9, 0)]));
        belts.Speed = 2;
        var ends = new List<((int X, int Y) Cell, string Item)>();
        belts.ReachedEnd += (_, cell, item) => ends.Add((cell, item));
        Assert.True(belts.Put(0, 0, "Deck"));
        Assert.False(belts.Put(0, 0, "Card"));

        // Half a second a cell: 2 s in, it's four cells along, gliding into the fifth.
        RunFor(game, 2.25);
        Assert.Equal("Deck", belts.ItemAt(5, 0));
        Assert.Equal(CellAt(4, 0).X + 0.5f, belts.ItemPosition(5, 0)!.Value.X, 3);

        RunFor(game, 4.5);
        Assert.Equal("Deck", belts.ItemAt(9, 0));
        Assert.True(belts.IsWaiting(9, 0));
        Assert.Equal([((9, 0), "Deck")], ends);
        RunFor(game, 6);
        Assert.Equal("Deck", belts.ItemAt(9, 0));
        Assert.Single(ends);
        Assert.Equal(["9 0 Deck"], belts.Items);
    }

    [Fact]
    public void A_full_end_backs_the_line_up_one_item_a_cell_and_items_never_close_up()
    {
        var (game, _, belts) = Factory((_, b) => b.LayPath([(0, 0), (5, 0)]));
        belts.Speed = 3;
        var put = 0;
        for (var step = 0; step < 600; step++)
        {
            if (belts.Put(0, 0, $"Deck {put}"))
                put++;
            game.Tick(Frame);
            var at = belts.Contents.Select(c => belts.ItemPosition(c.X, c.Y)!.Value.X).Order().ToList();
            for (var i = 1; i < at.Count; i++)
                Assert.True(at[i] - at[i - 1] >= 1 - 1e-4f, $"Two items closer than a cell: {at[i - 1]} and {at[i]}");
        }

        // Six cells, six items, all waiting, the first at the end; nothing more fits.
        Assert.Equal(6, put);
        Assert.Equal(6, belts.Count);
        Assert.Equal("Deck 0", belts.ItemAt(5, 0));
        Assert.All(Enumerable.Range(0, 6), x => Assert.True(belts.IsWaiting(x, 0)));

        // Taking the end one off lets the whole line move up a cell.
        belts.Take(5, 0);
        RunFor(game, game.Time.Total + 0.5);
        Assert.Equal("Deck 1", belts.ItemAt(5, 0));
        Assert.Null(belts.ItemAt(0, 0));
    }

    [Fact]
    public void Two_belts_merging_take_turns()
    {
        // A main line along row 0 and a side belt coming up column 3 into its side, both kept full, into a port that
        // takes everything at the end.
        var received = new List<string>();
        var (game, _, belts) = Factory((scene, b) =>
        {
            b.LayPath([(0, 0), (8, 0)]);
            b.LayPath([(3, 4), (3, 1)], lastWay: (0, 1));
            Port(scene, "Sink", 9, 0, ConveyorPortKind.In).GetComponent<ConveyorPort>()!.Received += (_, item) => received.Add(item);
        });
        belts.Speed = 4;
        for (var step = 0; step < 900; step++)
        {
            belts.Put(0, 0, "Main");
            belts.Put(3, 4, "Side");
            game.Tick(Frame);
        }

        Assert.True(received.Count > 30, $"Only {received.Count} came through.");
        var steady = received.Skip(4).ToList();
        for (var i = 1; i < steady.Count; i++)
            Assert.NotEqual(steady[i - 1], steady[i]);
    }

    [Fact]
    public void A_splitter_hands_items_to_its_belts_in_turn_and_passes_over_a_full_one()
    {
        var (ahead, up) = (new List<string>(), new List<string>());
        ConveyorPort? upPort = null;
        var (game, _, belts) = Factory((scene, b) =>
        {
            b.LayPath([(0, 4), (2, 4)]);
            b.Lay(2, 4, 1, 0, ConveyorCellKind.Split);
            b.LayPath([(3, 4), (6, 4)]);
            b.LayPath([(2, 3), (2, 1)], lastWay: (0, 1));
            Port(scene, "Ahead", 7, 4, ConveyorPortKind.In).GetComponent<ConveyorPort>()!.Received += (_, item) => ahead.Add(item);
            upPort = Port(scene, "Up", 2, 0, ConveyorPortKind.In).GetComponent<ConveyorPort>();
            upPort!.Received += (_, item) => up.Add(item);
        });
        Assert.Equal(["0 4 right", "1 4 right", "2 4 right split", "3 4 right", "4 4 right", "5 4 right", "6 4 right", "2 3 up", "2 2 up", "2 1 up"],
            belts.Belts);
        belts.Speed = 4;
        var n = 0;
        for (var step = 0; step < 600; step++)
        {
            if (belts.Put(0, 4, $"Deck {n}"))
                n++;
            game.Tick(Frame);
        }

        Assert.True(ahead.Count > 10);
        Assert.InRange(ahead.Count - up.Count, -1, 1);

        // With the way up shut, everything goes ahead.
        upPort!.Open = false;
        var (aheadBefore, upBefore) = (ahead.Count, up.Count);
        for (var step = 0; step < 600; step++)
        {
            belts.Put(0, 4, "Deck");
            game.Tick(Frame);
        }

        Assert.Equal(upBefore, up.Count);
        Assert.True(ahead.Count - aheadBefore > 20, $"Only {ahead.Count - aheadBefore} went ahead.");
        Assert.True(upPort.Blocked);
    }

    // A source machine with an inventory putting what it holds on a belt, and a machine taking decks off its end.
    private static (Game Game, Conveyor Belts, Inventory Source, Inventory Machine, ConveyorPort In) Machines(Action<Scene, Conveyor>? more = null)
    {
        Inventory? source = null, machine = null;
        ConveyorPort? input = null;
        var (game, _, belts) = Factory((scene, b) =>
        {
            b.LayPath([(0, 2), (4, 2)]);
            var maker = Port(scene, "Maker", -1, 2, ConveyorPortKind.Out);
            source = maker.AddComponent<Inventory>();
            source.Add("Deck", 5);
            source.Add("Card");
            var reader = new Entity("Reader");
            reader.Transform.Position = new Vector3(CellAt(5, 2), 0);
            machine = reader.AddComponent<Inventory>();
            input = Port(scene, "Reader In", 5, 2, ConveyorPortKind.In, "Deck", inside: reader).GetComponent<ConveyorPort>();
            scene.Add(reader);
            input!.Holds = 2;
            more?.Invoke(scene, b);
        });
        belts.Speed = 4;
        return (game, belts, source!, machine!, input!);
    }

    [Fact]
    public void Ports_put_what_a_machine_holds_on_a_belt_and_take_into_the_next_until_it_holds_enough()
    {
        var (game, belts, source, machine, input) = Machines();
        var ends = new List<string>();
        belts.ReachedEnd += (_, _, item) => ends.Add(item);
        RunFor(game, 6);

        // The reader holds its two decks; the other three and the card wait on the belt behind, the card last.
        Assert.Equal(0, source.Count("Deck"));
        Assert.Equal(0, source.Count("Card"));
        Assert.Equal(2, machine.Count("Deck"));
        Assert.Equal(["1 2 Card", "2 2 Deck", "3 2 Deck", "4 2 Deck"], belts.Items.Order());
        Assert.True(input.Blocked);

        // As the reader uses its decks, it takes the rest, and the card, the wrong item, waits at the end.
        machine.Take("Deck", 2);
        RunFor(game, 9);
        Assert.Equal(2, machine.Count("Deck"));
        machine.Take("Deck", 2);
        RunFor(game, 12);
        Assert.Equal(1, machine.Count("Deck"));
        Assert.Equal(["4 2 Card"], belts.Items);
        Assert.Equal("Card", ends[^1]);
        Assert.True(input.Blocked);

        // A worker carries it off by hand.
        Assert.Equal("Card", belts.Take(4, 2));
        game.Tick(Frame);
        Assert.False(input.Blocked);
    }

    [Fact]
    public void A_script_hands_an_out_port_items_and_hears_what_an_in_port_took()
    {
        var (game, _, belts) = Factory((scene, b) =>
        {
            b.LayPath([(2, 2), (6, 2)]);
            Port(scene, "Out", 2, 2, ConveyorPortKind.Out);
            Port(scene, "In", 7, 2, ConveyorPortKind.In).GetComponent<ConveyorPort>()!.Message = "Arrived";
        });
        belts.Speed = 5;
        var output = game.Scene.FindNamed("Out")!.GetComponent<ConveyorPort>()!;
        var input = game.Scene.FindNamed("In")!.GetComponent<ConveyorPort>()!;
        var (sent, took, heard) = (new List<string>(), new List<string>(), new List<object?>());
        output.Sent += (_, item) => sent.Add(item);
        input.Received += (_, item) => took.Add(item);
        game.On("Arrived", data => heard.Add(data));
        input.Accepts = item => item != "Bad";
        output.Give("Report 1");
        output.Give("Report 2");
        output.Give("Bad");
        Assert.Equal(3, output.Waiting.Count);
        RunFor(game, 4);

        Assert.Equal(["Report 1", "Report 2", "Bad"], sent);
        Assert.Equal(["Report 1", "Report 2"], took);
        Assert.Equal(["Report 1", "Report 2"], heard);
        Assert.Equal("Bad", belts.ItemAt(6, 2));
        Assert.Empty(output.Waiting);
    }

    // A line with a merge, a splitter and ports, fed and emptied with no code, so only game time moves it.
    private static Game Plant(float scale, float fastForward)
    {
        var (game, belts, _, _, _) = Machines((scene, b) =>
        {
            b.LayPath([(0, 6), (3, 6)]);
            b.Lay(3, 6, 1, 0, ConveyorCellKind.Split);
            b.LayPath([(4, 6), (8, 6)]);
            b.LayPath([(3, 7), (3, 9)], lastWay: (0, -1));
            b.LayPath([(6, 9), (6, 7)], lastWay: (0, 1));
            var feeder = Port(scene, "Feeder", -1, 6, ConveyorPortKind.Out);
            feeder.AddComponent<Inventory>().Add("Box", 40);
            var side = Port(scene, "Side", 6, 10, ConveyorPortKind.Out);
            side.AddComponent<Inventory>().Add("Crate", 40);
            var sink = Port(scene, "Sink", 9, 6, ConveyorPortKind.In);
            sink.AddComponent<Inventory>();
            sink.GetComponent<ConveyorPort>()!.Holds = 9;
        });
        belts.Speed = 2.7f;
        (game.Time.Scale, game.Time.FastForward) = (scale, fastForward);
        return game;
    }

    private static List<string> Where(Game game)
    {
        var belts = Belts(game);
        var sink = game.Scene.FindNamed("Sink")!.GetComponent<Inventory>()!;
        return [.. belts.Contents.Select(c => $"{c.X} {c.Y} {c.Item} {belts.ItemPosition(c.X, c.Y)!.Value.X:R} {belts.ItemPosition(c.X, c.Y)!.Value.Y:R}"),
            $"sink {sink.Count("Box")} {sink.Count("Crate")}"];
    }

    [Fact]
    public void A_line_is_in_the_same_place_after_the_same_game_time_at_1x_16x_and_fast_forward()
    {
        var normal = Plant(1, 1);
        var scaled = Plant(16, 1);
        var forward = Plant(1, 16);
        foreach (var seconds in new[] { 16 / 60.0 * 7, 16 / 60.0 * 20, 16 / 60.0 * 45, 16 / 60.0 * 90 })
        {
            RunFor(normal, seconds);
            RunFor(scaled, seconds);
            RunFor(forward, seconds);
            Assert.Equal(normal.Time.Total, scaled.Time.Total, 9);
            var expected = Where(normal);
            Assert.True(expected.Count > 5, "The line has things on it.");
            Assert.Equal(expected, Where(scaled));
            Assert.Equal(expected, Where(forward));
        }
    }

    [Fact]
    public void A_rewind_puts_items_back_where_they_were_partway_between_cells()
    {
        var game = Plant(1, 1);
        RunFor(game, 3.1);
        var timeline = new Timeline(new SceneSerializer(ComponentRegistry.CreateDefault()), framesPerSnapshot: 1000);
        timeline.Record(game, force: true);
        var then = Where(game);
        RunFor(game, 6);
        var later = Where(game);
        Assert.NotEqual(then, later);

        timeline.Restore(0, game);
        Assert.Equal(then, Where(game));
        RunFor(game, 6);
        Assert.Equal(later, Where(game));
    }

    [Fact]
    public void Belts_and_items_are_saved_with_the_scene()
    {
        var (_, scene, belts) = Factory((_, b) =>
        {
            b.LayPath([(2, 4), (5, 4), (5, 6)]);
            b.Lay(3, 4, 1, 0, ConveyorCellKind.Crossing);
        });
        belts.Put(4, 4, "Deck 7");
        Assert.Equal(["2 4 right", "3 4 right crossing", "4 4 right", "5 4 down", "5 5 down", "5 6 down"], belts.Belts);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var loaded = serializer.LoadFromString(serializer.SaveToString(scene)).FindNamed("Line")!.GetComponent<Conveyor>()!;
        Assert.Equal(belts.Belts, loaded.Belts);
        Assert.Equal(["4 4 Deck 7"], loaded.Items);
        Assert.Equal("Deck 7", loaded.ItemAt(4, 4));

        // Items read before their belts are laid still land on them; a row with no belt under it is left out.
        var other = new Conveyor { Items = ["1 1 Card", "9 9 Lost"], Belts = ["1 1 left", "0 1 v"] };
        Assert.Equal("Card", other.ItemAt(1, 1));
        Assert.Equal(new ConveyorCell(0, 1, 0, -1, ConveyorCellKind.Belt), other.CellAt(0, 1));
        Assert.Equal("Card", other.Remove(1, 1));
        Assert.Null(other.CellAt(1, 1));
    }

    // A worker on the left of a belt running down column 4 (rows 0 to 3; row 4 is open), walking to the right of it.
    private static (Game Game, GridMovement Worker, HashSet<(int X, int Y)> Visited) Walk(Action<Conveyor> lay)
    {
        var (game, scene, belts) = Factory((scene, b) =>
        {
            b.LayPath([(4, 0), (4, 3)]);
            lay(b);
        });
        scene.FindNamed("Floor")!.GetComponent<Tilemap>()!.Tiles = [.. Enumerable.Repeat(new string('.', 9), 5)];
        var entity = new Entity("Worker");
        entity.Transform.Position = new Vector3(CellAt(1, 1), 0);
        entity.AddComponent<BoxCollider>().Size = new Vector2(0.8f);
        entity.AddComponent<Body>().GravityScale = 0;
        var worker = entity.AddComponent<GridMovement>();
        (worker.StepTime, worker.Controllable) = (0.1f, false);
        scene.Add(entity);
        var visited = new HashSet<(int X, int Y)>();
        worker.WalkTo((7, 1));
        for (var step = 0; step < 600 && worker.Cell != (7, 1); step++)
        {
            game.Tick(Frame);
            visited.Add(worker.Cell);
        }

        Assert.Equal((7, 1), worker.Cell);
        return (game, worker, visited);
    }

    [Fact]
    public void Grid_movers_walk_round_solid_belts_and_over_a_crossing()
    {
        var (_, _, round) = Walk(_ => { });
        Assert.DoesNotContain(round, c => c.X == 4 && c.Y < 4);
        Assert.Contains((4, 4), round);

        var (_, _, across) = Walk(b => b.Lay(4, 1, 0, -1, ConveyorCellKind.Crossing));
        Assert.Contains((4, 1), across);
        Assert.DoesNotContain((4, 4), across);

        var (_, _, over) = Walk(b => b.Solid = false);
        Assert.DoesNotContain((4, 4), over);
    }

    [Fact]
    public void A_belt_blocks_only_movers_that_its_layer_blocks()
    {
        var (_, scene, belts) = Factory((_, b) => b.LayPath([(4, 0), (4, 3)]));
        var mover = new Entity("Worker").AddComponent<GridMovement>();
        scene.Add(mover.Entity!);
        var grid = GridCells.Of(scene.FindNamed("Floor")!.GetComponent<Tilemap>());
        Assert.True(GridWalls.Gather(scene, mover, grid).IsWall((4, 2)));
        mover.BlockingLayers = "Obstacle";
        Assert.False(GridWalls.Gather(scene, mover, grid).IsWall((4, 2)));
        belts.Layer = "Obstacle";
        Assert.True(GridWalls.Gather(scene, mover, grid).IsWall((4, 2)));
        Assert.False(GridWalls.Gather(scene, mover, grid).IsWall((5, 2)));
    }

    [Fact]
    public void Hundreds_of_items_are_one_quad_each_and_move_cheaply()
    {
        // A loop of 480 cells round a 121 by 121 square, every other cell full.
        var (game, scene, belts) = Factory();
        belts.LayPath([(0, 0), (120, 0), (120, 120), (0, 120), (0, 1)], lastWay: (0, 1));
        Assert.Equal(480, belts.Cells.Count);
        var n = 0;
        foreach (var cell in belts.Cells)
        {
            if (n++ % 2 == 0)
                belts.Put(cell.X, cell.Y, "Deck");
        }

        Assert.Equal(240, belts.Count);
        belts.ShowBelts = false;
        game.Tick(Frame);
        var draws = new List<SpriteDraw>();
        IDrawable drawn = belts;
        drawn.Draw(new Drawing(draws, belts.Entity!, new Box(new Vector2(60, -60), new Vector2(100, 100)), 0.05f, isPlaying: true));
        Assert.Equal(240, draws.Count);

        var clock = Stopwatch.StartNew();
        for (var step = 0; step < 600; step++)
            game.Tick(Frame);
        Assert.Equal(240, belts.Count);
        Assert.True(clock.Elapsed.TotalMilliseconds / 600 < 2, $"A step took {clock.Elapsed.TotalMilliseconds / 600:F3} ms.");
    }
}
