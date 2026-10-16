using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.Rhythm;

/// <summary>Which way a <see cref="NoteLanes"/>' notes travel to its hit line.</summary>
public enum NoteDirection
{
    /// <summary>From the top down to a hit line along the bottom.</summary>
    Down,

    /// <summary>From the bottom up to a hit line along the top.</summary>
    Up,

    /// <summary>From the right to a hit line on the left, the lanes one above another.</summary>
    Left,

    /// <summary>From the left to a hit line on the right, the lanes one above another.</summary>
    Right,
}

/// <summary>How well a note was hit, as a <see cref="NoteLanes"/> judges it.</summary>
public enum NoteJudgment
{
    /// <summary>Not judged yet.</summary>
    None,

    /// <summary>Within the Perfect Window of the beat: 300 points.</summary>
    Perfect,

    /// <summary>Within the Great Window: 200 points.</summary>
    Great,

    /// <summary>Within the Good Window: 100 points.</summary>
    Good,

    /// <summary>Let go by, pressed too early, or a hold let go of too soon: no points, and the combo starts again.</summary>
    Miss,
}

/// <summary>
/// A rhythm game's lanes, with no code: notes from a chart (a table of beats and lanes) scroll toward a hit line in time
/// with the music, and the player presses each lane's key, button or touch zone as its notes reach the line, holding a hold
/// note down until its end. Each press is judged by how far it is from the note's beat as the player hears it (their audio
/// offset allowed for), Perfect, Great, Good or Miss, which adds to the <see cref="Score"/>, the <see cref="Combo"/> and the
/// <see cref="Accuracy"/>, pops up over the lanes and sends a message for anything else in the game to act on.
/// </summary>
/// <remarks>
/// The chart is a table (Data &gt; New Table, or Entity &gt; Genres &gt; Rhythm &gt; Create Note Lanes, which makes one with
/// a song): a row a note, with its <c>Beat</c> (counting from 0, the music's first, as a <see cref="BeatClock"/> counts),
/// its <c>Lane</c> (1 for the first), a <c>Hold</c> in beats for a note to hold down (empty or 0 for a tap) and the
/// <c>Difficulty</c> it's in (empty for every one; "Easy, Normal" for two). It plays its <see cref="Song"/> (or keeps time
/// with the music playing) by its <see cref="BeatClock"/>, drawing the notes ahead by the player's visual offset so they
/// cross the line as the beat is heard, and judging each press by the beat as it's heard. It starts as it's shown (its
/// Start action starts it again, Play at a difficulty), pauses with the game, and says when the song is over with its
/// <see cref="CompleteMessage"/>. Each machine of a network game plays its own player's lanes and judges them there; what
/// they score goes to the host, which sends the messages and adds the points to that player's score.
/// </remarks>
[Category("Rhythm")]
[ComponentName("Griff.NoteLanes")]
[WorksWith(typeof(BeatClock), Note = "keeps the time the notes are judged by")]
[WorksWith(typeof(Text), Note = "shows its Score, Combo, Accuracy, Judgment or Grade")]
[WorksWith(typeof(Trigger), Note = "does something on a Hit, a Perfect or a Miss")]
[NetLocal]
public sealed class NoteLanes : Component, IDrawable, IPlaytestReport
{
    /// <summary>Points for a Perfect, a Great and a Good.</summary>
    public const int PerfectPoints = 300, GreatPoints = 200, GoodPoints = 100;

    private string? _actions = "Lane1, Lane2, Lane3, Lane4";
    private string?[] _actionList = ["Lane1", "Lane2", "Lane3", "Lane4"];
    private string? _colors = DefaultColors;
    private Color[] _colorList = ParseColors(DefaultColors);
    private const string DefaultColors = "#F2607E #F2BC4B #4BC4F2 #9A72F2";

