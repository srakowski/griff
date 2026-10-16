using System.Globalization;
using System.Numerics;
using Griff.Audio;
using Griff.Data;
using Griff.Editing;
using Griff.Input;
using Griff.Modules.Rhythm;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// Rhythm lanes: a chart of notes (a table of beats and lanes) scrolling to a hit line in time with the music, each press
/// judged against the beat as the player hears it.
/// </summary>
public class NoteLanesTests
{
    private const float Step = 1 / 60f;

    // At 120 beats a minute, a step of the game is a 30th of a beat.
    private const double StepBeats = 2.0 / 60;

    [Fact]
    public void AChart_IsATableOfBeatsAndLanes_PlayedAtADifficulty()
    {
        var chart = Chart(
            (8, 2, 0, null),
            (4, 1, 0, "Easy, Hard"),
            (6, 3, 2, "Hard"),
            (4, 4, 0, "Hard"),
            (10, 7, 0, null));
        var warnings = new List<string>();
        var (game, _, _, lanes, _) = Setup(chart, l => l.Difficulty = "Hard", warnings);

        // The notes that name Hard and those that name none, earliest first (then by lane); lane 7 isn't there.
        Assert.Equal(4, lanes.Notes);
        Assert.Equal([(4.0, 0), (4.0, 3), (6.0, 2), (8.0, 1)], lanes.ChartNotes.Select(n => (n.Beat, n.Lane)));
        Assert.Equal(2, lanes.ChartNotes[2].Hold);
        Assert.Contains(warnings, w => w.Contains("lanes the lanes haven't got") && w.Contains('5'));
        Assert.Equal("........", lanes.Played!.Replace('-', '.'));
        Assert.Equal(["Easy", "Hard"], RhythmChart.Difficulties(chart));

        lanes.Play("Easy");
        Tick(game, game.HostInput);
        Assert.Equal(2, lanes.Notes);
        Assert.Equal("Easy", lanes.Difficulty);

        lanes.Play("Expert");
        Tick(game, game.HostInput);
        Assert.Contains(warnings, w => w.Contains("no difficulty called \"Expert\"") && w.Contains("Easy, Hard"));
    }

    [Fact]
    public void APressOnTheBeat_IsPerfect_AndFurtherOffItsGreatGoodOrMiss()
    {
        var (game, input, _, lanes, clock) = Setup(Chart((4, 1, 0, null), (6, 1, 0, null), (8, 1, 0, null), (10, 1, 0, null), (12, 1, 0, null)));
        var hits = new List<int>();
        var (perfects, misses) = (0, 0);
        game.On("Hit", data => hits.Add((int)data!));
        game.On("Perfect", _ => perfects++);
        game.On("Miss", _ => misses++);

        Press(game, input, clock, 4, Key.D);
        Assert.Equal((NoteJudgment.Perfect, "Perfect", 1), (lanes.LastJudgment, lanes.Judgment, lanes.Combo));
        // 70 ms late, 120 ms late.
        Press(game, input, clock, 6 + 0.14, Key.D);
        Assert.InRange(lanes.Timing, 70, 70 + 17);
        Press(game, input, clock, 8 + 0.24, Key.D);
        Assert.Equal((1, 1, 1, 3), (lanes.Perfects, lanes.Greats, lanes.Goods, lanes.Combo));

        // 300 ms early isn't taken for the note; 160 ms early is a Miss, too soon.
        Press(game, input, clock, 10 - 0.6, Key.D);
        Assert.Equal(0, lanes.Misses);
        Press(game, input, clock, 10 - 0.32, Key.D);
        Assert.Equal((1, 0, 3), (lanes.Misses, lanes.Combo, lanes.BestCombo));

        // Let go by, the last is a Miss once it's later than the Good Window.
        RunTo(game, input, clock, 12 + 0.2);
        Assert.Equal(1, lanes.Misses);
        RunTo(game, input, clock, 12 + 0.32);
        Assert.Equal(2, lanes.Misses);

        Assert.Equal("P-G-O-M-M-", lanes.Played);
        Assert.Equal(600, lanes.Score);
        Assert.Equal(40, lanes.Accuracy, 3);
        Assert.Equal(("D", "Miss"), (lanes.Grade, lanes.Judgment));
        Assert.Equal([300, 200, 100], hits);
        Assert.Equal((1, 2), (perfects, misses));
    }

