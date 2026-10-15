using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.Match3;

/// <summary>
/// A match-3 board, with no code: the gems are the tiles of its entity's <see cref="Tilemap"/>, one kind per tile
/// number in <see cref="Gems"/>. Drag a gem onto a neighbor (or click one, then the other) to swap them; three or more of
/// a kind in a row or column clear, the gems above fall into the gaps, new ones drop in from the top, and whatever
/// lines up as they land clears too, a chain. With keys or a gamepad, the arrows move a cursor, the
/// <see cref="PickAction"/> (Space, the gamepad's A) picks a gem up and an arrow swaps it that way.
/// </summary>
/// <remarks>
/// The board is <see cref="Columns"/> by <see cref="Rows"/> cells from the tilemap's top left. Empty cells are filled with
/// gems as the game starts, and tiles that aren't gems are fixed blocks: they don't move or match, gems stop on them, and
/// the cells under one are filled from just under it. Without a tileset image each gem kind draws as a square of its own
/// color. The tiles are saved with the scene, so a rewind goes back to how the board was (the slides and falls under way
/// start again from there). In a network game the host runs the board and any player can swap.
/// <para>
/// Each gem cleared scores <see cref="Points"/>, more along a chain. Give it <see cref="Moves"/>, a
/// <see cref="TargetScore"/> or gems to <see cref="Collect"/> and it's a level: met goals send the
/// <see cref="WinMessage"/>, moves run out first the <see cref="OutOfMovesMessage"/>, and either way no more swaps are taken.
/// </para>
/// </remarks>
[Category("Gameplay")]
[Requires(typeof(Tilemap), Note = "holds the gems")]
[WorksWith(typeof(Counter), Note = "shows the score (Message \"Score\"), the moves left (Count Moves) or the gems of a kind cleared (Count Kind)")]
[ComponentName("Griff.MatchBoard")]
public sealed partial class MatchBoard : Component, IDrawable, IDrawsTilemap, INetState, ICounterSource
{
    // The board as it is mid-move (gems sliding, falling, clearing), as lines.
    void INetState.Network(NetState state) => state.Add<IReadOnlyList<string>>(() => MatchView.Write(this).ToArray(), lines => MatchView.Read(this, lines));

    // A counter shows its swaps out of its Moves, its score out of its Target Score, or the gems of a kind cleared out of
    // how many it asks for.
    (int Value, int Total)? ICounterSource.CountFor(Counter counter) =>
        counter.CountMoves ? (Swaps, Moves)
        : counter.CountScore ? (Score, TargetScore)
        : counter.CountKind >= 0 ? (ClearedOf(counter.CountKind), ToCollect(counter.CountKind))
        : null;

    private string? _gems = "0-5";
    private bool[] _gemSet = Tilemap.ParseTileSet("0-5");

    /// <summary>Cells across.</summary>
    public int Columns { get; set; } = 8;

    /// <summary>Cells down.</summary>
    public int Rows { get; set; } = 8;

    /// <summary>
    /// The tile numbers that are gems, each its own kind: numbers and ranges, e.g. "0-5" for six kinds. Any other tile on
    /// the board is a fixed block.
    /// </summary>
    public string? Gems
    {
        get => _gems;
        set
        {
            _gems = value;
            _gemSet = Tilemap.ParseTileSet(value);
        }
    }

    /// <summary>How many of a kind in a row (or column) clear.</summary>
    public int MatchLength { get; set; } = 3;

    /// <summary>
    /// Deal a fresh board each time the game starts, with no matches already made, keeping the blocks where they are.
    /// Untick it to play the gems as painted: a level laid out by hand (empty cells are still filled). It unticks itself
    /// once the board is dealt, so a rewind keeps the board it goes back to.
    /// </summary>
    public bool Deal { get; set; } = true;

    /// <summary>Picks the gems dealt and dropped in: the same number always gives the same ones. 0 is different each game.</summary>
    public int Seed { get; set; }