    /// <summary>
    /// The song to play, e.g. "Music/song.wav": from the start as the lanes start, once through. Its chart is the table named
    /// like it (Music/Groove.wav plays the Groove table) unless <see cref="Chart"/> says. Empty keeps time with the music
    /// playing (a Background Music's), or with none, clicks a metronome at its clock's tempo.
    /// </summary>
    [Group("Chart")]
    [AssetFile(AssetFileKind.Music)]
    public string? Song { get; set; }

    /// <summary>
    /// A name in the save data holding the song to play, e.g. "Song" (where a <see cref="SongSelect"/> keeps the one picked):
    /// while a song is saved there it's played in place of <see cref="Song"/>, with its own chart (the table named like it).
    /// </summary>
    [Names(NameKind.Saved)]
    public string? SavedSong { get; set; }

    /// <summary>
    /// The table of notes to play: a row a note, with a Beat, a Lane (from 1), a Hold in beats (empty for a tap) and the
    /// Difficulty it's in (empty for all). Empty: the table named like the <see cref="Song"/> (or the music playing).
    /// </summary>
    [Names(NameKind.Table)]
    public string? Chart { get; set; }

    /// <summary>
    /// Which of the chart's difficulties to play, e.g. "Hard": its notes whose Difficulty names it, and those that name none.
    /// Empty plays only the notes that name none.
    /// </summary>
    [Difficulties(One = true)]
    public string? Difficulty { get; set; } = "Normal";

    /// <summary>
    /// A value a menu saved (a <see cref="Choice"/>'s or a <see cref="SongSelect"/>'s Save As, e.g. "difficulty") naming the
    /// difficulty to play, when the chart has it; <see cref="Difficulty"/> otherwise.
    /// </summary>
    [Names(NameKind.Saved)]
    public string? SavedDifficulty { get; set; }

    /// <summary>The entity whose <see cref="BeatClock"/> times the notes. Empty: its own, or else the scene's first.</summary>
    [EntityName]
    public string? Clock { get; set; }

    /// <summary>Starts as soon as it's shown (as the scene starts, or its panel opens). Off, it waits for its Start or Play action.</summary>
    public bool StartOnShow { get; set; } = true;

    /// <summary>How loud its <see cref="Song"/> plays, 0 to 1, on top of the player's music volume.</summary>
    public float Volume { get; set; } = 0.8f;

    /// <summary>How many lanes, side by side.</summary>
    [Group("Lanes")]
    public int Lanes { get; set; } = 4;

    /// <summary>Which way the notes travel to the hit line.</summary>
    public NoteDirection Direction { get; set; }

    /// <summary>
    /// How fast the notes come, in world units a second: faster spreads them out. Each player can make it faster or slower
    /// still with their own note speed (<c>Game.NoteSpeed</c>, which a slider can set).
    /// </summary>
    public float Speed { get; set; } = 10;

    /// <summary>How long the lanes are, from where the notes come in to the far end past the hit line, in world units.</summary>
    public float Length { get; set; } = 9;

    /// <summary>How wide each lane is, in world units.</summary>
    public float LaneWidth { get; set; } = 1.3f;

    /// <summary>How far the hit line is from the end of the lanes the notes travel to, in world units.</summary>
    public float HitLine { get; set; } = 1.2f;

    /// <summary>
    /// The input actions that press the lanes, the first lane's first, separated by commas: Lane1 to Lane4 are D, F, J and K,
    /// the arrows, and the D-pad or the face buttons. A lane without one is pressed by touch alone.
    /// </summary>
    [Group("Controls")]
    [Names(NameKind.Action, Many = true)]
    public string? Actions
    {
        get => _actions;
        set
        {
            _actions = value;
            _actionList = [.. (value ?? "").Split(',').Select(a => a.Trim() is { Length: > 0 } name ? name : null)];
        }
    }

    /// <summary>A click or a tap on a lane presses it, and on a touch screen each finger on one does, so chords and holds play by touch.</summary>
    public bool Clicks { get; set; } = true;

