using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>Falling-block boards: pieces that fall, slide, turn and drop, lock where they land, and full rows that clear.</summary>
public class FallingBoardTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly List<(string Message, object? Data)> _heard = [];

    public FallingBoardTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Tap(Key key)
    {
        _input.SetKey(key, true);
        Run(Frame);
        _input.SetKey(key, false);
        Run(Frame);
    }

    private void Listen(params string[] messages)
    {
        foreach (var message in messages)
            _game.On(message, data => _heard.Add((message, data)));
    }

    // A board of the given size, its top left at the origin with cells a unit across, with blocks painted as a tilemap's rows.
    private FallingBoard Board(int columns, int rows, string pieces, string[]? painted = null, Action<FallingBoard>? setUp = null)
    {
        var entity = new Entity("Board");
        var map = entity.AddComponent<Tilemap>();
        map.Solid = false;
        map.Tiles = painted ?? [];
        var board = entity.AddComponent<FallingBoard>();
        (board.Columns, board.Rows, board.Pieces, board.Seed) = (columns, rows, pieces, 7);
        setUp?.Invoke(board);
        _scene.Add(entity);
        return board;
    }

    private static string[] Rows(FallingBoard board) => board.Entity!.GetComponent<Tilemap>()!.Tiles.ToArray();

    [Fact]
    public void A_piece_comes_in_at_the_top_in_the_middle_and_falls_a_row_every_fall_time()
    {
        var board = Board(10, 20, "T");
        Run(Frame);
        Assert.Equal("T", board.Piece);
        Assert.Equal([(4, 0), (3, 1), (4, 1), (5, 1)], board.PieceCells());

        Run(0.8f);
        Assert.Equal((3, 1), board.PieceAt);
        Run(0.8f);
        Assert.Equal((3, 2), board.PieceAt);
    }

    [Fact]
    public void Left_and_right_slide_it_held_down_it_keeps_going_and_the_walls_stop_it()
    {
        var board = Board(10, 20, "T", setUp: b => b.FallTime = 100);
        Run(Frame);
        Tap(Key.Right);
        Assert.Equal((4, 0), board.PieceAt);
        Tap(Key.A);
        Assert.Equal((3, 0), board.PieceAt);

        _input.SetKey(Key.Left, true);
        Run(0.1f);
        Assert.Equal((2, 0), board.PieceAt);
        Run(1);
        _input.SetKey(Key.Left, false);
        Assert.Equal((0, 0), board.PieceAt);
    }

    [Fact]
    public void Up_or_the_rotate_action_turns_it_and_off_a_wall_it_steps_aside_to_turn()
    {
        var board = Board(10, 20, "T", setUp: b => b.FallTime = 100);
        Run(Frame);
        Tap(Key.Up);
        Assert.Equal(1, board.Rotation);
        Assert.Equal([(4, 0), (4, 1), (5, 1), (4, 2)], board.PieceCells().OrderBy(c => c.Y).ThenBy(c => c.X));
        Tap(Key.J);
        Assert.Equal(2, board.Rotation);

        // Upright against the left wall, turning it flat would stick out, so it steps right.
        Tap(Key.Up);
        Tap(Key.Up);
        Tap(Key.Up);
        for (var i = 0; i < 6; i++)
            Tap(Key.Left);
        Assert.Equal((1, -1), (board.Rotation, board.PieceAt!.Value.X));
        Tap(Key.Up);
        Assert.Equal((2, 0), (board.Rotation, board.PieceAt!.Value.X));
        Assert.All(board.PieceCells(), c => Assert.InRange(c.X, 0, 9));
    }

    [Fact]
    public void Space_drops_it_all_the_way_and_it_locks_as_its_shapes_tile_then_the_next_comes_in()
    {
        var board = Board(6, 8, "T");
        Run(Frame);
        Assert.Equal(board.LandingCells(), board.PieceCells().Select(c => (c.X, c.Y + 6)));
        Tap(Key.Space);
        Assert.Equal(5, board.TileOf('T'));
        Assert.Equal(["", "", "", "", "", "", "..5", ".555"], Rows(board));
        Assert.Equal((1, 0), board.PieceAt);
    }

    [Fact]
    public void A_piece_resting_on_something_locks_once_its_lock_time_is_up_and_sliding_it_waits_again()
    {
        var board = Board(4, 4, "O", setUp: b => (b.FallTime, b.LockTime) = (0.1f, 0.5f));
        Run(0.25f);
        Assert.Equal((1, 2), board.PieceAt);
        Run(0.4f);
        Assert.Equal(-1, board.BlockAt(1, 3));
        Tap(Key.Right);
        Run(0.4f);
        Assert.Equal(-1, board.BlockAt(2, 3));
        Run(0.2f);
        Assert.Equal(3, board.BlockAt(2, 3));
        Assert.Equal(3, board.BlockAt(3, 2));
    }

    [Fact]
    public void Holding_down_drops_it_faster()
    {
        var board = Board(10, 20, "T", setUp: b => b.SoftDropTime = 0.05f);
        Run(Frame);
        _input.SetKey(Key.Down, true);
        Run(0.25f);
        _input.SetKey(Key.Down, false);
        Assert.InRange(board.PieceAt!.Value.Y, 4, 6);
    }

    [Fact]
    public void Full_rows_flash_then_clear_scoring_eight_times_the_points_for_four_and_the_level_goes_up()
    {
        Listen("Lines", "Score", "Level Up");
        var cleared = 0;
        var board = Board(4, 6, "I", ["", "", "000", "000", "000", "000"], b => (b.LinesPerLevel, b.ClearTime) = (2, 0.3f));
        board.Cleared += rows => cleared = rows;
        Run(Frame);

        // Turned upright, it steps right into the gap down the side, and drops into it.
        board.Rotate();
        board.Drop();
        Run(Frame);
        Assert.True(board.IsClearing);
        Assert.Null(board.Piece);
        Assert.Equal((4, 800, 3), (board.Lines, board.Score, board.Level));
        Assert.Equal(4, cleared);
        Assert.Equal([("Lines", (object?)4), ("Score", 800), ("Level Up", 2)], _heard);

        Run(0.35f);
        Assert.Empty(Rows(board));
        Assert.Equal("I", board.Piece);
    }

    [Fact]
    public void Rows_above_a_cleared_row_drop_down_and_the_score_goes_up_with_the_level()
    {
        var board = Board(4, 4, "O", ["", "", "1...", "11.."], b => (b.Level, b.ClearTime) = (2, 0));
        Run(Frame);
        Tap(Key.Right);
        board.Drop();
        Run(Frame * 3);
        Assert.Equal(["", "", "", "1.33"], Rows(board));
        Assert.Equal((1, 200), (board.Lines, board.Score));
    }

    [Fact]
    public void Each_level_falls_faster()
    {
        var board = Board(10, 20, "T", setUp: b => (b.FallTime, b.Speedup, b.Level) = (0.8f, 0.5f, 3));
        Run(Frame);
        Run(0.2f);
        Assert.Equal((3, 1), board.PieceAt);
    }

    [Fact]
    public void Once_a_new_piece_has_no_room_the_game_is_over_and_nothing_more_falls()
    {
        Listen("Game Over");
        var ended = false;
        var board = Board(4, 2, "O");
        board.Ended += () => ended = true;
        Run(Frame);
        board.Drop();
        Run(Frame * 2);
        Assert.True(board.GameOver);
        Assert.True(ended);
        Assert.Null(board.Piece);
        Assert.Equal([("Game Over", (object?)board.Entity)], _heard);
        Assert.Equal([".33", ".33"], Rows(board));

        Tap(Key.Space);
        Run(1);
        Assert.Equal([".33", ".33"], Rows(board));
    }

    [Fact]
    public void Every_shape_comes_once_in_each_round_and_the_same_seed_brings_the_same_ones()
    {
        var a = Board(10, 20, "IJLOSTZ", setUp: b => b.Preview = 20);
        var b = Board(10, 20, "IJLOSTZ", setUp: b => b.Preview = 20);
        Run(Frame);
        var order = a.Piece + a.NextPieces;
        Assert.Equal(order, b.Piece + b.NextPieces);
        Assert.Equal("IJLOSTZ", string.Concat(order[..7].Order()));
        Assert.Equal("IJLOSTZ", string.Concat(order[7..14].Order()));
    }

    [Fact]
    public void A_rewind_keeps_the_blocks_score_and_level_and_brings_in_a_new_piece()
    {
        var board = Board(4, 6, "O", ["", "", "", "", "", "11"], b => b.ClearTime = 0);
        Run(Frame);
        Tap(Key.Right);
        Tap(Key.Space);
        Assert.Equal((1, 100), (board.Lines, board.Score));

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var restored = serializer.LoadFromString(serializer.SaveToString(_scene));
        var again = restored.Entities.Single(e => e.Name == "Board").GetComponent<FallingBoard>()!;
        Assert.Equal((1, 100, 1), (again.Lines, again.Score, again.Level));
        Assert.Equal(Rows(board), restored.Entities.Single(e => e.Name == "Board").GetComponent<Tilemap>()!.Tiles);
        var game = new Game(restored, new InputState());
        game.Tick(Frame);
        Assert.Equal("O", again.Piece);
    }

    [Fact]
    public void Without_a_tileset_each_shape_draws_its_own_color_with_a_ghost_where_it_lands_and_the_next_ones_beside()
    {
        var board = Board(10, 20, "TI", setUp: b => b.Preview = 1);
        Run(Frame);
        var draws = SceneRenderer.Build(_scene, 1).Sprites.Where(d => d.Entity == board.Entity).ToList();
        // Backdrop, the ghost's four outlines of four edges, the piece, the preview's box and the next piece.
        Assert.Equal(1 + 16 + 4 + 1 + 4, draws.Count);
        var piece = draws.Skip(17).Take(4).ToList();
        var next = draws.Skip(22).ToList();
        Assert.All(piece, d => Assert.Equal(piece[0].Tint, d.Tint));
        Assert.NotEqual(piece[0].Tint, next[0].Tint);
        Assert.All(next, d => Assert.True(d.World.M41 > 10));
    }

    [Fact]
    public void In_a_network_game_a_guests_keys_move_the_piece_on_the_host_and_both_see_it()
    {
        static Scene Scene()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            var entity = new Entity("Board");
            entity.AddComponent<Tilemap>().Solid = false;
            var board = entity.AddComponent<FallingBoard>();
            (board.Columns, board.Rows, board.Pieces, board.Seed, board.FallTime) = (6, 8, "T", 3, 100);
            scene.Add(entity);
            return scene;
        }

        var world = new NetWorld(Scene);
        world.Run(0.2);
        var keys = NetWorld.Keys(world.Client);
        keys.SetKey(Key.Right, true);
        world.Run(0.1);
        keys.SetKey(Key.Right, false);
        world.Run(0.2);

        var hostBoard = world.Host.Scene.Entities.Single(e => e.Name == "Board").GetComponent<FallingBoard>()!;
        var guestBoard = world.Client.Scene.Entities.Single(e => e.Name == "Board").GetComponent<FallingBoard>()!;
        Assert.Equal((2, 0), hostBoard.PieceAt);
        Assert.Equal((2, 0), guestBoard.PieceAt);

        keys.SetKey(Key.Space, true);
        world.Run(0.1);
        keys.SetKey(Key.Space, false);
        world.Run(0.3);
        Assert.Equal("...5", Rows(hostBoard)[6]);
        Assert.Equal(Rows(hostBoard), Rows(guestBoard));
        Assert.Equal("T", guestBoard.Piece);
    }
}
