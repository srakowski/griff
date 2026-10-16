using System.Numerics;
using Griff.Data;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Rhythm;

/// <summary>
/// Plays note lanes with no code: reads each one's chart, plays its song (pausing it with the game, and carrying it on
/// after a rewind), reads its lanes' keys, buttons, clicks and touches, and judges each press against the beat as the
/// player hears it, by the lanes' beat clock. Each machine plays its own player's lanes; what they're judged is sent to the
/// host, which sends the lanes' messages and adds the points to that player's score.
/// </summary>
internal sealed class RhythmSystem : GameSystem
{
    internal const string RhythmCommand = "griff.rhythm";

    private const int JudgedCommand = 0;
    private const int CompletedCommand = 1;

    // The music heard jumping on this far (seconds) in one step means the game was held still meanwhile (paused in the
    // editor, stopped at a breakpoint, a long stall) while the music played on.
    private const double HeldStill = 1;

    // Lanes seen showing, so one that goes (its panel closing) takes its song with it.
    private readonly List<NoteLanes> _showing = [];

    // The save data the lanes last read from, and how many the game has had: lanes read again from new save data.
    private SaveData? _saves;
    private int _savesSeen;

    // The player's settings: how fast they like the notes to come.
    private readonly RhythmSettingsSystem _settings;

    public RhythmSystem(RhythmSettingsSystem settings) => _settings = settings;

    public override string Name => "Note Lanes";

    private IAudio Audio => Game.Audio;

    protected override void SceneChanged() => _showing.Clear();

    // After the beat clocks (and calibrations), before scripts: they see this step's judgments.
    internal void Run()
    {
        for (var i = _showing.Count - 1; i >= 0; i--)
        {
            var gone = _showing[i];
            if (gone.Entity is { } entity && entity.Scene == Game.Scene && entity.IsActive)
                continue;
            _showing.RemoveAt(i);
            gone.Seen = false;
            if (gone.Entity?.Scene == Game.Scene && gone.IsPlaying && PlayingIts(gone))
            {
                gone.SongTime = (float)Math.Max(0, Audio.MusicTime);
                Audio.StopMusic();
            }
        }

        var all = Game.Showing<NoteLanes>();
        if (all.Count == 0)
            return;
        foreach (var lanes in all)
        {
            if (!_showing.Contains(lanes))
                _showing.Add(lanes);
            if (lanes.Entity?.Scene == Game.Scene)
                Step(lanes);
        }
    }

