using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// Speeding a game up: Time.FastForward takes more steps of the usual length, so a sim plays out step for step as it does
/// at normal speed; a long step (a high Time.Scale) still walks a grid mover every cell it covers; and GameSpeed sets
/// either with no code.
/// </summary>
public class FastForwardTests
{
    private const float Frame = 1 / 60f;

    // A corridor 13 cells along and 12 down, round a corner: 25 cells from (1, 1) to (14, 13).
    private static readonly string[] Corridor =
    [
        "1111111111111111",
        "1..............1",
        .. Enumerable.Repeat("11111111111111.1", 12),
        "1111111111111111",
    ];

    private static (Game Game, InputState Input, GridMovement Worker) Walker(string[] rows, float stepTime = 0.1f)
    {
        var (scene, input) = (new Scene(), new InputState());
        var map = new Entity("Level");
        map.AddComponent<Tilemap>().Tiles = rows;
        scene.Add(map);
        var entity = new Entity("Worker");
        entity.Transform.Position = new Vector3(CellAt(1, 1), 0);
        entity.AddComponent<BoxCollider>().Size = new Vector2(0.8f);
        entity.AddComponent<Body>().GravityScale = 0;
        var worker = entity.AddComponent<GridMovement>();
        (worker.StepTime, worker.Controllable) = (stepTime, false);
        scene.Add(entity);
        return (new Game(scene, input), input, worker);
    }

    // Cell (x, y) is at (x + 0.5, -y - 0.5).
    private static Vector2 CellAt(int x, int y) => new(x + 0.5f, -y - 0.5f);

    private static Vector2 At(GridMovement m) => new(m.Entity!.Transform.Position.X, m.Entity.Transform.Position.Y);

    // Steps by hand until `seconds` of game time have gone by.
    private static void RunFor(Game game, InputState input, double seconds)
    {
        while (game.Time.Total < seconds - 1e-6)
        {
            game.Tick(Frame);
            input.EndFrame();
        }
    }

    // How many cells it has walked along the corridor, counting round the corner.
    private static float Along(GridMovement m)
    {
        var at = At(m);
        return at.Y > -1.5f - 1e-3f ? at.X - 1.5f : 13 + (-1.5f - at.Y);
    }

    [Fact]
    public void A_long_step_walks_every_cell_it_covers_so_a_walk_takes_the_same_game_time_at_16x_as_at_1x()
    {
        var (normal, normalInput, slow) = Walker(Corridor);
        var (fast, fastInput, quick) = Walker(Corridor);
        fast.Time.Scale = 16;
        slow.WalkTo((14, 13));
        quick.WalkTo((14, 13));
        double? normalArrived = null, fastArrived = null;
        slow.Arrived += (_, _) => normalArrived ??= normal.Time.Total;
        quick.Arrived += (_, _) => fastArrived ??= fast.Time.Total;

        // Each 16x step covers 0.27 s: 2.7 cells of 0.1 s. Halfway and round the corner, it's where 1x is, within a cell.
        foreach (var seconds in new[] { 0.8, 1.6, 2.4 })
        {
            RunFor(normal, normalInput, seconds);
            RunFor(fast, fastInput, seconds);
            Assert.InRange(Along(quick), Along(slow) - 1, Along(slow) + 1);
            Assert.True(Vector2.Distance(CellAt(quick.Cell.X, quick.Cell.Y), At(quick)) < 1 + 1e-3f, "It stays on the path.");
        }

        RunFor(normal, normalInput, 3);
        RunFor(fast, fastInput, 3);
        Assert.Equal((14, 13), slow.Cell);
        Assert.Equal((14, 13), quick.Cell);
        Assert.InRange(fastArrived!.Value, normalArrived!.Value - 0.1, normalArrived.Value + 16 * Frame);
        Assert.InRange(normalArrived.Value, 2.5 - 0.05, 2.5 + 0.05);
    }

    [Fact]
    public void Fast_forward_takes_steps_of_the_usual_length_and_more_of_them()
    {
        var game = new Game(new Scene(), new InputState());
        game.Time.FastForward = 16;
        var deltas = new List<(float Delta, float Unscaled)>();
        game.Stepped += (_, _) => deltas.Add((game.Time.Delta, game.Time.UnscaledDelta));

        game.Advance(Frame);

        Assert.Equal(16, game.Time.Frame);
        Assert.All(deltas, d => Assert.Equal(Frame, d.Delta, 6));
        // Real time stays real: each step is a sixteenth of the frame, for the camera's pan keys and fades.
        Assert.All(deltas, d => Assert.Equal(Frame / 16, d.Unscaled, 6));
        Assert.Equal(16 * Frame, game.Time.Total, 5);
    }

