using Griff.Scenes;

namespace Griff.Modules.Fighting;

/// <summary>When a fighter blocks by itself, with no one holding back: for a training dummy, or a computer opponent.</summary>
public enum AutoBlock
{
    /// <summary>Only when its player holds back.</summary>
    Never,

    /// <summary>Everything that can be blocked standing (mids and highs), as if holding back.</summary>
    Standing,

    /// <summary>Everything that can be blocked crouching (mids and lows), as if holding down-back.</summary>
    Crouching,

    /// <summary>Everything, standing or crouching as each hit needs.</summary>
    Always,
}

/// <summary>Which part of a move a fighter is in: starting up, hitting (active) or recovering; or none, free to act.</summary>
public enum MovePhase
{
    None,
    Startup,
    Active,
    Recovery,
}

/// <summary>
/// A fighting game's fighter with no code: walks forward and back, jumps (up, forward or back) and crouches on the
/// directions, faces its opponent by itself, and does its <see cref="Moves"/>, each written in numpad notation
/// (<c>5P</c> a standing punch, <c>2K</c> a crouching kick, <c>236P</c> a quarter circle forward and punch, <c>623P</c> a
/// dragon punch, <c>[4]6P</c> a charge) and played from an animation clip whose hitboxes (drawn in the Clip Editor) are
/// the part that hits, over the startup, active and recovery frames the move gives. Holding back blocks: a blocked hit
/// does only chip damage and holds it in blockstun, and lows must be blocked crouching, overheads standing. A hit holds it
/// in hitstun (its <see cref="Hitboxes"/>' Hitstun), where it can do nothing, and the hits an opponent lands while it's
/// still stunned count up as a combo. A button pressed a few frames early still comes out the moment it can (the input
/// buffer), and moves cancel into the ones they're allowed to once they've connected. Edit its moves in Entity &gt; Edit
/// Moves, or keep them in a table.
/// </summary>
/// <remarks>
/// It plays its <see cref="Animation"/>'s Idle, Walk (or Run), WalkBack, Crouch, Jump, Block, CrouchBlock and Hurt clips
/// when it has them, and KO (or Hurt) once its <see cref="Health"/> runs out, doing nothing more. Everything is counted in steps (frames, at 60 a second) and kept with the scene, so the same input
/// does the same thing every time, and a rewind carries on mid-motion and mid-move. It reads the controls of whoever plays
/// it; in a network game the host runs it, as it lands every hit.
/// </remarks>
[Category("Fighting")]
[Requires(typeof(Body))]
[Requires(typeof(Collider))]
[Requires(typeof(Hitboxes))]
[WorksWith(typeof(Animation), Note = "plays each move's clip, and Idle, Walk, Crouch, Jump, Block and Hurt")]
[WorksWith(typeof(Health), Note = "takes the damage of hits, and chip damage of blocked ones")]
[ComponentName("Griff.FightingCharacter")]
public sealed class FightingCharacter : Component, IPlaytestReport
{
    private IReadOnlyList<string> _moves = [];
    private IReadOnlyList<FighterMove> _parsed = [];
    private string? _buttons = DefaultButtons;
    private IReadOnlyList<(string Name, string Action)> _buttonList = ParseButtons(DefaultButtons);
    private string[] _buttonNames = [.. ParseButtons(DefaultButtons).Select(b => b.Name)];
    private string? _historyText;
    private int _historyWritten = -1;

    /// <summary>The buttons a fighter starts with: P (the Punch action: J or the gamepad's X) and K (Kick: K or the gamepad's A).</summary>
    public const string DefaultButtons = "P Punch, K Kick";

    /// <summary>
    /// Its moves, a line each: a name and a colon, the input in numpad notation as the fighter faces right (6 forward, 2
    /// down, 5 neither; <c>j.</c> in front for the air; <c>[4]</c> for back held as a charge), then any of <c>clip Name</c>
    /// (the clip it plays: its name without spaces if left out), <c>frames 3/2/6</c> (steps of startup, active and
    /// recovery; left out, the clip's own speed), <c>low</c> or <c>high</c>, <c>blockstun 10</c>, <c>chip 0.25</c>,
    /// <c>push 2,0</c> (forward and up as it hits), <c>message Name</c>, <c>sound Path</c> and last <c>cancels special</c>
    /// (or move names). E.g. <c>Jab: 5P frames 3/2/6 cancels special</c>, <c>Fireball: 236P frames 12/4/30</c>. When two
    /// fit, the longer motion wins, then the one whose last direction came nearest the button, then the one listed first.
    /// </summary>
    public IReadOnlyList<string> Moves
    {
        get => _moves;
        set
        {
            _moves = value ?? [];
            _parsed = FighterMove.ParseAll(_moves);
        }
    }