    /// <summary>
    /// Untick it to have the lanes ignore the keys, mouse, touches and gamepad while the song plays on: another part of the
    /// scene has the player's input. Its Focus action ticks it and unticks it on everything else the player plays.
    /// </summary>
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>A press this close to a note's beat (early or late), in milliseconds, is Perfect.</summary>
    [Group("Judging")]
    public float PerfectWindow { get; set; } = 45;

    /// <summary>A press this close, in milliseconds, is Great.</summary>
    public float GreatWindow { get; set; } = 90;

    /// <summary>A press this close, in milliseconds, is Good. A note this late with no press is a Miss.</summary>
    public float GoodWindow { get; set; } = 135;

    /// <summary>
    /// A press earlier than the Good Window but this close, in milliseconds, is a Miss: pressed too soon. Further off, a
    /// press isn't taken for that note at all.
    /// </summary>
    public float MissWindow { get; set; } = 180;

    /// <summary>
    /// The notes' colors, a lane each from the first, as hex colors separated by spaces ("#F2607E #F2BC4B"), round again
    /// for more lanes than colors.
    /// </summary>
    [Group("Look")]
    [ListOf(What = "Color", Colors = true, Separator = " ")]
    public string? Colors
    {
        get => _colors;
        set
        {
            _colors = value;
            _colorList = ParseColors(value);
        }
    }

    /// <summary>The lanes' own color, behind the notes. Transparent draws none.</summary>
    public Color LaneColor { get; set; } = Color.FromBytes(0x10, 0x14, 0x22, 0xC8);