    /// <summary>True while it stands still: nothing moves, falls or clears, and it takes no swaps. Its Pause and Resume actions set it.</summary>
    public bool Paused { get; set; }

    /// <summary>How many swaps have been made (not counting those that slid back). Saved with the scene, so a rewind takes them back.</summary>
    public int Swaps { get; set; }

    /// <summary>
    /// Points for each gem cleared, times how far into the chain it clears: gems in the second clear after a swap score
    /// double, the third triple. 0 keeps no score.
    /// </summary>
    [Group("Score and Goals")]
    public int Points { get; set; } = 10;

    /// <summary>The points scored so far. Saved with the scene, so a rewind takes them back.</summary>
    public int Score { get; set; }

    /// <summary>
    /// How many swaps the player gets: once they're used up and the board comes to rest, the level is over (won if its
    /// goals are met, lost if not). 0 is no limit.
    /// </summary>
    public int Moves { get; set; }

    /// <summary>A score that wins the level once it's reached. 0 is no score to reach.</summary>
    public int TargetScore { get; set; }

    /// <summary>
    /// Gems to clear to win the level, as kind:count pairs, e.g. "0:20, 3:15" for 20 of tile 0 and 15 of tile 3. With a
    /// <see cref="TargetScore"/> too, both have to be done.
    /// </summary>
    public string? Collect { get; set; }

    /// <summary>
    /// How many of each kind have been cleared so far, as kind:count pairs like <see cref="Collect"/>. Saved with the
    /// scene, so a rewind takes them back.
    /// </summary>
    public string? Cleared { get; set; }

    /// <summary>A swap that lines nothing up slides back. Untick it to let any two neighbors swap.</summary>
    [Group("Rules")]
    public bool OnlyMatchingSwaps { get; set; } = true;

    /// <summary>New gems drop in from the top to fill the gaps. Untick it for a board that empties out as it's cleared.</summary>
    public bool Refill { get; set; } = true;

    /// <summary>When no swap would line anything up, the gems are shuffled (into a board with no matches but a move to make).</summary>
    public bool ShuffleWhenStuck { get; set; } = true;

    /// <summary>The input action that picks up the gem under the cursor: "Jump" is Space or the gamepad's A. Empty only uses the mouse.</summary>
    [Names(NameKind.Action)]
    public string? PickAction { get; set; } = "Jump";

    /// <summary>Seconds a swap takes.</summary>
    [Group("Motion")]
    public float SwapTime { get; set; } = 0.15f;

    /// <summary>Seconds matched gems take to shrink away.</summary>
    public float ClearTime { get; set; } = 0.2f;

    /// <summary>Seconds a gem takes to fall one cell. Longer falls speed up as they go, as things falling do.</summary>
    public float FallTime { get; set; } = 0.12f;

    /// <summary>A color drawn behind the board's cells. Transparent draws nothing.</summary>
    [Group("Look")]
    public Color Background { get; set; } = Color.FromBytes(0x10, 0x14, 0x1C, 0xB0);

    /// <summary>The color of the picked-up gem's cell and the keyboard cursor.</summary>
    public Color Highlight { get; set; } = Color.FromBytes(0xFF, 0xFF, 0xFF, 0x50);

    /// <summary>A sound played as two gems swap, e.g. "Sounds/swap.wav".</summary>
    [Group("Sound and Messages")]
    public string? SwapSound { get; set; }

    /// <summary>A sound played as gems clear (once however many lines go at once).</summary>
    public string? MatchSound { get; set; }

    /// <summary>A sound played when a swap lines nothing up and slides back.</summary>
    public string? NoMatchSound { get; set; }

    /// <summary>A sound played as a stuck board is shuffled.</summary>
    public string? ShuffleSound { get; set; }

    /// <summary>
    /// A message sent with <c>Game.Send</c> as gems clear, with how many as its data: a <see cref="Counter"/> listening
    /// for it adds them up.
    /// </summary>
    [Names(NameKind.Message)]
    public string? MatchMessage { get; set; } = "Match";