    [Fact]
    public void APress_IsJudgedByTheBeatAsThePlayerHearsIt_TheirAudioOffsetAllowedFor()
    {
        var (game, input, audio, lanes, clock) = Setup(Chart((4, 2, 0, null), (8, 2, 0, null)));
        game.AudioOffset = 200;

        // Beat 4 reaches the speakers at 2 seconds, and the player hears it at 2.2: pressed then, it's on time.
        Press(game, input, clock, 4, Key.F);
        Assert.InRange(audio.MusicTime, 2.2, 2.2 + 2 * Step);
        Assert.Equal(NoteJudgment.Perfect, lanes.LastJudgment);

        // Pressed just after the music reaches beat 8, before the player hears it, it's 150 ms early: a Miss.
        while (audio.MusicTime < 4.05 - 1e-6)
            Tick(game, input);
        input.SetKey(Key.F, true);
        Tick(game, input);
        input.SetKey(Key.F, false);
        Tick(game, input);
        Assert.Equal((1, 1), (lanes.Perfects, lanes.Misses));
    }

    [Fact]
    public void AHoldNote_IsHeldToItsEnd()
    {
        var (game, input, _, lanes, clock) = Setup(Chart((4, 1, 2, null), (8, 1, 2, null), (12, 1, 2, null)));

        // Held from its beat past its end: both Perfect.
        Press(game, input, clock, 4, Key.D, release: false);
        RunTo(game, input, clock, 5.5);
        Assert.Equal("P.", lanes.Played![..2]);
        RunTo(game, input, clock, 6.05);
        Assert.Equal("PP", lanes.Played![..2]);
        Assert.Equal(2, lanes.Combo);
        input.SetKey(Key.D, false);

        // Let go halfway: its end is a Miss.
        Press(game, input, clock, 8, Key.D, release: false);
        RunTo(game, input, clock, 9);
        input.SetKey(Key.D, false);
        Tick(game, input);
        Assert.Equal("PM", lanes.Played![2..4]);
        Assert.Equal(0, lanes.Combo);

        // Let go just short of its end (within the Good Window): held all the same.
        Press(game, input, clock, 12, Key.D, release: false);
        RunTo(game, input, clock, 13.8);
        input.SetKey(Key.D, false);
        Tick(game, input);
        Assert.Equal("PP", lanes.Played![4..6]);
        Assert.Equal((5, 1), (lanes.Perfects, lanes.Misses));
    }

    [Fact]
    public void TheLanes_TakeKeysTheGamepadClicksAndSeveralFingersAtOnce()
    {
        var chart = Chart((4, 1, 0, null), (4, 4, 0, null), (6, 2, 0, null), (8, 2, 0, null), (8, 3, 0, null), (10, 3, 2, null), (11, 1, 0, null));
        var (game, input, _, lanes, clock) = Setup(chart);

        // A chord on the gamepad: the D-pad's left and B.
        RunTo(game, input, clock, 4 - StepBeats);
        input.SetGamepad([GamepadButton.DPadLeft, GamepadButton.B], Vector2.Zero);
        Tick(game, input);
        input.SetGamepad([], Vector2.Zero);
        Assert.Equal(2, lanes.Perfects);

        // A click on the second lane.
        RunTo(game, input, clock, 6 - StepBeats);
        input.MouseWorld = LaneAt(lanes, 1);
        input.SetMouseButton(MouseButton.Left, true);
        Tick(game, input);
        input.SetMouseButton(MouseButton.Left, false);
        Assert.Equal(3, lanes.Perfects);

        // Two fingers at once, on the second and third lanes (the first is the mouse too, which isn't read as well).
        RunTo(game, input, clock, 8 - StepBeats);
        input.SetTouch(1, LaneAt(lanes, 1), down: true);
        input.SetTouch(2, LaneAt(lanes, 2), down: true);
        input.MouseWorld = LaneAt(lanes, 1);
        input.SetMouseButton(MouseButton.Left, true);
        Tick(game, input);
        input.SetTouch(1, LaneAt(lanes, 1), down: false);
        input.SetTouch(2, LaneAt(lanes, 2), down: false);
        input.SetMouseButton(MouseButton.Left, false);
        Tick(game, input);
        Assert.Equal((5, 0), (lanes.Perfects, lanes.Misses));

        // A finger holds the third lane while another taps the first.
        RunTo(game, input, clock, 10 - StepBeats);
        input.SetTouch(3, LaneAt(lanes, 2), down: true);
        RunTo(game, input, clock, 11 - StepBeats);
        input.SetTouch(4, LaneAt(lanes, 0), down: true);
        Tick(game, input);
        input.SetTouch(4, LaneAt(lanes, 0), down: false);
        RunTo(game, input, clock, 12.1);
        Assert.Equal((8, 0), (lanes.Perfects, lanes.Misses));
        Assert.Equal("P-P-P-P-P-PPP-", lanes.Played);
    }

