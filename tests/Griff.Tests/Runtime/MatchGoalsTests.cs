using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>Match-3 levels: points for the gems cleared, a move limit, goals that win the level, and counters that show them.</summary>
public class MatchGoalsTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly List<(string Message, object? Data)> _heard = [];

    public MatchGoalsTests() => _game = new Game(_scene, _input);

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

    // A board laid out by hand, as in MatchBoardTests: nothing refills, so what's left is easy to read.
    private MatchBoard Board(string[] rows, Action<MatchBoard>? setUp = null)
    {
        var entity = new Entity("Board");
        var map = entity.AddComponent<Tilemap>();
        map.Solid = false;
        map.Tiles = rows;
        var board = entity.AddComponent<MatchBoard>();
        (board.Columns, board.Rows, board.Deal, board.Seed, board.Refill) = (rows.Max(r => r.Length), rows.Length, false, 7, false);
        board.ShuffleWhenStuck = false;
        setUp?.Invoke(board);
        _scene.Add(entity);
        return board;
    }

    private Counter Counter(Action<Counter> setUp)
    {
        var entity = new Entity("Counter");
        entity.AddComponent<Text>();
        var counter = entity.AddComponent<Counter>();
        setUp(counter);
        _scene.Add(entity);
        return counter;
    }

    private static string Shown(Counter counter) => counter.Entity!.GetComponent<Text>()!.Value ?? "";

    // Swapping the last two gems of the bottom row lines up three 0s.
    private static readonly string[] OneMove = ["1234", "2345", "3451", "0010"];

    // Swapping the bottom left pair lines up the 0s down the left; the 5 above falls in beside the two 5s: a chain.
    private static readonly string[] Chain = ["523", "234", "042", "055", "103"];

    [Fact]
    public void Each_gem_cleared_scores_its_points_and_says_so()
    {
        var board = Board(OneMove);
        Listen("Score");

        board.Swap(2, 3, 3, 3);
        Run(1.5f);

        Assert.Equal(30, board.Score);
        Assert.Equal("0:3", board.Cleared);
        Assert.Equal(3, board.ClearedOf(0));
        Assert.Equal([("Score", (object?)30)], _heard);
    }

    [Fact]
    public void Gems_cleared_further_along_a_chain_score_more()
    {
        var board = Board(Chain, b => b.Points = 5);
        Listen("Score");

        board.Swap(0, 4, 1, 4);
        Run(2.5f);

        // Three 0s at 5 points, then three 5s at double.
        Assert.Equal([("Score", (object?)15), ("Score", 30)], _heard);
        Assert.Equal(45, board.Score);
        Assert.Equal("0:3, 5:3", board.Cleared);
    }

    [Fact]
    public void Reaching_the_target_score_wins_the_level_once_the_board_comes_to_rest_and_no_more_swaps_are_taken()
    {
        var board = Board(Chain, b => (b.TargetScore, b.Moves, b.OnlyMatchingSwaps) = (20, 5, false));
        Listen("Level Complete", "Out of Moves");
        var finished = new List<bool>();
        board.Finished += finished.Add;

        board.Swap(0, 4, 1, 4);
        Run(Frame * 20);
        // Past the target partway through the chain: the win waits till it's done.
        Assert.Empty(_heard);
        Run(2.5f);

        Assert.True(board.GoalsMet);
        Assert.True(board.IsOver);
        Assert.Equal(4, board.MovesLeft);
        Assert.Equal([("Level Complete", (object?)board.Entity)], _heard);
        Assert.Equal([true], finished);

        var rows = board.Entity!.GetComponent<Tilemap>()!.Tiles.ToArray();
        board.Swap(1, 4, 2, 4);
        Run(1);
        Assert.Equal(rows, board.Entity.GetComponent<Tilemap>()!.Tiles);
        Assert.Equal(1, board.Swaps);
    }

    [Fact]
    public void Running_out_of_moves_with_the_goals_not_met_loses_the_level()
    {
        var board = Board(OneMove, b => (b.TargetScore, b.Moves, b.OnlyMatchingSwaps) = (1000, 1, false));
        Listen("Level Complete", "Out of Moves");
        var finished = new List<bool>();
        board.Finished += finished.Add;

        board.Swap(2, 3, 3, 3);
        Run(1.5f);

        Assert.Equal(0, board.MovesLeft);
        Assert.False(board.GoalsMet);
        Assert.True(board.IsOver);
        Assert.Equal([("Out of Moves", (object?)board.Entity)], _heard);
        Assert.Equal([false], finished);

        board.Swap(0, 0, 1, 0);
        Run(1);
        Assert.Equal(1, board.Swaps);
    }

    [Fact]
    public void Gems_to_collect_win_once_enough_of_each_kind_are_cleared()
    {
        var won = Board(Chain, b => (b.Collect, b.Moves) = ("0:3, 5:3", 3));
        Listen("Level Complete", "Out of Moves");
        won.Swap(0, 4, 1, 4);
        Run(2.5f);
        Assert.Equal([("Level Complete", (object?)won.Entity)], _heard);
        Assert.Equal(3, won.ToCollect(5));
    }

    [Fact]
    public void Gems_still_to_collect_when_the_last_move_is_made_lose_the_level()
    {
        var lost = Board(Chain, b => (b.Collect, b.Moves) = ("0:3, 5:4", 1));
        Listen("Level Complete", "Out of Moves");
        lost.Swap(0, 4, 1, 4);
        Run(2.5f);
        Assert.Equal([("Out of Moves", (object?)lost.Entity)], _heard);
    }

    [Fact]
    public void A_board_with_no_goals_or_move_limit_never_ends()
    {
        var board = Board(OneMove);
        Listen("Level Complete", "Out of Moves");
        board.Swap(2, 3, 3, 3);
        Run(1.5f);

        Assert.False(board.GoalsMet);
        Assert.False(board.IsOver);
        Assert.Equal(-1, board.MovesLeft);
        Assert.Empty(_heard);
    }

    [Fact]
    public void Counters_show_the_boards_score_its_moves_left_and_the_gems_cleared_out_of_the_goals()
    {
        var board = Board(Chain, b => (b.TargetScore, b.Moves, b.Collect) = (500, 10, "5:12"));
        var score = Counter(c => (c.CountScore, c.Format) = (true, "Score: {0} / {2}"));
        var moves = Counter(c => (c.CountMoves, c.Format) = (true, "Moves: {3}"));
        var fives = Counter(c => (c.CountGem, c.Format) = (5, "{0} / {2}, {3} to go"));
        // Listens for the board's Score Message, as a plain score counter does.
        var heard = Counter(c => c.Format = "{0}");
        Run(Frame);
        Assert.Equal("Score: 0 / 500", Shown(score));
        Assert.Equal("Moves: 10", Shown(moves));
        Assert.Equal("0 / 12, 12 to go", Shown(fives));

        board.Swap(0, 4, 1, 4);
        Run(2.5f);

        Assert.Equal("Score: 90 / 500", Shown(score));
        Assert.Equal("Moves: 9", Shown(moves));
        Assert.Equal("3 / 12, 9 to go", Shown(fives));
        Assert.Equal("90", Shown(heard));
        // Counters that count something of their own don't add up their Message too.
        Assert.Equal(3, fives.Value);
    }

    [Fact]
    public void A_rewind_keeps_the_score_and_the_gems_cleared()
    {
        var board = Board(Chain, b => (b.TargetScore, b.Moves) = (1000, 5));
        board.Swap(0, 4, 1, 4);
        Run(2.5f);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var restored = serializer.LoadFromString(serializer.SaveToString(_scene));
        var again = restored.Entities.Single(e => e.Name == "Board").GetComponent<MatchBoard>()!;
        Assert.Equal(90, again.Score);
        Assert.Equal("0:3, 5:3", again.Cleared);
        Assert.Equal(4, again.MovesLeft);
    }
}