    /// <summary>A message sent as a chain goes on (the second clear and on after one swap), with how long it is so far as its data.</summary>
    [Names(NameKind.Message)]
    public string? ChainMessage { get; set; }

    /// <summary>A message sent once the board comes to rest after a swap, with how many clears the swap set off as its data.</summary>
    [Names(NameKind.Message)]
    public string? SettledMessage { get; set; }

    /// <summary>A message sent as a stuck board is shuffled, with this entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? ShuffleMessage { get; set; }

    /// <summary>
    /// A message sent as gems clear, with the points they scored as its data: a <see cref="Counter"/> listening for it
    /// (its Message is "Score" to begin with) shows the score.
    /// </summary>
    [Names(NameKind.Message)]
    public string? ScoreMessage { get; set; } = "Score";

    /// <summary>A sound played when the level is won: its goals met, once the board comes to rest.</summary>
    public string? WinSound { get; set; }

    /// <summary>A sound played when the moves run out with the goals not met.</summary>
    public string? OutOfMovesSound { get; set; }

    /// <summary>
    /// A message sent once the goals are met and the board comes to rest, with this entity as its data: a panel with
    /// this as its Open Message shows the level is won.
    /// </summary>
    [Names(NameKind.Message)]
    public string? WinMessage { get; set; } = "Level Complete";

    /// <summary>
    /// A message sent once the moves run out with the goals not met and the board comes to rest, with this entity as
    /// its data. A board with no goals sends it too: the end of a game played for score.
    /// </summary>
    [Names(NameKind.Message)]
    public string? OutOfMovesMessage { get; set; } = "Out of Moves";

    /// <summary>How many swaps are left of <see cref="Moves"/>: 0 once they've run out, -1 with no limit.</summary>
    [DoNotSerialize]
    public int MovesLeft => Moves > 0 ? Math.Max(0, Moves - Swaps) : -1;

    /// <summary>True if the board has goals (a <see cref="TargetScore"/> or gems to <see cref="Collect"/>) and they're all met.</summary>
    [DoNotSerialize]
    public bool GoalsMet
    {
        get
        {
            var collect = ParseCounts(Collect);
            if (TargetScore <= 0 && collect.Count == 0)
                return false;
            var cleared = ParseCounts(Cleared);
            return Score >= TargetScore && collect.All(c => cleared.GetValueOrDefault(c.Key) >= c.Value);
        }
    }

    /// <summary>True once the level is over, won or out of moves: no more swaps are taken.</summary>
    [DoNotSerialize]
    public bool IsOver => GoalsMet || MovesLeft == 0;

    /// <summary>True while gems are swapping, clearing or falling: swaps wait until it's still.</summary>
    [DoNotSerialize]
    public bool IsBusy => Motions.Count > 0 || Clearing.Count > 0;

    /// <summary>How many clears the last swap has set off so far: 1 for a plain match, more for a chain.</summary>
    [DoNotSerialize]
    public int Chain { get; internal set; }

    /// <summary>The cell of the gem this machine's player has picked up to swap, or null.</summary>
    [DoNotSerialize]
    public (int X, int Y)? Picked => Hands.TryGetValue(LocalPlayer, out var hand) ? hand.Picked : Remote.GetValueOrDefault(LocalPlayer).Picked;

    /// <summary>The cell this machine's player's keyboard cursor is on, or null while they use the mouse.</summary>
    [DoNotSerialize]
    public (int X, int Y)? Cursor => Hands.TryGetValue(LocalPlayer, out var hand) ? hand.Cursor : Remote.GetValueOrDefault(LocalPlayer).Cursor;

    /// <summary>Raised as gems clear, once for each match (gems of one kind lined up together).</summary>
    public event Action<MatchInfo>? Matched;