    [Fact]
    public void FingersOnTheScreen_PlacedInTheWorld_PressTheLanesTheyreOn()
    {
        var (game, input, _, lanes, clock) = Setup(Chart((4, 2, 0, null), (4, 3, 0, null)));
        RunTo(game, input, clock, 4 - StepBeats);
        // On the screen (from -1 to 1 across the view), put in the world as a platform does before the step; down and up
        // again before the step comes.
        input.SetTouch(101, new Vector2(-0.065f, -0.33f), down: true, onScreen: true);
        input.SetTouch(102, new Vector2(0.065f, -0.33f), down: true, onScreen: true);
        input.PlaceTouches(screen => screen * 10);
        input.SetTouch(101, new Vector2(-0.065f, -0.33f), down: false, onScreen: true);
        input.SetTouch(102, new Vector2(0.065f, -0.33f), down: false, onScreen: true);
        input.PlaceTouches(screen => screen * 10);
        Tick(game, input);
        Assert.Equal(2, lanes.Perfects);
    }

    [Fact]
    public void Touches_GoDownMoveAndLift_ADayAtATime()
    {
        var input = new InputState();
        input.SetTouch(7, new Vector2(1, 2), down: true);
        Assert.Equal([new TouchPoint(7, new Vector2(1, 2), Pressed: true, Released: false)], input.Touches);
        input.EndFrame();
        input.SetTouch(7, new Vector2(3, 2), down: true);
        input.SetTouch(8, new Vector2(0, 0), down: true, onScreen: true);
        input.PlaceTouches(screen => screen * 10 + new Vector2(5, 5));
        Assert.Equal([new TouchPoint(7, new Vector2(3, 2), false, false), new TouchPoint(8, new Vector2(5, 5), true, false)], input.Touches);
        input.EndFrame();
        input.SetTouch(7, new Vector2(3, 2), down: false);
        Assert.True(input.Touches.Single(t => t.Id == 7).Released);
        input.EndFrame();
        Assert.Equal([8], input.Touches.Select(t => t.Id));
        input.ReleaseAll();
        Assert.Empty(input.Touches);
    }

    [Fact]
    public void TheNotes_ScrollToTheHitLine_AheadByTheVisualOffset_AtTheSpeedAsked_EitherWay()
    {
        var (game, input, _, lanes, clock) = Setup(Chart((4, 2, 0, null)));
        RunTo(game, input, clock, 3);

        // Down: the hit line is 1.2 from the bottom of lanes 9 long; the note is a beat (half a second) above it at 10 a second.
        var note = NoteDrawn(lanes, 1);
        var hitLine = -(9 / 2f - 1.2f);
        Assert.Equal(hitLine + (4 - lanes.ShownBeats) * 0.5 * 10, note.Y, 3);
        Assert.Equal(lanes.LaneAcross(1), note.X, 3);

        // Seen 100 ms late, it's drawn a tenth of a second (a unit) further on; twice the player's note speed spreads it twice as far.
        game.VisualOffset = 100;
        game.NoteSpeed = 2;
        Tick(game, input);
        Assert.Equal(clock.Beats + 0.2, lanes.ShownBeats, 3);
        Assert.Equal(hitLine + (4 - lanes.ShownBeats) * 0.5 * 20, NoteDrawn(lanes, 1).Y, 3);

        // Coming from the left to a hit line on the right, the lanes one above another.
        lanes.Direction = NoteDirection.Right;
        var across = NoteDrawn(lanes, 1);
        Assert.Equal(-hitLine - (4 - lanes.ShownBeats) * 0.5 * 20, across.X, 3);
        Assert.Equal(-lanes.LaneAcross(1), across.Y, 3);
        Assert.Equal(1, lanes.LaneAt(new Vector2(0, -lanes.LaneAcross(1))));
    }

