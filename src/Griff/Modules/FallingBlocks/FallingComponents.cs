using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.FallingBlocks;

/// <summary>
/// A falling-block board, with no code: pieces of four blocks drop in at the top one after another, the player slides
/// and turns them as they fall, and each locks where it lands. A row filled from side to side clears and the rows above
/// drop down; every so many rows the game speeds up, and it's over once a new piece has no room to come in. The blocks
/// that have landed are the tiles of its entity's <see cref="Tilemap"/>.
/// </summary>
/// <remarks>
/// The board is <see cref="Columns"/> by <see cref="Rows"/> cells from the tilemap's top left. Left and right (arrows,
/// A and D, the d-pad or the stick) slide the piece, held down they keep going; down drops it faster; up or the
/// <see cref="RotateAction"/> turns it; the <see cref="DropAction"/> drops it all the way at once. Each kind of piece
/// lands as its own tile (from <see cref="FirstTile"/>), so a tileset gives each its look; without a tileset image each
/// kind draws as a square of its own color. Tiles painted on the board to begin with are blocks already there. The
/// tiles, score, rows and level are saved with the scene, so a rewind goes back to them (with a new piece coming in). In
/// a network game the host runs the board and any player can move the piece.
/// </remarks>
[Category("Gameplay")]
[Requires(typeof(Tilemap), Note = "holds the blocks that have landed")]
[WorksWith(typeof(Counter), Note = "shows the score (Count Score), the rows cleared (\"Lines\") or the level (\"Level Up\", starting at 1)")]
[ComponentName("Griff.FallingBoard")]
public sealed partial class FallingBoard : Component, IDrawable, IDrawsTilemap, INetState, ICounterSource
{
    // The piece falling, what's coming and rows clearing, as lines.
    void INetState.Network(NetState state) => state.Add<IReadOnlyList<string>>(() => FallingView.Write(this).ToArray(), lines => FallingView.Read(this, lines));

    // A counter counting a score shows its.
    (int Value, int Total)? ICounterSource.CountFor(Counter counter) => counter.CountScore ? (Score, 0) : null;

    /// <summary>Every shape there is, in the order their tiles go: each lands as <see cref="FirstTile"/> plus its place here.</summary>
    public const string AllShapes = "IJLOSTZ";

    /// <summary>Cells across.</summary>
    public int Columns { get; set; } = 10;

    /// <summary>Cells down.</summary>
    public int Rows { get; set; } = 20;

    /// <summary>
    /// The shapes that come, by letter: I (four in a line), J, L, O (a square), S, T and Z. Each comes once in every
    /// round of them, shuffled, so none is kept away for long. "IIO" would deal twice as many I's as O's.
    /// </summary>
    public string? Pieces { get; set; } = AllShapes;

    /// <summary>The tile the I lands as; the J lands as the next, then L, O, S, T and Z.</summary>
    public int FirstTile { get; set; }

    /// <summary>Picks the order the pieces come in: the same number always gives the same ones. 0 is different each game.</summary>
    public int Seed { get; set; }

    /// <summary>True while it stands still: the piece doesn't fall and takes no moves. Its Pause and Resume actions set it.</summary>
    public bool Paused { get; set; }

    /// <summary>Seconds a piece takes to fall one row at level 1.</summary>
    [Group("Speed")]
    public float FallTime { get; set; } = 0.8f;

    /// <summary>How much faster each level falls: each level's fall takes this much of the one before's time (0.8 is a fifth quicker).</summary>
    public float Speedup { get; set; } = 0.8f;

    /// <summary>Rows to clear for each level: every this many, the level goes up and the pieces fall faster. 0 never speeds up.</summary>
    public int LinesPerLevel { get; set; } = 10;

    /// <summary>The level now, from 1. Set it higher to start faster. Saved with the scene, so a rewind takes it back.</summary>
    public int Level { get; set; } = 1;

    /// <summary>Seconds a row while down is held: the soft drop.</summary>
    public float SoftDropTime { get; set; } = 0.04f;