    /// <summary>Raised as two gems swap for good, with the two cells.</summary>
    public event Action<(int X, int Y), (int X, int Y)>? Swapped;

    /// <summary>Raised once the board comes to rest after a swap, with the length of the chain it set off.</summary>
    public event Action<int>? Settled;

    /// <summary>Raised once the board comes to rest with the level over: true if its goals were met, false if the moves ran out.</summary>
    public event Action<bool>? Finished;

    /// <summary>How many gems of kind <paramref name="kind"/> (a tile number) have been cleared.</summary>
    public int ClearedOf(int kind) => ParseCounts(Cleared).GetValueOrDefault(kind);

    /// <summary>How many gems of kind <paramref name="kind"/> <see cref="Collect"/> asks for: 0 if none.</summary>
    public int ToCollect(int kind) => ParseCounts(Collect).GetValueOrDefault(kind);

    /// <summary>True if <paramref name="tile"/> is a gem rather than a block (or empty, -1).</summary>
    public bool IsGem(int tile) => Tilemap.InSet(_gemSet, tile);

    /// <summary>The gem kinds, lowest first.</summary>
    public IReadOnlyList<int> Kinds() => Enumerable.Range(0, _gemSet.Length).Where(i => _gemSet[i]).ToList();

    /// <summary>The tile in cell (<paramref name="x"/>, <paramref name="y"/>): a gem kind, a block, or -1 for empty or off the board.</summary>
    public int GemAt(int x, int y) =>
        x >= 0 && y >= 0 && x < Columns && y < Rows && Entity?.GetComponent<Tilemap>() is { } map ? map.GetTile(x, y) : -1;

    /// <summary>
    /// Swaps the gems in two neighboring cells, as a player does (sliding back if it lines nothing up and
    /// <see cref="OnlyMatchingSwaps"/> is on), once the board is still. False if they aren't two neighboring cells.
    /// </summary>
    public bool Swap(int x1, int y1, int x2, int y2)
    {
        if (Math.Abs(x1 - x2) + Math.Abs(y1 - y2) != 1)
            return false;
        SwapRequests.Add(((x1, y1), (x2, y2)));
        return true;
    }

    /// <summary>A swap that would line something up, or null if there's none: a hint to show the player.</summary>
    public ((int X, int Y) From, (int X, int Y) To)? FindMove() =>
        Entity?.GetComponent<Tilemap>() is { } map ? MatchRules.FindMove(TileGrid.Read(Columns, Rows, map), MatchLength, IsGem) : null;

    /// <summary>
    /// Deals a fresh board into <paramref name="map"/> straight away, as the game does when it starts: every gem replaced,
    /// none lined up, and a move to make. Blocks stay where they are.
    /// </summary>
    public void DealInto(Tilemap map, Random random)
    {
        var grid = TileGrid.Read(Columns, Rows, map);
        MatchRules.Deal(grid, Kinds(), MatchLength, IsGem, random, all: true);
        grid.Write(map);
    }

    /// <summary>Shuffles the gems at the start of the next frame, as a stuck board is.</summary>
    [Action]
    public void Shuffle() => ShuffleRequested = true;

    /// <summary>Stops the board where it is: nothing moves, falls or clears, and no swaps are taken, until it's resumed.</summary>
    [Action]
    public void Pause() => Paused = true;

    /// <summary>Lets a paused board go on.</summary>
    [Action]
    public void Resume() => Paused = false;

    /// <summary>Starts the board over at the start of the next frame: a fresh deal (blocks stay), no swaps, score or gems cleared.</summary>
    [Action]
    public void Restart() => RestartRequested = true;

    internal void RaiseMatched(MatchInfo match) => Matched?.Invoke(match);

    internal void RaiseSwapped((int X, int Y) a, (int X, int Y) b) => Swapped?.Invoke(a, b);

    internal void RaiseSettled(int chain) => Settled?.Invoke(chain);

    internal void RaiseFinished(bool won) => Finished?.Invoke(won);