    private void Step(NoteLanes lanes)
    {
        lanes.Now = Game.Time.RealTotal;
        SizeLanes(lanes);
        ReadSaved(lanes);
        var wanted = lanes.Wanted;
        lanes.Wanted = LanesRequest.None;
        if (!lanes.Seen)
        {
            lanes.Seen = true;
            // Playing already: a rewind (or a scene loaded mid-song) carries on where it was.
            if (lanes.IsPlaying && wanted == LanesRequest.None)
            {
                LoadChart(lanes);
                Resume(lanes);
                return;
            }

            if (lanes.StartOnShow && wanted == LanesRequest.None)
                wanted = LanesRequest.Start;
        }

        if (wanted == LanesRequest.Stop)
            Stop(lanes);
        LoadChart(lanes);
        if (wanted == LanesRequest.Start)
        {
            Begin(lanes);
            return;
        }

        var clock = ClockOf(lanes);
        var paused = Game.PausedThisFrame || lanes.Entity!.IsPaused;
        lanes.SpeedNow = Math.Max(0, lanes.Speed) * _settings.NoteSpeed;
        // Its song started again this step (from where it was): the clock picks it up next step.
        var restarted = KeepSong(lanes, paused);
        var music = !restarted && clock is { IsPlaying: true } && (!OwnsSong(lanes) || PlayingIts(lanes));
        if (clock is not null && music)
        {
            (lanes.ShownBeats, lanes.SecondsPerBeat) = (clock.Shown, clock.SecondsPerBeat);
            if (lanes.IsPlaying)
                lanes.Progress = lanes.LastBeat > 0 ? (float)Math.Clamp(clock.Beats / lanes.LastBeat, 0, 1) : 0;
        }
        // Held still by a pause, the notes stay where they were.
        lanes.Following = music || lanes.SongHeld || restarted;
        if (restarted)
            return;

        if (!lanes.IsPlaying || paused)
        {
            if (!lanes.IsPlaying)
                Array.Clear(lanes.Down);
            return;
        }

        if (!music || clock is null)
        {
            SongOver(lanes);
            lanes.LastBeats = double.NaN;
            return;
        }

        // Held still while other music played on: the notes that went by meanwhile are left out rather than missed.
        if (!OwnsSong(lanes) && double.IsFinite(lanes.LastBeats) && (clock.Beats - lanes.LastBeats) * clock.SecondsPerBeat > HeldStill)
            lanes.Fresh = true;
        lanes.LastBeats = clock.Beats;

        // Starting, or carrying on: the notes that went by before it began are left out rather than missed.
        if (lanes.Fresh)
        {
            lanes.Fresh = false;
            LeaveOut(lanes, clock, gone: true);
        }

        Span<bool> pressed = stackalloc bool[lanes.Down.Length];
        ReadInput(lanes, pressed);
        Judge(lanes, clock, pressed);
        if (lanes.Entity?.Scene != Game.Scene)
            return;
        MissLate(lanes, clock);
        if (lanes.Entity?.Scene != Game.Scene)
            return;

        // Every note done with: lanes keeping time with other music are over a bar after the last note ends.
        if (lanes.IsPlaying && lanes.FirstOpen >= lanes.ChartNotes.Length && !Holds(lanes) && !OwnsSong(lanes) &&
            clock.Beats > lanes.LastBeat + Math.Max(1, clock.BeatsPerBar))
            Complete(lanes);
    }

    // ----- Starting, stopping and the song -----

    private void Begin(NoteLanes lanes)
    {
        (lanes.Score, lanes.Combo, lanes.BestCombo) = (0, 0, 0);
        (lanes.Perfects, lanes.Greats, lanes.Goods, lanes.Misses) = (0, 0, 0, 0);
        (lanes.LastJudgment, lanes.PoppedAt, lanes.Timing, lanes.Timed) = (NoteJudgment.None, double.NegativeInfinity, 0, false);
        FreshMarks(lanes);
        Array.Fill(lanes.Holding, -1);
        Array.Clear(lanes.Down);
        (lanes.IsPlaying, lanes.SongTime, lanes.SongHeld, lanes.Fresh, lanes.Progress) = (true, 0, false, true, 0);
        ReadBest(lanes);
        if (OwnsSong(lanes))
        {
            Audio.PlayMusic(lanes.PlayingSong!.Trim(), Volume(lanes), loop: false);
            if (!PlayingIts(lanes))
                Game.Warn($"The note lanes \"{lanes.Entity!.Name}\" can't play their song \"{lanes.PlayingSong!.Trim()}\": it isn't in the project's Assets.");
        }
        else if (Audio.Music is null)
        {
            // Nothing to keep time with: a metronome at the clock's tempo, so the chart plays.
            var clock = ClockOf(lanes);
            Audio.PlayMetronome(clock is { Tempo: > 0 } ? clock.Tempo : 120, clock?.BeatsPerBar ?? 4, 0.6f);
        }
    }

    // A rewind, or a scene loaded mid-song: its song carries on from where it had got to.
    private void Resume(NoteLanes lanes)
    {
        Array.Fill(lanes.Holding, -1);
        lanes.Fresh = true;
        if (OwnsSong(lanes))
            Audio.PlayMusicFrom(lanes.PlayingSong!.Trim(), lanes.SongTime, Volume(lanes), loop: false);
    }

