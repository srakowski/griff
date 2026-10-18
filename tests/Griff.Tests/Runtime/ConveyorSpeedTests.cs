using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// Conveyor cells with speeds of their own: a slow run feeding a fast one with no pause at the join (one item a cell and
/// with a Spacing), speeds in the Belts rows and from a script, a run's speed set at once, belt art stepping at each
/// cell's speed, and the same line at any game speed and after a rewind.
/// </summary>
public class ConveyorSpeedTests
{
    private const float Frame = 1 / 60f;

    // An empty floor 24 cells across and 12 down, cell (x, y) at (x + 0.5, -y - 0.5).
    private static (Game Game, Scene Scene, Conveyor Belts) Factory(Action<Scene, Conveyor>? build = null, float spacing = 0)
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
        entity.Transform.Position = new Vector3(x + 0.5f, -y - 0.5f, 0);
        var port = entity.AddComponent<ConveyorPort>();
        (port.Kind, port.Items) = (kind, items);
        scene.Add(entity);
        return entity;
    }

    private static void RunFor(Game game, double seconds)
    {
        while (game.Time.Total < seconds - 1e-6)
            game.Tick(Frame);
    }

    private static Conveyor Belts(Game game) => game.Scene.FindNamed("Line")!.GetComponent<Conveyor>()!;

    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    public void A_slow_run_feeds_a_fast_one_at_half_its_speed_with_no_pause_at_the_join(float spacing)
    {
        // Three cells at a cell a second, then five at two.
        var (game, _, belts) = Factory((_, b) => b.Belts =
            ["0 0 right 1", "1 0 right 1", "2 0 right 1", "3 0 right 2", "4 0 right 2", "5 0 right 2", "6 0 right 2", "7 0 right 2"], spacing);
        belts.Speed = 5;
        Assert.Equal(1, belts.SpeedAt(1, 0));
        Assert.Equal(2, belts.SpeedAt(6, 0));
        Assert.True(belts.Put(0, 0, "Deck"));
        float X() => belts.Positions.Single().Position.X;

        // Along the slow run a cell a second, to the edge between the runs, then the fast run's two.
        RunFor(game, 1);
        Assert.Equal(1.5f, X(), 3);
        RunFor(game, 2.5);
        Assert.Equal(3f, X(), 3);
        RunFor(game, 2.75);
        Assert.Equal(3.5f, X(), 3);
        RunFor(game, 3.75);
        Assert.Equal(5.5f, X(), 3);

        // Every frame it moves on at one speed or the other (or between, the frame it crosses): never waiting at the join.
        var (game2, _, belts2) = Factory((_, b) => b.LayPath([(0, 0), (2, 0)], speed: 1), spacing);
        belts2.LayPath([(3, 0), (7, 0)], speed: 2);
        Assert.Equal(["0 0 right 1", "1 0 right 1", "2 0 right 1", "3 0 right 2", "4 0 right 2", "5 0 right 2", "6 0 right 2", "7 0 right 2"],
            belts2.Belts);
        belts2.Put(0, 0, "Deck");
        var end = spacing > 0 ? 7 + 1 - spacing / 2 : 7.5f;
        var was = belts2.Positions.Single().Position.X;
        var frames = 0;
        while (was < end - 1e-3f)
        {
            game2.Tick(Frame);
            var now = belts2.Positions.Single().Position.X;
            // (The last frame stops it partway, at its resting place.)
            Assert.InRange(now - was, now < end - 1e-3f ? 1 / 60f - 1e-4f : 1e-4f, 2 / 60f + 1e-4f);
            (was, frames) = (now, frames + 1);
        }

        // 2.5 cells at one a second, then the rest at two.
        Assert.InRange(frames, (int)((2.5 + (end - 3) / 2) * 60) - 1, (int)((2.5 + (end - 3) / 2) * 60) + 1);
    }

    [Fact]
    public void Speeds_read_and_write_in_the_belts_rows_and_from_a_script()
    {
        var (_, _, belts) = Factory();
        belts.Belts = ["0 0 right 0.5", "1 0 right split @3", "2 0 right", "3 0 > crossing 1.25", "4 0 right -2"];
        Assert.Equal(["0 0 right 0.5", "1 0 right split 3", "2 0 right", "3 0 right crossing 1.25", "4 0 right"], belts.Belts);
        Assert.Equal(ConveyorCellKind.Split, belts.CellAt(1, 0)!.Value.Kind);
        Assert.Equal(3, belts.CellAt(1, 0)!.Value.Speed);
        Assert.Equal(0, belts.CellAt(2, 0)!.Value.Speed);
        Assert.Equal(belts.Speed, belts.SpeedAt(2, 0));
        Assert.Equal(0, belts.SpeedAt(9, 9));

        // Turning a cell keeps its speed; setting one gives it one, and 0 takes it off.
        belts.Lay(0, 0, 0, -1);
        Assert.Equal(0.5f, belts.CellAt(0, 0)!.Value.Speed);
        Assert.True(belts.SetSpeed(2, 0, 4));
        Assert.Equal(4, belts.SpeedAt(2, 0));
        Assert.True(belts.SetSpeed(2, 0, 0));
        Assert.Equal("2 0 right", belts.Belts[2]);
        Assert.False(belts.SetSpeed(9, 9, 1));
    }

    [Fact]
    public void A_runs_speed_is_set_up_to_a_merge_or_a_splitter()
    {
        // A chute down column 5 dropping onto the side of a belt along row 6, which ends at a splitter.
        var (_, _, belts) = Factory((_, b) =>
        {
            b.LayPath([(5, 2), (5, 5)], lastWay: (0, -1));
            b.LayPath([(0, 6), (9, 6)]);
            b.Lay(9, 6, 1, 0, ConveyorCellKind.Split);
            b.LayPath([(10, 6), (12, 6)]);
        });

        Assert.Equal(4, belts.SetRunSpeed(5, 3, 0.5f));
        Assert.Equal([(5, 2), (5, 3), (5, 4), (5, 5)], belts.RunThrough(5, 4));
        Assert.All(Enumerable.Range(2, 4), y => Assert.Equal(0.5f, belts.SpeedAt(5, y)));

        // The belt the chute drops onto: up to the cell it merges into, then from there to the splitter.
        Assert.Equal([(0, 6), (1, 6), (2, 6), (3, 6), (4, 6)], belts.RunThrough(2, 6));
        Assert.Equal([(5, 6), (6, 6), (7, 6), (8, 6)], belts.RunThrough(7, 6));
        Assert.Equal(4, belts.SetRunSpeed(7, 6, 3));
        Assert.Equal(belts.Speed, belts.SpeedAt(4, 6));
        Assert.Equal(3, belts.SpeedAt(5, 6));
        Assert.Equal(belts.Speed, belts.SpeedAt(9, 6));

        // A run is cells of one speed: the fast stretch stops it.
        Assert.Equal([(0, 6), (1, 6), (2, 6), (3, 6), (4, 6)], belts.RunThrough(0, 6));
        belts.SetSpeed(2, 6, 1);
        Assert.Equal([(2, 6)], belts.RunThrough(2, 6));
    }

    [Fact]
    public void Belt_art_steps_at_each_cells_own_speed()
    {
        var (game, scene, belts) = Factory((_, b) => b.LayPath([(0, 0), (5, 0)]));
        belts.SetSpeed([(3, 0), (4, 0), (5, 0)], 2);
        var art = new Entity("Belt Art");
        var tiles = art.AddComponent<Tilemap>();
        (tiles.Tiles, tiles.AnimatedTiles) = (["......"], "1-4 @8");
        scene.Add(art);
        (belts.BeltTiles, belts.MovingArrows, belts.Speed) = ("Belt Art", true, 1);

        // Four frames to a cell: a quarter cell on the slow cells show the second, half a cell on the fast ones the third.
        RunFor(game, 0.25 + 1e-3);
        Assert.Equal(2, tiles.ShownTile(1, 0, 1));
        Assert.Equal(3, tiles.ShownTile(4, 0, 1));
        RunFor(game, 0.5 + 1e-3);
        Assert.Equal(3, tiles.ShownTile(1, 0, 1));
        Assert.Equal(1, tiles.ShownTile(4, 0, 1));

        // All at one speed again, the map steps as one.
        belts.SetSpeed([(3, 0), (4, 0), (5, 0)], 0);
        game.Tick(Frame);
        Assert.Null(tiles.BeltTravels);
    }

    // A slow chute dropping onto the side of a fast belt with a splitter on it, fed and emptied by ports.
    private static Game Plant(float spacing, float scale, float fastForward)
    {
        var (game, _, belts) = Factory((scene, b) =>
        {
            b.LayPath([(0, 6), (3, 6)], speed: 3.1f);
            b.Lay(3, 6, 1, 0, ConveyorCellKind.Split);
            b.LayPath([(4, 6), (8, 6)]);
            b.LayPath([(3, 7), (3, 9)], lastWay: (0, -1), speed: 1.3f);
            b.LayPath([(6, 1), (6, 5)], lastWay: (0, -1), speed: 0.7f);
            Port(scene, "Feeder", -1, 6, ConveyorPortKind.Out).AddComponent<Inventory>().Add("Box", 60);
            Port(scene, "Chute", 6, 0, ConveyorPortKind.Out).AddComponent<Inventory>().Add("Crate", 60);
            var sink = Port(scene, "Sink", 9, 6, ConveyorPortKind.In);
            sink.AddComponent<Inventory>();
            sink.GetComponent<ConveyorPort>()!.Holds = 30;
        }, spacing);
        belts.Speed = 2.2f;
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

    [Theory]
    [InlineData(0f)]
    [InlineData(0.3f)]
    public void A_line_of_several_speeds_is_in_the_same_place_at_1x_16x_and_fast_forward(float spacing)
    {
        var normal = Plant(spacing, 1, 1);
        var scaled = Plant(spacing, 16, 1);
        var forward = Plant(spacing, 1, 16);
        foreach (var seconds in new[] { 16 / 60.0 * 7, 16 / 60.0 * 20, 16 / 60.0 * 45, 16 / 60.0 * 90, 16 / 60.0 * 180 })
        {
            RunFor(normal, seconds);
            RunFor(scaled, seconds);
            RunFor(forward, seconds);
            var expected = Where(normal);
            Assert.True(expected.Count > 5, "The line has things on it.");
            Assert.Equal(expected, Where(scaled));
            Assert.Equal(expected, Where(forward));
        }

        Assert.Contains(Where(normal), row => row.StartsWith("6 ", StringComparison.Ordinal) && row.Contains("Crate"));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.3f)]
    public void A_rewind_puts_items_back_partway_along_cells_of_several_speeds(float spacing)
    {
        var game = Plant(spacing, 1, 1);
        RunFor(game, 2.1);
        var timeline = new Timeline(new SceneSerializer(ComponentRegistry.CreateDefault()), framesPerSnapshot: 1000);
        timeline.Record(game, force: true);
        var then = Where(game);
        RunFor(game, 3.7);
        var later = Where(game);
        Assert.NotEqual(then, later);

        timeline.Restore(0, game);
        Assert.Equal(then, Where(game));
        Assert.Equal(0.7f, Belts(game).SpeedAt(6, 3));
        RunFor(game, 3.7);
        Assert.Equal(later, Where(game));
    }
}