    /// <summary>How big the judgment that pops up as a note is hit (and the combo under it) is drawn, in world units. 0 shows none.</summary>
    public float PopSize { get; set; } = 0.7f;

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/> (an entity's sprite says for it).</summary>
    public int Order { get; set; } = 300;

    /// <summary>A sound played on each hit (not a Miss), e.g. "Sounds/tick.wav". Empty plays none: the music is the sound.</summary>
    [Group("Sound and Messages")]
    public string? HitSound { get; set; }

    /// <summary>A sound played on each Miss.</summary>
    public string? MissSound { get; set; }

    /// <summary>
    /// A message sent on each Perfect, Great and Good, with the points it scored as its data: a <see cref="Counter"/>
    /// listening for it adds up the score, and a <see cref="Trigger"/> can fire a weapon on it.
    /// </summary>
    [Names(NameKind.Message)]
    public string? HitMessage { get; set; } = "Hit";

    /// <summary>A message sent on each Perfect, as well as the <see cref="HitMessage"/>: for a bonus.</summary>
    [Names(NameKind.Message)]
    public string? PerfectMessage { get; set; } = "Perfect";

    /// <summary>A message sent on each Miss.</summary>
    [Names(NameKind.Message)]
    public string? MissMessage { get; set; } = "Miss";

    /// <summary>
    /// A message sent once the song is over (every note judged, and its song done), with the score as its data: a panel with
    /// this as its Open Message shows the results.
    /// </summary>
    [Names(NameKind.Message)]
    public string? CompleteMessage { get; set; } = "Song Complete";

    /// <summary>
    /// A name to keep the best score of each song and difficulty under in the save data, with its grade: "Best" keeps a Normal
    /// play of the Groove chart as "Best.Groove.Normal" (and "Best.Groove.Normal.Grade"), which <see cref="Best"/> and a
    /// <see cref="SongSelect"/> show. Empty keeps none.
    /// </summary>
    [Group("Scores")]
    [Names(NameKind.Saved, Defines = true)]
    public string? SaveBestAs { get; set; } = "Best";

    /// <summary>True while its chart plays. Saved with the scene, so a rewind carries the song on from there.</summary>
    [Group("State")]
    [Output]
    public bool IsPlaying { get; set; }

    /// <summary>Seconds into its <see cref="Song"/> it has got to. The game sets it, so a rewind or a pause carries the song on from there.</summary>
    public float SongTime { get; set; }

    /// <summary>
    /// How each note went, in the chart's order, two marks a note (the press, then a hold's end): . not yet, P Perfect, G
    /// Great, O Good, M Miss, S left out (gone by before it started), - no end (a tap). The game sets it; a rewind takes it
    /// back.
    /// </summary>
    [PlainText("how each note went: the game writes it")]
    public string? Played
    {
        get
        {
            if (_playedText is null)
                _playedText = new string(Marks);
            return _playedText;
        }
        set
        {
            Marks = (value ?? "").ToCharArray();
            _playedText = value ?? "";
            MarksSet = true;
        }
    }

    /// <summary>The points scored: 300 a Perfect, 200 a Great, 100 a Good.</summary>
    [Output]
    public int Score { get; set; }

    /// <summary>Hits in a row since the last Miss (a hold's end counts as one).</summary>
    [Output]
    public int Combo { get; set; }

    /// <summary>The longest combo so far.</summary>
    [Output]
    public int BestCombo { get; set; }

    /// <summary>How many Perfects so far.</summary>
    [Output]
    public int Perfects { get; set; }

    /// <summary>How many Greats so far.</summary>
    [Output]
    public int Greats { get; set; }

    /// <summary>How many Goods so far.</summary>
    [Output]
    public int Goods { get; set; }

    /// <summary>How many Misses so far.</summary>
    [Output]
    public int Misses { get; set; }

    /// <summary>
    /// The best score kept for the song and difficulty it plays (see <see cref="SaveBestAs"/>), 0 before any: this play's
    /// once it's over and beats it. Set by the game.
    /// </summary>
    [Output]
    public int Best { get; set; }

    /// <summary>True once the song is over with a score beating the best kept for it: a "New best!" on the results. Set by the game.</summary>
    [Output("New Best")]
    public bool NewBest { get; set; }

    /// <summary>True while every note judged so far was hit (one at least, and no Miss): a "Full combo!" on the results.</summary>
    [DoNotSerialize]
    [Output("Full Combo")]
    public bool FullCombo => Misses == 0 && Perfects + Greats + Goods > 0;

    /// <summary>The name of the chart it plays (its song's, "Groove" for Music/Groove.wav): a title over the lanes or on the results.</summary>
    [DoNotSerialize]
    [Output]
    public string Title { get; internal set; } = "";

    /// <summary>The difficulty it plays: a saved one (see <see cref="SavedDifficulty"/>), or its <see cref="Difficulty"/>.</summary>
    [DoNotSerialize]
    [Output("Playing")]
    public string PlayingDifficulty { get; internal set; } = "";

    /// <summary>How far through the chart it is, from 0 at the start of the song to 1 at its last note's end: a bar shows it.</summary>
    [DoNotSerialize]
    [Output]
    public float Progress { get; internal set; }

    /// <summary>How many notes the chart it plays has (a hold is one note, judged as it's pressed and at its end).</summary>
    [DoNotSerialize]
    [Output]
    public int Notes { get; internal set; }

    /// <summary>
    /// How well it's going, from 0 to 100: the points scored out of what Perfects all along would have scored. 100 before
    /// the first note.
    /// </summary>
    [DoNotSerialize]
    [Output]
    public float Accuracy
    {
        get
        {
            var judged = Perfects + Greats + Goods + Misses;
            return judged == 0 ? 100 : 100f * (Perfects * PerfectPoints + Greats * GreatPoints + Goods * GoodPoints) / (judged * PerfectPoints);
        }
    }

    /// <summary>A letter for the <see cref="Accuracy"/>: S from 95, A from 90, B from 80, C from 70, D below (empty before the first note).</summary>
    [DoNotSerialize]
    [Output]
    public string Grade => Perfects + Greats + Goods + Misses == 0 ? "" : GradeFor(Accuracy);

    /// <summary>The last judgment, "Perfect", "Great", "Good" or "Miss" (empty before the first).</summary>
    [DoNotSerialize]
    [Output]
    public string Judgment => LastJudgment == NoteJudgment.None ? "" : JudgmentNames[(int)LastJudgment];

    /// <summary>
    /// How far the last press that was taken for a note was from its beat as the player heard it, in milliseconds: below 0
    /// early, above 0 late. For an "Early" or "Late" under the judgment, or to see whether a player's offset needs setting.
    /// </summary>
    [DoNotSerialize]
    [Output]
    public float Timing { get; internal set; }

    /// <summary>Raised as a note is judged (a hold twice: as it's pressed and at its end), with the lane it's in (from 0).</summary>
    public event Action<NoteJudgment, int>? Judged;

    /// <summary>Raised once the song is over, every note judged.</summary>
    public event Action<NoteLanes>? Completed;

    /// <summary>Starts the chart from the top, at its <see cref="Difficulty"/>: the song from its start, the score at 0.</summary>
    [Action]
    public void Start() => Wanted = LanesRequest.Start;

    /// <summary>Starts the chart from the top at <paramref name="difficulty"/> (e.g. "Hard"), which becomes its Difficulty.</summary>
    [Action]
    public void Play(string difficulty)
    {
        Difficulty = difficulty;
        SavedDifficulty = null;
        Wanted = LanesRequest.Start;
    }

    /// <summary>Stops the chart where it is, and its song.</summary>
    [Action]
    public void Stop() => Wanted = LanesRequest.Stop;

    /// <summary>The name of a judgment, as <see cref="Judgment"/> says it.</summary>
    public static readonly IReadOnlyList<string> JudgmentNames = ["", "Perfect", "Great", "Good", "Miss"];

    /// <summary>The points a judgment scores.</summary>
    public static int PointsFor(NoteJudgment judgment) => judgment switch
    {
        NoteJudgment.Perfect => PerfectPoints,
        NoteJudgment.Great => GreatPoints,
        NoteJudgment.Good => GoodPoints,
        _ => 0,
    };

    /// <summary>
    /// The name in the save data the best score of <paramref name="chart"/> at <paramref name="difficulty"/> is kept under
    /// by lanes whose Save Best As is <paramref name="saveAs"/>: "Best.Groove.Normal" (its grade under the same with ".Grade").
    /// </summary>
    public static string BestKey(string saveAs, string chart, string? difficulty) =>
        string.IsNullOrWhiteSpace(difficulty) ? $"{saveAs.Trim()}.{chart.Trim()}" : $"{saveAs.Trim()}.{chart.Trim()}.{difficulty.Trim()}";

    /// <summary>The grade an accuracy (0 to 100) gets: S from 95, A from 90, B from 80, C from 70, D below.</summary>
    public static string GradeFor(float accuracy) => accuracy switch
    {
        >= 95 => "S",
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        _ => "D",
    };

    /// <summary>
    /// How a press <paramref name="milliseconds"/> from a note's beat (below 0 early, above late) is judged by its windows:
    /// None when it's too far off to be taken for that note.
    /// </summary>
    public NoteJudgment JudgmentFor(float milliseconds)
    {
        var off = Math.Abs(milliseconds);
        return off <= PerfectWindow ? NoteJudgment.Perfect
            : off <= GreatWindow ? NoteJudgment.Great
            : off <= GoodWindow ? NoteJudgment.Good
            : off <= Math.Max(MissWindow, GoodWindow) ? NoteJudgment.Miss
            : NoteJudgment.None;
    }

    public override string? SetupNote()
    {
        var keyless = Enumerable.Range(0, Math.Max(0, Lanes)).Where(i => ActionOf(i) is null).Select(i => i + 1).ToList();
        if (keyless.Count == 0 || keyless.Count == Lanes && !Clicks)
            return keyless.Count == 0 ? null : "No lane can be pressed: give it Actions, or tick Clicks.";
        var lanes = keyless.Count == 1 ? $"Lane {keyless[0]} has" : $"Lanes {string.Join(", ", keyless.Take(keyless.Count - 1))} and {keyless[^1]} have";
        return $"{lanes} no action in Actions{(Clicks ? ", so only a click or a touch presses it" : " and can't be pressed")}.";
    }

    string IPlaytestReport.Report()
    {
        var judged = Perfects + Greats + Goods + Misses;
        var state = IsPlaying ? "playing" : judged > 0 ? "done" : "waiting";
        var difficulty = PlayingDifficulty is { Length: > 0 } d ? $" {d}" : "";
        var last = LastJudgment == NoteJudgment.None ? "" : $", last {Judgment}{(Timed ? $" (last press {Timing:+0;-0;0} ms)" : "")}";
        return $", {state}{difficulty}: {Notes} notes, score {Score}, combo {Combo}, {Perfects} Perfect, {Greats} Great, {Goods} Good, {Misses} Miss, accuracy {Accuracy:0.#}%{last}";
    }

    /// <summary>The action that presses lane <paramref name="lane"/> (from 0), or null for none.</summary>
    public string? ActionOf(int lane) => lane >= 0 && lane < _actionList.Length ? _actionList[lane] : null;

    /// <summary>The color of lane <paramref name="lane"/>'s notes (from 0).</summary>
    public Color ColorOf(int lane) => _colorList.Length == 0 ? Color.White : _colorList[((lane % _colorList.Length) + _colorList.Length) % _colorList.Length];

    private static Color[] ParseColors(string? text) =>
        [.. (text ?? "").Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries).Select(c => Color.TryParse(c, out var color) ? color : (Color?)null).OfType<Color>()];