    // Adds gems cleared of a kind to Cleared.
    internal void AddCleared(int kind, int count)
    {
        var counts = ParseCounts(Cleared);
        counts[kind] = counts.GetValueOrDefault(kind) + count;
        Cleared = string.Join(", ", counts.OrderBy(c => c.Key).Select(c => $"{c.Key}:{c.Value}"));
    }

    // "0:20, 3:15" as kind to count; pairs that don't read are skipped, and a kind named twice adds up.
    internal static Dictionary<int, int> ParseCounts(string? text)
    {
        var counts = new Dictionary<int, int>();
        foreach (var pair in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var kind) &&
                int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var count))
                counts[kind] = counts.GetValueOrDefault(kind) + count;
        }

        return counts;
    }

    // ----- What the game keeps while it runs (rewinds start these afresh) -----

    internal bool Started { get; set; }

    internal Random? Random { get; set; }

    // Gems drawn away from their cells, sliding or falling back in.
    internal Dictionary<(int X, int Y), GemMotion> Motions { get; } = [];

    // Gems shrinking away, and how long they've been at it.
    internal Dictionary<(int X, int Y), float> Clearing { get; } = [];

    // A swap under way: the two cells, and whether it's sliding back.
    internal ((int X, int Y) A, (int X, int Y) B, bool Back)? Swapping { get; set; }

    internal List<((int X, int Y) A, (int X, int Y) B)> SwapRequests { get; } = [];

    internal bool ShuffleRequested { get; set; }

    internal bool RestartRequested { get; set; }

    // A swap has set things off and the board hasn't come to rest yet.
    internal bool AwaitingSettle { get; set; }

    // The tiles after a shuffle that found no move, so a board with none to find isn't shuffled again and again.
    internal IReadOnlyList<string>? StuckTiles { get; set; }

    // Each player's picked-up gem and keyboard cursor (keyed by player id, 0 offline), and where their mouse went down.
    internal Dictionary<int, MatchHand> Hands { get; } = [];

    // This machine's player (0 offline), whose hand Picked and Cursor show.
    internal int LocalPlayer { get; set; }

    // On a player's machine, everyone's picked-up gem and cursor, as the host sends them (see MatchView).
    internal Dictionary<int, ((int X, int Y)? Picked, (int X, int Y)? Cursor)> Remote { get; } = [];

    // ----- How the gems were as the last step began, to draw them between steps (see StepBlend) -----

    // The step the gems last moved in: they're drawn between how they were and are only just after it.
    internal long MovedIn { get; set; } = -1;

    // How long each gem clearing had been at it as that step began.
    internal Dictionary<(int X, int Y), float> ClearingFrom { get; } = [];

    // Gems that came home in that step, drawn the rest of the way in.
    internal Dictionary<(int X, int Y), GemMotion> Landed { get; } = [];

    // Gems that finished clearing in that step, drawn shrinking the rest of the way away.
    internal List<VanishedGem> Vanished { get; } = [];
}

/// <summary>A gem that finished clearing in the last step: its cell and kind, and how long it had been clearing as the step began and ended.</summary>
internal readonly record struct VanishedGem(int X, int Y, int Tile, float From, float To);

/// <summary>Gems of one kind lined up and cleared together on a <see cref="MatchBoard"/>.</summary>
/// <param name="Kind">The gem's tile number.</param>
/// <param name="Cells">The cells cleared.</param>
/// <param name="Chain">1 for a match the swap made, 2 and on for those that lined up as gems landed.</param>
public sealed record MatchInfo(int Kind, IReadOnlyList<(int X, int Y)> Cells, int Chain)
{
    /// <summary>How many gems it cleared.</summary>
    public int Count => Cells.Count;
}

/// <summary>A gem drawn away from its cell: <see cref="From"/> cells off (Y down) to start with, back home over <see cref="Seconds"/>.</summary>
internal sealed class GemMotion(Vector2 from, float seconds, bool falls, bool grows = false)
{
    public Vector2 From { get; } = from;