    [Fact]
    public void TheSong_PlaysOnceFromTheTop_AndOnceItsOverTheLanesAreComplete()
    {
        var (game, input, audio, lanes, clock) = Setup(Chart((4, 1, 0, null), (6, 2, 0, null)), l => l.Song = "Music/Song.wav");
        audio.MusicLength = 4;
        var complete = new List<object?>();
        game.On("Song Complete", data => complete.Add(data));

        Assert.Equal("Music/Song.wav", audio.Music);
        Assert.True(lanes.IsPlaying);
        Press(game, input, clock, 4, Key.D);
        Press(game, input, clock, 6, Key.F);
        Assert.True(lanes.IsPlaying, "the song plays on to its end");
        while (audio.Music is not null)
            Tick(game, input);
        Tick(game, input);
        Assert.False(lanes.IsPlaying);
        Assert.Equal([600], complete);
        Assert.Equal(("S", 100f), (lanes.Grade, lanes.Accuracy));

        // Start plays it again from the top.
        lanes.Start();
        Tick(game, input);
        Assert.Equal((true, 0, "....", "Music/Song.wav"), (lanes.IsPlaying, lanes.Score, lanes.Played!.Replace('-', '.'), audio.Music));
    }

    [Fact]
    public void WithNoSong_TheLanesKeepTimeWithTheMusicPlaying_OrAMetronome()
    {
        var (game, input, audio, lanes, clock) = Setup(Chart((2, 1, 0, null)));
        Assert.Equal(IAudio.MetronomeMusic, audio.Music);
        Press(game, input, clock, 2, Key.D);
        RunTo(game, input, clock, 6.1);
        Assert.False(lanes.IsPlaying);
        Assert.Null(audio.Music);
        Assert.Equal(1, lanes.Perfects);
    }

    [Fact]
    public void ASongsChart_IsTheTableNamedLikeIt_AndAMenuCanSaveTheDifficulty()
    {
        var groove = GrooveSong.Chart();
        var (game, input, _, lanes, _) = Setup(groove, l => (l.Song, l.Chart, l.Difficulty, l.SavedDifficulty) = (GrooveSong.Song, null, "Normal", "difficulty"));
        Assert.Equal(groove.Rows.Count(r => r["Difficulty"].ToString() == "Normal"), lanes.Notes);

        game.SaveData.Set("difficulty", "Hard");
        lanes.Start();
        Tick(game, input);
        Assert.Equal(("Hard", groove.Rows.Count(r => r["Difficulty"].ToString() == "Hard")), (lanes.PlayingDifficulty, lanes.Notes));

        // A difficulty the chart hasn't got isn't taken.
        game.SaveData.Set("difficulty", "Impossible");
        Tick(game, input);
        Assert.Equal("Normal", lanes.PlayingDifficulty);
    }

    [Fact]
    public void Pausing_StopsTheSongWhereItWas_AndItCarriesOnFromThere()
    {
        var (game, input, audio, lanes, clock) = Setup(Chart((4, 1, 0, null), (8, 1, 0, null)), l => l.Song = "Music/Song.wav");
        Press(game, input, clock, 4, Key.D);
        RunTo(game, input, clock, 6);
        var at = audio.MusicTime;

        game.Pause(lanes.Entity!);
        for (var i = 0; i < 30; i++)
            Tick(game, input);
        Assert.Null(audio.Music);
        Assert.Equal(at, lanes.SongTime, 2);
        Assert.Equal(0, lanes.Misses);

        game.Resume(lanes.Entity!);
        Tick(game, input);
        Assert.Equal("Music/Song.wav", audio.Music);
        Assert.Equal(at, audio.MusicTime, 1);
        Press(game, input, clock, 8, Key.D);
        Assert.Equal(2, lanes.Perfects);
    }

