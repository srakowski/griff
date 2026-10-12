namespace Griff.Scenes;

/// <summary>
/// Turn-based play with no code: sides take turns, each acting on its own turn while the others wait. List its
/// <see cref="Sides"/> in order ("Player, Enemy", or "Blue, Red" for two people at one keyboard) and give whatever acts a
/// <see cref="TurnTaker"/> naming its side. Off its side's turn a turn taker takes no input and its patrols, chases,
/// attacks and shooters hold still; everything else (physics, animations, things with no turn taker) runs in real time
/// as ever, so a turn can be a stretch of real-time play, as in Worms. A turn ends when its side presses End Turn (Tab,
/// the gamepad's Back, or a button whose Action is End Turn), when its time runs out, or once every turn taker on it has
/// used its actions. With a <see cref="Text"/> beside it, it shows whose turn it is. For a roguelike, where everything
/// moves when the player moves, give the grid player and the grid enemies turn takers with 1 action and set
/// <see cref="Between"/> to 0: each step is a turn, and an enemy with nowhere to go passes.
/// </summary>
/// <remarks>
/// One per scene (the first is used). In a network game the host runs it, and with <see cref="Multiplayer.Teams"/> each
/// player's character is on its player's team: "Blue, Red" sides for "Blue, Red" teams.
/// </remarks>
[Category("Gameplay")]
[WorksWith(typeof(TurnTaker), Note = "on whatever takes turns")]
[WorksWith(typeof(Text), Note = "shows whose turn it is")]
[ComponentName("Griff.TurnOrder")]
public sealed class TurnOrder : Component
{
    /// <summary>
    /// The sides in the order they take turns, separated by commas, e.g. "Player, Enemy". A side with no turn takers left
    /// is skipped (unless no side has any, for a game whose scripts take the turns).
    /// </summary>
    public string? Sides { get; set; } = "Player, Enemy";

    /// <summary>
    /// The sides the computer plays, e.g. "Enemy". End Turn doesn't end their turns: they end once their turn takers have
    /// used their actions or <see cref="ComputerTurnTime"/> runs out, or when a script ends them.
    /// </summary>
    public string? ComputerSides { get; set; } = "Enemy";

    /// <summary>Seconds a person's turn lasts before it passes by itself, e.g. 30 for a timer as in Worms. 0 lasts until they end it.</summary>
    [Group("Turns")]
    public float TurnTime { get; set; }

    /// <summary>
    /// Seconds a computer side's turn lasts at most: how long its patrols, chases and shooters get to act. 0 lasts until
    /// its turn takers have used their actions.
    /// </summary>
    [ShownWhen(nameof(ComputerSides))]
    public float ComputerTurnTime { get; set; } = 2;

    /// <summary>Seconds between one turn and the next, while nobody acts: a last shot lands, a swing finishes.</summary>
    public float Between { get; set; } = 0.5f;

    /// <summary>
    /// The input action that ends a person's turn: "EndTurn" is Tab or the gamepad's Back. Empty ends turns only with an
    /// End Turn button or a script.
    /// </summary>
    [Names(NameKind.Action)]
    public string? EndAction { get; set; } = "EndTurn";

    /// <summary>
    /// What its <see cref="Text"/> says: {0} is the side whose turn it is, {1} the round and {2} the seconds left of a
    /// timed turn, e.g. "{0}'s turn ({2})".
    /// </summary>
    [Group("Messages")]
    public string? Format { get; set; } = "{0}'s turn";

    /// <summary>
    /// A message sent with <c>Game.Send</c> as each turn starts, with the side's name as its data. {0} in it is the side's
    /// name: "{0}Turn" sends "PlayerTurn", then "EnemyTurn".
    /// </summary>
    [Names(NameKind.Message)]
    public string? Message { get; set; } = "{0}Turn";

    /// <summary>A sound played as each turn starts, e.g. "Sounds/turn.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>
    /// The side whose turn it is. Empty starts with the first side; set it to start with another, or from a script to hand
    /// the turn straight to a side (its turn starts afresh).
    /// </summary>
    [Group("State")]
    public string? Current { get; set; }

    /// <summary>How many times the turns have come round, from 1. The game counts it; it's 0 until the game starts.</summary>
    public int Round { get; set; }

    /// <summary>Seconds left of a timed turn. The game counts it down.</summary>
    public float TimeLeft { get; set; }

    /// <summary>Seconds until the <see cref="Current"/> side's turn starts, between turns. The game counts it down.</summary>
    public float StartsIn { get; set; }

    /// <summary>The sides in order, from <see cref="Sides"/>.</summary>
    public IReadOnlyList<string> SideList => Split(Sides);

    /// <summary>True between turns, while nobody acts.</summary>
    public bool IsBetweenTurns => StartsIn > 0;