    /// <summary>Seconds a piece rests on what's under it before it locks. Sliding or turning it starts the wait again (a few times).</summary>
    public float LockTime { get; set; } = 0.5f;

    /// <summary>Seconds full rows flash before they clear and the rows above drop.</summary>
    public float ClearTime { get; set; } = 0.3f;

    /// <summary>Points for clearing one row, times the level. Two rows at once score three times as much, three five times, four eight times.</summary>
    [Group("Score")]
    public int Points { get; set; } = 100;

    /// <summary>The points scored so far. Saved with the scene, so a rewind takes them back.</summary>
    public int Score { get; set; }

    /// <summary>How many rows have been cleared. Saved with the scene, so a rewind takes them back.</summary>
    public int Lines { get; set; }

    /// <summary>True once the blocks have stacked up to the top and a new piece has no room: nothing more falls. Saved with the scene.</summary>
    public bool GameOver { get; set; }

    /// <summary>The input action that turns the piece clockwise, as up does: "Fire" is J, the gamepad's X or the right shoulder. Empty only uses up.</summary>
    [Group("Controls")]
    [Names(NameKind.Action)]
    public string? RotateAction { get; set; } = "Fire";

    /// <summary>The input action that drops the piece all the way down at once: "Jump" is Space or the gamepad's A. Empty for none.</summary>
    [Names(NameKind.Action)]
    public string? DropAction { get; set; } = "Jump";

    /// <summary>Seconds left or right is held before the piece keeps sliding.</summary>
    public float RepeatDelay { get; set; } = 0.17f;

    /// <summary>Seconds between each cell the piece slides while left or right is held.</summary>
    public float RepeatEvery { get; set; } = 0.05f;

    /// <summary>A color drawn behind the board's cells. Transparent draws nothing.</summary>
    [Group("Look")]
    public Color Background { get; set; } = Color.FromBytes(0x10, 0x14, 0x1C, 0xB0);

    /// <summary>The color of the outline showing where the piece will land. Transparent shows none.</summary>
    public Color Ghost { get; set; } = Color.FromBytes(0xFF, 0xFF, 0xFF, 0x40);

    /// <summary>How many of the pieces coming next to show, beside the board on the right. 0 shows none.</summary>
    public int Preview { get; set; } = 3;

    /// <summary>A sound played as the piece slides a cell, e.g. "Sounds/select.wav".</summary>
    [Group("Sound and Messages")]
    public string? MoveSound { get; set; }

    /// <summary>A sound played as the piece turns.</summary>
    public string? RotateSound { get; set; }

    /// <summary>A sound played as a piece locks into place.</summary>
    public string? LockSound { get; set; }

    /// <summary>A sound played as full rows clear.</summary>
    public string? ClearSound { get; set; }

    /// <summary>A sound played as the level goes up.</summary>
    public string? LevelUpSound { get; set; }

    /// <summary>A sound played when the game is over.</summary>
    public string? GameOverSound { get; set; }

    /// <summary>
    /// A message sent with <c>Game.Send</c> as rows clear, with how many as its data: a <see cref="Counter"/> listening for
    /// it adds them up.
    /// </summary>
    [Names(NameKind.Message)]
    public string? LinesMessage { get; set; } = "Lines";

    /// <summary>
    /// A message sent as rows clear, with the points they scored as its data: a <see cref="Counter"/> listening for it
    /// (its Message is "Score" to begin with) shows the score.
    /// </summary>
    [Names(NameKind.Message)]
    public string? ScoreMessage { get; set; } = "Score";

    /// <summary>
    /// A message sent as the level goes up, with how many levels it went up (1) as its data: a <see cref="Counter"/>
    /// listening for it, starting at 1, shows the level.
    /// </summary>
    [Names(NameKind.Message)]
    public string? LevelMessage { get; set; } = "Level Up";

    /// <summary>A message sent once the game is over, with this entity as its data: a panel with this as its Open Message shows it.</summary>
    [Names(NameKind.Message)]
    public string? GameOverMessage { get; set; } = "Game Over";

