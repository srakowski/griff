using Griff.Scenes;

namespace Griff.Modules.Rhythm;

/// <summary>
/// A rhythm game's song select, with no code: the player moves up and down its songs' cards (the entity's children, in
/// order, one for each of its <see cref="Songs"/>), left and right through the difficulties, hears each song as it's picked
/// out, and plays it with a press (or a click or a tap on its card); the song and difficulty are kept in the save data and
/// <see cref="Scene"/> loads, where <see cref="NoteLanes"/> with a Saved Song and Saved Difficulty play them. What it says
/// about the song picked out (its title, difficulty, notes, best score and grade) are outputs for texts to show.
/// </summary>
/// <remarks>
/// A song's chart is the table named like it (Music/First Light.wav plays the First Light table), and its best score is
/// the one the lanes kept under <see cref="BestFrom"/> (their Save Best As). Up and down (the arrows, W and S, the D-pad or
/// the stick) move between the songs and left and right between the difficulties; Space, Enter or the gamepad's A (its
/// <see cref="PlayActions"/>) play; Esc, Backspace or B go <see cref="Back"/>. A click or tap on a card picks it out, and
/// another on the one picked out plays it. Its <see cref="Cursor"/> is moved over the card picked out.
/// </remarks>
[Category("Rhythm")]
[ComponentName("Griff.SongSelect")]
[WorksWith(typeof(NoteLanes), Note = "play the song and difficulty picked (in its Scene)", Back = "play the song a song select picked")]
[WorksWith(typeof(Text), Note = "shows its Title, Difficulty, Notes, Best and Best Grade, and each card's Grade:<song>")]
[NetLocal]
public sealed class SongSelect : Component, IPlaytestReport
{
    private IReadOnlyList<string> _songs = [];
    private string[] _titles = [];

    /// <summary>
    /// The songs to pick from, in the order of its cards, e.g. "Music/First Light.wav". Each one's chart is the table named
    /// like it.
    /// </summary>
    [Songs]
    public IReadOnlyList<string> Songs
    {
        get => _songs;
        set
        {
            _songs = value ?? [];
            _titles = [.. _songs.Select(TitleOf)];
        }
    }

    /// <summary>The difficulties to pick from, easiest first: the names the charts' Difficulty column uses.</summary>
    [Difficulties]
    public IReadOnlyList<string> Difficulties { get; set; } = ["Easy", "Normal", "Hard"];

    /// <summary>The scene to load as a song is picked, e.g. "Play": its Note Lanes play what was picked.</summary>
    [Names(NameKind.Scene)]
    public string? Scene { get; set; }

    /// <summary>The scene Back (Esc, Backspace or the gamepad's B) goes to, e.g. the title. Empty: Back does nothing here.</summary>
    [Names(NameKind.Scene)]
    public string? Back { get; set; }

    /// <summary>A name to keep the song picked under in the save data, e.g. "Song": a Note Lanes' Saved Song.</summary>
    [Group("Saving")]
    [Names(NameKind.Saved, Defines = true)]
    public string? SaveAs { get; set; } = "Song";

    /// <summary>A name to keep the difficulty picked under in the save data, e.g. "Difficulty": a Note Lanes' Saved Difficulty.</summary>
    [Names(NameKind.Saved, Defines = true)]
    public string? SaveDifficultyAs { get; set; } = "Difficulty";

    /// <summary>The name the lanes keep their best scores under (their Save Best As), to show each song's best: "Best".</summary>
    [Names(NameKind.Saved)]
    public string? BestFrom { get; set; } = "Best";

    /// <summary>Plays the song picked out, from <see cref="PreviewFrom"/>, over and over while it's picked out.</summary>
    [Group("Preview")]
    public bool Preview { get; set; } = true;

    /// <summary>Seconds into each song its preview starts.</summary>
    public float PreviewFrom { get; set; }

    /// <summary>How loud the preview plays, 0 to 1, on top of the player's music volume.</summary>
    public float PreviewVolume { get; set; } = 0.7f;