    // ----- Kept while it plays (not saved: a rewind starts them afresh) -----

    internal LanesRequest Wanted { get; set; }

    // Seen by the game in this scene: on first sight it starts (or carries on, after a rewind).
    internal bool Seen { get; set; }

    // The notes it plays, and what they were read from (the table, its version, the difficulty and the lanes).
    internal RhythmNote[] ChartNotes { get; set; } = [];

    internal (string? Table, int Version, string? Difficulty, string? Saved, int Lanes)? ChartFrom { get; set; }

    // When the last note's over, in beats.
    internal double LastBeat { get; set; } = double.NegativeInfinity;

    // The song it plays (a saved one, or its own), and the chart's name.
    internal string? PlayingSong { get; set; }

    internal string? ChartName { get; set; }

    // What it read from the save data (a saved song and difficulty), and the save data's version then: read again only as
    // something's saved, so a step makes no garbage.
    internal (int Saves, long Version, string? Song, string? Difficulty) SavesRead { get; set; } = (-1, 0, null, null);

    internal string? SavedSongValue { get; set; }

    internal string? SavedDifficultyValue { get; set; }

    // Each note's marks (see Played), and the text made from them for saving.
    internal char[] Marks { get; set; } = [];

    private string? _playedText = "";

    internal bool MarksSet { get; set; }