    /// <summary>True while it's <paramref name="side"/>'s turn (not between turns).</summary>
    public bool IsTurn(string? side) => Round > 0 && StartsIn <= 0 && SameSide(side, Current);

    /// <summary>True if the computer plays <paramref name="side"/> (see <see cref="ComputerSides"/>).</summary>
    public bool IsComputer(string? side) => Split(ComputerSides).Any(s => SameSide(s, side));

    /// <summary>
    /// Ends the current turn at the start of the next frame, whoever's it is (a script's own End Turn, or a computer side
    /// whose script has finished its moves). Between turns it does nothing.
    /// </summary>
    public void EndTurn() => EndRequested = true;

    /// <summary>Raised as each side's turn starts, with the side's name.</summary>
    public event Action<string>? TurnStarted;

    /// <summary>Raised as each side's turn ends, with the side's name.</summary>
    public event Action<string>? TurnEnded;

    /// <summary>True if two side names are the same side (ignoring case and spaces at the ends).</summary>
    public static bool SameSide(string? a, string? b) =>
        string.Equals(a?.Trim() ?? "", b?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);

    internal void RaiseTurnStarted(string side) => TurnStarted?.Invoke(side);

    internal void RaiseTurnEnded(string side) => TurnEnded?.Invoke(side);

    private static string[] Split(string? names) =>
        (names ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Asked to end the turn (a script, or someone's End Turn); the side the game last started a turn for, so a script
    // setting Current is noticed. Not saved: a rewound game takes up the turn it finds.
    internal bool EndRequested { get; set; }

    internal string? Shown { get; set; }
}

/// <summary>
/// Takes turns in the scene's <see cref="TurnOrder"/>: it acts only on its <see cref="Side"/>'s turn. Off its turn the
/// player's controls don't reach it (nor scripts on it reading input) and its Patrol, Chase, Grid Chase, Grid Patrol,
/// Attack and Shooter hold still; it still falls, gets pushed and gets hurt. Give it <see cref="Actions"/> to limit what
/// it does in a turn: each attack swing, each shot and each step of its Grid Movement uses one, and once every turn taker
/// on a side has used theirs (and their swings have landed), the turn passes. With actions, a Grid Chase or Grid Patrol
/// steps once an action and passes when it has nowhere to go or nothing to swing at, and a Grid Patrol waits whole turns.
/// </summary>
[Category("Gameplay")]
[WorksWith(typeof(TurnOrder), Note = "the turns it takes, one per scene")]
[ComponentName("Griff.TurnTaker")]
public sealed class TurnTaker : Component
{
    /// <summary>
    /// The side it acts for, one of its turn order's Sides, e.g. "Player". In a network game with teams, a player's
    /// character is on its player's team instead.
    /// </summary>
    public string? Side { get; set; } = "Player";

    /// <summary>
    /// How many actions it gets each turn: attack swings, shots, grid steps, or <see cref="Act"/> from a script. Once it
    /// has used them it waits for its next turn. 0 has no limit, so its side's turn ends some other way.
    /// </summary>
    public int Actions { get; set; }

    /// <summary>How many of its actions it has used this turn. The game sets it back to 0 as each of its side's turns starts.</summary>
    [Group("State")]
    public int ActionsUsed { get; set; }

    /// <summary>How many actions it has left this turn; -1 with no limit.</summary>
    public int ActionsLeft => Actions > 0 ? Math.Max(0, Actions - ActionsUsed) : -1;

    /// <summary>True while it can act: it's its side's turn and it has actions left. The game keeps it up to date.</summary>
    [DoNotSerialize]
    public bool IsTurn { get; internal set; }

    /// <summary>
    /// Uses <paramref name="count"/> of its actions, for something a script does (a spell, a move). False, using none,
    /// if it isn't its turn or it doesn't have that many left.
    /// </summary>
    public bool Act(int count = 1)
    {
        if (!IsTurn || count < 0 || (Actions > 0 && ActionsUsed + count > Actions))
            return false;
        ActionsUsed += count;
        if (Actions > 0 && ActionsUsed >= Actions)
            IsTurn = false;
        return true;
    }

    /// <summary>Raised as its side's turn starts.</summary>
    public event Action<TurnTaker>? TurnStarted;

    /// <summary>Raised as its side's turn ends.</summary>
    public event Action<TurnTaker>? TurnEnded;

    /// <summary>The turn taker <paramref name="entity"/> acts for: its own, or the nearest above it (a sword's hitbox).</summary>
    public static TurnTaker? Of(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<TurnTaker>() is { } taker)
                return taker;
        }

        return null;
    }

    internal void RaiseTurnStarted() => TurnStarted?.Invoke(this);

    internal void RaiseTurnEnded() => TurnEnded?.Invoke(this);
}