    [Fact]
    public void A_walk_fast_forwarded_is_the_same_step_for_step_as_at_normal_speed()
    {
        var (normal, _, slow) = Walker(Corridor);
        var (fast, _, quick) = Walker(Corridor);
        fast.Time.FastForward = 16;
        slow.WalkTo((14, 13));
        quick.WalkTo((14, 13));
        var slowSteps = new List<Vector2>();
        var quickSteps = new List<Vector2>();
        normal.Stepped += (_, _) => slowSteps.Add(At(slow));
        fast.Stepped += (_, _) => quickSteps.Add(At(quick));

        // 3 seconds of game time: 180 frames at 1x, a sixteenth of that fast-forwarded.
        while (normal.Time.Frame < 180)
            normal.Advance(Frame);
        while (fast.Time.Frame < 180)
            fast.Advance(Frame);

        Assert.True(fast.Time.DrawnFrames < 20, $"{fast.Time.DrawnFrames} frames for 180 steps.");
        Assert.Equal(slowSteps.Take(180), quickSteps.Take(180));
        Assert.Equal((14, 13), quick.Cell);
    }

    [Fact]
    public void Fast_forward_stays_between_1_and_64()
    {
        var time = new Time { FastForward = 0.5f };
        Assert.Equal(1, time.FastForward);
        time.FastForward = 1000;
        Assert.Equal(Time.MaxFastForward, time.FastForward);
        time.FastForward = float.NaN;
        Assert.Equal(1, time.FastForward);
    }

    [Fact]
    public void Game_speed_sets_fast_forward_or_the_scale_as_its_speed_changes()
    {
        var scene = new Scene();
        var entity = new Entity("Speed");
        var speed = entity.AddComponent<GameSpeed>();
        speed.Speeds = ["1", "2", "4", "16"];
        scene.Add(entity);
        var game = new Game(scene, new InputState());

        speed.SetSpeed(4);
        game.Tick(Frame);
        Assert.Equal((1f, 4f), (game.Time.Scale, game.Time.FastForward));

        speed.Faster();
        game.Tick(Frame);
        Assert.Equal(16, speed.Speed);
        Assert.Equal(16, game.Time.FastForward);

        speed.Exact = false;
        game.Tick(Frame);
        Assert.Equal((16f, 1f), (game.Time.Scale, game.Time.FastForward));

        speed.Slower();
        speed.Exact = true;
        game.Tick(Frame);
        Assert.Equal(4, speed.Speed);

        speed.SetSpeed(0);
        game.Tick(Frame);
        Assert.Equal((0f, 1f), (game.Time.Scale, game.Time.FastForward));
        speed.Faster();
        game.Tick(Frame);
        Assert.Equal(1, speed.Speed);

        // A script setting the time itself isn't undone while the speed stays as it was.
        game.Time.Scale = 0.5f;
        game.Tick(Frame);
        Assert.Equal(0.5f, game.Time.Scale);
    }

    [Fact]
    public void Game_speed_actions_can_be_done_by_name()
    {
        var actions = ComponentActions.Of(typeof(GameSpeed)).Select(a => a.Name).ToList();
        Assert.Contains("SetSpeed", actions);
        Assert.Contains("Faster", actions);
        Assert.Contains("Slower", actions);
    }

    [Fact]
    public void A_press_is_one_cell_even_in_a_long_step_and_a_held_key_walks_on()
    {
        var (game, input, player) = Walker(["11111111", "1......1", "11111111"]);
        (player.Controllable, player.Repeat) = (true, false);
        game.Time.Scale = 16;
        game.Tick(Frame);
        input.SetKey(Key.D, true);
        game.Tick(Frame);
        input.EndFrame();
        input.SetKey(Key.D, false);
        for (var i = 0; i < 10; i++)
        {
            game.Tick(Frame);
            input.EndFrame();
        }

        Assert.Equal((2, 1), player.Cell);

        // Held (repeating) for a 16x step, it walks the 2.7 cells that step covers, as it would at 1x.
        player.Repeat = true;
        input.SetKey(Key.D, true);
        game.Tick(Frame);
        input.EndFrame();
        input.SetKey(Key.D, false);
        Assert.Equal(2.5f + 16 * Frame / 0.1f, At(player).X, 3);
    }
}