    internal void MarksChanged() => _playedText = null;

    // The first note whose press is still to be judged: the notes before it are all done with.
    internal int FirstOpen { get; set; }

    // Starting (or carrying on): the notes that went by before it began are left out, once the clock has the music.
    internal bool Fresh { get; set; }

    // Its song was stopped by a pause, to start again where it was.
    internal bool SongHeld { get; set; }

    // Each lane: held down, the hold note it's holding (-1 none), and when it was last pressed and hit (real seconds).
    internal bool[] Down { get; set; } = [];

    internal int[] Holding { get; set; } = [];

    internal double[] PressedAt { get; set; } = [];

    internal double[] HitAt { get; set; } = [];

    // What it draws by: the beats to show (ahead by the visual offset), seconds a beat, world units a second, real seconds.
    internal double ShownBeats { get; set; }

    internal double SecondsPerBeat { get; set; } = 0.5;

    internal float SpeedNow { get; set; } = 10;

    internal bool Following { get; set; }

    internal double Now { get; set; }

    // Whether a press has been timed since it started (Timing says how far off it was).
    internal bool Timed { get; set; }

    // The beats heard last step, to see the game was held still while other music played on.
    internal double LastBeats { get; set; } = double.NaN;

    // The judgment popped up, and when.
    internal NoteJudgment LastJudgment { get; set; }

    internal double PoppedAt { get; set; } = double.NegativeInfinity;

