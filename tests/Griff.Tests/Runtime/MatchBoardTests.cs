using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>Match-3 boards: swaps, matches that clear, gems that fall and refill, chains and shuffles.</summary>
public class MatchBoardTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly List<(string Message, object? Data)> _heard = [];

    public MatchBoardTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Listen(params string[] messages)
    {
        foreach (var message in messages)
            _game.On(message, data => _heard.Add((message, data)));
    }

    // A board laid out by hand, its top left at the origin with cells a unit across, one character per cell (as a tilemap's).
    private MatchBoard Board(string[] rows, Action<MatchBoard>? setUp = null)
    {
        var entity = new Entity("Board");
        var map = entity.AddComponent<Tilemap>();
        map.Solid = false;
        map.Tiles = rows;
        var board = entity.AddComponent<MatchBoard>();
        (board.Columns, board.Rows, board.Deal, board.Seed) = (rows.Select(r => r.Length).DefaultIfEmpty().Max(), rows.Length, false, 7);
        // Small boards soon run out of moves; only the tests of shuffling shuffle.
        board.ShuffleWhenStuck = false;
        setUp?.Invoke(board);
        _scene.Add(entity);
        return board;
    }

    private static string[] Rows(MatchBoard board) => board.Entity!.GetComponent<Tilemap>()!.Tiles.ToArray();

    private static Vector2 Middle(int x, int y) => new(x + 0.5f, -(y + 0.5f));

    private static bool Full(MatchBoard board) =>
        Enumerable.Range(0, board.Rows).All(y => Enumerable.Range(0, board.Columns).All(x => board.IsGem(board.GemAt(x, y))));

    // Three of a kind side by side, across or down.
    private static bool AnyLinedUp(MatchBoard board)
    {
        for (var y = 0; y < board.Rows; y++)
        {
            for (var x = 0; x < board.Columns; x++)
            {
                var gem = board.GemAt(x, y);
                if (board.IsGem(gem) && ((board.GemAt(x + 1, y) == gem && board.GemAt(x + 2, y) == gem) ||
                                         (board.GemAt(x, y + 1) == gem && board.GemAt(x, y + 2) == gem)))
                    return true;
            }
        }

        return false;
    }

    // The board's gems as drawn (its backdrop and highlights left out): where each is and how big.
    private List<(Vector2 At, float Size, float Alpha)> Gems(MatchBoard board) =>
        SceneRenderer.Build(_scene, 1).Sprites.Where(d => d.Entity == board.Entity && d.Tint != board.Background && d.Tint != board.Highlight)
            .Select(d => (new Vector2(d.World.M41, d.World.M42), d.World.M11, d.Tint.A)).ToList();

    private void Tap(Key key)
    {
        _input.SetKey(key, true);
        Run(Frame);
        _input.SetKey(key, false);
        Run(Frame);
    }

    private void Click(Vector2 at)
    {
        _input.MouseWorld = at;
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(Frame);
    }

    // No lines yet; swapping the last two gems of the bottom row lines up three 0s.
    private static readonly string[] OneMove = ["1234", "2345", "3451", "0010"];

    [Fact]
    public void A_board_is_dealt_full_with_nothing_lined_up_and_a_move_to_make_and_the_same_seed_deals_the_same()
    {
        var board = Board([], b => (b.Columns, b.Rows, b.Deal, b.Seed) = (8, 8, true, 42));
        Run(Frame);

        Assert.True(Full(board));
        Assert.False(AnyLinedUp(board));
        Assert.NotNull(board.FindMove());
        Assert.False(board.Deal);
        Assert.False(board.IsBusy);

        // Even over gems already on it (Create Match Board deals one so it shows in the editor): only the seed counts.
        var again = new Scene();
        var other = new Entity("Board");
        var otherMap = other.AddComponent<Tilemap>();
        var copy = other.AddComponent<MatchBoard>();
        (copy.Columns, copy.Rows, copy.Deal, copy.Seed) = (8, 8, true, 42);
        copy.DealInto(otherMap, new Random(1));
        Assert.NotEqual(Rows(board), Rows(copy));
        again.Add(other);
        new Game(again, new InputState()).Tick(Frame);
        Assert.Equal(Rows(board), Rows(copy));
    }

    [Fact]
    public void Dealing_keeps_the_blocks_and_replaces_the_painted_gems()
    {
        // Tile 9 isn't a gem: a block.
        var board = Board(["0000", "0990", "0000"], b => (b.Deal, b.Seed) = (true, 3));
        Run(Frame);

        Assert.Equal(9, board.GemAt(1, 1));
        Assert.Equal(9, board.GemAt(2, 1));
        Assert.False(AnyLinedUp(board));
    }

    [Fact]
    public void A_swap_that_lines_three_up_clears_them_and_what_was_above_falls_into_the_gap()
    {
        var board = Board(OneMove, b => b.Refill = false);
        Listen("Match");
        var swapped = new List<((int, int), (int, int))>();
        var matches = new List<MatchInfo>();
        var settled = new List<int>();
        board.Swapped += (a, b) => swapped.Add((a, b));
        board.Matched += matches.Add;
        board.Settled += settled.Add;

        Assert.True(board.Swap(2, 3, 3, 3));
        Run(Frame);
        Assert.True(board.IsBusy);
        Run(1.5f);

        Assert.False(board.IsBusy);
        Assert.Equal(["...4", "1235", "2341", "3451"], Rows(board));
        Assert.Equal(1, board.Swaps);
        Assert.Equal([((2, 3), (3, 3))], swapped);
        Assert.Equal([("Match", (object?)3)], _heard);
        var match = Assert.Single(matches);
        Assert.Equal(0, match.Kind);
        Assert.Equal([(0, 3), (1, 3), (2, 3)], match.Cells);
        Assert.Equal(1, match.Chain);
        Assert.Equal([1], settled);
    }

    [Fact]
    public void A_swap_that_lines_nothing_up_slides_back()
    {
        var board = Board(OneMove);
        var swapped = 0;
        board.Swapped += (_, _) => swapped++;

        // The two only nudge into each other's cells: the tiles never change, so a rewind can't land on them swapped.
        board.Swap(0, 0, 1, 0);
        Run(Frame * 8);
        Assert.Equal(OneMove, Rows(board));
        Assert.Contains(Gems(board), g => g.At.X is > 0.55f and < 1.45f && g.At.Y > -1 && g.At.Y < 0);
        Run(1);

        Assert.Equal(OneMove, Rows(board));
        Assert.Equal(0, board.Swaps);
        Assert.Equal(0, swapped);
        Assert.False(board.IsBusy);
    }

    [Fact]
    public void Without_only_matching_swaps_any_two_neighbors_trade_places()
    {
        var board = Board(OneMove, b => b.OnlyMatchingSwaps = false);
        board.Swap(0, 0, 1, 0);
        Run(1);

        Assert.Equal("2134", Rows(board)[0]);
        Assert.Equal(1, board.Swaps);
    }

    [Fact]
    public void Gems_that_land_in_a_line_clear_too_as_a_chain()
    {
        // Swapping the bottom left pair lines up the 0s down the left; the 5 above falls in beside the two 5s.
        var board = Board(["523", "234", "042", "055", "103"], b => b.Refill = false);
        board.ChainMessage = "Chain";
        board.SettledMessage = "Settled";
        Listen("Match", "Chain", "Settled");
        var matches = new List<MatchInfo>();
        board.Matched += matches.Add;

        board.Swap(0, 4, 1, 4);
        Run(2.5f);

        Assert.Equal(["", ".23", ".34", ".42", "213"], Rows(board));
        Assert.Equal([0, 5], matches.Select(m => m.Kind));
        Assert.Equal([1, 2], matches.Select(m => m.Chain));
        Assert.Equal([("Match", (object?)3), ("Match", 3), ("Chain", 2), ("Settled", 2)], _heard);
        Assert.Equal(2, board.Chain);
    }

    [Fact]
    public void New_gems_drop_in_from_the_top_and_the_board_comes_to_rest_full_with_nothing_lined_up()
    {
        var board = Board(OneMove);
        board.Swap(2, 3, 3, 3);
        Run(Frame * 25);

        // Partway: the new gems are falling in from above the board, fading in as they come onto it.
        Assert.True(board.IsBusy);
        Assert.Contains(Gems(board), g => g.At.Y > -0.5f && g.Alpha < 1);

        Run(4);
        Assert.False(board.IsBusy);
        Assert.True(Full(board));
        Assert.False(AnyLinedUp(board));
    }

    [Fact]
    public void Gems_stop_on_blocks_and_the_cells_under_a_block_fill_from_just_under_it()
    {
        // The 0s under the block clear; nothing falls through the block, so a new gem grows in its place.
        var board = Board(["1234", "9123", "0010"]);
        board.Swap(2, 2, 3, 2);
        // Under the block, a gem only ever shrinks or grows in its own cell: nothing slides down past the block.
        var sizes = new List<float>();
        for (var i = 0; i < 60; i++)
        {
            Run(Frame);
            var under = Gems(board).Where(g => g.At.X < 1 && g.At.Y < -1.5f).ToList();
            Assert.All(under, g => Assert.Equal(Middle(0, 2), g.At));
            sizes.Add(under.Count == 0 ? 0 : under[0].Size);
        }

        var smallest = sizes.IndexOf(sizes.Min());
        Assert.True(sizes.Skip(smallest).Max() > sizes[smallest] + 0.3f);
        Run(2.5f);
        Assert.Equal(9, board.GemAt(0, 1));
        Assert.True(Enumerable.Range(0, 3).All(y => Enumerable.Range(0, 4).All(x => board.GemAt(x, y) >= 0)));
        Assert.False(AnyLinedUp(board));
    }

    [Fact]
    public void Dragging_a_gem_onto_its_neighbor_swaps_them()
    {
        var board = Board(OneMove, b => b.Refill = false);
        _input.MouseWorld = Middle(2, 3);
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        _input.MouseWorld = Middle(2, 3) + new Vector2(0.7f, 0.1f);
        Run(Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(1.5f);

        Assert.Equal(1, board.Swaps);
        Assert.Equal("...4", Rows(board)[0]);
    }

    [Fact]
    public void A_quick_flick_let_go_over_the_neighbor_swaps_them_too()
    {
        var board = Board(OneMove, b => b.Refill = false);
        _input.MouseWorld = Middle(2, 3);
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        _input.MouseWorld = Middle(3, 3);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(1.5f);

        Assert.Equal(1, board.Swaps);
        Assert.Null(board.Picked);
    }

    [Fact]
    public void Clicking_a_gem_then_its_neighbor_swaps_them_and_clicking_it_again_puts_it_down()
    {
        var board = Board(OneMove, b => b.Refill = false);
        Click(Middle(2, 3));
        Assert.Equal((2, 3), board.Picked);
        Click(Middle(2, 3));
        Assert.Null(board.Picked);

        Click(Middle(2, 3));
        Click(Middle(3, 3));
        Run(1.5f);
        Assert.Equal(1, board.Swaps);
    }

    [Fact]
    public void The_arrows_move_a_cursor_the_pick_action_picks_a_gem_up_and_an_arrow_swaps_it()
    {
        var board = Board(OneMove, b => b.Refill = false);

        // The first press shows the cursor in the middle.
        Tap(Key.Right);
        Assert.Equal((1, 1), board.Cursor);
        Tap(Key.Right);
        Tap(Key.Down);
        Tap(Key.Down);
        Assert.Equal((2, 3), board.Cursor);
        Tap(Key.Space);
        Assert.Equal((2, 3), board.Picked);
        Tap(Key.Right);
        Run(1.5f);

        Assert.Equal(1, board.Swaps);
        Assert.Equal((3, 3), board.Cursor);
        Assert.Null(board.Picked);
    }

    [Fact]
    public void A_board_with_no_swap_left_is_shuffled_into_one_that_has()
    {
        var board = Board(["012", "120", "201"], b => (b.Gems, b.ShuffleWhenStuck) = ("0-2", true));
        board.ShuffleMessage = "Shuffled";
        Listen("Shuffled");
        Assert.Null(board.FindMove());

        Run(1);

        Assert.Single(_heard);
        Assert.NotNull(board.FindMove());
        Assert.False(AnyLinedUp(board));
        Assert.Equal(3, Rows(board).Sum(r => r.Count(c => c == '0')));
    }

    [Fact]
    public void A_board_that_cant_be_shuffled_into_a_move_isnt_shuffled_again_and_again()
    {
        // Two kinds on a board too small to line three up any way at all.
        var board = Board(["01", "10"], b => (b.Gems, b.ShuffleWhenStuck) = ("0-1", true));
        board.ShuffleMessage = "Shuffled";
        Listen("Shuffled");
        Run(3);
        Assert.Single(_heard);
    }

    [Fact]
    public void Without_refill_the_board_empties_out_as_its_cleared()
    {
        var board = Board(OneMove, b => b.Refill = false);
        board.Swap(2, 3, 3, 3);
        Run(2);
        Assert.Equal(-1, board.GemAt(0, 0));
        Assert.False(board.IsBusy);
    }

    [Fact]
    public void A_rewind_partway_through_a_fall_carries_on_from_the_tiles_and_keeps_the_board_and_its_swaps()
    {
        var board = Board([], b => (b.Columns, b.Rows, b.Deal, b.Seed) = (6, 6, true, 5));
        Run(Frame);
        var move = board.FindMove()!.Value;
        board.Swap(move.From.X, move.From.Y, move.To.X, move.To.Y);
        Run(0.45f);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var json = serializer.SaveToString(_scene);
        var restored = serializer.LoadFromString(json);
        var again = restored.Entities.Single(e => e.Name == "Board").GetComponent<MatchBoard>()!;
        Assert.False(again.Deal);
        Assert.Equal(1, again.Swaps);

        var game = new Game(restored, new InputState());
        for (var i = 0; i < 300; i++)
            game.Tick(Frame);
        Assert.True(Full(again));
        Assert.False(AnyLinedUp(again));
        Assert.Equal(1, again.Swaps);
    }

    [Fact]
    public void A_rewind_in_the_middle_of_a_swap_has_it_counted_and_clears_what_it_lined_up()
    {
        var board = Board(OneMove, b => b.Refill = false);
        board.Swap(2, 3, 3, 3);
        Run(Frame * 3);
        Assert.True(board.IsBusy);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var restored = serializer.LoadFromString(serializer.SaveToString(_scene));
        var again = restored.Entities.Single(e => e.Name == "Board").GetComponent<MatchBoard>()!;
        Assert.Equal(1, again.Swaps);
        var game = new Game(restored, new InputState());
        for (var i = 0; i < 120; i++)
            game.Tick(Frame);
        Assert.Equal(["...4", "1235", "2341", "3451"], Rows(again));
        Assert.Equal(1, again.Swaps);
    }

    [Fact]
    public void Scripts_can_ask_for_a_swap_while_the_board_moves_and_it_waits_till_its_still()
    {
        var board = Board(OneMove, b => b.Refill = false);
        board.Swap(0, 0, 1, 0);
        Run(Frame);
        Assert.True(board.IsBusy);
        // Asked for while the first slides back: made once it's done.
        board.Swap(2, 3, 3, 3);
        Run(2);
        Assert.Equal(1, board.Swaps);
    }

    [Fact]
    public void A_board_with_no_tileset_draws_each_kind_of_gem_its_own_color_and_a_gem_sliding_where_it_is_now()
    {
        var board = Board(OneMove);
        var draws = SceneRenderer.Build(_scene, 1).Sprites.Where(d => d.Entity == board.Entity).ToList();
        // The backdrop, then sixteen gems.
        Assert.Equal(17, draws.Count);
        var gems = draws.Skip(1).ToList();
        Assert.Equal(gems[0].Tint, gems.Single(d => Near(d, Middle(3, 2))).Tint);
        Assert.NotEqual(gems[0].Tint, gems[1].Tint);

        board.Swap(0, 0, 1, 0);
        Run(Frame * 4);
        var moving = SceneRenderer.Build(_scene, 1).Sprites.Where(d => d.Entity == board.Entity).Skip(1).First();
        Assert.InRange(moving.World.M41, 0.6f, 1.45f);

        static bool Near(SpriteDraw draw, Vector2 at) => Vector2.Distance(new Vector2(draw.World.M41, draw.World.M42), at) < 0.01f;
    }

    [Fact]
    public void In_a_network_game_a_players_swap_runs_on_the_host_and_both_see_the_board()
    {
        static Scene Scene()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            var entity = new Entity("Board");
            entity.AddComponent<Tilemap>().Tiles = OneMove;
            var board = entity.AddComponent<MatchBoard>();
            (board.Columns, board.Rows, board.Deal, board.Refill, board.ShuffleWhenStuck) = (4, 4, false, false, false);
            scene.Add(entity);
            return scene;
        }

        var world = new NetWorld(Scene);
        var keys = NetWorld.Keys(world.Client);
        keys.MouseWorld = Middle(2, 3);
        keys.SetMouseButton(MouseButton.Left, true);
        world.Run(0.1);
        keys.MouseWorld = Middle(3, 3);
        world.Run(0.1);
        keys.SetMouseButton(MouseButton.Left, false);
        world.Run(0.12);

        // Partway, the guest's machine draws the gems moving as the host has them.
        var guestBoard = world.Client.Scene.Entities.Single(e => e.Name == "Board").GetComponent<MatchBoard>()!;
        Assert.True(guestBoard.IsBusy);

        world.Run(2);
        var hostBoard = world.Host.Scene.Entities.Single(e => e.Name == "Board").GetComponent<MatchBoard>()!;
        Assert.Equal(1, hostBoard.Swaps);
        Assert.Equal("...4", Rows(hostBoard)[0]);
        Assert.Equal(Rows(hostBoard), Rows(guestBoard));
        Assert.Equal(1, guestBoard.Swaps);
        Assert.False(guestBoard.IsBusy);
    }
}