    public float Seconds { get; } = Math.Max(seconds, 1e-4f);

    /// <summary>Falling speeds up as it goes; a slide eases in and out.</summary>
    public bool Falls { get; } = falls;

    /// <summary>Grows into its cell from nothing: a new gem under a block, or one shuffled in.</summary>
    public bool Grows { get; } = grows;

    /// <summary>
    /// Goes out to <see cref="From"/> and back home again instead: a swap that lines nothing up, the gem nudging into its
    /// neighbor's cell.
    /// </summary>
    public bool Returns { get; init; }

    public float Elapsed { get; set; }

    /// <summary>What <see cref="Elapsed"/> was as the last step began, to draw it between steps.</summary>
    public float ElapsedFrom { get; set; }

    public float Progress => ProgressAt(Elapsed);

    public bool IsDone => Elapsed >= Seconds;

    /// <summary>How far from its cell it's drawn now, in cells.</summary>
    public Vector2 Offset => OffsetAt(Elapsed);

    /// <summary>How far through it is <paramref name="elapsed"/> seconds in, from 0 to 1.</summary>
    public float ProgressAt(float elapsed) => Math.Clamp(elapsed / Seconds, 0, 1);

    /// <summary>How far from its cell it's drawn <paramref name="elapsed"/> seconds in, in cells.</summary>
    public Vector2 OffsetAt(float elapsed)
    {
        var t = ProgressAt(elapsed);
        if (Returns)
        {
            var away = t < 0.5f ? 2 * t : 2 - 2 * t;
            return From * (away * away * (3 - 2 * away));
        }

        var eased = Falls ? t * t : t * t * (3 - 2 * t);
        return From * (1 - eased);
    }
}

/// <summary>One player's hand on a <see cref="MatchBoard"/>: the gem they picked up, the keyboard cursor, a mouse drag.</summary>
internal sealed class MatchHand
{
    public (int X, int Y)? Picked { get; set; }

    public (int X, int Y)? Cursor { get; set; }

    // The cell the mouse went down on, while it's held, and whether that press picked it up just now.
    public (int X, int Y)? Pressed { get; set; }

    public bool PickedByPress { get; set; }

    // The arrow last held and how long, for repeats as it's held.
    public (int X, int Y) Held { get; set; }

    public float HeldFor { get; set; }

    public float NextRepeat { get; set; }
}

/// <summary>Finding matches and moves on a board of tiles, for <see cref="MatchBoard"/>.</summary>
internal static class MatchRules
{
    /// <summary>Every run of <paramref name="length"/> or more gems of a kind across or down, joined into matches where they touch.</summary>
    public static List<(int Kind, List<(int X, int Y)> Cells)> Find(TileGrid grid, int length, Func<int, bool> isGem)
    {
        length = Math.Max(2, length);
        var matched = new HashSet<(int, int)>();
        for (var y = 0; y < grid.Rows; y++)
            Runs(grid.Columns, x => (x, y));
        for (var x = 0; x < grid.Columns; x++)
            Runs(grid.Rows, y => (x, y));

        // Matched cells of one kind side by side are one match: an L or a T clears as one.
        var matches = new List<(int, List<(int X, int Y)>)>();
        var seen = new HashSet<(int, int)>();
        foreach (var start in matched.OrderBy(c => c.Item2).ThenBy(c => c.Item1))
        {
            if (!seen.Add(start))
                continue;
            var kind = grid[start.Item1, start.Item2];
            var cells = new List<(int X, int Y)>();
            var stack = new Stack<(int X, int Y)>([start]);
            while (stack.TryPop(out var cell))
            {
                cells.Add(cell);
                foreach (var next in new[] { (cell.X + 1, cell.Y), (cell.X - 1, cell.Y), (cell.X, cell.Y + 1), (cell.X, cell.Y - 1) })
                {
                    if (matched.Contains(next) && grid[next.Item1, next.Item2] == kind && seen.Add(next))
                        stack.Push(next);
                }
            }

            matches.Add((kind, cells.OrderBy(c => c.Y).ThenBy(c => c.X).ToList()));
        }

        return matches;

        void Runs(int count, Func<int, (int X, int Y)> at)
        {
            var from = 0;
            for (var i = 1; i <= count; i++)
            {
                var (fx, fy) = at(from);
                var kind = grid[fx, fy];
                var same = false;
                if (i < count)
                {
                    var (x, y) = at(i);
                    same = grid[x, y] == kind;
                }

                if (same)
                    continue;
                if (i - from >= length && isGem(kind))
                {
                    for (var j = from; j < i; j++)
                        matched.Add(at(j));
                }

                from = i;
            }
        }
    }

