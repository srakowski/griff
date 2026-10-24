using System.Diagnostics;
using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// A conveyor with a Spacing: items gliding several to a cell and piling up at a full end, splitters filtering by item
/// and alternating between two machines, a line passing under another, a full loop that keeps moving, belt art in step,
/// an out port's tray, the same line at any game speed and after a rewind, saving, and hundreds drawn cheaply.
/// </summary>
[Collection(TimingCollection.Name)]
public class ConveyorSpacingTests
{
    private const float Frame = 1 / 60f;

    // An empty floor 24 cells across and 12 down, cell (x, y) at (x + 0.5, -y - 0.5).
    private static (Game Game, Scene Scene, Conveyor Belts) Factory(Action<Scene, Conveyor>? build = null, float spacing = 0.25f)
    {
        var scene = new Scene();
        var floor = new Entity("Floor");
        floor.AddComponent<Tilemap>().Tiles = [.. Enumerable.Repeat(new string('.', 24), 12)];
        scene.Add(floor);
        var line = new Entity("Line");
        var belts = line.AddComponent<Conveyor>();
        belts.Spacing = spacing;
        scene.Add(line);
        build?.Invoke(scene, belts);
        return (new Game(scene, new InputState()), scene, belts);
    }

    private static Entity Port(Scene scene, string name, int x, int y, ConveyorPortKind kind, string? items = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(CellAt(x, y), 0);
        var port = entity.AddComponent<ConveyorPort>();
        (port.Kind, port.Items) = (kind, items);
        scene.Add(entity);
        return entity;
    }

    private static Vector2 CellAt(int x, int y) => new(x + 0.5f, -y - 0.5f);

    private static void RunFor(Game game, double seconds)
    {
        while (game.Time.Total < seconds - 1e-6)
            game.Tick(Frame);
    }

    private static Conveyor Belts(Game game) => game.Scene.FindNamed("Line")!.GetComponent<Conveyor>()!;

    // How far apart each item is from the next along a straight line running right.
    private static List<float> Gaps(Conveyor belts)
    {
        var at = belts.Positions.Select(p => p.Position.X).Order().ToList();
        return [.. at.Zip(at.Skip(1), (a, b) => b - a)];
    }

    [Fact]
    public void Items_glide_several_to_a_cell_a_spacing_apart_and_pile_up_at_a_full_end()
    {
        var (game, _, belts) = Factory((_, b) => b.LayPath([(0, 0), (5, 0)]));
        belts.Speed = 2;
        var ends = new List<string>();
        belts.ReachedEnd += (_, _, item) => ends.Add(item);

        // The first glides smoothly, two cells a second, from its cell's middle.
        Assert.True(belts.Put(0, 0, "Deck 0"));
        Assert.False(belts.Put(0, 0, "Too close"));
        RunFor(game, 0.75);
        Assert.Equal(CellAt(0, 0).X + 1.5f, belts.Positions.Single().Position.X, 3);

        var put = 1;
        for (var step = 0; step < 900; step++)
        {
            if (belts.Put(0, 0, $"Deck {put}"))
                put++;
            game.Tick(Frame);
            Assert.All(Gaps(belts), gap => Assert.True(gap >= 0.25f - 1e-4f, $"Two items closer than the spacing: {gap}"));
        }

        // Packed a quarter cell apart from the end's resting place (an eighth short of its edge) back to the first cell:
        // four to a cell, a pile you can see.
        Assert.Equal(22, belts.Count);
        Assert.Equal(22, put);
        Assert.Equal(CellAt(5, 0).X + 0.375f, belts.Positions.Max(p => p.Position.X), 3);
        Assert.All(Gaps(belts), gap => Assert.Equal(0.25f, gap, 3));
        Assert.Equal(4, belts.ItemsAt(3, 0).Count);
        Assert.All(Enumerable.Range(0, 6), x => Assert.True(belts.IsWaiting(x, 0)));
        Assert.Equal(["Deck 0"], ends);
        Assert.Equal("Deck 0", belts.ItemAt(5, 0));

        // Taking the end one off lets the pile move up a quarter cell.
        Assert.Equal("Deck 0", belts.Take(5, 0));
        RunFor(game, game.Time.Total + 0.5);
        Assert.Equal(21, belts.Count);
        Assert.Equal("Deck 1", belts.ItemAt(5, 0));
        Assert.Equal(CellAt(5, 0).X + 0.375f, belts.ItemPosition(5, 0)!.Value.X, 3);
        Assert.Equal(["Deck 0", "Deck 1"], ends);
    }