    /// <summary>The shape of the piece falling now, e.g. "T", or null between pieces.</summary>
    [DoNotSerialize]
    public string? Piece => Falling is { } piece ? piece.Shape.ToString() : null;

    /// <summary>The cell of the falling piece's box's top left (it may be off the board), or null between pieces.</summary>
    [DoNotSerialize]
    public (int X, int Y)? PieceAt => Falling is { } piece ? (piece.X, piece.Y) : null;

    /// <summary>How many quarter turns clockwise the falling piece has made, 0 to 3.</summary>
    [DoNotSerialize]
    public int Rotation => Falling?.Rotation ?? 0;

    /// <summary>The shapes coming next, first first, e.g. "SZO": as many as <see cref="Preview"/> shows, or more.</summary>
    [DoNotSerialize]
    public string NextPieces => new(Coming.ToArray());

    /// <summary>True while full rows are clearing: the next piece waits.</summary>
    [DoNotSerialize]
    public bool IsClearing => ClearingRows.Count > 0;

    /// <summary>Raised as a piece locks, with the cells it locked into.</summary>
    public event Action<IReadOnlyList<(int X, int Y)>>? Locked;

    /// <summary>Raised as full rows clear, with how many.</summary>
    public event Action<int>? Cleared;

    /// <summary>Raised as the level goes up, with the new level.</summary>
    public event Action<int>? LeveledUp;

    /// <summary>Raised when the game is over.</summary>
    public event Action? Ended;

    /// <summary>The tile in cell (<paramref name="x"/>, <paramref name="y"/>): a block that has landed, or -1 for empty or off the board.</summary>
    public int BlockAt(int x, int y) =>
        x >= 0 && y >= 0 && x < Columns && y < Rows && Entity?.GetComponent<Tilemap>() is { } map ? map.GetTile(x, y) : -1;

    /// <summary>The cells the falling piece covers now (empty between pieces).</summary>
    public IReadOnlyList<(int X, int Y)> PieceCells() => Falling is { } piece ? FallingShapes.Cells(piece.Shape, piece.Rotation, piece.X, piece.Y) : [];

    /// <summary>The cells the falling piece would land on if dropped now (empty between pieces).</summary>
    public IReadOnlyList<(int X, int Y)> LandingCells() =>
        Falling is { } piece ? FallingShapes.Cells(piece.Shape, piece.Rotation, piece.X, piece.Y + DropDistance()) : [];

    /// <summary>The tile a shape lands as: <see cref="FirstTile"/> plus its place in <see cref="AllShapes"/>.</summary>
    public int TileOf(char shape) => FirstTile + Math.Max(0, AllShapes.IndexOf(char.ToUpperInvariant(shape)));

    /// <summary>Slides the falling piece a cell left (-1) or right (1), as a player does, at the start of the next frame.</summary>
    public void Move(int direction) => Requests.Add(direction < 0 ? FallingRequest.Left : FallingRequest.Right);

    /// <summary>Turns the falling piece a quarter clockwise (or with <paramref name="clockwise"/> false, back), at the start of the next frame.</summary>
    public void Rotate(bool clockwise = true) => Requests.Add(clockwise ? FallingRequest.Turn : FallingRequest.TurnBack);

    /// <summary>Drops the falling piece all the way down and locks it, at the start of the next frame.</summary>
    public void Drop() => Requests.Add(FallingRequest.Drop);

    /// <summary>Stops the board where it is: the piece doesn't fall and takes no moves, until it's resumed.</summary>
    [Action]
    public void Pause() => Paused = true;

    /// <summary>Lets a paused board go on.</summary>
    [Action]
    public void Resume() => Paused = false;

    /// <summary>Starts the board over at the start of the next frame: empty, at the level it started at, with no score or lines.</summary>
    [Action]
    public void Restart() => RestartRequested = true;

    internal void RaiseLocked(IReadOnlyList<(int X, int Y)> cells) => Locked?.Invoke(cells);

    internal void RaiseCleared(int rows) => Cleared?.Invoke(rows);

    internal void RaiseLeveledUp(int level) => LeveledUp?.Invoke(level);

    internal void RaiseEnded() => Ended?.Invoke();