    private string _comboText = "";
    private int _comboShown = -1;

    internal void RaiseJudged(NoteJudgment judgment, int lane) => Judged?.Invoke(judgment, lane);

    internal void RaiseCompleted() => Completed?.Invoke(this);

    // ----- Where things are, in its own space -----

    // Which way the notes travel, and across which way the lanes are laid side by side (the first lane first).
    internal (Vector2 Travel, Vector2 Across) Axes => Direction switch
    {
        NoteDirection.Up => (Vector2.UnitY, Vector2.UnitX),
        NoteDirection.Left => (-Vector2.UnitX, -Vector2.UnitY),
        NoteDirection.Right => (Vector2.UnitX, -Vector2.UnitY),
        _ => (-Vector2.UnitY, Vector2.UnitX),
    };

    internal float LaneCount => Math.Clamp(Lanes, 1, 16);

    internal float Width => Math.Max(0.05f, LaneWidth);

    internal float Long => Math.Max(0.5f, Length);

    // How far along the way the notes travel the hit line is from the middle.
    internal float HitAlong => Long / 2 - Math.Clamp(HitLine, 0, Long);

    internal float LaneAcross(int lane) => (lane - (LaneCount - 1) / 2) * Width;

    /// <summary>The lane (from 0) at <paramref name="local"/> in its own space, or -1 off the lanes.</summary>
    internal int LaneAt(Vector2 local)
    {
        var (travel, across) = Axes;
        var along = Vector2.Dot(local, travel);
        if (Math.Abs(along) > Long / 2)
            return -1;
        var lane = (int)MathF.Floor(Vector2.Dot(local, across) / Width + LaneCount / 2);
        return lane >= 0 && lane < (int)LaneCount ? lane : -1;
    }

    int? IDrawable.DrawOrder => Order;

    int IDrawable.DrawStep => DrawSteps.Shapes;