    private static void Near(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
    }

    [Fact]
    public void Items_turn_corners_along_the_belt()
    {
        var (game, _, belts) = Factory((_, b) => b.LayPath([(0, 0), (2, 0), (2, 3)]));
        belts.Speed = 1;
        belts.Put(0, 0, "Deck");

        // A cell and three quarters on, it's come into the corner from the left; then through its middle and down.
        RunFor(game, 1.75);
        var at = belts.Positions.Single();
        Assert.Equal((2, 0), (at.X, at.Y));
        Near(CellAt(2, 0) - new Vector2(0.25f, 0), at.Position);
        RunFor(game, 2.25);
        at = belts.Positions.Single();
        Assert.Equal((2, 0), (at.X, at.Y));
        Assert.Equal(CellAt(2, 0).X, at.Position.X, 3);
        Assert.Equal(CellAt(2, 0).Y - 0.25f, at.Position.Y, 3);
        RunFor(game, 3.25);
        Near(CellAt(2, 1) - new Vector2(0, 0.25f), belts.ItemPosition(2, 1)!.Value);

        // It rests at the end an eighth short of its edge.
        RunFor(game, 10);
        Assert.True(belts.IsWaiting(2, 3));
        Assert.Equal(CellAt(2, 3).Y - 0.375f, belts.ItemPosition(2, 3)!.Value.Y, 3);
    }