    // True if the shape fits there: on the board (or above it) and over no block.
    internal bool Fits(char shape, int rotation, int x, int y) =>
        FallingShapes.Cells(shape, rotation, x, y).All(c => c.X >= 0 && c.X < Columns && c.Y < Rows && (c.Y < 0 || BlockAt(c.X, c.Y) < 0));

    // How many rows the falling piece can drop before it rests on something.
    internal int DropDistance()
    {
        if (Falling is not { } piece)
            return 0;
        var rows = 0;
        while (rows <= Rows && Fits(piece.Shape, piece.Rotation, piece.X, piece.Y + rows + 1))
            rows++;
        return rows;
    }

    // ----- What the game keeps while it runs (rewinds start these afresh) -----

    internal bool Started { get; set; }

    internal bool RestartRequested { get; set; }

    // The level it started at, to go back to on a restart.
    internal int? StartLevel { get; set; }

    internal Random? Random { get; set; }

    internal FallingPiece? Falling
    {
        get => _falling;
        set
        {
            // The same piece slid or fallen is drawn between where it was and is; a new piece, or one turned, as it is.
            if (value is { } now && _falling is { } was && now.Shape == was.Shape && now.Rotation == was.Rotation)
                _pieceAt.Set(this, new Vector2(now.X, now.Y));
            else
                _pieceAt = new Smoothed<Vector2>(value is null ? Vector2.Zero : new Vector2(value.X, value.Y));
            _falling = value;
        }
    }

    private FallingPiece? _falling;

    // The shapes coming next, topped up a round at a time.
    internal List<char> Coming { get; } = [];

    // Full rows flashing before they clear, and how long they've been at it.
    internal List<int> ClearingRows { get; } = [];

    internal float ClearingFor
    {
        get => _clearingFor.Value;
        set => _clearingFor.Set(this, value);
    }

    // Seconds towards the next row down, and how long the piece has rested on something.
    internal float FallFor { get; set; }

    internal float RestingFor { get; set; }

    // How many times sliding or turning has started the lock wait again for this piece.
    internal int LockResets { get; set; }

    internal List<FallingRequest> Requests { get; } = [];

    // Each player's held left or right, for repeats (keyed by player id, 0 offline).
    internal Dictionary<int, (int Way, float HeldFor, float NextRepeat)> Held { get; } = [];

    // ----- How the board was as the last step began, to draw it between steps (see StepBlend) -----

    // Where the falling piece's box was and is, in cells, and how long the full rows had been clearing.
    private Smoothed<Vector2> _pieceAt;
    private Smoothed<float> _clearingFor;

    // The step full rows last went in, the blocks that went (and how long they had been clearing as it began and ended),
    // and how many rows each row now on the board dropped in it.
    internal long RemovedIn { get; set; } = -1;

    internal List<(int X, int Y, int Tile)> Removed { get; } = [];

    internal (float From, float To) RemovedClearing { get; set; }

    internal int[] DroppedBy { get; set; } = [];

    // The falling piece's box's top left in a frame drawn as `blend` says, in cells (each `size` world units): between
    // where it was and is if it slid or fell in the last step, where it ends up if that's a jump (see StepBlend).
    internal Vector2 PieceDrawn(StepBlend blend, float size) =>
        _pieceAt.ChangedIn == blend.Step && size > 0 ? blend.Between(_pieceAt.Was * size, _pieceAt.Value * size) / size : _pieceAt.Value;

    // How long the full rows have been clearing, in a frame drawn as `blend` says.
    internal float ClearingDrawn(StepBlend blend) => blend.Of(_clearingFor);
}

/// <summary>The piece falling on a <see cref="FallingBoard"/>: its shape, where its box's top left is, and how it's turned.</summary>
internal sealed record FallingPiece(char Shape, int X, int Y, int Rotation);

internal enum FallingRequest
{
    Left,
    Right,
    Turn,
    TurnBack,
    Drop,
}