    /// <summary>True if the cell lines up <paramref name="length"/> of its kind across or down.</summary>
    public static bool MatchesAt(TileGrid grid, int x, int y, int length, Func<int, bool> isGem)
    {
        var kind = grid[x, y];
        if (!isGem(kind))
            return false;
        return Count(1, 0) + Count(-1, 0) + 1 >= length || Count(0, 1) + Count(0, -1) + 1 >= length;

        int Count(int dx, int dy)
        {
            var n = 0;
            for (var (cx, cy) = (x + dx, y + dy); grid[cx, cy] == kind; (cx, cy) = (cx + dx, cy + dy))
                n++;
            return n;
        }
    }

    /// <summary>The first swap of two neighboring gems that lines something up, reading row by row; null if there's none.</summary>
    public static ((int X, int Y) From, (int X, int Y) To)? FindMove(TileGrid grid, int length, Func<int, bool> isGem)
    {
        for (var y = 0; y < grid.Rows; y++)
        {
            for (var x = 0; x < grid.Columns; x++)
            {
                foreach (var (nx, ny) in new[] { (x + 1, y), (x, y + 1) })
                {
                    if (!isGem(grid[x, y]) || !isGem(grid[nx, ny]) || grid[x, y] == grid[nx, ny])
                        continue;
                    (grid[x, y], grid[nx, ny]) = (grid[nx, ny], grid[x, y]);
                    var lines = MatchesAt(grid, x, y, length, isGem) || MatchesAt(grid, nx, ny, length, isGem);
                    (grid[x, y], grid[nx, ny]) = (grid[nx, ny], grid[x, y]);
                    if (lines)
                        return ((x, y), (nx, ny));
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Fills every empty cell (or with <paramref name="all"/>, every gem's cell too) with gems that line nothing up, so the
    /// board starts still; blocks stay. Tries again until there's a move to make, if it can.
    /// </summary>
    public static void Deal(TileGrid grid, IReadOnlyList<int> kinds, int length, Func<int, bool> isGem, Random random, bool all)
    {
        if (kinds.Count == 0)
            return;
        var original = grid.Copy();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            // The cells to deal start empty, so what was in them (another deal, or gems painted by hand) can't sway what's
            // dealt: the same seed and blocks always deal the same board.
            for (var y = 0; y < grid.Rows; y++)
            {
                for (var x = 0; x < grid.Columns; x++)
                    grid[x, y] = original[x, y] < 0 || (all && isGem(original[x, y])) ? -1 : original[x, y];
            }

            for (var y = 0; y < grid.Rows; y++)
            {
                for (var x = 0; x < grid.Columns; x++)
                {
                    var was = original[x, y];
                    if (!(was < 0 || (all && isGem(was))))
                    {
                        grid[x, y] = was;
                        continue;
                    }

                    // A kind that doesn't line up with the gems already dealt to the left and above, if there is one.
                    var choices = kinds.OrderBy(_ => random.Next()).ToList();
                    grid[x, y] = choices[0];
                    foreach (var kind in choices)
                    {
                        grid[x, y] = kind;
                        if (!MatchesAt(grid, x, y, length, isGem))
                            break;
                    }
                }
            }

            if (FindMove(grid, length, isGem) is not null)
                return;
        }
    }

    /// <summary>
    /// Shuffles the gems among their cells (blocks stay) into a board with no matches and a move to make, if one can be
    /// found; returns where each gem came from.
    /// </summary>
    public static Dictionary<(int X, int Y), (int X, int Y)> Shuffle(TileGrid grid, int length, Func<int, bool> isGem, Random random)
    {
        var cells = new List<(int X, int Y)>();
        for (var y = 0; y < grid.Rows; y++)
        {
            for (var x = 0; x < grid.Columns; x++)
            {
                if (isGem(grid[x, y]))
                    cells.Add((x, y));
            }
        }

        var original = cells.Select(c => grid[c.X, c.Y]).ToList();
        var order = Enumerable.Range(0, cells.Count).ToList();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            order = order.OrderBy(_ => random.Next()).ToList();
            for (var i = 0; i < cells.Count; i++)
                grid[cells[i].X, cells[i].Y] = original[order[i]];
            if (Find(grid, length, isGem).Count == 0 && FindMove(grid, length, isGem) is not null)
                break;
        }

        return Enumerable.Range(0, cells.Count).ToDictionary(i => cells[i], i => cells[order[i]]);
    }
}

/// <summary>A <see cref="MatchBoard"/>'s slides, falls, clears and cursors as lines of text, for players' machines to draw.</summary>
internal static class MatchView
{
    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    public static IReadOnlyList<string> Write(MatchBoard board)
    {
        var lines = new List<string>();
        foreach (var ((x, y), m) in board.Motions.OrderBy(m => m.Key))
            lines.Add(string.Create(Invariant, $"m {x} {y} {m.From.X:0.##} {m.From.Y:0.##} {m.Seconds:0.###} {m.Elapsed:0.###} {(m.Falls ? 1 : 0)} {(m.Grows ? 1 : 0)} {(m.Returns ? 1 : 0)}"));
        foreach (var ((x, y), t) in board.Clearing.OrderBy(c => c.Key))
            lines.Add(string.Create(Invariant, $"c {x} {y} {t:0.###}"));
        foreach (var (player, hand) in board.Hands.OrderBy(h => h.Key))
        {
            if (hand.Picked is { } picked)
                lines.Add(string.Create(Invariant, $"p {player} {picked.X} {picked.Y}"));
            if (hand.Cursor is { } cursor)
                lines.Add(string.Create(Invariant, $"k {player} {cursor.X} {cursor.Y}"));
        }

        return lines;
    }

    public static void Read(MatchBoard board, IReadOnlyList<string>? lines)
    {
        board.Motions.Clear();
        board.Clearing.Clear();
        board.Remote.Clear();
        foreach (var line in lines ?? [])
        {
            var parts = line.Split(' ');
            int I(int i) => int.TryParse(parts[i], Invariant, out var v) ? v : 0;
            float F(int i) => float.TryParse(parts[i], System.Globalization.NumberStyles.Float, Invariant, out var v) ? v : 0;
            switch (parts[0])
            {
                case "m" when parts.Length >= 9:
                    board.Motions[(I(1), I(2))] = new GemMotion(new Vector2(F(3), F(4)), F(5), I(7) == 1, I(8) == 1) { Elapsed = F(6), Returns = parts.Length > 9 && I(9) == 1 };
                    break;
                case "c" when parts.Length >= 4:
                    board.Clearing[(I(1), I(2))] = F(3);
                    break;
                case "p" or "k" when parts.Length >= 4:
                    var was = board.Remote.GetValueOrDefault(I(1));
                    board.Remote[I(1)] = parts[0] == "p" ? (((int, int)?)(I(2), I(3)), was.Cursor) : (was.Picked, (I(2), I(3)));
                    break;
            }
        }
    }
}