    /// <summary>
    /// A table of more moves, a row each, after its own: the row's name, and columns Input, Clip, Startup, Active,
    /// Recovery, Hits (Low or High), Blockstun, Chip, Push, Cancels, Message and Sound. Empty for its own moves only.
    /// </summary>
    [Names(NameKind.Table)]
    public string? MovesTable { get; set; }

    /// <summary>
    /// The buttons its moves name and the input action each is, e.g. "P Punch, K Kick" or "L Light, M Medium, H Heavy" (with
    /// those actions added in Controls). Up to eight.
    /// </summary>
    public string? Buttons
    {
        get => _buttons;
        set
        {
            _buttons = value;
            _buttonList = ParseButtons(value);
            _buttonNames = [.. _buttonList.Select(b => b.Name)];
        }
    }

    /// <summary>Who it fights, by name: it faces them. Empty for the nearest other fighter not on its team.</summary>
    [EntityName]
    public string? Opponent { get; set; }

    /// <summary>Turns to face its opponent whenever it's on the ground and free, so forward is always towards them.</summary>
    public bool FaceOpponent { get; set; } = true;

    // ----- Movement -----

    /// <summary>How fast it walks forward, in units a second.</summary>
    [Group("Movement")]
    public float WalkSpeed { get; set; } = 3;

    /// <summary>How fast it walks back, in units a second.</summary>
    public float BackWalkSpeed { get; set; } = 2.4f;

    /// <summary>How high a jump goes, in units. 0 can't jump.</summary>
    public float JumpHeight { get; set; } = 2.6f;

    /// <summary>How fast a jump forward or back goes sideways, in units a second.</summary>
    public float JumpSpeed { get; set; } = 3.5f;

    /// <summary>How quickly it stops sliding on the ground (after a knockback or a push), in units a second per second.</summary>
    public float Friction { get; set; } = 30;

    // ----- Blocking -----

    /// <summary>Holding back (away from the opponent) blocks; down-back blocks crouching. Off, it never blocks unless <see cref="AutoBlock"/> says.</summary>
    [Group("Blocking")]
    public bool HoldBackToBlock { get; set; } = true;

    /// <summary>Blocks by itself: never, everything standing, everything crouching, or always (for a training dummy).</summary>
    public AutoBlock AutoBlock { get; set; }

    /// <summary>Its <see cref="AutoBlock"/> in words, for a training menu's label: "Never", "Standing", "Crouching" or "Always".</summary>
    [DoNotSerialize]
    [Output("Auto Block")]
    public string AutoBlockName => AutoBlock.ToString();

    /// <summary>How much of a blocked hit's damage it still takes (chip damage), 0 to 1. A move's own chip says otherwise.</summary>
    public float ChipDamage { get; set; } = 0.1f;

    /// <summary>How much of a hit's knockback a blocked one still pushes it back with, 0 to 1.</summary>
    public float BlockPushback { get; set; } = 0.5f;

    /// <summary>A sound played as it blocks a hit, e.g. "Sounds/block.wav".</summary>
    public string? BlockSound { get; set; }

    /// <summary>A prefab spawned where a blocked hit meets it: a spark.</summary>
    [Names(NameKind.Prefab)]
    public string? BlockEffect { get; set; }

    /// <summary>A message sent as it blocks a hit, with the chip damage as its data.</summary>
    [Names(NameKind.Message)]
    public string? BlockMessage { get; set; }

    // ----- Input -----

    /// <summary>
    /// How many steps (frames, at 60 a second) a press is remembered: one made that early still comes out the moment it
    /// can (as a move ends, or lands), once. 1 takes only presses made as it can act.
    /// </summary>
    [Group("Input")]
    public int BufferFrames { get; set; } = 8;

    /// <summary>How many steps a motion (a quarter circle, a dragon punch) may take, from its first direction to the button.</summary>
    public int MotionFrames { get; set; } = 16;

    /// <summary>How many steps a charge must be held (back for <c>[4]6P</c>, down for <c>[2]8K</c>) before it's let go.</summary>
    public int ChargeFrames { get; set; } = 40;

    /// <summary>
    /// Untick it to ignore the player's input (a training dummy, a cutscene); it still blocks by <see cref="AutoBlock"/>,
    /// and its Perform action still does moves. Its Focus action ticks it and unticks it on everything else the player plays.
    /// </summary>
    [Focus]
    public bool Controllable { get; set; } = true;