    // A splitter at (2, 4) going right, ahead to a port at (7, 4) and up to one at (2, 0).
    private static (Game Game, Conveyor Belts, List<string> Ahead, List<string> Up, ConveyorPort UpPort) Splitter(float spacing, Action<Scene>? more = null)
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
            more?.Invoke(scene);
        }, spacing);
        belts.Speed = 4;
        return (game, belts, ahead, up, upPort!);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    public void A_splitter_sends_items_its_filters_name_one_way_and_the_rest_the_other(float spacing)
    {
        var (game, belts, ahead, up, _) = Splitter(spacing);
        belts.Filter(2, 3, "Box");
        Assert.Equal(["2 3 Box"], belts.Filters);
        Assert.Equal("Box", belts.FilterAt(2, 3));
        var n = 0;
        for (var step = 0; step < 900; step++)
        {
            if (belts.Put(0, 4, n % 3 == 0 ? $"Box {n}" : $"Deck {n}"))
                n++;
            game.Tick(Frame);
        }

        Assert.True(up.Count > 5 && ahead.Count > 10, $"{up.Count} up, {ahead.Count} ahead");
        Assert.All(up, item => Assert.StartsWith("Box", item));
        Assert.All(ahead, item => Assert.StartsWith("Deck", item));

        belts.Filter(2, 3, null);
        Assert.Empty(belts.Filters);
    }

    [Fact]
    public void A_splitter_alternates_between_two_machines_and_fills_the_other_when_one_is_full()
    {
        // Two machines with a table of four decks each, at the ends of a splitter's two ways.
        var received = new List<string>();
        var (game, belts, _, _, _) = Splitter(0.25f, scene =>
        {
            foreach (var name in (string[])["Ahead", "Up"])
            {
                var port = scene.FindNamed(name)!.GetComponent<ConveyorPort>()!;
                port.Entity!.AddComponent<Inventory>();
                (port.Items, port.Holds) = ("Deck", 4);
                port.Received += (p, _) => received.Add(p.Entity!.Name);
            }
        });
        var n = 0;
        for (var step = 0; step < 600; step++)
        {
            if (n < 6 && belts.Put(0, 4, "Deck"))
                n++;
            game.Tick(Frame);
        }

        // The way up is a cell shorter, so its decks get there first; each machine has three.
        Assert.Equal(3, received.Count(r => r == "Ahead"));
        Assert.Equal(3, received.Count(r => r == "Up"));

        // With the up machine's table full, the rest go ahead until that's full too; then they pile up behind the splitter.
        var up = game.Scene.FindNamed("Up")!.GetComponent<Inventory>()!;
        up.Add("Deck");
        for (var step = 0; step < 900; step++)
        {
            belts.Put(0, 4, "Deck");
            game.Tick(Frame);
        }

        Assert.Equal(4, game.Scene.FindNamed("Ahead")!.GetComponent<Inventory>()!.Count("Deck"));
        Assert.Equal(4, up.Count("Deck"));
        Assert.Equal(7, received.Count);
        Assert.Equal("Ahead", received[^1]);
        Assert.True(belts.IsWaiting(0, 4));
    }

    [Fact]
    public void A_line_passes_under_another_neither_waiting_and_walkers_cross_it()
    {
        // Row 4 runs right with an under cell at (5, 4); column 5 runs down across it, through the tunnel.
        var (across, down) = (new List<string>(), new List<string>());
        var (game, _, belts) = Factory((scene, b) =>
        {
            b.LayPath([(0, 4), (9, 4)]);
            b.Lay(5, 4, 1, 0, ConveyorCellKind.Under);
            b.LayPath([(5, 0), (5, 3)], lastWay: (0, -1));
            b.LayPath([(5, 5), (5, 9)], lastWay: (0, -1));
            Port(scene, "Across", 10, 4, ConveyorPortKind.In).GetComponent<ConveyorPort>()!.Received += (_, item) => across.Add(item);
            Port(scene, "Down", 5, 10, ConveyorPortKind.In).GetComponent<ConveyorPort>()!.Received += (_, item) => down.Add(item);
        }, 0.5f);
        Assert.Contains("5 4 right under", belts.Belts);
        Assert.False(belts.BlocksWalking((5, 4)));
        Assert.True(belts.BlocksWalking((4, 4)));
        belts.Speed = 3;

        // Both fed as fast as they carry, six a second once the first item is through, as a line with nothing crossing it.
        var under = false;
        for (var step = 0; step < 600; step++)
        {
            belts.Put(0, 4, "Deck");
            belts.Put(5, 0, "Box");
            game.Tick(Frame);
            Assert.DoesNotContain("Box", belts.Positions.Where(p => p.Y == 4 && p.X != 5).Select(p => p.Item));
            under |= belts.ItemsAt(5, 4).Contains("Box") && belts.ItemsAt(5, 4).Contains("Deck");
        }

        Assert.True(under, "A box and a deck were in the under cell at once.");
        Assert.All(across, item => Assert.Equal("Deck", item));
        Assert.All(down, item => Assert.Equal("Box", item));
        Assert.InRange(across.Count, 40, 42);
        Assert.InRange(down.Count, 40, 42);

        // A box in the tunnel is under the belt: only the decks over it are drawn.
        var draws = new List<SpriteDraw>();
        IDrawable drawn = belts;
        belts.ShowBelts = false;
        drawn.Draw(new Drawing(draws, belts.Entity!, new Box(new Vector2(12, -6), new Vector2(20, 20)), 0.05f, isPlaying: true));
        Assert.Equal(belts.Count - belts.Positions.Count(p => (p.X, p.Y) == (5, 4) && p.Item == "Box"), draws.Count);
    }

    [Fact]
    public void A_full_loop_keeps_moving()
    {
        // A loop round a 3 by 3 square (8 cells), packed half a cell apart: 16 items with no room between any two.
        var (game, _, belts) = Factory((_, b) => b.LayPath([(0, 0), (2, 0), (2, 2), (0, 2), (0, 1)], lastWay: (0, 1)), 0.5f);
        var ring = belts.Cells.Select(c => c.Cell).ToList();
        Assert.Equal(8, ring.Count);
        belts.Items = [.. ring.SelectMany((c, i) => new[] { Row(c, ring[(i + 7) % 8], i * 2), Row(c, ring[(i + 1) % 8], i * 2 + 1) })];
        Assert.Equal(16, belts.Count);
        static string Row((int X, int Y) cell, (int X, int Y) toward, int n) =>
            FormattableString.Invariant($"{cell.X + (toward.X - cell.X) * 0.25f} {cell.Y + (toward.Y - cell.Y) * 0.25f} Deck {n}");

        // At a cell a second, every item is a cell on after each second.
        belts.Speed = 1;
        for (var second = 1; second <= 3; second++)
        {
            RunFor(game, second);
            Assert.Equal(16, belts.Count);
            foreach (var (x, y, item) in belts.Contents)
            {
                var n = int.Parse(item[5..], System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(ring[(n / 2 + second) % 8], (x, y));
            }

            Assert.All(belts.Cells, c => Assert.False(belts.IsWaiting(c.X, c.Y)));
        }
    }

    // The nearest any two items are, in world units.
    private static float Closest(Conveyor belts)
    {
        var at = belts.Positions.Select(p => p.Position).ToList();
        var closest = float.MaxValue;
        for (var i = 0; i < at.Count; i++)
        {
            for (var j = i + 1; j < at.Count; j++)
                closest = MathF.Min(closest, Vector2.Distance(at[i], at[j]));
        }

        return closest;
    }

    [Fact]
    public void A_loop_fed_from_the_side_fills_and_keeps_moving_and_one_jammed_at_a_splitter_stops_without_closing_up()
    {
        // A 3 by 3 loop, a belt from the left merging into its side at (3, 4).
        var (game, _, belts) = Factory(spacing: 0.5f);
        belts.LayPath([(3, 3), (5, 3), (5, 5), (3, 5), (3, 4)], lastWay: (0, 1));
        belts.LayPath([(0, 4), (2, 4)]);
        belts.Speed = 2;
        for (var step = 0; step < 900; step++)
        {
            belts.Put(0, 4, "Deck");
            game.Tick(Frame);
            Assert.True(Closest(belts) >= 0.35f, $"Two items {Closest(belts)} apart.");
        }

        // The loop is full (sixteen) and still going round; the feeding belt waits.
        Assert.Equal(16, belts.Contents.Count(c => c.X >= 3));
        Assert.All(belts.Cells.Where(c => c.X >= 3), c => Assert.False(belts.IsWaiting(c.X, c.Y)));
        Assert.True(belts.IsWaiting(2, 4));

        // A splitter on the loop sending every deck out to a belt with nothing at its end: once that's full, the loop
        // stops where it is, every item still half a cell from the next.
        belts.Lay(5, 4, 0, -1, ConveyorCellKind.Split);
        belts.LayPath([(6, 4), (7, 4)]);
        belts.Filter(6, 4, "Deck");
        for (var step = 0; step < 900; step++)
        {
            game.Tick(Frame);
            Assert.True(Closest(belts) >= 0.35f, $"Two items {Closest(belts)} apart.");
        }

        Assert.All(belts.Cells.Where(c => c.X is >= 3 and <= 5 && belts.ItemsAt(c.X, c.Y).Count > 0), c => Assert.True(belts.IsWaiting(c.X, c.Y)));
        Assert.Equal(4, belts.ItemsAt(6, 4).Count + belts.ItemsAt(7, 4).Count);
    }

    [Fact]
    public void Two_belts_merging_take_turns()
    {
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

        Assert.True(received.Count > 100, $"Only {received.Count} came through.");
        var steady = received.Skip(20).ToList();
        for (var i = 1; i < steady.Count; i++)
            Assert.NotEqual(steady[i - 1], steady[i]);
    }

    [Fact]
    public void Belt_art_moves_with_the_items_and_stops_with_the_line()
    {
        var (game, scene, belts) = Factory((_, b) => b.LayPath([(0, 0), (5, 0)]));
        var art = new Entity("Belt Art");
        var tiles = art.AddComponent<Tilemap>();
        (tiles.Tiles, tiles.AnimatedTiles) = (["......"], "1-4 @8");
        scene.Add(art);
        (belts.BeltTiles, belts.MovingArrows, belts.Speed) = ("Belt Art", true, 1);

        // Four frames to a cell: a quarter cell on is the second, three quarters the fourth, a whole cell round again.
        RunFor(game, 0.25 + 1e-3);
        Assert.Equal(2, tiles.ShownTile(1));
        RunFor(game, 0.75 + 1e-3);
        Assert.Equal(4, tiles.ShownTile(1));
        RunFor(game, 1.0 + 1e-3);
        Assert.Equal(1, tiles.ShownTile(1));
        Assert.Equal(3, tiles.ShownTile(3));

        belts.Running = false;
        RunFor(game, 1.6);
        Assert.Equal(1, tiles.ShownTile(1));

        // Without a conveyor driving it, a map plays its animated tiles by time.
        Assert.Null(scene.FindNamed("Floor")!.GetComponent<Tilemap>()!.BeltTravel);
    }

    [Fact]
    public void An_out_port_keeps_a_tray_of_items_waiting_and_is_full_at_its_holds()
    {
        var (game, scene, belts) = Factory((scene, b) => b.LayPath([(1, 2), (6, 2)]));
        var tray = Port(scene, "Tray", 0, 2, ConveyorPortKind.Out).GetComponent<ConveyorPort>()!;
        tray.Holds = 6;
        for (var i = 0; i < 6; i++)
            Assert.True(tray.Give($"Box {i}"));
        Assert.True(tray.Full);
        Assert.False(tray.Give("Box 6"));
        Assert.Equal(6, tray.Waiting.Count);

        game.Tick(Frame);
        Assert.False(tray.Full);
        Assert.True(tray.Give("Box 6"));
        RunFor(game, 10);
        Assert.Empty(tray.Waiting);
        Assert.Equal(7, belts.Count);
        Assert.Equal(4, belts.ItemsAt(6, 2).Count);
        Assert.Equal(3, belts.ItemsAt(5, 2).Count);
    }

    // A line with a merge, a splitter and ports, several items to a cell, fed and emptied with no code.
    private static Game Plant(float scale, float fastForward)
    {
        var (game, scene, belts) = Factory((scene, b) =>
        {
            b.LayPath([(0, 6), (3, 6)]);
            b.Lay(3, 6, 1, 0, ConveyorCellKind.Split);
            b.LayPath([(4, 6), (8, 6)]);
            b.LayPath([(3, 7), (3, 9)], lastWay: (0, -1));
            b.LayPath([(6, 9), (6, 7)], lastWay: (0, 1));
            var feeder = Port(scene, "Feeder", -1, 6, ConveyorPortKind.Out);
            feeder.AddComponent<Inventory>().Add("Box", 60);
            var side = Port(scene, "Side", 6, 10, ConveyorPortKind.Out);
            side.AddComponent<Inventory>().Add("Crate", 60);
            var sink = Port(scene, "Sink", 9, 6, ConveyorPortKind.In);
            sink.AddComponent<Inventory>();
            sink.GetComponent<ConveyorPort>()!.Holds = 30;
        }, 0.3f);
        belts.Speed = 2.7f;
        (game.Time.Scale, game.Time.FastForward) = (scale, fastForward);
        return game;
    }

    private static List<string> Where(Game game)
    {
        var belts = Belts(game);
        var sink = game.Scene.FindNamed("Sink")!.GetComponent<Inventory>()!;
        return [.. belts.Positions.Select(p => $"{p.X} {p.Y} {p.Item} {p.Position.X:R} {p.Position.Y:R}"),
            $"sink {sink.Count("Box")} {sink.Count("Crate")}"];
    }

    [Fact]
    public void A_line_is_in_the_same_place_after_the_same_game_time_at_1x_16x_and_fast_forward()
    {
        var normal = Plant(1, 1);
        var scaled = Plant(16, 1);
        var forward = Plant(1, 16);
        var piled = false;
        foreach (var seconds in new[] { 16 / 60.0 * 7, 16 / 60.0 * 20, 16 / 60.0 * 45, 16 / 60.0 * 90, 16 / 60.0 * 180 })
        {
            RunFor(normal, seconds);
            RunFor(scaled, seconds);
            RunFor(forward, seconds);
            Assert.Equal(normal.Time.Total, scaled.Time.Total, 9);
            var expected = Where(normal);
            Assert.True(expected.Count > 8, "The line has things on it.");
            Assert.Equal(expected, Where(scaled));
            Assert.Equal(expected, Where(forward));
            piled |= Belts(normal).Cells.Any(c => Belts(normal).ItemsAt(c.X, c.Y).Count >= 3);
        }

        Assert.True(piled, "Some cell held three items.");
    }

    [Fact]
    public void A_rewind_puts_items_back_where_they_were_several_to_a_cell_partway_along()
    {
        var game = Plant(1, 1);
        RunFor(game, 2.1);
        var belts = Belts(game);
        Assert.Contains(belts.Cells, c => belts.ItemsAt(c.X, c.Y).Count >= 2);
        var timeline = new Timeline(new SceneSerializer(ComponentRegistry.CreateDefault()), framesPerSnapshot: 1000);
        timeline.Record(game, force: true);
        var then = Where(game);
        RunFor(game, 3.7);
        var later = Where(game);
        Assert.NotEqual(then, later);

        timeline.Restore(0, game);
        Assert.Equal(then, Where(game));
        RunFor(game, 3.7);
        Assert.Equal(later, Where(game));
    }

    [Fact]
    public void Spacing_filters_unders_and_items_between_middles_are_saved_with_the_scene()
    {
        var (_, scene, belts) = Factory((_, b) =>
        {
            b.LayPath([(2, 4), (5, 4), (5, 6)]);
            b.Lay(3, 4, 1, 0, ConveyorCellKind.Under);
            b.Lay(4, 4, 1, 0, ConveyorCellKind.Split);
            b.Filter(4, 3, "Deck");
        });
        belts.Items = ["2.25 4 Deck 1", "2 4 Deck 2", "4.375 4 Box", "5 4.25 Card"];
        Assert.Equal(["2.25 4 Deck 1", "2 4 Deck 2", "4.375 4 Box", "5 4.25 Card"], belts.Items);
        Assert.Equal(["Deck 1", "Deck 2"], belts.ItemsAt(2, 4));

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var loaded = serializer.LoadFromString(serializer.SaveToString(scene)).FindNamed("Line")!.GetComponent<Conveyor>()!;
        Assert.Equal(0.25f, loaded.Spacing);
        Assert.Equal(belts.Belts, loaded.Belts);
        Assert.Contains("3 4 right under", loaded.Belts);
        Assert.Equal(["4 3 Deck"], loaded.Filters);
        Assert.Equal(belts.Items, loaded.Items);

        // Read in any order (the items, then the belts, then the spacing), several to a cell are kept.
        var other = new Conveyor { Items = ["1 1 Card", "1.25 1 Deck", "0 1.3 Box"], Belts = ["1 1 right", "0 1 down", "2 1 right"], Spacing = 0.25f };
        Assert.Equal(["Deck", "Card"], other.ItemsAt(1, 1));
        Assert.Equal(["Box"], other.ItemsAt(0, 1));

        // A scene from before, with one item a cell, reads as it did.
        var before = new Conveyor { Belts = ["1 1 right", "2 1 right"], Items = ["1 1 Card", "2 1 Deck"] };
        Assert.Equal(0, before.Spacing);
        Assert.Equal(["1 1 Card", "2 1 Deck"], before.Items);
    }

    [Fact]
    public void Hundreds_of_items_in_a_full_loop_are_one_quad_each_and_move_cheaply()
    {
        // A loop of 480 cells round a 121 by 121 square, packed half a cell apart: 960 items, every one moving.
        var (game, _, belts) = Factory(spacing: 0.5f);
        belts.LayPath([(0, 0), (120, 0), (120, 120), (0, 120), (0, 1)], lastWay: (0, 1));
        var ring = belts.Cells.Select(c => c.Cell).ToList();
        Assert.Equal(480, ring.Count);
        var rows = new List<string>();
        for (var i = 0; i < ring.Count; i++)
        {
            foreach (var toward in new[] { ring[(i + ring.Count - 1) % ring.Count], ring[(i + 1) % ring.Count] })
                rows.Add(FormattableString.Invariant($"{ring[i].X + (toward.X - ring[i].X) * 0.25f} {ring[i].Y + (toward.Y - ring[i].Y) * 0.25f} Deck"));
        }

        belts.Items = rows;
        Assert.Equal(960, belts.Count);
        belts.ShowBelts = false;
        game.Tick(Frame);
        var draws = new List<SpriteDraw>();
        IDrawable drawn = belts;
        drawn.Draw(new Drawing(draws, belts.Entity!, new Box(new Vector2(60, -60), new Vector2(100, 100)), 0.05f, isPlaying: true));
        Assert.Equal(960, draws.Count);
        Assert.Single(draws.Select(d => d.Image).Distinct());

        var first = belts.Positions[0].Position;
        // The fastest of six runs of 100 steps, so other tests' threads and garbage collections don't count against it.
        var fastest = double.MaxValue;
        for (var run = 0; run < 6; run++)
        {
            var clock = Stopwatch.StartNew();
            for (var step = 0; step < 100; step++)
                game.Tick(Frame);
            fastest = Math.Min(fastest, clock.Elapsed.TotalMilliseconds / 100);
        }

        Assert.Equal(960, belts.Count);
        Assert.All(belts.Cells.Take(20), c => Assert.False(belts.IsWaiting(c.X, c.Y)));
        Assert.True(fastest < 3, $"A step took {fastest:F3} ms.");
        Assert.NotEqual(first, belts.Positions[0].Position);
    }
}