    private void Stop(NoteLanes lanes)
    {
        if (lanes.IsPlaying && (PlayingIts(lanes) || !OwnsSong(lanes) && Audio.Music == IAudio.MetronomeMusic))
            Audio.StopMusic();
        lanes.IsPlaying = false;
        lanes.SongHeld = false;
        Array.Fill(lanes.Holding, -1);
        Array.Clear(lanes.Down);
    }

    // Its song stops while the game is paused (where it had got to kept), and starts again from there as it goes on; true
    // if it started again this step.
    private bool KeepSong(NoteLanes lanes, bool paused)
    {
        if (!lanes.IsPlaying || !OwnsSong(lanes))
            return false;
        if (paused)
        {
            if (!lanes.SongHeld && PlayingIts(lanes))
            {
                lanes.SongTime = (float)Math.Max(0, Audio.MusicTime);
                Audio.StopMusic();
                lanes.SongHeld = true;
            }

            return false;
        }

        if (lanes.SongHeld)
        {
            lanes.SongHeld = false;
            Audio.PlayMusicFrom(lanes.PlayingSong!.Trim(), lanes.SongTime, Volume(lanes), loop: false);
            return true;
        }

        if (!PlayingIts(lanes))
            return false;
        // Held still while its song played on: the song goes back to where the lanes had got to.
        var heard = Math.Max(0, Audio.MusicTime);
        if (heard - lanes.SongTime > HeldStill)
        {
            Audio.PlayMusicFrom(lanes.PlayingSong!.Trim(), lanes.SongTime, Volume(lanes), loop: false);
            return true;
        }

        lanes.SongTime = (float)heard;
        return false;
    }

    // Its own song has ended (or something else took the music over): the notes it didn't reach are left out, and it's over.
    private void SongOver(NoteLanes lanes)
    {
        if (!OwnsSong(lanes) || lanes.SongHeld || PlayingIts(lanes))
            return;
        LeaveOut(lanes, null, gone: false);
        Complete(lanes);
    }

    private void Complete(NoteLanes lanes)
    {
        lanes.IsPlaying = false;
        Array.Fill(lanes.Holding, -1);
        if (!OwnsSong(lanes) && Audio.Music == IAudio.MetronomeMusic)
            Audio.StopMusic();
        lanes.Progress = 1;
        KeepBest(lanes);
        lanes.RaiseCompleted();
        Game.Net.SendCommand(RhythmCommand, new object?[] { CompletedCommand, lanes.Entity!.Name, 0, lanes.Score });
    }

    // The best score of the song at its difficulty kept so far, as a play starts.
    private void ReadBest(NoteLanes lanes)
    {
        lanes.NewBest = false;
        lanes.Best = BestKeyOf(lanes) is { } key ? Math.Max(0, Game.SaveData.GetInt(key)) : 0;
    }

    // Once the song's over: a score beating the best kept for it is kept in its place, with its grade.
    private void KeepBest(NoteLanes lanes)
    {
        if (BestKeyOf(lanes) is not { } key || lanes.Perfects + lanes.Greats + lanes.Goods + lanes.Misses == 0)
            return;
        var best = Math.Max(0, Game.SaveData.GetInt(key));
        if (lanes.Score <= best && Game.SaveData.Has(key))
        {
            (lanes.Best, lanes.NewBest) = (best, false);
            return;
        }

        Game.SaveData.Set(key, lanes.Score);
        Game.SaveData.Set(key + ".Grade", lanes.Grade);
        (lanes.Best, lanes.NewBest) = (lanes.Score, lanes.Score > best);
    }

    private static string? BestKeyOf(NoteLanes lanes) =>
        string.IsNullOrWhiteSpace(lanes.SaveBestAs) || string.IsNullOrWhiteSpace(lanes.ChartName) ? null
            : NoteLanes.BestKey(lanes.SaveBestAs, lanes.ChartName, lanes.PlayingDifficulty);