    // ----- State, kept with the scene -----

    /// <summary>The move it's doing, by name; empty when it's free. Set by the game.</summary>
    [Group("State")]
    [Output("Move")]
    public string? Move { get; set; }

    /// <summary>Steps into the move it's doing. Set by the game.</summary>
    public int MoveFrame { get; set; }

    /// <summary>True once the move it's doing has hit or been blocked: what lets it cancel. Set by the game.</summary>
    public bool Connected { get; set; }

    /// <summary>Steps of blockstun it has left: after blocking a hit, it can do nothing else until they're up. Set by the game.</summary>
    [Output]
    public int Blockstun { get; set; }

    /// <summary>True while it's crouching. Set by the game.</summary>
    [Output]
    public bool Crouching { get; set; }

    /// <summary>
    /// True while something holds it still and deaf to its controls (a versus match before a round's "Fight!" and after its
    /// K.O.): it stands, falls and recovers, but walks, jumps, blocks by hand and starts no moves. Set by the game.
    /// </summary>
    [Output]
    public bool Held { get; set; }

    /// <summary>True while it's blocking: holding back on the ground and free, or in blockstun. Set by the game.</summary>
    [Output]
    public bool Blocking { get; set; }

    /// <summary>True on the step it blocks a hit. Set by the game.</summary>
    [Output("Blocked")]
    public bool JustBlocked { get; set; }

    /// <summary>
    /// How many hits its current combo has: each hit it lands while the one it's hitting is still in hitstun from the last
    /// adds one; it's 0 again once they recover (<see cref="ComboHits"/> shows it from the second hit on). Set by the game.
    /// </summary>
    [Output]
    public int Combo { get; set; }

    /// <summary>Its combo's hits for a HUD: from the second hit on (one hit is no combo yet), 0 before.</summary>
    [DoNotSerialize]
    [Output("Combo Hits")]
    public int ComboHits => Combo >= 2 ? Combo : 0;

    /// <summary>The damage its current combo has done. Set by the game.</summary>
    [Output]
    public float ComboDamage { get; set; }

    /// <summary>The longest combo it has landed. Set by the game.</summary>
    [Output]
    public int BestCombo { get; set; }

    /// <summary>
    /// True if it stood on something after the last step's physics: what it walks, jumps and blocks by (kept with the
    /// scene, so a rewind knows it's on the ground before physics has run again). Set by the game.
    /// </summary>
    [NotSent]
    public bool OnGround { get; set; } = true;

    /// <summary>Who its combo is on (an id). Set by the game.</summary>
    [NotSent]
    public string? ComboOn { get; set; }

    /// <summary>
    /// What it has pressed lately, oldest first: a direction a step as it faced (with any buttons pressed then), a run of
    /// the same written once with how many ("5*40 2 3 6P"). What motions and the input buffer read. Set by the game.
    /// </summary>
    [NotSent]
    public string? History
    {
        get
        {
            if (_historyWritten != Input.Version)
                (_historyText, _historyWritten) = (Input.Write(_buttonNames), Input.Version);
            return _historyText;
        }
        set
        {
            Input.Read(value, _buttonNames);
            (_historyText, _historyWritten) = (value, Input.Version);
        }
    }

    /// <summary>Which part of its move it's in: Startup, Active (its hitboxes hit), Recovery, or None when it's free.</summary>
    [DoNotSerialize]
    [Output("Phase")]
    public string PhaseName => Phase switch
    {
        MovePhase.Startup => "Startup",
        MovePhase.Active => "Active",
        MovePhase.Recovery => "Recovery",
        _ => "",
    };

    /// <summary>Which part of its move it's in.</summary>
    [DoNotSerialize]
    public MovePhase Phase { get; internal set; }

    /// <summary>True while it can do something new: not in a move, hitstun or blockstun.</summary>
    [DoNotSerialize]
    [Output("Free")]
    public bool IsFree => string.IsNullOrEmpty(Move) && Blockstun <= 0 && (Entity?.GetComponent<Hitboxes>()?.Hitstun ?? 0) <= 0;

    /// <summary>Its <see cref="Moves"/> read (those that make sense), without the table's.</summary>
    [DoNotSerialize]
    public IReadOnlyList<FighterMove> MoveList => _parsed;

    /// <summary>Its <see cref="Buttons"/> read: each button's name and the action it is.</summary>
    [DoNotSerialize]
    public IReadOnlyList<(string Name, string Action)> ButtonList => _buttonList;