/// <summary>The seven shapes of four blocks, each in a box it turns within.</summary>
internal static class FallingShapes
{
    // Each shape's cells, as it comes in (flat side down), in a box as wide as it is (the I's and O's are 4 and 2 wide,
    // the rest 3), y down.
    private static readonly Dictionary<char, (int Size, (int X, int Y)[] Cells)> Shapes = new()
    {
        ['I'] = (4, [(0, 1), (1, 1), (2, 1), (3, 1)]),
        ['J'] = (3, [(0, 0), (0, 1), (1, 1), (2, 1)]),
        ['L'] = (3, [(2, 0), (0, 1), (1, 1), (2, 1)]),
        ['O'] = (2, [(0, 0), (1, 0), (0, 1), (1, 1)]),
        ['S'] = (3, [(1, 0), (2, 0), (0, 1), (1, 1)]),
        ['T'] = (3, [(1, 0), (0, 1), (1, 1), (2, 1)]),
        ['Z'] = (3, [(0, 0), (1, 0), (1, 1), (2, 1)]),
    };

    public static bool IsShape(char shape) => Shapes.ContainsKey(shape);

    public static int Size(char shape) => Shapes[shape].Size;

    /// <summary>The cells a shape covers, turned <paramref name="rotation"/> quarters clockwise, with its box's top left at (x, y).</summary>
    public static IReadOnlyList<(int X, int Y)> Cells(char shape, int rotation, int x, int y)
    {
        var (size, cells) = Shapes[shape];
        var turns = ((rotation % 4) + 4) % 4;
        return cells.Select(c =>
        {
            var (cx, cy) = c;
            for (var i = 0; i < turns; i++)
                (cx, cy) = (size - 1 - cy, cx);
            return (x + cx, y + cy);
        }).ToList();
    }

    /// <summary>The letters of <paramref name="pieces"/> that are shapes, upper case; every shape if there are none.</summary>
    public static IReadOnlyList<char> Parse(string? pieces)
    {
        var shapes = (pieces ?? "").Select(char.ToUpperInvariant).Where(IsShape).ToList();
        return shapes.Count > 0 ? shapes : FallingBoard.AllShapes.ToList();
    }
}

/// <summary>A <see cref="FallingBoard"/>'s falling piece, what's coming and the rows clearing, as lines of text for players' machines to draw.</summary>
internal static class FallingView
{
    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    public static IReadOnlyList<string> Write(FallingBoard board)
    {
        var lines = new List<string>();
        if (board.Falling is { } piece)
            lines.Add(string.Create(Invariant, $"p {piece.Shape} {piece.X} {piece.Y} {piece.Rotation}"));
        if (board.Coming.Count > 0)
            lines.Add($"n {new string(board.Coming.ToArray())}");
        if (board.ClearingRows.Count > 0)
            lines.Add(string.Create(Invariant, $"c {board.ClearingFor:0.###} {string.Join(' ', board.ClearingRows)}"));
        return lines;
    }

    public static void Read(FallingBoard board, IReadOnlyList<string>? lines)
    {
        // The piece is set once, so one that slid or fell since the last update glides there (see FallingBoard.Falling).
        FallingPiece? piece = null;
        board.Coming.Clear();
        board.ClearingRows.Clear();
        foreach (var line in lines ?? [])
        {
            var parts = line.Split(' ');
            int I(int i) => int.TryParse(parts[i], Invariant, out var v) ? v : 0;
            switch (parts[0])
            {
                case "p" when parts.Length >= 5 && parts[1].Length == 1 && FallingShapes.IsShape(parts[1][0]):
                    piece = new FallingPiece(parts[1][0], I(2), I(3), I(4));
                    break;
                case "n" when parts.Length >= 2:
                    board.Coming.AddRange(parts[1].Where(FallingShapes.IsShape));
                    break;
                case "c" when parts.Length >= 2:
                    board.ClearingFor = float.TryParse(parts[1], System.Globalization.NumberStyles.Float, Invariant, out var t) ? t : 0;
                    board.ClearingRows.AddRange(Enumerable.Range(2, parts.Length - 2).Select(I));
                    break;
            }
        }

        board.Falling = piece;
    }
}