    // What its Saved Song and Saved Difficulty name in the save data: read again only when something's been saved (or they
    // name something else), so a step makes no garbage. The song it plays is the saved one, or its own.
    private void ReadSaved(NoteLanes lanes)
    {
        var saves = Game.SaveData;
        if (saves != _saves)
            (_saves, _savesSeen) = (saves, _savesSeen + 1);
        var read = lanes.SavesRead;
        if (read.Saves != _savesSeen || read.Version != saves.Version || read.Song != lanes.SavedSong || read.Difficulty != lanes.SavedDifficulty)
        {
            lanes.SavesRead = (_savesSeen, saves.Version, lanes.SavedSong, lanes.SavedDifficulty);
            lanes.SavedSongValue = Saved(lanes.SavedSong);
            lanes.SavedDifficultyValue = Saved(lanes.SavedDifficulty);
        }

        lanes.PlayingSong = lanes.SavedSongValue ?? lanes.Song;

        string? Saved(string? name) =>
            string.IsNullOrWhiteSpace(name) || saves.GetString(name.Trim()) is not { } value || string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool OwnsSong(NoteLanes lanes) => !string.IsNullOrWhiteSpace(lanes.PlayingSong);

    private bool PlayingIts(NoteLanes lanes) =>
        OwnsSong(lanes) && Audio.Music is { } music && string.Equals(music.Trim(), lanes.PlayingSong!.Trim(), StringComparison.OrdinalIgnoreCase);

    private static float Volume(NoteLanes lanes) => float.IsFinite(lanes.Volume) ? Math.Clamp(lanes.Volume, 0, 1) : 0.8f;

    // Its own clock, the one it names, or the scene's first.
    private BeatClock? ClockOf(NoteLanes lanes) =>
        string.IsNullOrWhiteSpace(lanes.Clock) ? lanes.Entity!.GetComponent<BeatClock>() ?? Game.ClockFor(null) : Game.ClockFor(lanes.Clock);

    // ----- The chart -----

    private void LoadChart(NoteLanes lanes)
    {
        var name = !string.IsNullOrWhiteSpace(lanes.Chart) ? lanes.Chart.Trim()
            : OwnsSong(lanes) ? ChartNamed(lanes.PlayingSong!)
            : Audio.Music is { } music && music != IAudio.MetronomeMusic ? ChartNamed(music)
            : null;
        var count = (int)lanes.LaneCount;
        var version = Game.Tables.Version;
        var saved = lanes.SavedDifficultyValue;
        if (lanes.ChartFrom is { } from && from.Table == name && from.Version == version && from.Lanes == count &&
            from.Difficulty == lanes.Difficulty && from.Saved == saved)
            return;
        lanes.ChartFrom = (name, version, lanes.Difficulty, saved, count);
        (lanes.ChartName, lanes.Title) = (name, name ?? "");

        Table? table = null;
        try
        {
            table = name is null ? null : Game.Tables.Find(name);
        }
        catch (InvalidDataException ex)
        {
            Game.Warn($"The chart of the note lanes \"{lanes.Entity!.Name}\" doesn't read: {ex.Message}");
        }

        var difficulty = Pick(lanes, table, saved);
        if (lanes.PlayingDifficulty != difficulty)
            lanes.PlayingDifficulty = difficulty;

        var notes = Array.Empty<RhythmNote>();
        if (table is not null)
        {
            (notes, var problems) = RhythmChart.Read(table, difficulty, count);
            foreach (var problem in problems)
                Game.Warn(problem);
        }
        else if (name is not null && Game.Tables.ProblemWith(name) is null)
        {
            Game.Warn($"The note lanes \"{lanes.Entity!.Name}\" have no chart: there's no table called \"{name}\" (Data > New Table, with Beat, Lane, Hold and Difficulty columns).");
        }

        var changed = !notes.SequenceEqual(lanes.ChartNotes);
        lanes.ChartNotes = notes;
        lanes.Notes = notes.Length;
        lanes.LastBeat = notes.Length == 0 ? double.NegativeInfinity : notes.Max(n => n.Beat + n.Hold);
        if (lanes.Marks.Length != notes.Length * 2)
        {
            // A chart changed while it plays carries on from where the song is.
            FreshMarks(lanes);
            if (lanes.IsPlaying)
                lanes.Fresh = true;
        }

        if (changed)
            Array.Fill(lanes.Holding, -1);
        lanes.FirstOpen = 0;
        AdvanceOpen(lanes);
    }

    // The difficulty a menu saved (if the chart has it), or its own.
    private static string Pick(NoteLanes lanes, Table? table, string? saved)
    {
        if (saved is { Length: > 0 } && (table is null || RhythmChart.Difficulties(table).Contains(saved.Trim(), StringComparer.OrdinalIgnoreCase)))
            return saved.Trim();
        return lanes.Difficulty?.Trim() ?? "";
    }

    // The table a song's chart is: named like its file ("Music/Groove.wav" plays "Groove"). Kept, so a step makes no garbage.
    private string ChartNamed(string song)
    {
        if (!_chartNames.TryGetValue(song, out var name))
            _chartNames[song] = name = Path.GetFileNameWithoutExtension(song.Trim());
        return name;
    }

    private readonly Dictionary<string, string> _chartNames = [];

    private static void FreshMarks(NoteLanes lanes)
    {
        var notes = lanes.ChartNotes;
        var marks = new char[notes.Length * 2];
        for (var i = 0; i < notes.Length; i++)
            (marks[i * 2], marks[i * 2 + 1]) = ('.', notes[i].Hold > 0 ? '.' : '-');
        lanes.Marks = marks;
        lanes.MarksChanged();
        lanes.FirstOpen = 0;
    }

    private static void AdvanceOpen(NoteLanes lanes)
    {
        var marks = lanes.Marks;
        var open = lanes.FirstOpen;
        while (open < lanes.ChartNotes.Length && marks[open * 2] != '.')
            open++;
        lanes.FirstOpen = open;
    }

    // Leaves out the notes still to play that have gone by (or all of them): marked S, counted nowhere.
    private static void LeaveOut(NoteLanes lanes, BeatClock? clock, bool gone)
    {
        var notes = lanes.ChartNotes;
        var marks = lanes.Marks;
        var changed = false;
        for (var i = lanes.FirstOpen; i < notes.Length; i++)
        {
            if (clock is not null && clock.SecondsFrom(notes[i].Beat) * 1000 <= lanes.GoodWindow && gone)
                break;
            if (marks[i * 2] == '.')
            {
                marks[i * 2] = 'S';
                changed = true;
            }

            if (marks[i * 2 + 1] == '.' && !IsHeld(lanes, i))
            {
                marks[i * 2 + 1] = 'S';
                changed = true;
            }
        }

        if (changed)
            lanes.MarksChanged();
        AdvanceOpen(lanes);
    }

    private static bool IsHeld(NoteLanes lanes, int note) => Array.IndexOf(lanes.Holding, note) >= 0;

    private static bool Holds(NoteLanes lanes)
    {
        foreach (var held in lanes.Holding)
        {
            if (held >= 0)
                return true;
        }

        return false;
    }

    // ----- Playing -----

    private static void SizeLanes(NoteLanes lanes)
    {
        var count = (int)lanes.LaneCount;
        if (lanes.Down.Length == count)
            return;
        lanes.Down = new bool[count];
        lanes.Holding = new int[count];
        Array.Fill(lanes.Holding, -1);
        lanes.PressedAt = new double[count];
        lanes.HitAt = new double[count];
        Array.Fill(lanes.PressedAt, double.NegativeInfinity);
        Array.Fill(lanes.HitAt, double.NegativeInfinity);
    }

    // Each lane's action, and clicks and fingers on it: held down, and pressed this step. Like a menu's controls, the lanes
    // listen while they're on the top panel (or none is open), and while they have the player's input.
    private void ReadInput(NoteLanes lanes, Span<bool> pressed)
    {
        var input = Game.LocalInput;
        var down = lanes.Down;
        Array.Clear(down);
        var top = Game.TopPanel;
        if (!lanes.Controllable || top is { Entity: { } owner } && !lanes.Entity!.IsSelfOrDescendantOf(owner))
            return;

        for (var lane = 0; lane < down.Length; lane++)
        {
            if (lanes.ActionOf(lane) is not { } action)
                continue;
            down[lane] = input.IsDown(action);
            pressed[lane] = input.WasPressed(action);
        }

        if (!lanes.Clicks || !Matrix4x4.Invert(lanes.Entity!.Transform.WorldMatrix, out var toLocal))
            return;
        var touches = input.Touches;
        if (touches.Count > 0)
        {
            // Every finger (the first is the mouse too, so the mouse isn't read as well).
            foreach (var touch in touches)
            {
                var lane = lanes.LaneAt(Vector2.Transform(touch.World, toLocal));
                if (lane < 0)
                    continue;
                down[lane] |= !touch.Released;
                pressed[lane] |= touch.Pressed;
            }
        }
        else if (input.IsMouseInView)
        {
            var lane = lanes.LaneAt(Vector2.Transform(input.MouseWorld, toLocal));
            if (lane >= 0)
            {
                down[lane] |= input.IsDown(MouseButton.Left);
                pressed[lane] |= input.WasPressed(MouseButton.Left);
            }
        }
    }

    // A press takes the first note in its lane not yet pressed, if it's near enough; a hold held to its end (or let go just
    // short of it) is done.
    private void Judge(NoteLanes lanes, BeatClock clock, Span<bool> pressed)
    {
        var notes = lanes.ChartNotes;
        var marks = lanes.Marks;
        var reach = Math.Max(lanes.MissWindow, lanes.GoodWindow);
        for (var lane = 0; lane < pressed.Length; lane++)
        {
            if (!pressed[lane])
                continue;
            lanes.PressedAt[lane] = lanes.Now;
            for (var i = lanes.FirstOpen; i < notes.Length; i++)
            {
                var note = notes[i];
                if (note.Lane != lane || marks[i * 2] != '.')
                    continue;
                var off = clock.SecondsFrom(note.Beat) * 1000;
                if (off < -reach)
                    break;
                var judgment = lanes.JudgmentFor(off);
                if (judgment == NoteJudgment.None)
                    continue;
                (lanes.Timing, lanes.Timed) = (MathF.Round(off), true);
                Mark(lanes, i, end: false, judgment);
                if (note.Hold > 0)
                {
                    if (judgment == NoteJudgment.Miss)
                        Mark(lanes, i, end: true, NoteJudgment.Miss);
                    else
                    {
                        if (lanes.Holding[lane] is var held and >= 0 && marks[held * 2 + 1] == '.')
                            Mark(lanes, held, end: true, NoteJudgment.Miss);
                        lanes.Holding[lane] = i;
                    }
                }

                break;
            }
        }

        for (var lane = 0; lane < lanes.Holding.Length; lane++)
        {
            var held = lanes.Holding[lane];
            if (held < 0)
                continue;
            var note = notes[held];
            var fromEnd = clock.SecondsFrom(note.Beat + note.Hold) * 1000;
            if (fromEnd >= 0 || !lanes.Down[lane])
            {
                lanes.Holding[lane] = -1;
                Mark(lanes, held, end: true, fromEnd >= -lanes.GoodWindow ? NoteJudgment.Perfect : NoteJudgment.Miss);
            }
        }

        AdvanceOpen(lanes);
    }

    // A note gone further past the line than the Good Window, unpressed, is a Miss (a hold's end too).
    private void MissLate(NoteLanes lanes, BeatClock clock)
    {
        var notes = lanes.ChartNotes;
        var marks = lanes.Marks;
        for (var i = lanes.FirstOpen; i < notes.Length; i++)
        {
            if (marks[i * 2] != '.')
                continue;
            if (clock.SecondsFrom(notes[i].Beat) * 1000 <= lanes.GoodWindow)
                break;
            Mark(lanes, i, end: false, NoteJudgment.Miss);
            if (notes[i].Hold > 0)
                Mark(lanes, i, end: true, NoteJudgment.Miss);
        }

        AdvanceOpen(lanes);
    }

    private void Mark(NoteLanes lanes, int note, bool end, NoteJudgment judgment)
    {
        lanes.Marks[note * 2 + (end ? 1 : 0)] = judgment switch
        {
            NoteJudgment.Perfect => 'P',
            NoteJudgment.Great => 'G',
            NoteJudgment.Good => 'O',
            _ => 'M',
        };
        lanes.MarksChanged();

        var points = NoteLanes.PointsFor(judgment);
        switch (judgment)
        {
            case NoteJudgment.Perfect:
                lanes.Perfects++;
                break;
            case NoteJudgment.Great:
                lanes.Greats++;
                break;
            case NoteJudgment.Good:
                lanes.Goods++;
                break;
            default:
                lanes.Misses++;
                break;
        }

        var lane = lanes.ChartNotes[note].Lane;
        lanes.Score += points;
        lanes.Combo = judgment == NoteJudgment.Miss ? 0 : lanes.Combo + 1;
        lanes.BestCombo = Math.Max(lanes.BestCombo, lanes.Combo);
        (lanes.LastJudgment, lanes.PoppedAt) = (judgment, lanes.Now);
        if (judgment != NoteJudgment.Miss && lane < lanes.HitAt.Length)
            lanes.HitAt[lane] = lanes.Now;
        var sound = judgment == NoteJudgment.Miss ? lanes.MissSound : lanes.HitSound;
        if (!string.IsNullOrWhiteSpace(sound))
            Audio.Play(sound.Trim());
        lanes.RaiseJudged(judgment, lane);
        Game.Net.SendCommand(RhythmCommand, new object?[] { JudgedCommand, lanes.Entity!.Name, (int)judgment, points });
    }

    // ----- What the host does with them -----

    /// <summary>
    /// On the host (or offline): a player's lanes judged a note (or their song is over). Their lanes' messages go out from
    /// the host's copy, to every machine, and in a network game the points go to that player's score.
    /// </summary>
    internal void RunCommand(NetPlayer? player, object? args)
    {
        if (args is not object?[] { Length: 4 } a || a[0] is not int kind || a[1] is not string name || a[2] is not int judged ||
            a[3] is not int points)
            return;
        var lanes = Game.Find(name)?.GetComponent<NoteLanes>();
        if (kind == CompletedCommand)
        {
            Send(lanes?.CompleteMessage, points, lanes);
            return;
        }

        if (kind != JudgedCommand || !Enum.IsDefined((NoteJudgment)judged) || (NoteJudgment)judged == NoteJudgment.None)
            return;
        points = Math.Clamp(points, 0, NoteLanes.PerfectPoints);
        if (Game.Net.IsOnline && player is not null && points > 0)
            Game.Net.AddScore(player, points);
        if ((NoteJudgment)judged == NoteJudgment.Miss)
        {
            Send(lanes?.MissMessage, null, lanes);
            return;
        }

        Send(lanes?.HitMessage, points, lanes);
        if ((NoteJudgment)judged == NoteJudgment.Perfect)
            Send(lanes?.PerfectMessage, null, lanes);
    }

    private void Send(string? message, object? data, NoteLanes? lanes)
    {
        if (lanes?.Entity is { } from && !string.IsNullOrWhiteSpace(message))
            Game.Send(message.Trim(), data, from);
    }
}