    // Ids, timers and the input history only the game reads.
    public override bool Uses(string property) =>
        property is not (nameof(MoveFrame) or nameof(Connected) or nameof(ComboOn) or nameof(History) or nameof(OnGround));

    public override string? SetupNote() => "Edit its moves in Entity > Edit Moves, and draw their hitboxes in the Clip Editor.";

    /// <summary>Raised as it starts a move, with the move.</summary>
    public event Action<FighterMove>? MoveStarted;

    /// <summary>Raised as it blocks a hit, with the hit (its damage the chip it took).</summary>
    public event Action<HitInfo>? Blocked;

    /// <summary>Does the move called <paramref name="move"/> next step if it's free then, as if its input were pressed: for a trigger, a wire or a computer player.</summary>
    [Action]
    public void Perform(string move) => Requested = string.IsNullOrWhiteSpace(move) ? null : move.Trim();

    /// <summary>Ends its combo count (the HUD's "hits" goes back to 0).</summary>
    [Action]
    public void ResetCombo() => (Combo, ComboDamage, ComboOn) = (0, 0, null);

    /// <summary>Changes its <see cref="AutoBlock"/> to the next: Never, Standing, Crouching, Always, and round again (a training dummy's guard).</summary>
    [Action]
    public void NextAutoBlock() => AutoBlock = AutoBlock == AutoBlock.Always ? AutoBlock.Never : AutoBlock + 1;

    /// <summary>
    /// Puts it back as a round starts: no move, blockstun, combo or presses waiting, and free (its hitstun is its
    /// <see cref="Hitboxes"/>'). Its health and place are the match's to set.
    /// </summary>
    internal void ResetForRound()
    {
        (Move, Current, MoveFrame, Connected, Phase, Requested) = (null, null, 0, false, MovePhase.None, null);
        (Blockstun, Blocking, JustBlocked, Crouching) = (0, false, false, false);
        (Combo, ComboDamage, ComboOn) = (0, 0, null);
        Input.Clear();
        _historyWritten = -1;
    }

    // ----- For the game -----

    /// <summary>What it has pressed lately (see <see cref="History"/>).</summary>
    internal InputHistory Input { get; } = new();

    internal string[] ButtonNames => _buttonNames;

    /// <summary>A move asked for by the Perform action, done next step if it can.</summary>
    internal string? Requested { get; set; }

    /// <summary>The way it held the stick this step in the world (-1 left, 1 right) and whether down: what blocking reads.</summary>
    internal int HeldX { get; set; }

    internal bool HeldDown { get; set; }

    /// <summary>The move it's doing, or null.</summary>
    internal FighterMove? Current { get; set; }

    internal void RaiseMoveStarted(FighterMove move) => MoveStarted?.Invoke(move);

    internal void RaiseBlocked(HitInfo hit) => Blocked?.Invoke(hit);