    [Fact]
    public void AGameHeldStill_WhileTheMusicPlaysOn_DoesntMissTheNotesThatWentBy()
    {
        // Its own song (paused in the editor, a breakpoint): the song goes back to where the lanes had got to.
        var (game, input, audio, lanes, clock) = Setup(Chart((4, 1, 0, null), (6, 2, 0, null), (8, 1, 0, null)), l => l.Song = "Music/Song.wav");
        Press(game, input, clock, 4, Key.D);
        var at = audio.MusicTime;
        audio.Advance(3);
        Tick(game, input);
        Assert.InRange(audio.MusicTime, at - 0.1, at + 0.1);
        Tick(game, input);
        Assert.InRange(clock.Beats, 4, 4.3);
        Press(game, input, clock, 6, Key.F);
        Assert.Equal(("P-P-.-", 0), (lanes.Played, lanes.Misses));

        // Other music: the notes that went by are left out.
        var (other, otherInput, otherAudio, otherLanes, otherClock) = Setup(Chart((4, 1, 0, null), (6, 2, 0, null), (10, 1, 0, null)));
        Press(other, otherInput, otherClock, 4, Key.D);
        otherAudio.Advance(2.5f);
        Tick(other, otherInput);
        Press(other, otherInput, otherClock, 10, Key.D);
        Assert.Equal(("P-S-P-", 0), (otherLanes.Played, otherLanes.Misses));
    }

    [Fact]
    public void ARewind_CarriesTheSongOnFromWhereItWas_WithWhatWasJudged()
    {
        var chart = Chart((4, 1, 0, null), (6, 2, 0, null), (8, 3, 0, null));
        var (game, input, _, lanes, clock) = Setup(chart, l => l.Song = "Music/Song.wav");
        Press(game, input, clock, 4, Key.D);
        Press(game, input, clock, 6 + 0.14, Key.F);
        RunTo(game, input, clock, 7);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = serializer.LoadFromString(serializer.SaveToString(game.Scene));
        var input2 = new InputState();
        var audio2 = new SilentAudio();
        var again = new Game(scene, input2) { Audio = audio2, Tables = new TableSet([chart]) };
        Tick(again, input2);
        var copy = scene.FindNamed("Lanes")!.GetComponent<NoteLanes>()!;
        Assert.Equal("Music/Song.wav", audio2.Music);
        Assert.InRange(audio2.MusicTime, lanes.SongTime - 1e-4, lanes.SongTime + Step + 1e-4);
        Assert.Equal(("P-G-.-", 500, 2), (copy.Played, copy.Score, copy.Combo));

        Press(again, input2, scene.FindNamed("Lanes")!.GetComponent<BeatClock>()!, 8, Key.J);
        Assert.Equal(("P-G-P-", 800, 3), (copy.Played, copy.Score, copy.Combo));
    }

    [Fact]
    public void TheSameInputs_AtTheSameTimes_ScoreTheSame()
    {
        string Play()
        {
            var chart = Chart((4, 1, 0, null), (4.5, 2, 0, null), (5, 3, 1, null), (6, 4, 0, null), (7, 1, 0, null), (7.5, 2, 0, null));
            var (game, input, _, lanes, clock) = Setup(chart);
            Press(game, input, clock, 4.03, Key.D);
            Press(game, input, clock, 4.62, Key.F);
            Press(game, input, clock, 4.95, Key.J, release: false);
            RunTo(game, input, clock, 5.7);
            input.SetKey(Key.J, false);
            Press(game, input, clock, 6.1, Key.K);
            Press(game, input, clock, 7.2, Key.D);
            RunTo(game, input, clock, 8.5);
            return $"{lanes.Played} {lanes.Score} {lanes.BestCombo} {lanes.Accuracy:0.###}";
        }

        var first = Play();
        Assert.Equal(first, Play());
        Assert.Matches(@"^[PGOM-]{12} \d+", first);
    }