    // The lanes, the hit line with a pad on it for each lane (lit while it's held), the notes on their way (holds as bars to
    // their ends), a flash where a note is hit, and the judgment popping up with the combo under it.
    void IDrawable.Draw(Drawing drawing)
    {
        var (travel, across) = Axes;
        var count = (int)LaneCount;
        var width = Width;
        var length = Long;
        var hit = HitAlong;
        var vertical = Direction is NoteDirection.Down or NoteDirection.Up;
        Vector2 Size(float acrossSize, float alongSize) => vertical ? new Vector2(acrossSize, alongSize) : new Vector2(alongSize, acrossSize);
        Vector2 At(float lane, float along) => across * lane + travel * along;

        for (var lane = 0; lane < count; lane++)
        {
            var x = LaneAcross(lane);
            var down = lane < Down.Length && Down[lane];
            if (LaneColor.A > 0)
            {
                drawing.Rect(At(x, 0), Size(width * 0.96f, length), down ? Lighter(LaneColor, 0.08f) : LaneColor);
            }

            var color = ColorOf(lane);
            var pressed = lane < PressedAt.Length ? Now - PressedAt[lane] : double.PositiveInfinity;
            var pad = down ? 0.75f : pressed < 0.12 ? 0.75f * (float)(1 - pressed / 0.12) : 0;
            drawing.Rect(At(x, hit), Size(width * 0.86f, width * 0.32f), color with { A = 0.22f + pad * 0.6f });
        }

        drawing.Rect(At(0, hit), Size(width * count, 0.06f), Color.White with { A = 0.8f });

        var notes = ChartNotes;
        var perBeat = SecondsPerBeat;
        var speed = SpeedNow;
        var marks = Marks;
        if (Following && notes.Length > 0 && marks.Length == notes.Length * 2 && speed > 0)
        {
            var near = length / 2;
            var far = -length / 2;
            var noteLong = width * 0.3f;
            for (var i = 0; i < notes.Length; i++)
            {
                var note = notes[i];
                var head = (float)(hit - (note.Beat - ShownBeats) * perBeat * speed);
                if (head < far - noteLong)
                    break;
                var press = marks[i * 2];
                var end = marks[i * 2 + 1];
                var x = LaneAcross(note.Lane);
                var color = ColorOf(note.Lane);
                var missed = press is 'M' or 'S' || end is 'M' or 'S';
                if (note.Hold > 0)
                {
                    var tail = (float)(hit - (note.Beat + note.Hold - ShownBeats) * perBeat * speed);
                    if (end is 'P')
                        continue;
                    var held = press is 'P' or 'G' or 'O' && end == '.';
                    var from = held ? Math.Min(head, hit) : head;
                    if (tail > near || (missed && from > near))
                        continue;
                    var top = Math.Max(tail, far);
                    var bottom = Math.Min(from, near);
                    if (bottom > top)
                        drawing.Rect(At(x, (top + bottom) / 2), Size(width * 0.42f, bottom - top), color with { A = missed ? 0.25f : held ? 0.9f : 0.6f });
                    if (!held && head <= near + noteLong)
                        drawing.Rect(At(x, head), Size(width * 0.84f, noteLong), color with { A = missed ? 0.3f : 1 });
                    continue;
                }

                if (press != '.' && press != 'M' && press != 'S')
                    continue;
                if (head > near + noteLong)
                    continue;
                drawing.Rect(At(x, head), Size(width * 0.84f, noteLong), color with { A = missed ? 0.3f : 1 });
            }
        }

        // Flashes where notes were hit.
        for (var lane = 0; lane < Math.Min(count, HitAt.Length); lane++)
        {
            var since = Now - HitAt[lane];
            if (since is >= 0 and < 0.18)
            {
                var t = (float)(since / 0.18);
                drawing.Rect(At(LaneAcross(lane), hit), Size(width * (0.9f + 0.4f * t), width * (0.45f + 0.4f * t)), Color.White with { A = 0.7f * (1 - t) });
            }
        }

        // The judgment, popping up then fading, and the combo.
        var size = PopSize;
        var age = Now - PoppedAt;
        if (size > 0 && LastJudgment != NoteJudgment.None && age is >= 0 and < 0.6)
        {
            var grow = age < 0.08 ? 1.3f - 0.3f * (float)(age / 0.08) : 1;
            var fade = age < 0.35 ? 1 : 1 - (float)((age - 0.35) / 0.25);
            var world = Entity!.Transform.WorldMatrix;
            var popAt = Vector2.Transform(At(0, hit - 2.4f * size - width * 0.3f), world);
            drawing.Text(Judgment, popAt, size * grow, JudgmentColor(LastJudgment) with { A = fade });
            if (Combo >= 2)
            {
                if (_comboShown != Combo)
                    (_comboShown, _comboText) = (Combo, Combo.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var comboAt = Vector2.Transform(At(0, hit - 3.5f * size - width * 0.3f), world);
                drawing.Text(_comboText, comboAt, size * 0.75f, Color.White with { A = 0.85f * fade });
            }
        }
    }

    /// <summary>The color a judgment pops up in.</summary>
    public static Color JudgmentColor(NoteJudgment judgment) => judgment switch
    {
        NoteJudgment.Perfect => Color.FromBytes(0x7F, 0xE6, 0xFF),
        NoteJudgment.Great => Color.FromBytes(0x86, 0xF0, 0x7C),
        NoteJudgment.Good => Color.FromBytes(0xF2, 0xD3, 0x5C),
        _ => Color.FromBytes(0xF2, 0x5C, 0x5C),
    };

    private static Color Lighter(Color color, float by) => color with { R = Math.Min(1, color.R + by), G = Math.Min(1, color.G + by), B = Math.Min(1, color.B + by) };
}

/// <summary>A note of a chart: its beat, its lane (from 0), how many beats it's held (0 for a tap) and its row in the table.</summary>
internal readonly record struct RhythmNote(double Beat, int Lane, double Hold, string Row);

/// <summary>What a lanes' action asked for, done on the game's next step.</summary>
internal enum LanesRequest
{
    None,
    Start,
    Stop,
}