    /// <summary>"P Punch, K Kick" read: a name and an action each, up to eight.</summary>
    public static IReadOnlyList<(string Name, string Action)> ParseButtons(string? text)
    {
        var buttons = new List<(string, string)>();
        foreach (var entry in (text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var words = entry.Split([' ', '\t', '=', ':'], 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length == 0 || !words[0].All(char.IsLetter) || buttons.Count >= 8)
                continue;
            buttons.Add((words[0].ToUpperInvariant(), words.Length > 1 ? words[1] : words[0]));
        }

        return buttons;
    }

    string IPlaytestReport.Report() =>
        (string.IsNullOrEmpty(Move) ? "" : $", doing {Move} ({PhaseName.ToLowerInvariant()}, step {MoveFrame})") +
        (Blockstun > 0 ? $", blockstun {Blockstun}" : "") + (Combo > 1 ? $", a {Combo}-hit combo" : "");

    // ----- Frame data -----

    /// <summary>
    /// How <paramref name="move"/> plays on a fighter with <paramref name="animation"/> and <paramref name="boxes"/> at
    /// <paramref name="stepRate"/> steps a second: its startup, active and recovery in steps (its own, or from its clip's
    /// frames before, with and after hitboxes), and which of the clip's frames shows at each step.
    /// </summary>
    public static MoveTiming TimingOf(FighterMove move, Animation? animation, Hitboxes? boxes, int stepRate = 60)
    {
        var clip = animation?.ClipList.FirstOrDefault(c => string.Equals(c.Name, move.ClipName, StringComparison.OrdinalIgnoreCase));
        return TimingOf(move, animation, boxes, stepRate, clip?.Name);
    }

    // Without looking the clip up by LINQ: the game asks every step.
    internal static MoveTiming TimingOf(FighterMove move, Animation? animation, Hitboxes? boxes, int stepRate, string? clip)
    {
        if (animation is null || clip is null)
        {
            var (s, a, r) = (move.Startup, move.Active, move.Recovery);
            if (s + a + r == 0)
                (s, a, r) = (1, 1, 1);
            return new MoveTiming(s, a, r, 0, 1, -1, -1, false, move.Startup == 0, move.Active == 0, move.Recovery == 0);
        }

        var (_, count, fps, _) = animation.ClipSettings(clip);
        count = Math.Max(1, count);
        var (firstHit, lastHit) = (-1, -1);
        for (var frame = 0; frame < count && boxes is not null; frame++)
        {
            foreach (var box in boxes.On(clip, frame))
            {
                if (!box.IsHit)
                    continue;
                if (firstHit < 0)
                    firstHit = frame;
                lastHit = frame;
                break;
            }
        }

        var (before, during) = firstHit < 0 ? (count, 0) : (firstHit, lastHit - firstHit + 1);
        var after = count - before - during;
        int Natural(int frames) => frames == 0 ? 0 : Math.Max(1, (int)MathF.Round(frames * stepRate / (fps > 0 ? fps : 10)));
        var startup = move.Startup > 0 ? move.Startup : Natural(before);
        var active = move.Active > 0 ? move.Active : Natural(during);
        var recovery = move.Recovery > 0 ? move.Recovery : Natural(after);
        if (startup + active + recovery == 0)
            startup = 1;
        return new MoveTiming(startup, active, recovery, count, fps, firstHit, lastHit, true, move.Startup == 0, move.Active == 0, move.Recovery == 0);
    }
}

/// <summary>
/// How a move plays: its startup, active and recovery in steps, and the clip they're shown with (how many frames it has, at
/// what speed, and which of them have hitboxes).
/// </summary>
/// <param name="Startup">Steps before it can hit.</param>
/// <param name="Active">Steps it can hit for.</param>
/// <param name="Recovery">Steps after, before the fighter is free.</param>
/// <param name="FrameCount">How many frames its clip has (0 with no clip).</param>
/// <param name="FramesPerSecond">How fast its clip plays by itself.</param>
/// <param name="FirstHit">Its clip's first frame with a hitbox, or -1.</param>
/// <param name="LastHit">Its clip's last frame with a hitbox, or -1.</param>
/// <param name="HasClip">True if the fighter's animation has its clip.</param>
/// <param name="StartupFromClip">True if its startup is its clip's own (the move gives none).</param>
/// <param name="ActiveFromClip">True if its active steps are its clip's own.</param>
/// <param name="RecoveryFromClip">True if its recovery is its clip's own.</param>
public readonly record struct MoveTiming(int Startup, int Active, int Recovery, int FrameCount, float FramesPerSecond, int FirstHit, int LastHit,
    bool HasClip, bool StartupFromClip, bool ActiveFromClip, bool RecoveryFromClip)
{
    /// <summary>Steps it takes, start to end.</summary>
    public int Total => Startup + Active + Recovery;

    /// <summary>Which part of it step <paramref name="step"/> (from 0) is in.</summary>
    public MovePhase PhaseAt(int step) =>
        step < 0 || step >= Total ? MovePhase.None : step < Startup ? MovePhase.Startup : step < Startup + Active ? MovePhase.Active : MovePhase.Recovery;

    /// <summary>
    /// Which of its clip's frames shows at step <paramref name="step"/>: its frames before its hitboxes spread over its
    /// startup, those with hitboxes over its active steps and the rest over its recovery (a clip with no hitboxes over the
    /// whole move).
    /// </summary>
    public int FrameAt(int step)
    {
        if (FrameCount <= 0)
            return 0;
        step = Math.Clamp(step, 0, Math.Max(0, Total - 1));
        if (FirstHit < 0)
            return Spread(0, FrameCount, step, Total, 0);
        var during = LastHit - FirstHit + 1;
        var after = FrameCount - FirstHit - during;
        if (step < Startup)
            return Spread(0, FirstHit, step, Startup, 0);
        if (step < Startup + Active)
            return Spread(FirstHit, during, step - Startup, Active, FirstHit);
        return Spread(LastHit + 1, after, step - Startup - Active, Recovery, LastHit);
    }

    // A span's frames spread evenly over its steps; with no frames of its own, the one beside it.
    private static int Spread(int first, int frames, int step, int steps, int otherwise) =>
        frames <= 0 || steps <= 0 ? otherwise : first + Math.Min(frames - 1, step * frames / steps);
}