    [Fact]
    public void InANetworkGame_EachMachineJudgesItsOwnPlayersLanes_AndTheHostSendsTheirHitsAndScoresThem()
    {
        var chart = Chart((4, 1, 0, null), (6, 2, 0, null));
        Scene Make()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            var entity = new Entity("Lanes");
            entity.AddComponent<BeatClock>().Tempo = 120;
            var lanes = entity.AddComponent<NoteLanes>();
            (lanes.Chart, lanes.Difficulty, lanes.Song) = ("Chart", "", "Music/Song.wav");
            scene.Add(entity);
            return scene;
        }

        var world = new Griff.Tests.Networking.NetWorld(Make, setUp: game => (game.Audio, game.Tables) = (new SilentAudio(), new TableSet([chart])));
        var (host, client) = (world.Host, world.Client);
        var heard = new List<string>();
        host.On("Hit", data => heard.Add($"host heard Hit {data}"));
        client.On("Hit", data => heard.Add($"client heard Hit {data}"));
        // The guest's headphones are late: their own clock allows for it, and nobody else's timing changes.
        client.AudioOffset = 150;
        var clientLanes = client.Scene.FindNamed("Lanes")!.GetComponent<NoteLanes>()!;
        var clientClock = client.Scene.FindNamed("Lanes")!.GetComponent<BeatClock>()!;
        var hostLanes = host.Scene.FindNamed("Lanes")!.GetComponent<NoteLanes>()!;
        Assert.True(clientLanes.IsPlaying);

        for (var i = 0; i < 600 && clientClock.Beats < 4 - StepBeats - 1e-6; i++)
            world.Run(Griff.Tests.Networking.NetWorld.Frame);
        Griff.Tests.Networking.NetWorld.Keys(client).SetKey(Key.D, true);
        world.Run(Griff.Tests.Networking.NetWorld.Frame);
        Griff.Tests.Networking.NetWorld.Keys(client).SetKey(Key.D, false);
        Assert.Equal((1, 300), (clientLanes.Perfects, clientLanes.Score));
        Assert.Equal(0, hostLanes.Perfects);

