using System.Numerics;
using Griff.Audio;
using Griff.Data;
using Griff.Input;
using Griff.Modules.Rhythm;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Projects;

/// <summary>
/// The Rhythm template, Neon Pulse: a title with settings, a song select of three songs made in Griff (previewed, each with
/// its best score and grade at the difficulty picked out), and each song's lanes at Easy, Normal and Hard, every chart fair
/// and fully clearable, its difficulty rising, played to an S with perfect timing (by the keys, a controller or fingers),
/// a lower grade for missed notes, the best kept, the player's audio offset allowed for, a pause menu, and exports for the
/// desktop and the web.
/// </summary>
public sealed class RhythmTemplateTests : IDisposable
{
    private const float Frame = 1 / 60f;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-rhythm-" + Guid.NewGuid().ToString("N"));
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();
    private readonly InputState _input = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<Project> CreateProject() => await Project.CreateAsync(_root, "Neon", ProjectTemplates.Get("Rhythm"));

    private Game Start(Project project, string scene, SaveData? saves = null)
    {
        Scene Load(string name) => GameLoader.LoadScene(project, _registry, name);
        var game = new Game(Load(scene), _input)
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            SceneLoader = Load,
            SceneName = scene,
            Components = _registry,
            ViewAspect = 16 / 9f,
            SaveData = saves ?? new SaveData(),
            Audio = new SongAudio(),
        };
        game.UseProjectSettings(project);
        return game;
    }

    // The lanes playing `song` at `difficulty`, as the song select leaves them.
    private Game Play(Project project, string song, string difficulty, SaveData? saves = null)
    {
        saves ??= new SaveData();
        saves.Set(RhythmTemplate.SavedSong, RhythmTemplate.SongPath(song));
        saves.Set(RhythmTemplate.SavedDifficulty, difficulty);
        var game = Start(project, RhythmTemplate.PlayScene, saves);
        Step(game);
        return game;
    }

    private void Run(Game game, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
            Step(game);
    }

    private void Step(Game game)
    {
        game.Tick(Frame);
        _input.EndFrame();
    }

    private void Tap(Game game, Key key)
    {
        _input.SetKey(key, true);
        Step(game);
        _input.SetKey(key, false);
        Run(game, 0.1f);
    }

    // Picks a button on the pause menu with the keys (Resume is picked as it opens) and clicks it with Enter.
    private void Pick(Game game, string button)
    {
        string[] order = ["Resume", "Restart", "Open Settings", "Songs"];
        for (var i = 0; i < Array.IndexOf(order, button); i++)
            Tap(game, Key.Down);
        Assert.Equal(button, game.Focused!.Entity!.Name);
        Tap(game, Key.Enter);
    }

    private static NoteLanes LanesOf(Game game) => game.Find(RhythmTemplate.LanesName)!.GetComponent<NoteLanes>()!;

    private static SongSelect SelectOf(Game game) => game.Find(RhythmTemplate.SelectName)!.GetComponent<SongSelect>()!;

    private static RhythmTemplate.Song SongNamed(string title) => RhythmTemplate.Songs.Single(s => s.Title == title);

    // The chart of a song at a difficulty, as the template wrote it into the project.
    private static List<(double Beat, int Lane, double Hold)> ChartOf(Project project, string song, string difficulty)
    {
        var table = TableFile.Load(Path.Combine(project.AssetsPath, TableFile.Folder, song + TableFile.Extension));
        return [.. table.Rows.Where(r => r["Difficulty"].ToString() == difficulty)
            .Select(r => (r["Beat"].AsNumber, (int)r["Lane"].AsWholeNumber, r["Hold"].IsEmpty ? 0 : r["Hold"].AsNumber))
            .OrderBy(n => n.Item1).ThenBy(n => n.Item2)];
    }

    // ----- How a player presses the lanes

    public enum Hands
    {
        Keys,
        Controller,
        Fingers,
    }

    private static readonly string[] LowGrades = ["B", "C", "D"];

    private static readonly Key[] LaneKeys = [Key.D, Key.F, Key.J, Key.K];
    private static readonly GamepadButton[] LaneButtons = [GamepadButton.DPadLeft, GamepadButton.A, GamepadButton.Y, GamepadButton.B];

    private void Hold(Game game, Hands hands, int lane, bool down)
    {
        switch (hands)
        {
            case Hands.Keys:
                _input.SetKey(LaneKeys[lane - 1], down);
                break;
            case Hands.Controller:
                _input.HoldGamepadButton(0, LaneButtons[lane - 1], down);
                break;
            default:
                var lanes = LanesOf(game);
                var at = Vector2.Transform(lanes.Axes.Across * lanes.LaneAcross(lane - 1) + lanes.Axes.Travel * lanes.HitAlong, lanes.Entity!.Transform.WorldMatrix);
                _input.SetTouch(10 + lane, at, down);
                break;
        }
    }

    /// <summary>
    /// Plays the lanes' song through, pressing each note's lane in the step its beat is nearest, `late` milliseconds after the
    /// music reaches it (as it reaches the speakers: a player with an audio offset hears it that much later), holding a hold to its end, and leaving out the notes `skip` says; returns once the song's over.
    /// </summary>
    private void PlayThrough(Game game, List<(double Beat, int Lane, double Hold)> chart, Hands hands = Hands.Keys, float late = 0, Func<int, bool>? skip = null)
    {
        var lanes = LanesOf(game);
        var clock = game.Find(RhythmTemplate.LanesName)!.GetComponent<BeatClock>()!;
        var heldUntil = new double[5];
        Array.Fill(heldUntil, double.NegativeInfinity);
        var pressed = new bool[5];
        var next = 0;
        for (var steps = 0; steps < 60 * 90 && lanes.IsPlaying; steps++)
        {
            var perBeat = clock.SecondsPerBeat;
            var stepBeats = Frame / perBeat;
            // The beat this step will judge at as the music plays it (the clock counts it as heard, the player's offset later), one
            // step on from now, the press `late` after the note.
            var at = clock.Beats + game.AudioOffset / 1000 / perBeat + stepBeats - late / 1000 / perBeat;
            var down = new bool[5];
            for (var lane = 1; lane <= 4; lane++)
                down[lane] = at < heldUntil[lane];
            while (next < chart.Count && chart[next].Beat < at + stepBeats / 2)
            {
                var (beat, lane, hold) = chart[next];
                if (skip?.Invoke(next) != true)
                {
                    down[lane] = true;
                    heldUntil[lane] = hold > 0 ? beat + hold + stepBeats : double.NegativeInfinity;
                }

                next++;
            }

            for (var lane = 1; lane <= 4; lane++)
            {
                // A tap goes up the step after it went down, so the next in that lane is a press of its own.
                var wanted = down[lane] && !(pressed[lane] && heldUntil[lane] == double.NegativeInfinity);
                if (wanted != pressed[lane])
                    Hold(game, hands, lane, wanted);
                pressed[lane] = wanted;
            }

            Step(game);
        }

        for (var lane = 1; lane <= 4; lane++)
            Hold(game, hands, lane, false);
        Step(game);
    }

    // ----- The charts

    [Fact]
    public async Task Every_chart_is_fair_and_each_songs_difficulty_rises()
    {
        var project = await CreateProject();
        foreach (var song in RhythmTemplate.Songs)
        {
            var perBeat = 60.0 / song.Music.Tempo;
            var beats = song.Music.Bars * 4;
            var counts = new List<double>();
            foreach (var difficulty in ChartWriter.Difficulties)
            {
                var chart = ChartOf(project, song.Title, difficulty);
                var label = $"{song.Title} {difficulty}";
                Assert.True(chart.Count >= 20, $"{label}: {chart.Count} notes.");
                Assert.All(chart, n => Assert.InRange(n.Lane, 1, 4));
                // A bar to get ready, and over before the song is.
                Assert.True(chart[0].Beat >= 4, $"{label} starts at beat {chart[0].Beat}.");
                Assert.True(chart.Max(n => n.Beat + n.Hold) <= beats - 1, $"{label} runs to the song's end.");

                // In each lane, a note comes well after the last there (or its hold's end): no press could be taken for the
                // wrong one, and nothing's played in a lane while it's held.
                foreach (var lane in Enumerable.Range(1, 4))
                {
                    var inLane = chart.Where(n => n.Lane == lane).ToList();
                    for (var i = 1; i < inLane.Count; i++)
                    {
                        var gap = (inLane[i].Beat - inLane[i - 1].Beat - inLane[i - 1].Hold) * perBeat;
                        Assert.True(gap >= ChartWriter.SameLaneGap - 1e-6, $"{label}: lane {lane} has notes {gap:0.000} s apart at beat {inLane[i].Beat}.");
                    }
                }

                // Two hands' worth at most: no more than two lanes pressed at once (a hold counting), and on Easy one.
                foreach (var note in chart)
                {
                    var busy = chart.Count(n => n.Beat == note.Beat || n.Hold > 0 && n.Beat < note.Beat && n.Beat + n.Hold >= note.Beat);
                    Assert.True(busy <= (difficulty == "Easy" ? 1 : 2), $"{label}: {busy} lanes at once at beat {note.Beat}.");
                }

                // Never closer together than the difficulty's quickest step: Easy a beat, Normal half a beat, Hard a quarter.
                var moments = chart.Select(n => n.Beat).Distinct().Order().ToList();
                var quickest = moments.Zip(moments.Skip(1), (a, b) => b - a).Min();
                Assert.True(quickest >= difficulty switch { "Easy" => 1, "Normal" => 0.5, _ => 0.25 } - 1e-9, $"{label}: notes {quickest} beats apart.");

                // How many a second, from the first note to the last.
                counts.Add(chart.Count / ((chart[^1].Beat - chart[0].Beat) * perBeat));
                Assert.Contains(chart, n => n.Hold > 0);
            }

            Assert.True(counts[0] < counts[1] && counts[1] < counts[2], $"{song.Title}: notes a second {string.Join(", ", counts.Select(c => c.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))}.");
            Assert.True(counts[0] < 1.8, $"{song.Title} Easy has {counts[0]:0.00} notes a second.");
        }

        // The songs get harder in turn: each one's Normal busier than the one before.
        var normals = RhythmTemplate.Songs.Select(s => ChartOf(project, s.Title, "Normal").Count / (s.Music.Bars * 4 * 60.0 / s.Music.Tempo)).ToList();
        Assert.Equal(normals.Order(), normals);
    }

    [Theory]
    [InlineData("First Light", "Easy")]
    [InlineData("First Light", "Normal")]
    [InlineData("First Light", "Hard")]
    [InlineData("Neon Avenue", "Easy")]
    [InlineData("Neon Avenue", "Normal")]
    [InlineData("Neon Avenue", "Hard")]
    [InlineData("Overdrive", "Easy")]
    [InlineData("Overdrive", "Normal")]
    [InlineData("Overdrive", "Hard")]
    public async Task Every_chart_is_cleared_with_perfect_timing_to_an_S(string song, string difficulty)
    {
        var project = await CreateProject();
        var chart = ChartOf(project, song, difficulty);
        var game = Play(project, song, difficulty);
        var lanes = LanesOf(game);
        Assert.Equal((song, difficulty, chart.Count), (lanes.Title, lanes.PlayingDifficulty, lanes.Notes));
        Assert.Equal(RhythmTemplate.SongPath(song), game.Audio.Music);

        PlayThrough(game, chart);

        // Every note (and every hold's end) Perfect, in time with the song, which played to its end.
        var judged = chart.Count + chart.Count(n => n.Hold > 0);
        Assert.False(lanes.IsPlaying);
        var played = lanes.Played!;
        var missed = string.Join(", ", chart.Select((n, i) => (n, i)).Where(x => played[x.i * 2] != 'P' || played[x.i * 2 + 1] is not ('P' or '-'))
            .Select(x => $"{x.n.Beat}:{x.n.Lane}{(x.n.Hold > 0 ? $"h{x.n.Hold}" : "")} {played.Substring(x.i * 2, 2)}"));
        Assert.True(missed.Length == 0, $"Not Perfect: {missed}");
        Assert.Equal((judged, 0, 0, 0), (lanes.Perfects, lanes.Greats, lanes.Goods, lanes.Misses));
        Assert.Equal((100f, "S", judged * NoteLanes.PerfectPoints, judged), (lanes.Accuracy, lanes.Grade, lanes.Score, lanes.BestCombo));
        Assert.True(lanes.FullCombo);
        Assert.Equal(1f, lanes.Progress);
        Assert.Equal(RhythmTemplate.ResultsName, game.TopPanel?.Entity!.Name);
    }

    [Theory]
    [InlineData(Hands.Controller)]
    [InlineData(Hands.Fingers)]
    public async Task A_controller_and_fingers_play_a_song_perfectly_too(Hands hands)
    {
        var project = await CreateProject();
        var chart = ChartOf(project, "Neon Avenue", "Normal");
        var game = Play(project, "Neon Avenue", "Normal");
        PlayThrough(game, chart, hands);

        var lanes = LanesOf(game);
        Assert.Equal((0, "S"), (lanes.Misses, lanes.Grade));
        Assert.Equal(100f, lanes.Accuracy);
    }

    [Fact]
    public async Task Missed_notes_and_late_presses_get_a_lower_grade()
    {
        var project = await CreateProject();
        var chart = ChartOf(project, "First Light", "Normal");

        // Every fourth note let go by: a Miss each, the combo broken, a C or so.
        var game = Play(project, "First Light", "Normal");
        PlayThrough(game, chart, skip: i => i % 4 == 1);
        var lanes = LanesOf(game);
        var skipped = Enumerable.Range(0, chart.Count).Where(i => i % 4 == 1).ToList();
        Assert.Equal(skipped.Count + skipped.Count(i => chart[i].Hold > 0), lanes.Misses);
        Assert.True(lanes.BestCombo < chart.Count / 2);
        Assert.False(lanes.FullCombo);
        Assert.Contains(lanes.Grade, LowGrades);
        Assert.Equal(NoteLanes.GradeFor(lanes.Accuracy), lanes.Grade);

        // Each press 70 ms late: Greats, an A at best.
        game = Play(project, "First Light", "Normal");
        PlayThrough(game, chart, late: 70);
        lanes = LanesOf(game);
        Assert.Equal(0, lanes.Misses);
        Assert.True(lanes.Greats > chart.Count / 2, $"{lanes.Perfects} Perfect, {lanes.Greats} Great.");
        Assert.NotEqual("S", lanes.Grade);
        Assert.InRange(lanes.Timing, 50, 90);
    }

    [Fact]
    public async Task The_players_audio_offset_is_allowed_for()
    {
        var project = await CreateProject();
        var chart = ChartOf(project, "Neon Avenue", "Easy");

        // They hear the song 150 ms late and press with what they hear: every note Perfect.
        var saves = new SaveData();
        var game = Play(project, "Neon Avenue", "Easy", saves);
        game.AudioOffset = 150;
        PlayThrough(game, chart, late: 150);
        Assert.Equal((0, 0, "S"), (LanesOf(game).Misses, LanesOf(game).Greats, LanesOf(game).Grade));

        // The same presses without the offset: Misses (or at best Goods), 150 ms late.
        game = Play(project, "Neon Avenue", "Easy");
        PlayThrough(game, chart, late: 150);
        Assert.Equal(0, LanesOf(game).Perfects);
        Assert.NotEqual("S", LanesOf(game).Grade);

        // The setting is the player's own, kept with their settings: the Settings panel's slider sets it.
        Assert.Equal(GameSetting.AudioOffset, game.Find("Audio Offset")!.GetComponent<Slider>()!.Setting);
        Assert.Equal(GameSetting.NoteSpeed, game.Find("Note Speed")!.GetComponent<Slider>()!.Setting);
        Assert.NotNull(game.Find(CalibrationScreen.Name)!.GetComponent<Calibration>());
    }

    // ----- Best scores

    [Fact]
    public async Task The_best_score_and_grade_are_kept_for_each_song_and_difficulty()
    {
        var project = await CreateProject();
        var saves = new SaveData();
        var chart = ChartOf(project, "First Light", "Normal");

        // A first clear is a new best, kept with its grade.
        var game = Play(project, "First Light", "Normal", saves);
        Assert.Equal((0, false), (LanesOf(game).Best, LanesOf(game).NewBest));
        PlayThrough(game, chart);
        var best = LanesOf(game).Score;
        Assert.Equal((best, true), (LanesOf(game).Best, LanesOf(game).NewBest));
        Assert.Equal(best, saves.GetInt("Best.First Light.Normal"));
        Assert.Equal("S", saves.GetString("Best.First Light.Normal.Grade"));
        Assert.Equal("NEW BEST!", game.Find(RhythmTemplate.ResultsName)!.Children.Single(c => c.Name == "New Best").GetComponent<Text>()!.Shown);

        // A worse go keeps it.
        game = Play(project, "First Light", "Normal", saves);
        Assert.Equal(best, LanesOf(game).Best);
        PlayThrough(game, chart, skip: i => i % 3 == 0);
        Assert.Equal((best, false), (LanesOf(game).Best, LanesOf(game).NewBest));
        Assert.Equal(best, saves.GetInt("Best.First Light.Normal"));
        Assert.Equal("", game.Find(RhythmTemplate.ResultsName)!.Children.Single(c => c.Name == "New Best").GetComponent<Text>()!.Shown);

        // Retry starts the song over.
        Tap(game, Key.Enter);
        Run(game, 0.8f);
        Assert.Equal(RhythmTemplate.PlayScene, game.SceneName);
        Assert.True(LanesOf(game).IsPlaying);
        Assert.Equal((0, best), (LanesOf(game).Score, LanesOf(game).Best));

        // The song select shows it for that song and difficulty, and nothing for the others.
        game = Start(project, RhythmTemplate.SelectScene, saves);
        Run(game, 0.2f);
        var select = SelectOf(game);
        Assert.Equal(("First Light", "Normal"), (select.Title, select.Difficulty));
        Assert.Equal((best, "S", chart.Count), (select.Best, select.BestGrade, select.Notes));
        Assert.Equal("S", select.GradeOf("First Light"));
        Assert.Equal("", select.GradeOf("Overdrive"));
        Assert.Equal("S", game.Find("First Light")!.Children.Single(c => c.Name == "Grade").GetComponent<Text>()!.Shown);
        Tap(game, Key.Right);
        Assert.Equal(("Hard", 0, ""), (select.Difficulty, select.Best, select.BestGrade));
        Assert.Equal("", select.GradeOf("First Light"));
    }

    // ----- Menus

    [Fact]
    public async Task The_title_leads_to_the_song_select_which_previews_and_plays_the_song_picked()
    {
        var project = await CreateProject();
        var saves = new SaveData();
        var game = Start(project, Project.DefaultSceneName, saves);
        Run(game, 0.3f);
        Assert.Equal(TemplateSounds.Music, game.Audio.Music);

        // Enter plays: the song select, the first song picked out and heard.
        Tap(game, Key.Enter);
        Run(game, 0.8f);
        Assert.Equal(RhythmTemplate.SelectScene, game.SceneName);
        var select = SelectOf(game);
        Assert.Equal(("First Light", "Normal"), (select.Title, select.Difficulty));
        Assert.Equal("Music/First Light.wav", game.Audio.Music);

        // Down to the next song (heard instead), right to Hard; Up and Down wrap round.
        Tap(game, Key.Down);
        Assert.Equal("Neon Avenue", select.Title);
        Assert.Equal("Music/Neon Avenue.wav", game.Audio.Music);
        Tap(game, Key.Up);
        Tap(game, Key.Up);
        Assert.Equal("Overdrive", select.Title);
        Tap(game, Key.Down);
        Tap(game, Key.Down);
        Tap(game, Key.Right);
        Tap(game, Key.Right);
        Assert.Equal(("Neon Avenue", "Hard"), (select.Title, select.Difficulty));
        Assert.Equal(ChartOf(project, "Neon Avenue", "Hard").Count, select.Notes);
        var cursor = game.Find("Cursor")!.Transform.Position;
        var card = game.Find("Neon Avenue")!.Transform.WorldPosition;
        Assert.True(Vector2.Distance(new Vector2(card.X, card.Y), new Vector2(cursor.X, cursor.Y)) < 0.05f, $"The cursor is at {cursor}, the card at {card}.");

        // Enter plays it, at Hard.
        Tap(game, Key.Enter);
        Run(game, 1);
        Assert.Equal(RhythmTemplate.PlayScene, game.SceneName);
        var lanes = LanesOf(game);
        Assert.Equal(("Neon Avenue", "Hard", true), (lanes.Title, lanes.PlayingDifficulty, lanes.IsPlaying));
        Assert.Equal("Music/Neon Avenue.wav", game.Audio.Music);
        Assert.Equal(("Music/Neon Avenue.wav", "Hard"), (saves.GetString(RhythmTemplate.SavedSong), saves.GetString(RhythmTemplate.SavedDifficulty)));

        // Back to the songs from the pause menu: the one played is still picked out; Esc goes back to the title.
        Tap(game, Key.Escape);
        Pick(game, "Songs");
        Run(game, 0.8f);
        Assert.Equal(RhythmTemplate.SelectScene, game.SceneName);
        Assert.Equal(("Neon Avenue", "Hard"), (SelectOf(game).Title, SelectOf(game).Difficulty));
        Tap(game, Key.Escape);
        Run(game, 0.8f);
        Assert.Equal(Project.DefaultSceneName, game.SceneName);
    }

    [Fact]
    public async Task A_controller_and_a_finger_pick_songs_too()
    {
        var project = await CreateProject();
        var game = Start(project, RhythmTemplate.SelectScene);
        Run(game, 0.2f);
        var select = SelectOf(game);

        // The D-pad picks out the last song, A plays it.
        _input.HoldGamepadButton(0, GamepadButton.DPadUp, true);
        Step(game);
        _input.HoldGamepadButton(0, GamepadButton.DPadUp, false);
        Run(game, 0.1f);
        _input.HoldGamepadButton(0, GamepadButton.DPadLeft, true);
        Step(game);
        _input.HoldGamepadButton(0, GamepadButton.DPadLeft, false);
        Run(game, 0.1f);
        Assert.Equal(("Overdrive", "Easy"), (select.Title, select.Difficulty));
        _input.HoldGamepadButton(0, GamepadButton.A, true);
        Step(game);
        _input.HoldGamepadButton(0, GamepadButton.A, false);
        Run(game, 1);
        Assert.Equal((RhythmTemplate.PlayScene, "Overdrive", "Easy"), (game.SceneName, LanesOf(game).Title, LanesOf(game).PlayingDifficulty));

        // A finger: a tap on a card picks it out, another plays it; the arrows beside the difficulty step it.
        game = Start(project, RhythmTemplate.SelectScene);
        Run(game, 0.2f);
        select = SelectOf(game);
        TapAt(game, game.Find("Neon Avenue")!.Transform.WorldPosition);
        Assert.Equal(("Neon Avenue", RhythmTemplate.SelectScene), (select.Title, game.SceneName));
        TapAt(game, game.Find("Harder")!.Transform.WorldPosition);
        Assert.Equal("Hard", select.Difficulty);
        TapAt(game, game.Find("Neon Avenue")!.Transform.WorldPosition);
        Run(game, 1);
        Assert.Equal((RhythmTemplate.PlayScene, "Neon Avenue", "Hard"), (game.SceneName, LanesOf(game).Title, LanesOf(game).PlayingDifficulty));
    }

    private void TapAt(Game game, Vector3 at)
    {
        // A tap is a click there (the first finger is the mouse).
        _input.MouseWorld = new Vector2(at.X, at.Y);
        _input.SetMouseButton(MouseButton.Left, true);
        Step(game);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(game, 0.1f);
    }

    [Fact]
    public async Task The_pause_menu_holds_the_song_and_opens_the_settings()
    {
        var project = await CreateProject();
        var game = Play(project, "First Light", "Normal");
        Run(game, 3);
        var lanes = LanesOf(game);
        Tap(game, Key.Escape);
        Assert.True(game.IsPaused);
        Assert.Null(game.Audio.Music);
        var held = lanes.SongTime;
        Run(game, 1);
        Assert.Equal(held, lanes.SongTime);

        // Settings over the pause menu: the note speed, and Back (Esc) to the pause menu.
        Pick(game, "Open Settings");
        Assert.Equal(RhythmTemplate.SettingsName, game.TopPanel?.Entity!.Name);
        Tap(game, Key.Escape);
        Assert.Equal(RhythmTemplate.PausedName, game.TopPanel?.Entity!.Name);

        // Resume: the song carries on from where it was.
        Tap(game, Key.Escape);
        Assert.False(game.IsPaused);
        Assert.Equal(RhythmTemplate.SongPath("First Light"), game.Audio.Music);
        Assert.InRange(lanes.SongTime, held, held + 0.3f);
        Assert.True(lanes.IsPlaying);
    }

    // ----- Sounds, garbage and exports

    [Fact]
    public async Task Its_songs_are_made_in_Griff_and_its_scenes_sounds_are_hooked_up()
    {
        var project = await CreateProject();
        foreach (var song in RhythmTemplate.Songs)
        {
            var path = Path.Combine(project.AssetsPath, RhythmTemplate.SongPath(song.Title));
            Assert.Equal(song.Music.Tempo, Wav.ReadMusicRecipe(path)!.Tempo);
            Assert.Equal(MusicSynth.Seconds(song.Music), Wav.Decode(File.ReadAllBytes(path))!.Seconds, 2);
        }

        foreach (var sound in new[] { RhythmTemplate.StartSound, RhythmTemplate.MissSound, RhythmTemplate.ResultsSound })
            Assert.True(Wav.Decode(File.ReadAllBytes(Path.Combine(project.AssetsPath, sound))) is { Seconds: > 0 }, sound);

        var select = Start(project, RhythmTemplate.SelectScene);
        Assert.Equal((TemplateSounds.Select, RhythmTemplate.StartSound), (SelectOf(select).MoveSound, SelectOf(select).PickSound));
        Assert.Null(select.Find("Music"));
        var play = Start(project, RhythmTemplate.PlayScene);
        Assert.Equal(RhythmTemplate.MissSound, LanesOf(play).MissSound);
        Assert.Equal(RhythmTemplate.ResultsSound, play.Find(RhythmTemplate.ResultsName)!.GetComponent<Panel>()!.Sound);
        Assert.Null(play.Find("Music"));
    }

    [Fact]
    public async Task Playing_makes_no_garbage_between_judgments()
    {
        var project = await CreateProject();
        // Once the song is under way, a step allocates nothing but as a note is judged (its message, and the texts showing
        // the score written again) and the step after. The least of a few goes counts (see the Pinball template's).
        List<string>? least = null;
        for (var attempt = 0; attempt < 4 && least is not { Count: 0 }; attempt++)
        {
            var game = Play(project, "First Light", "Easy");
            var lanes = LanesOf(game);
            Run(game, 1);
            var noisy = new List<string>();
            var (was, after) = (-1, false);
            for (var frame = 0; frame < 60 * 12; frame++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                Step(game);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                var judged = lanes.Perfects + lanes.Greats + lanes.Goods + lanes.Misses;
                if (judged == was && !after && allocated > 0)
                    noisy.Add($"frame {frame}: {allocated} bytes");
                (after, was) = (judged != was, judged);
            }

            if (least is null || noisy.Count < least.Count)
                least = noisy;
        }

        Assert.True(least!.Count == 0, $"Steps between judgments allocated: {string.Join("; ", least)}");
    }

    [Fact]
    public async Task Exports_for_the_desktop_and_the_web_carry_its_songs_and_charts_and_play()
    {
        var project = await CreateProject();
        string[] wanted = ["Assets/Music/First Light.wav", "Assets/Music/Overdrive.wav", "Assets/Tables/Neon Avenue.table", "Assets/Textures/stage.png",
            "Assets/Scenes/Select.scene", "Assets/Scenes/Play.scene", "Assets/Sounds/miss.wav"];

        // The desktop: one .exe, holding the game's files, whose songs play as in the editor.
        var player = Path.Combine(_root, "player", "Griff.Player.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(player)!);
        File.WriteAllText(player, "player exe");
        var desktop = GameExporter.Export(project, player, Path.Combine(_root, "out"));
        Assert.True(desktop.Success, desktop.Error);
        var extracted = GamePackage.Extract(desktop.Executable!, Path.Combine(_root, "cache"), "Neon")!;
        Assert.All(wanted, file => Assert.True(File.Exists(Path.Combine(extracted, file)), file));
        var loaded = await GameLoader.LoadAsync(extracted);
        Assert.Empty(loaded.Problems);
        var saves = new SaveData();
        saves.Set(RhythmTemplate.SavedSong, RhythmTemplate.SongPath("Overdrive"));
        saves.Set(RhythmTemplate.SavedDifficulty, "Easy");
        var game = (loaded with { Scene = loaded.LoadScene(RhythmTemplate.PlayScene), SceneName = RhythmTemplate.PlayScene })
            .NewGame(new GameHostOptions { Input = _input, SaveData = saves, Audio = new SongAudio() });
        game.ViewAspect = 16 / 9f;
        Step(game);
        PlayThrough(game, ChartOf(project, "Overdrive", "Easy"));
        Assert.Equal(("S", 0), (LanesOf(game).Grade, LanesOf(game).Misses));

        // The web: the files the browser fetches are listed.
        var web = Path.Combine(_root, "web-player");
        Directory.CreateDirectory(Path.Combine(web, "_framework"));
        File.WriteAllText(Path.Combine(web, WebExporter.Page), "<html>");
        var site = WebExporter.Export(project, web, Path.Combine(_root, "web-out"));
        Assert.True(site.Success, site.Error);
        var gameFolder = Path.Combine(_root, "web-out", ExportLayout.GameFolder);
        var listed = File.ReadAllLines(Path.Combine(gameFolder, ExportLayout.WebFileList));
        Assert.All(wanted, file => Assert.Contains(file, listed));
        Assert.All(wanted, file => Assert.True(File.Exists(Path.Combine(gameFolder, file)), file));
    }

    /// <summary>
    /// Audio that keeps time for a song as if it were heard, at the tempo it says and for as long as it lasts (the songs and
    /// the theme are all made in Griff), so the lanes play it through to its end.
    /// </summary>
    private sealed class SongAudio : IAudio
    {
        private bool _loops;

        public float Volume { get; set; } = 1;

        public float MusicVolume { get; set; } = 1;

        public float SoundVolume { get; set; } = 1;

        public bool Muted { get; set; }

        public string? Music { get; private set; }

        public double MusicTime { get; private set; }

        public double MusicLength { get; private set; }

        public float MusicTempo { get; private set; }

        public void Play(string sound, float volume = 1)
        {
        }

        public void PlayMusic(string music, float volume = 0.6f, bool loop = true) => PlayMusicFrom(music, 0, volume, loop);

        public void PlayMusicFrom(string music, double seconds, float volume = 0.6f, bool loop = true)
        {
            var recipe = RhythmTemplate.Songs.FirstOrDefault(s => RhythmTemplate.SongPath(s.Title) == music)?.Music;
            (Music, MusicTime, _loops) = (music, Math.Max(0, seconds), loop);
            (MusicTempo, MusicLength) = recipe is null ? (120f, 30.0) : ((float)recipe.Tempo, MusicSynth.Seconds(recipe));
        }

        public void PlayMetronome(float tempo, int beatsPerBar = 4, float volume = 1) => (Music, MusicTime, MusicTempo, MusicLength) = (IAudio.MetronomeMusic, 0, tempo, 0);

        public void CrossfadeMusic(string music, float seconds, float volume = 0.6f, bool loop = true) => PlayMusic(music, volume, loop);

        public void FadeOutMusic(float seconds) => StopMusic();

        public void StopMusic() => (Music, MusicTime) = (null, 0);

        public void StopAll() => StopMusic();

        public void Advance(float seconds)
        {
            if (Music is null)
                return;
            MusicTime += seconds;
            if (MusicLength > 0 && MusicTime >= MusicLength)
            {
                if (_loops)
                    MusicTime -= MusicLength;
                else
                    StopMusic();
            }
        }
    }
}