    /// <summary>The input actions that play the song picked out, separated by commas: Jump is Space and the gamepad's A, Interact Enter and E.</summary>
    [Group("Controls")]
    [Names(NameKind.Action, Many = true)]
    public string? PlayActions
    {
        get => _playActions;
        set
        {
            _playActions = value;
            PlayList = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    private string? _playActions = "Jump, Interact";

    internal string[] PlayList { get; private set; } = ["Jump", "Interact"];

    /// <summary>A click or a tap on a card picks it out, and another on the card picked out plays it.</summary>
    public bool Clicks { get; set; } = true;

    /// <summary>
    /// Untick it to ignore the player's keys, clicks and gamepad (its actions still work). Its Focus action ticks it and
    /// unticks it on everything else the player plays.
    /// </summary>
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>Seconds after a song is picked before its scene loads, to hear the pick.</summary>
    public float Delay { get; set; } = 0.5f;

    /// <summary>What's moved over the card picked out, by name.</summary>
    [Group("Look and Sound")]
    [EntityName]
    public string? Cursor { get; set; } = "Cursor";

    /// <summary>A sound played as the song or difficulty picked out changes.</summary>
    public string? MoveSound { get; set; }

    /// <summary>A sound played as a song is played.</summary>
    public string? PickSound { get; set; }

    // ----- State, kept with the scene -----

    /// <summary>The song picked out, from 0. Set by the game (and as it starts, to the one saved last).</summary>
    [Group("State")]
    public int Index { get; set; }

    /// <summary>The difficulty picked out, from 0 (Easy). Set by the game.</summary>
    public int DifficultyIndex { get; set; } = 1;

    /// <summary>True once a song's been picked to play: its scene loads after <see cref="Delay"/>. Set by the game.</summary>
    public bool Picked { get; set; }

    /// <summary>Seconds until the song's scene loads, once it's been picked. Set by the game.</summary>
    public float Timer { get; set; }

    /// <summary>The song picked out's title: its file's name ("First Light"), which its chart is named.</summary>
    [DoNotSerialize]
    [Output]
    public string Title => _titles.Length == 0 ? "" : _titles[Math.Clamp(Index, 0, _titles.Length - 1)];

    /// <summary>The difficulty picked out: "Normal".</summary>
    [DoNotSerialize]
    [Output]
    public string Difficulty => Difficulties.Count == 0 ? "" : Difficulties[Math.Clamp(DifficultyIndex, 0, Difficulties.Count - 1)];

    /// <summary>How many notes the song picked out has at the difficulty picked out.</summary>
    [DoNotSerialize]
    [Output]
    public int Notes { get; internal set; }

    /// <summary>The best score kept for the song picked out at the difficulty picked out (0 before it's been played).</summary>
    [DoNotSerialize]
    [Output]
    public int Best { get; internal set; }

    /// <summary>The grade of that best score: "S", "A"... (empty before it's been played).</summary>
    [DoNotSerialize]
    [Output("Best Grade")]
    public string BestGrade { get; internal set; } = "";

    /// <summary>
    /// The grade of the best score kept for a song at the difficulty picked out, by its title or path: "Grade:First Light",
    /// for a badge on its card. Empty before it's been played.
    /// </summary>
    [Output("Grade", Example = "First Light")]
    public string GradeOf(string song) => BestKept(song).Grade;

    /// <summary>The best score kept for a song at the difficulty picked out, by its title or path: "Best Score:First Light".</summary>
    [Output("Best Score", Example = "First Light")]
    public int BestOf(string song) => BestKept(song).Best;

    /// <summary>Its songs' titles, one by one, in the order of its <see cref="Songs"/>.</summary>
    [DoNotSerialize]
    public IReadOnlyList<string> Titles => _titles;

    // Timers only the game reads.
    public override bool Uses(string property) => property != nameof(Timer);

    /// <summary>Raised as a song is picked to play.</summary>
    public event Action<SongSelect>? Played;

    /// <summary>Picks out the next song down (round to the first after the last).</summary>
    [Action]
    public void Next() => Requested = (Requested.Song + 1, Requested.Difficulty, Requested.Play);

    /// <summary>Picks out the song before (round to the last before the first).</summary>
    [Action]
    public void Previous() => Requested = (Requested.Song - 1, Requested.Difficulty, Requested.Play);

    /// <summary>Picks out the next difficulty, a step harder (stopping at the hardest).</summary>
    [Action("Next Difficulty")]
    public void NextDifficulty() => Requested = (Requested.Song, Requested.Difficulty + 1, Requested.Play);

    /// <summary>Picks out the difficulty a step easier (stopping at the easiest).</summary>
    [Action("Previous Difficulty")]
    public void PreviousDifficulty() => Requested = (Requested.Song, Requested.Difficulty - 1, Requested.Play);

    /// <summary>Plays the song picked out, at the difficulty picked out.</summary>
    [Action]
    public void Play() => Requested = (Requested.Song, Requested.Difficulty, true);

    /// <summary>Picks out the song called <paramref name="song"/> (its title or path) and plays it, as a click on its card twice does.</summary>
    [Action]
    public void Pick(string song) => PickRequested = string.IsNullOrWhiteSpace(song) ? null : song.Trim();

    /// <summary>The title a song's path gives: "Music/First Light.wav" (and "First Light") is First Light.</summary>
    public static string TitleOf(string? song) =>
        string.IsNullOrWhiteSpace(song) ? "" : Path.GetFileNameWithoutExtension(song.Trim().Replace('\\', '/'));

    /// <summary>Which of its songs <paramref name="song"/> (a title or a path) is, from 0; -1 if it isn't one.</summary>
    public int IndexOf(string? song)
    {
        var title = TitleOf(song);
        for (var i = 0; i < _titles.Length; i++)
        {
            if (string.Equals(_titles[i], title, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    // ----- Kept by the game while it runs -----

    // Moves asked for by its actions, done on the game's next step: songs and difficulties to step by, and whether to play.
    internal (int Song, int Difficulty, bool Play) Requested { get; set; }

    internal string? PickRequested { get; set; }

    internal bool Seen { get; set; }

    // The way the player held the stick last step (so holding moves once, then again after a moment), and for how long.
    internal (int X, int Y, float Held) LastHeld { get; set; }

    // The best score and grade of each song at the difficulty picked out, by title, and what they were read for.
    internal Dictionary<string, (int Best, string Grade)> Bests { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal (int Difficulty, long Saves, int Tables, int Index)? ReadFor { get; set; }

    // The song its preview plays.
    internal string? Previewing { get; set; }

    private (int Best, string Grade) BestKept(string song) =>
        Bests.TryGetValue(song, out var kept) || Bests.TryGetValue(TitleOf(song), out kept) ? kept : (0, "");

    internal void RaisePlayed() => Played?.Invoke(this);

    string IPlaytestReport.Report() =>
        $", {(Picked ? "playing" : "on")} {Title} at {Difficulty}: {Notes} notes, best {Best}{(BestGrade.Length > 0 ? $" ({BestGrade})" : "")}";
}