        world.Run(0.3);
        var guest = host.Network.Players.Single(p => !p.IsHost);
        Assert.Equal(300, guest.Score);
        Assert.Equal(0, host.Network.Players.Single(p => p.IsHost).Score);
        Assert.Contains("host heard Hit 300", heard);
        Assert.Contains("client heard Hit 300", heard);
    }

    [Fact]
    public void TheNoteSpeed_IsThePlayersSetting_KeptInTheirSaveData_AndASliderSetsIt()
    {
        var game = new Game(new Scene(), new InputState());
        Assert.Equal(1, game.NoteSpeed);
        game.NoteSpeed = 2.5f;
        Assert.Equal(2.5f, game.SaveData.GetFloat(Game.NoteSpeedSaveKey));
        var next = new Game(new Scene(), new InputState()) { SaveData = game.SaveData };
        Assert.Equal(2.5f, next.NoteSpeed);
        next.NoteSpeed = 100;
        Assert.Equal(Game.MaxNoteSpeed, next.NoteSpeed);

        var scene = new Scene();
        var entity = new Entity("Speed");
        var slider = entity.AddComponent<Slider>();
        (slider.Min, slider.Max, slider.Step, slider.Setting) = (0.5f, 3, 0.25f, GameSetting.NoteSpeed);
        scene.Add(entity);
        var input = new InputState();
        var withSlider = new Game(scene, input);
        Tick(withSlider, input);
        Assert.Equal(1, slider.Value);
        withSlider.Focus(slider);
        input.SetKey(Key.Right, true);
        Tick(withSlider, input);
        input.SetKey(Key.Right, false);
        Assert.Equal(1.25f, withSlider.NoteSpeed);
    }

    [Fact]
    public void CreateNoteLanes_MakesLanesWithASongAndItsChart_ThatPlayStraightAway()
    {
        var preset = EntityPreset.All.OfType<NoteLanesPreset>().Single();
        var files = preset.Files();
        Assert.Contains("Music/Groove.wav", files.Keys);
        var chart = TableFile.Read(System.Text.Encoding.UTF8.GetString(files["Tables/Groove.table"]), "Groove");
        Assert.Equal(["Easy", "Normal", "Hard"], RhythmChart.Difficulties(chart));
        var (notes, problems) = RhythmChart.Read(chart, "Normal", 4);
        Assert.Empty(problems);
        Assert.InRange(notes.Length, 40, 80);
        Assert.Contains(notes, n => n.Hold > 0);
        // The song is 16 bars at 120: every note ends before it does, and no lane has two notes at once.
        Assert.All(["Easy", "Normal", "Hard"], difficulty =>
        {
            var (all, _) = RhythmChart.Read(chart, difficulty, 4);
            Assert.All(all, n => Assert.InRange(n.Beat + n.Hold, 4, 63));
            for (var lane = 0; lane < 4; lane++)
            {
                var inLane = all.Where(n => n.Lane == lane).ToList();
                for (var i = 1; i < inLane.Count; i++)
                    Assert.True(inLane[i].Beat > inLane[i - 1].Beat + inLane[i - 1].Hold, $"{difficulty}: lane {lane + 1} overlaps at beat {inLane[i].Beat}");
            }
        });
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(files["Music/Groove.wav"], 0, 4));

        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        scene.Add(camera);
        var entity = new Entity(preset.EntityName(scene));
        preset.Configure(entity, scene);
        scene.Add(entity);
        foreach (var child in preset.CreateChildren(scene))
            scene.Add(child, entity);
        var lanes = entity.GetComponent<NoteLanes>()!;
        Assert.NotNull(entity.GetComponent<BeatClock>());
        Assert.Equal((GrooveSong.Song, "Normal"), (lanes.Song, lanes.Difficulty));

        var input = new InputState();
        var audio = new SilentAudio { MusicTempo = 120 };
        var game = new Game(scene, input) { Audio = audio, Tables = new TableSet([chart]) };
        Tick(game, input);
        Assert.Equal((GrooveSong.Song, notes.Length), (audio.Music, lanes.Notes));
        var clock = entity.GetComponent<BeatClock>()!;
        var first = notes[0];
        Press(game, input, clock, first.Beat, Enum.Parse<Key>("DFJK"[first.Lane].ToString()));
        Assert.Equal(1, lanes.Perfects);
        Tick(game, input);
        Assert.Equal("Score 300", scene.FindNamed("Score")!.GetComponent<Text>()!.Shown);
    }

    [Fact]
    public void ThePlaytestReport_SaysHowItsGoing()
    {
        var (game, input, _, lanes, clock) = Setup(Chart((4, 1, 0, null), (6, 1, 0, null)));
        Press(game, input, clock, 4, Key.D);
        Assert.InRange(lanes.Timing, 0, 17);
        Assert.Equal($", playing: 2 notes, score 300, combo 1, 1 Perfect, 0 Great, 0 Good, 0 Miss, accuracy 100%, last Perfect (last press {lanes.Timing:+0;-0;0} ms)", ((IPlaytestReport)lanes).Report());
        Assert.Null(lanes.SetupNote());
        lanes.Lanes = 6;
        Assert.Equal("Lanes 5 and 6 have no action in Actions, so only a click or a touch presses it.", lanes.SetupNote());
    }

    [Fact]
    public void MusicStartedPartWayIn_IsHeardFromThere_LoopingOrNot()
    {
        var now = 0.0;
        var mixer = new Mixer { Clock = () => now };
        var clip = new SoundClip(new float[Mixer.SampleRate * 2], 1, Mixer.SampleRate);
        var voice = mixer.Play(clip.Open(), skip: Mixer.SampleRate / 2);
        Assert.Equal(0.5, voice.Heard, 3);
        var looping = mixer.Play(clip.Open(), loop: true, skip: Mixer.SampleRate * 5);
        Assert.Equal(5, looping.Heard, 3);

        var audio = new MixerAudio(mixer, new OneSong(clip));
        audio.PlayMusicFrom("Music/Song.wav", 1.25, 0.5f, loop: false);
        Assert.Equal(("Music/Song.wav", 1.25), (audio.Music, Math.Round(audio.MusicTime, 3)));

        var silent = new SilentAudio();
        silent.PlayMusicFrom("Music/Song.wav", 3);
        Assert.Equal(3, silent.MusicTime);

        // Music that doesn't loop, played to its end, is music no more: the lanes playing it know their song is over.
        audio.PlayMusicFrom("Music/Song.wav", 1.9, 0.5f, loop: false);
        var buffer = new float[2048];
        for (var i = 0; i < 20; i++)
            mixer.Mix(buffer);
        audio.Advance(0.1f);
        Assert.Null(audio.Music);
        Assert.Equal(0, audio.MusicTime);
    }

    // ----- Helpers -----

    private static Table Chart(params (double Beat, int Lane, double Hold, string? Difficulty)[] notes)
    {
        var columns = new[]
        {
            new TableColumn("Beat"), new TableColumn("Lane", ColumnType.WholeNumber), new TableColumn("Hold"), new TableColumn("Difficulty", ColumnType.Text),
        };
        var rows = notes.Select((n, i) => ($"Note {i + 1}", (IReadOnlyList<TableValue>)
        [
            TableValue.Of(n.Beat), TableValue.Of((long)n.Lane), n.Hold > 0 ? TableValue.Of(n.Hold) : TableValue.Empty, TableValue.Of(n.Difficulty),
        ]));
        return new Table("Chart", columns, rows);
    }

    private static (Game Game, InputState Input, SilentAudio Audio, NoteLanes Lanes, BeatClock Clock) Setup(Table chart, Action<NoteLanes>? set = null,
        List<string>? warnings = null)
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        scene.Add(camera);
        var entity = new Entity("Lanes");
        var clock = entity.AddComponent<BeatClock>();
        clock.Tempo = 120;
        var lanes = entity.AddComponent<NoteLanes>();
        (lanes.Chart, lanes.Difficulty) = (chart.Name, "");
        set?.Invoke(lanes);
        scene.Add(entity);
        var input = new InputState();
        var audio = new SilentAudio();
        var game = new Game(scene, input) { Audio = audio, Tables = new TableSet([chart]) };
        if (warnings is not null)
            game.Problems.Reported += p => warnings.Add(p.Message);
        Tick(game, input);
        return (game, input, audio, lanes, clock);
    }

    // The middle of lane `lane` (from 0), in the world.
    private static Vector2 LaneAt(NoteLanes lanes, int lane) => Vector2.Transform(lanes.Axes.Across * lanes.LaneAcross(lane), lanes.Entity!.Transform.WorldMatrix);

    // Where the note in lane `lane` is drawn, in the lanes' own space: the full-colored quad the width of a note.
    private static Vector2 NoteDrawn(NoteLanes lanes, int lane)
    {
        var draws = new List<SpriteDraw>();
        ((IDrawable)lanes).Draw(new Drawing(draws, lanes.Entity!, new Box(Vector2.Zero, new Vector2(100)), 0.05f, isPlaying: true));
        var color = lanes.ColorOf(lane);
        var note = draws.Single(d => d.Text is null && d.Tint == color && Math.Abs(Math.Max(d.World.M11, d.World.M22) - lanes.LaneWidth * 0.84f) < 1e-4);
        return new Vector2(note.World.M41, note.World.M42);
    }

    // Steps until this step's beat (as heard) is at `beat` or just past it.
    private static void RunTo(Game game, InputState input, BeatClock clock, double beat)
    {
        for (var i = 0; i < 60 * 60 && clock.Beats < beat - 1e-6; i++)
            Tick(game, input);
    }

    // Presses `key` in the step whose beat (as heard) is `beat` or the first just past it.
    private static void Press(Game game, InputState input, BeatClock clock, double beat, Key key, bool release = true)
    {
        RunTo(game, input, clock, beat - StepBeats);
        input.SetKey(key, true);
        Tick(game, input);
        if (release)
            input.SetKey(key, false);
    }

    private static void Tick(Game game, IHostInput input)
    {
        game.Tick(Step);
        input.EndFrame();
    }

    private sealed class OneSong(SoundClip clip) : ISoundLibrary
    {
        public SoundClip? Load(string sound) => clip;
    }
}
