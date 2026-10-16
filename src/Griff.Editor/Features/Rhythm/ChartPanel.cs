using System.Globalization;
using System.IO;
using System.Windows.Threading;
using Griff.Audio;
using Griff.Data;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Undo;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Audio;
using Griff.Editor.Features.Data;
using Griff.Editor.Features.Play;
using Griff.Editing;
using Griff.Editor.Themes;
using Griff.Modules.Rhythm;
using Griff.Platform.Windows;
using Griff.Runtime;
using Griff.Scenes;
using InputKey = Griff.Input.Key;

namespace Griff.Editor.Features.Rhythm;

/// <summary>
/// The Chart panel: a rhythm chart (a table of notes: Beat, Lane, Hold, Difficulty) on a timeline, its notes in their lanes
/// over the song's beats and bars, to record by tapping along and to edit by hand. Pick the chart
/// (<see cref="ChartName"/>; selecting Note Lanes picks theirs, with their song, beat clock and lanes), the
/// <see cref="Difficulty"/>, the <see cref="Snap"/> and whether a take goes over what's there or in its place
/// (<see cref="Replace"/>); then <see cref="Record"/> plays the song from the playhead (a bar before it to get ready, clicks
/// counting in where there's no song yet) with the metronome if it's on, and each lane's key or button pressed is a note at
/// the beat the player heard it on (their <see cref="AudioOffset"/> allowed for), snapped to the grid, a key held half a beat
/// or more a hold. <see cref="Stop"/> puts the take in as one undo step. <see cref="Play"/> plays from the playhead, ticking
/// on each note. Notes are added, taken out, moved and made holds with <see cref="AddNote"/>, <see cref="RemoveNote"/>,
/// <see cref="MoveNote"/> and <see cref="SetHold"/>, each an undo step of the chart's table (the Tables panel's undo too).
/// Agents read <see cref="NoteLines"/> and <see cref="Summary"/>, and record a take of taps at given moments with
/// <see cref="RecordTake"/>.
/// </summary>
public sealed class ChartPanel : ToolPanel, IDocumentEditor, IDisposable
{
    private const string MusicFolder = "Music/";
    private static readonly string[] DefaultDifficulties = ["Easy", "Normal", "Hard"];
    private static readonly string DefaultColors = "#F2607E #F2BC4B #4BC4F2 #9A72F2";

    private readonly TableStore _tables;
    private readonly IProjectSession _projectSession;
    private readonly ISceneDocuments _sceneDocuments;
    private readonly ISelection _selection;
    private readonly PlayMode _play;
    private readonly DispatcherTimer _timer;
    private SceneDocument? _document;
    private string? _chart;
    private string? _lanesEntity;
    private string? _song;
    private string _difficulty = "Normal";
    private ChartSnap _snap = ChartSnap.All[1];
    private bool _replace;
    private bool _metronome = true;
    private bool _ticks = true;
    private float _audioOffset;
    private double _playhead;
    private double _pixelsPerBeat = 48;
    private double _viewStart;
    private string? _selected;
    private string? _message;
    private List<ChartRow> _rows = [];
    private (string Path, DateTime Written, SoundClip? Clip, float[] Peaks, float Tempo)? _songData;

    // What's playing, and the take being recorded.
    private Voice? _voice;
    private ChartPlaybackStream? _stream;
    private bool _recording;
    private double _recordFrom;
    private double _playedFrom;
    private readonly List<ChartTap> _taps = [];
    private readonly Dictionary<int, double> _held = [];
    private List<ChartNote> _pending = [];
    private int _takes;
    private Thread? _padThread;
    private volatile bool _padPolling;
    private Mixer? _output;

    public ChartPanel(TableStore tables, ISceneDocuments sceneDocuments, IProjectSession projectSession, ISelection selection, PlayMode play)
    {
        _tables = tables;
        _projectSession = projectSession;
        _sceneDocuments = sceneDocuments;
        _selection = selection;
        _play = play;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
        _timer.Stop();
        tables.Changed += (_, name) =>
        {
            if (string.IsNullOrEmpty(name) || string.Equals(name, _chart, StringComparison.OrdinalIgnoreCase) || _chart is null)
                Refresh();
            else
                OnPropertyChanged(nameof(ChartNames));
        };
        selection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ISelection.Entity) && selection.Entity?.GetComponent<NoteLanes>() is { } lanes)
                FollowLanes(lanes);
        };
        projectSession.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IProjectSession.Project))
            {
                Stop(keep: false);
                (_chart, _lanesEntity, _song, _songData, _selected, _takes) = (null, null, null, null, null, 0);
                _audioOffset = SavedAudioOffset();
                Refresh();
            }
            else if (e.PropertyName == nameof(ISceneDocuments.Scene))
            {
                Track(sceneDocuments.EditScene);
            }
        };
        Track(sceneDocuments.EditScene);
        _audioOffset = SavedAudioOffset();
        Refresh();
    }

    public override string Id => "Chart";
    public override string Title => "Chart";
    public override string? Icon => Icons.Music;
    public override PanelZone Zone => PanelZone.Bottom;
    public override int Order => 5;

    // ----- Which chart

    /// <summary>The project's charts: its tables with a Beat and a Lane column.</summary>
    public IReadOnlyList<string> ChartNames => [.. _tables.Tables.Where(t => ChartTable.IsChart(t.Value)).Select(t => t.Name)];

    /// <summary>The chart showing (a table's name, "Groove"); setting it shows another.</summary>
    public string? ChartName
    {
        get => _chart;
        set => Open(value);
    }

    /// <summary>The chart's table with its formulas worked out, or null with none (or its file doesn't read).</summary>
    public Table? Table => _tables.Computed(_chart);

    public bool HasChart => ChartTable.IsChart(Table);

    /// <summary>
    /// Shows the chart called <paramref name="chart"/> (a table with Beat and Lane columns), with the song named like it and
    /// the timing of the Note Lanes that play it, if the scene has any. False if there's no such chart.
    /// </summary>
    public bool Open(string? chart)
    {
        if (_tables.Entry(chart) is not { } entry)
        {
            if (chart is not null)
                Message = $"There's no table called {chart}.";
            return false;
        }

        if (entry.Value is { } table && !ChartTable.IsChart(table))
        {
            Message = $"The {entry.Name} table isn't a chart: a chart has a Beat and a Lane column (it has {string.Join(", ", table.Columns.Select(c => c.Name))}).";
            return false;
        }

        if (string.Equals(entry.Name, _chart, StringComparison.Ordinal))
            return true;
        Stop(keep: false);
        _chart = entry.Name;
        _selected = null;
        // The lanes that play it, for their song, clock, lanes and colors; or the song named like it.
        _lanesEntity = LanesPlaying(entry.Name)?.Entity?.Name;
        _song = LanesNow is { Song: { Length: > 0 } song } ? song.Trim() : SongNamed(entry.Name);
        _difficulty = LanesNow is { Difficulty: { Length: > 0 } difficulty } ? difficulty.Trim() : Difficulties is var all && all.Contains("Normal") ? "Normal" : all is [var first, ..] ? first : "";
        _playhead = 0;
        _viewStart = 0;
        Message = null;
        Refresh();
        return true;
    }

    /// <summary>Shows the chart <paramref name="lanes"/> play, with their song, beat clock, lanes and colors.</summary>
    public void FollowLanes(NoteLanes lanes)
    {
        var name = !string.IsNullOrWhiteSpace(lanes.Chart) ? lanes.Chart.Trim()
            : !string.IsNullOrWhiteSpace(lanes.Song) ? Path.GetFileNameWithoutExtension(lanes.Song.Trim()) : null;
        if (name is null || _tables.Entry(name) is not { } entry || !ChartTable.IsChart(entry.Value))
            return;
        if (!string.Equals(entry.Name, _chart, StringComparison.OrdinalIgnoreCase))
            Open(entry.Name);
        if (_recording)
            return;
        _lanesEntity = lanes.Entity?.Name;
        if (!string.IsNullOrWhiteSpace(lanes.Song))
            _song = lanes.Song.Trim();
        if (!string.IsNullOrWhiteSpace(lanes.Difficulty))
            _difficulty = lanes.Difficulty.Trim();
        Refresh();
    }

    // ----- What plays it

    /// <summary>The Note Lanes whose song, clock, lanes and colors the chart is shown with (its entity's name), or null.</summary>
    public string? LanesEntity => LanesNow?.Entity?.Name;

    private NoteLanes? LanesNow => _lanesEntity is null ? null : SceneLanes().FirstOrDefault(l => l.Entity?.Name == _lanesEntity);

    private IEnumerable<NoteLanes> SceneLanes() =>
        _sceneDocuments.EditScene?.Scene.Entities.Select(e => e.GetComponent<NoteLanes>()).OfType<NoteLanes>() ?? [];

    // The scene's Note Lanes that play the chart: those naming it, or whose song is named like it.
    private NoteLanes? LanesPlaying(string chart) => SceneLanes().FirstOrDefault(l =>
        string.Equals(!string.IsNullOrWhiteSpace(l.Chart) ? l.Chart.Trim() : Path.GetFileNameWithoutExtension(l.Song?.Trim() ?? ""), chart, StringComparison.OrdinalIgnoreCase));

    private BeatClock? Clock => LanesNow is { } lanes
        ? (string.IsNullOrWhiteSpace(lanes.Clock) ? lanes.Entity?.GetComponent<BeatClock>() ?? FirstClock()
            : _sceneDocuments.EditScene?.Scene.Entities.FirstOrDefault(e => e.Name == lanes.Clock.Trim())?.GetComponent<BeatClock>())
        : null;

    private BeatClock? FirstClock() => _sceneDocuments.EditScene?.Scene.Entities.Select(e => e.GetComponent<BeatClock>()).OfType<BeatClock>().FirstOrDefault();

    /// <summary>The music in the project to pick a song from.</summary>
    public IReadOnlyList<string> SongNames
    {
        get
        {
            if (_projectSession.Project?.AssetsPath is not { } root || Path.Combine(root, "Music") is var music && !Directory.Exists(music))
                return [];
            return [.. Directory.EnumerateFiles(music, "*.*", SearchOption.AllDirectories)
                .Where(p => EditorAudio.Extensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
                .Select(p => Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/'))
                .Where(p => p.StartsWith(MusicFolder, StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>
    /// The song it plays and records to, e.g. "Music/Groove.wav": the lanes' Song, or the music named like the chart. Empty
    /// plays clicks alone at the clock's tempo.
    /// </summary>
    public string? Song
    {
        get => _song;
        set
        {
            Stop(keep: false);
            _song = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            Refresh();
        }
    }

    public bool HasSong => SongClip is not null;

    // The song decoded (kept until its file changes), its peaks to draw and the tempo its recipe says.
    private SoundClip? SongClip => SongData?.Clip;

    private (string Path, DateTime Written, SoundClip? Clip, float[] Peaks, float Tempo)? SongData
    {
        get
        {
            if (_song is null || _projectSession.Project?.AssetsPath is not { } root)
                return null;
            var path = Path.Combine(root, _song.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                return null;
            var written = File.GetLastWriteTimeUtc(path);
            if (_songData is { } data && data.Path == path && data.Written == written)
                return data;
            var library = new FileSoundLibrary(root);
            var clip = library.Load(_song);
            var peaks = clip is null ? [] : clip.Peaks((int)Math.Clamp(clip.Seconds * PeaksPerSecond, 1, 200_000));
            var tempo = library.MusicRecipeOf(_song)?.Clamped().Tempo ?? 0;
            _songData = (path, written, clip, peaks, tempo);
            return _songData;
        }
    }

    /// <summary>How many slices a second <see cref="SongPeaks"/> has.</summary>
    public const double PeaksPerSecond = 100;

    /// <summary>The song's shape, the loudest sample of each hundredth of a second (0 to 1), for the timeline to draw.</summary>
    public float[] SongPeaks => SongData?.Peaks ?? [];

    /// <summary>
    /// When the chart's beats are: the lanes' Beat Clock's tempo (or the song's own, or 120), beats in a bar and first beat.
    /// </summary>
    public ChartTiming Timing
    {
        get
        {
            var clock = Clock;
            var tempo = clock is { Tempo: > 0 } ? clock.Tempo : SongData?.Tempo is > 0 and var own ? own : 120;
            return new ChartTiming(tempo, clock?.BeatsPerBar ?? 4, clock?.Offset ?? 0);
        }
    }

    /// <summary>The timing in words, and where it comes from: "120 beats a minute, 4 a bar, from the Note Lanes' Beat Clock".</summary>
    public string TimingText
    {
        get
        {
            var timing = Timing;
            var clock = Clock;
            var from = clock is not null ? $"from the Beat Clock on {clock.Entity?.Name}" : SongData?.Tempo > 0 ? "from the song" : "(no Beat Clock says)";
            var offset = timing.Offset != 0 ? string.Create(CultureInfo.InvariantCulture, $", first beat {timing.Offset:0.###} s in") : "";
            return string.Create(CultureInfo.InvariantCulture, $"{timing.Tempo:0.##} BPM, {timing.PerBar}/4{offset} {from}");
        }
    }

    /// <summary>How many lanes: the lanes', or as many as the chart uses (at least 4).</summary>
    public int LaneCount => LanesNow is { Lanes: > 0 } lanes ? Math.Clamp(lanes.Lanes, 1, 16) : Math.Clamp(_rows.Select(r => r.Note.Lane).DefaultIfEmpty(4).Max(), 4, 16);

    /// <summary>Each lane's color as a hex color, the first lane's first: the lanes' Colors, round again for more lanes.</summary>
    public IReadOnlyList<string> LaneColors
    {
        get
        {
            var colors = (LanesNow?.Colors ?? DefaultColors).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (colors.Length == 0)
                colors = DefaultColors.Split(' ');
            return [.. Enumerable.Range(0, LaneCount).Select(i => colors[i % colors.Length])];
        }
    }

    /// <summary>Each lane's input action (Lane1 to Lane4, or the lanes' Actions), the first lane's first.</summary>
    public IReadOnlyList<string> LaneActions
    {
        get
        {
            var named = (LanesNow?.Actions ?? "Lane1, Lane2, Lane3, Lane4").Split(',').Select(a => a.Trim()).ToList();
            return [.. Enumerable.Range(0, LaneCount).Select(i => i < named.Count ? named[i] : "")];
        }
    }

    // The game's actions as its controls bind them.
    private InputState Controls()
    {
        var input = new InputState();
        if (_projectSession.Project is { } project)
            input.UseControls(project.Controls);
        return input;
    }

    /// <summary>The lane (from 1) a key presses while recording, by the lanes' actions as the game binds them; 0 for none.</summary>
    public int LaneForKey(InputKey key)
    {
        if (key == InputKey.None)
            return 0;
        var input = Controls();
        var actions = LaneActions;
        for (var i = 0; i < actions.Count; i++)
        {
            if (actions[i].Length > 0 && input.BindingsOf(actions[i]).Any(b => b.Key == key))
                return i + 1;
        }

        return 0;
    }

    /// <summary>The keys each lane is pressed with while recording, in words: "D, F, J, K".</summary>
    public string LaneKeys
    {
        get
        {
            var input = Controls();
            return string.Join(", ", LaneActions.Select(a => a.Length == 0 ? "-" :
                input.BindingsOf(a).FirstOrDefault(b => b.Key is not null) is { Key: { } key } ? key.ToString() : a));
        }
    }

    // ----- Difficulty, grid and how takes go in

    /// <summary>The difficulties to record and edit: the chart's own, and Easy, Normal and Hard.</summary>
    public IReadOnlyList<string> Difficulties
    {
        get
        {
            var named = Table is { } table ? RhythmChart.Difficulties(table).ToList() : [];
            foreach (var name in DefaultDifficulties)
            {
                if (!named.Contains(name, StringComparer.OrdinalIgnoreCase))
                    named.Add(name);
            }

            return named;
        }
    }

    /// <summary>The difficulty recorded into and edited ("Normal"); the notes of the others show faintly behind.</summary>
    public string Difficulty
    {
        get => _difficulty;
        set
        {
            var name = (value ?? "").Trim();
            if (name == _difficulty)
                return;
            _difficulty = name;
            _selected = null;
            Refresh();
        }
    }

    /// <summary>The grids to snap to.</summary>
    public IReadOnlyList<string> Snaps => ChartSnap.Names;

    /// <summary>The grid notes snap to as they're recorded, added and moved: 1/4, 1/8, 1/8 triplets, 1/16 or 1/16 triplets.</summary>
    public string Snap
    {
        get => _snap.Name;
        set
        {
            _snap = ChartSnap.Named(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SnapStep));
        }
    }

    /// <summary>How many beats apart the grid's lines are.</summary>
    public double SnapStep => _snap.Step;

    /// <summary><paramref name="beat"/> snapped to the grid.</summary>
    public double SnapBeat(double beat) => ChartRecording.Round(_snap.Snap(beat));

    /// <summary>The ways a take goes in.</summary>
    public IReadOnlyList<string> RecordModes { get; } = ["Overdub", "Replace"];

    /// <summary>Overdub adds a take to the notes there; Replace takes the difficulty's notes out from where it started to where it stopped first.</summary>
    public string RecordMode
    {
        get => _replace ? "Replace" : "Overdub";
        set => Replace = string.Equals(value?.Trim(), "Replace", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when a take goes in place of the notes it covers (Replace), false when it adds to them (Overdub).</summary>
    public bool Replace
    {
        get => _replace;
        set
        {
            if (_replace == value)
                return;
            _replace = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecordMode));
        }
    }

    /// <summary>Clicks on every beat as it plays and records (it always counts in where there's no song yet).</summary>
    public bool Metronome
    {
        get => _metronome;
        set
        {
            if (!SetProperty(ref _metronome, value))
                return;
            if (_stream is not null)
                _stream.Metronome = value;
        }
    }

    /// <summary>A tick on each note of the difficulty as it plays (not while recording).</summary>
    public bool Ticks
    {
        get => _ticks;
        set
        {
            if (SetProperty(ref _ticks, value))
                SetTicks();
        }
    }

    /// <summary>
    /// How late the player hears the sound, in milliseconds (Bluetooth headphones: 150 to 250): taps are taken as being on
    /// the beat they heard. It's the player's audio offset from playing the game in the editor (a Calibration sets it), and
    /// setting it here sets that too.
    /// </summary>
    public float AudioOffset
    {
        get => _audioOffset;
        set
        {
            value = float.IsFinite(value) ? Math.Clamp(value, -1000, 1000) : 0;
            if (!SetProperty(ref _audioOffset, value))
                return;
            if (_projectSession.Project is { } project)
            {
                try
                {
                    var saves = new SaveData(SaveData.EditorPath(project.RootPath));
                    saves.Set(Game.AudioOffsetSaveKey, value);
                }
                catch (IOException)
                {
                    // Kept here for this session.
                }
            }
        }
    }

    private float SavedAudioOffset()
    {
        if (_projectSession.Project is not { } project)
            return 0;
        try
        {
            var saves = new SaveData(SaveData.EditorPath(project.RootPath));
            return saves.Has(Game.AudioOffsetSaveKey) ? Math.Clamp(saves.GetFloat(Game.AudioOffsetSaveKey), -1000, 1000) : 0;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return 0;
        }
    }

    // ----- The timeline

    /// <summary>Where the playhead is, in beats: where playing and recording start. While playing, the beat being heard.</summary>
    public double Playhead
    {
        get => _playhead;
        set
        {
            value = double.IsFinite(value) ? Math.Clamp(value, FirstBeat, LengthInBeats) : 0;
            if (_recording)
                return;
            if (IsPlaying)
            {
                PlayFrom(value);
                return;
            }

            if (SetProperty(ref _playhead, value))
                OnPropertyChanged(nameof(PlayheadText));
        }
    }

    /// <summary>The playhead in bars and beats: "Bar 3, beat 2 (beat 9)".</summary>
    public string PlayheadText
    {
        get
        {
            var perBar = Timing.PerBar;
            var beat = Math.Floor(_playhead + 1e-6);
            var bar = (int)Math.Floor(beat / perBar);
            return string.Create(CultureInfo.InvariantCulture, $"Bar {bar + 1}, beat {beat - bar * perBar + 1:0} ({ChartTable.Text(_playhead)})");
        }
    }

    /// <summary>The earliest beat on the timeline: where the song starts (before beat 0 when its first beat is a lead-in).</summary>
    public double FirstBeat => Math.Min(0, Math.Floor(Timing.BeatAt(0)));

    /// <summary>How many beats the timeline runs to: the song's end, or past the last note (at least 16 bars).</summary>
    public double LengthInBeats
    {
        get
        {
            var timing = Timing;
            var last = _rows.Count == 0 ? 0 : _rows.Max(r => r.Note.Beat + r.Note.Hold);
            // The song's length (and the notes', should any go past it); without one, two bars past the last note.
            if (SongClip is { } clip)
                return Math.Ceiling(Math.Max(timing.BeatAt(clip.Seconds), last + 1));
            return Math.Ceiling(Math.Max(last + timing.PerBar * 2, timing.PerBar * 16));
        }
    }

    /// <summary>How wide a beat is drawn, in pixels (zoom).</summary>
    public double PixelsPerBeat
    {
        get => _pixelsPerBeat;
        set => SetProperty(ref _pixelsPerBeat, double.IsFinite(value) ? Math.Clamp(value, 8, 400) : 48);
    }

    /// <summary>The beat at the timeline's left edge (scrolling).</summary>
    public double ViewStart
    {
        get => _viewStart;
        set => SetProperty(ref _viewStart, double.IsFinite(value) ? Math.Clamp(value, FirstBeat, Math.Max(FirstBeat, LengthInBeats - 1)) : 0);
    }

    // ----- The notes

    /// <summary>Every note of the chart, with whether it's in the difficulty being edited.</summary>
    public IReadOnlyList<ChartRow> Rows => _rows;

    /// <summary>The notes of the difficulty, earliest first: "Normal 3: beat 4, lane 2, hold 1".</summary>
    public IReadOnlyList<string> NoteLines => [.. Mine().Select(r => $"{r.Row}: {r.Note}")];

    /// <summary>How many notes the difficulty has.</summary>
    public int NoteCount => Mine().Count();

    private IEnumerable<ChartRow> Mine() => _rows.Where(r => r.InDifficulty).OrderBy(r => r.Note.Beat).ThenBy(r => r.Note.Lane);

    /// <summary>The note picked (its row's name), or null.</summary>
    public string? SelectedNote
    {
        get => _selected;
        set
        {
            if (value is not null && _rows.All(r => !string.Equals(r.Row, value, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"The chart has no note called \"{value}\".", nameof(value));
            value = value is null ? null : _rows.First(r => string.Equals(r.Row, value, StringComparison.OrdinalIgnoreCase)).Row;
            if (SetProperty(ref _selected, value))
                OnPropertyChanged(nameof(SelectedText));
        }
    }

    /// <summary>The note picked in words, or what to do.</summary>
    public string SelectedText => _rows.FirstOrDefault(r => r.Row == _selected) is { Row: not null } row ? $"{row.Row}: {row.Note}" : "";

    /// <summary>
    /// The chart in words: how many notes the difficulty has, in which lanes, from where to where, and how many holds and
    /// chords; or why there's nothing to show.
    /// </summary>
    public string Summary
    {
        get
        {
            if (Notice is { } notice)
                return notice;
            var mine = Mine().ToList();
            if (mine.Count == 0)
                return $"{_chart} has no notes in {DifficultyName} yet: Record and tap along, or click the lanes.";
            var holds = mine.Count(r => r.Note.Hold > 0);
            var chords = mine.GroupBy(r => Math.Round(r.Note.Beat, 4)).Count(g => g.Count() > 1);
            var perBar = Timing.PerBar;
            return string.Create(CultureInfo.InvariantCulture,
                $"{_chart} in {DifficultyName}: {mine.Count} notes over {LaneCount} lanes, bars {Math.Floor(mine[0].Note.Beat / perBar) + 1} to {Math.Floor(mine[^1].Note.Beat / perBar) + 1}, {holds} holds, {chords} chords.");
        }
    }

    private string DifficultyName => _difficulty.Length > 0 ? _difficulty : "every difficulty";

    /// <summary>Why there's nothing to edit, or null.</summary>
    public string? Notice =>
        _projectSession.Project is null ? "Open a project to chart a song."
        : _chart is null ? ChartNames.Count == 0
            ? "No charts yet. Entity > Genres > Rhythm > Create Note Lanes makes a song and its chart; or Data > New Table with Beat, Lane, Hold and Difficulty columns."
            : "Pick a chart, or select Note Lanes."
        : _tables.Entry(_chart) is { Problem: { } problem } ? $"The {_chart} table doesn't read: {problem}"
        : Table is { } table && ChartTable.WhyNotEditable(table) is { } why ? why
        : null;

    public bool HasNotice => Notice is not null;

    /// <summary>What the last thing done said: what it did, or why it couldn't.</summary>
    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>Says <paramref name="message"/> in the panel (why something couldn't be done).</summary>
    public void Say(string message) => Message = message;

    /// <summary>The beat the take being recorded started at (the playhead, on the grid).</summary>
    public double RecordingFrom => _recordFrom;

    /// <summary>The notes of the take being recorded, so far.</summary>
    public IReadOnlyList<ChartNote> Pending => _pending;

    // ----- Editing

    private Table Editable()
    {
        if (Notice is { } notice)
            throw new InvalidOperationException(notice);
        if (_tables.Find(_chart) is not { } table)
            throw new InvalidOperationException($"There's no {_chart} table.");
        return table;
    }

    /// <summary>Adds a note at <paramref name="beat"/> in <paramref name="lane"/> (from 1), held <paramref name="hold"/> beats (0 for a tap), as one undo step. Returns its row.</summary>
    public string AddNote(double beat, int lane, double hold = 0)
    {
        if (lane < 1 || lane > LaneCount)
            throw new ArgumentOutOfRangeException(nameof(lane), $"There are {LaneCount} lanes, from 1.");
        var note = new ChartNote(ChartRecording.Round(beat), lane, ChartRecording.Round(Math.Max(0, hold)));
        if (Mine().Any(r => r.Note.Lane == lane && Math.Abs(r.Note.Beat - note.Beat) < 1e-4))
            throw new InvalidOperationException($"There's a note at beat {ChartTable.Text(note.Beat)} in lane {lane} already.");
        var (after, row) = ChartTable.Add(Editable(), note, _difficulty);
        Commit(after, $"Add a note at beat {ChartTable.Text(note.Beat)}");
        SelectedNote = row;
        Message = $"Added {row}: {note}.";
        return row;
    }

    /// <summary>Takes the note in row <paramref name="row"/> out of the difficulty, as one undo step.</summary>
    public void RemoveNote(string row)
    {
        var at = Row(row);
        Commit(ChartTable.Remove(Editable(), at.Row, _difficulty), $"Remove the note at beat {ChartTable.Text(at.Note.Beat)}");
        if (string.Equals(_selected, at.Row, StringComparison.OrdinalIgnoreCase))
            SelectedNote = null;
        Message = $"Removed {at.Row} ({at.Note}).";
    }

    /// <summary>Moves the note in row <paramref name="row"/> to <paramref name="beat"/> in <paramref name="lane"/>, as one undo step. Returns its row now.</summary>
    public string MoveNote(string row, double beat, int lane)
    {
        if (lane < 1 || lane > LaneCount)
            throw new ArgumentOutOfRangeException(nameof(lane), $"There are {LaneCount} lanes, from 1.");
        var at = Row(row);
        var moved = at.Note with { Beat = ChartRecording.Round(beat), Lane = lane };
        if (moved == at.Note)
            return at.Row;
        if (Mine().Any(r => r.Row != at.Row && r.Note.Lane == lane && Math.Abs(r.Note.Beat - moved.Beat) < 1e-4))
            throw new InvalidOperationException($"There's a note at beat {ChartTable.Text(moved.Beat)} in lane {lane} already.");
        var (after, now) = ChartTable.Change(Editable(), at.Row, moved, _difficulty);
        Commit(after, $"Move the note at beat {ChartTable.Text(at.Note.Beat)}");
        SelectedNote = now;
        Message = $"Moved {now} to {moved}.";
        return now;
    }

    /// <summary>Makes the note in row <paramref name="row"/> held for <paramref name="hold"/> beats (0 makes it a tap), as one undo step. Returns its row now.</summary>
    public string SetHold(string row, double hold)
    {
        var at = Row(row);
        var held = at.Note with { Hold = ChartRecording.Round(Math.Max(0, hold)) };
        if (held == at.Note)
            return at.Row;
        var (after, now) = ChartTable.Change(Editable(), at.Row, held, _difficulty);
        Commit(after, held.Hold > 0 ? $"Hold the note at beat {ChartTable.Text(at.Note.Beat)}" : $"Make the note at beat {ChartTable.Text(at.Note.Beat)} a tap");
        SelectedNote = now;
        Message = held.Hold > 0 ? $"{now} is held for {ChartTable.Text(held.Hold)} beats." : $"{now} is a tap.";
        return now;
    }

    /// <summary>The difficulty's note at <paramref name="beat"/> in <paramref name="lane"/> (its row), or null.</summary>
    public string? NoteAt(double beat, int lane) =>
        Mine().FirstOrDefault(r => r.Note.Lane == lane && Math.Abs(r.Note.Beat - beat) < 1e-4).Row;

    private ChartRow Row(string row) =>
        _rows.FirstOrDefault(r => string.Equals(r.Row, row?.Trim(), StringComparison.OrdinalIgnoreCase)) is { Row: not null } found && found.InDifficulty ? found
        : throw new ArgumentException($"{DifficultyName} has no note called \"{row}\".", nameof(row));

    // A change to the chart, as one undo step of its table: it makes the chart the document Edit > Undo goes to.
    private void Commit(Table after, string description)
    {
        _tables.Commit(after, description);
        Edited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The undo history of the chart's table: Edit &gt; Undo and Redo take its changes back while it's the active document.</summary>
    public EditHistory? History => _chart is null ? null : _tables.HistoryOf(_chart);

    /// <summary>The chart, as undoing in it says it: "the Song Chart chart".</summary>
    public string? DocumentName => _chart is null ? null : $"the {_chart} chart";

    /// <summary>Not while a take records: undo and redo wait until it's put in.</summary>
    public bool CanChange => !_recording;

    /// <summary>Raised after it changes the chart (a take, or a note added, removed, moved or held).</summary>
    public event EventHandler? Edited;

    /// <summary>What undo would undo on the chart, or null.</summary>
    public string? UndoDescription => History?.UndoDescription;

    /// <summary>What redo would redo on the chart, or null.</summary>
    public string? RedoDescription => History?.RedoDescription;

    // ----- Playing

    // What the song plays through, and whose clock says what's been heard: the sound card's (the editor's one mixer) unless
    // a test gives its own, with a clock it moves itself.
    internal Mixer Output
    {
        get => _output ?? AudioOutput.Mixer;
        set => _output = value;
    }

    /// <summary>True while the song plays (recording or not).</summary>
    public bool IsPlaying => _voice is not null;

    /// <summary>True while a take is being recorded.</summary>
    public bool IsRecording => _recording;

    /// <summary>Plays from the playhead (the song, with clicks if the metronome is on and a tick on each note), until stopped or the song ends.</summary>
    public void Play() => PlayFrom(_playhead);

    /// <summary>Plays from <paramref name="beat"/>.</summary>
    public void PlayFrom(double beat)
    {
        if (!HasChart)
            throw new InvalidOperationException(Notice ?? "Pick a chart first.");
        if (_recording)
            throw new InvalidOperationException("A take is being recorded: stop it first.");
        StopVoice();
        beat = Math.Clamp(beat, FirstBeat, LengthInBeats);
        Start(beat, leadIn: 0);
    }

    /// <summary>
    /// Starts recording a take into the difficulty from the playhead: the song plays from a bar before it, counting in with
    /// clicks where there's no song yet, and each lane pressed (<see cref="Press"/>, by its key or button) from the playhead
    /// on is a note. <see cref="Stop"/> puts the take in.
    /// </summary>
    public void Record()
    {
        Editable();
        StopVoice();
        _taps.Clear();
        _held.Clear();
        _pending = [];
        _recording = true;
        _recordFrom = _snap.Floor(_playhead + 1e-6);
        _playhead = _recordFrom;
        OnPropertyChanged(nameof(Playhead));
        OnPropertyChanged(nameof(PlayheadText));
        Start(_recordFrom, leadIn: Timing.PerBar * Timing.SecondsPerBeat);
        StartPad();
        Message = $"Recording {DifficultyName} from {PlayheadText}: tap {LaneKeys} along to the song. Stop (Space) puts the take in.";
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(CanChange));
    }

    // Plays from `beat`, `leadIn` seconds of the song (or clicks) before it.
    private void Start(double beat, double leadIn)
    {
        var timing = Timing;
        var from = timing.SecondsAt(beat);
        _stream = new ChartPlaybackStream(SongClip, timing, from, leadIn, LanesNow?.Volume is { } v && float.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0.8f)
        {
            Metronome = _metronome,
        };
        SetTicks();
        _playedFrom = beat - leadIn / timing.SecondsPerBeat;
        _voice = Output.Play(_stream, 1);
        _timer.Start();
        OnPropertyChanged(nameof(IsPlaying));
    }

    private void SetTicks()
    {
        if (_stream is not null)
            _stream.Ticks = _ticks && !_recording ? [.. Mine().Select(r => r.Note.Beat).Order()] : [];
    }

    /// <summary>Seconds into the song that have been heard (through the speakers' delay, not yet the player's offset), or NaN when not playing.</summary>
    public double SongHeard => _voice is { } voice && _stream is { } stream ? stream.SongSecondsAt(voice.Heard) : double.NaN;

    /// <summary>The beat the player is hearing now (their audio offset allowed for), or the playhead when not playing.</summary>
    public double HeardBeat => _voice is null ? _playhead : Timing.BeatAt(SongHeard - _audioOffset / 1000.0);

    private void Tick()
    {
        if (_voice is null)
        {
            _timer.Stop();
            return;
        }

        // Not back past where it started (the moment before the first of it is heard).
        var beat = HeardBeat;
        _playhead = Math.Max(Math.Max(beat, _playedFrom), FirstBeat);
        OnPropertyChanged(nameof(Playhead));
        OnPropertyChanged(nameof(PlayheadText));

        if (_recording)
            UpdatePending();
        // The song's over (or there's none and the chart is).
        if (!_voice.IsPlaying || _stream is { SongSeconds: 0 } && beat > LengthInBeats)
            Stop();
    }

    /// <summary>Stops playing; a take being recorded goes into the chart as one undo step. The playhead goes back to where a take started.</summary>
    public void Stop() => Stop(keep: true);

    private void Stop(bool keep)
    {
        if (_voice is null && !_recording)
            return;
        var heard = SongHeard;
        StopVoice();
        if (!_recording)
            return;
        _recording = false;
        StopPad();
        var now = double.IsFinite(heard) ? heard : 0;
        foreach (var (lane, pressed) in _held)
            _taps.Add(new ChartTap(lane, pressed, now));
        _held.Clear();
        var to = Timing.BeatAt(now - _audioOffset / 1000.0);
        _pending = [];
        OnPropertyChanged(nameof(IsRecording));
        if (keep)
            PutIn([.. _taps], _recordFrom, to);
        _taps.Clear();
        _playhead = _recordFrom;
        OnPropertyChanged(nameof(Playhead));
        OnPropertyChanged(nameof(PlayheadText));
        OnPropertyChanged(nameof(CanChange));
    }

    private void StopVoice()
    {
        _voice?.Stop();
        _voice = null;
        _stream = null;
        _timer.Stop();
        OnPropertyChanged(nameof(IsPlaying));
    }

    // A take's taps made notes and put into the chart, as one undo step.
    private string PutIn(List<ChartTap> taps, double from, double to)
    {
        var notes = ChartRecording.Notes(taps, Timing, _snap, _audioOffset, from, LaneCount);
        if (notes.Count == 0 && !_replace)
        {
            Message = taps.Count == 0 ? "Nothing was tapped, so the chart is as it was." : "The taps all came before the playhead, so the chart is as it was.";
            return Message;
        }

        var (after, added, removed) = ChartTable.Record(Editable(), notes, _difficulty, _replace, from, Math.Max(from, to));
        _takes++;
        var perBar = Timing.PerBar;
        var range = string.Create(CultureInfo.InvariantCulture, $"bar {Math.Floor(from / perBar) + 1} to bar {Math.Floor(Math.Max(from, to) / perBar) + 1}");
        Commit(after, $"Record take {_takes} ({Count(added, "note")} in {DifficultyName})");
        Message = $"Take {_takes}: {Count(added, "note")} in {DifficultyName} from {range}" +
                  (_replace ? $", in place of {removed}." : notes.Count > added ? $" ({Count(notes.Count - added, "was", "were")} there already)." : ".") +
                  (added > 0 ? " Undo takes it out." : "");
        return Message;
    }

    // "1 note", "3 notes"; "1 was", "2 were".
    private static string Count(int n, string one, string? many = null) => $"{n} {(n == 1 ? one : many ?? one + "s")}";

    /// <summary>A lane (from 1) pressed down while recording: a note at the beat heard now, a hold if it's held half a beat.</summary>
    public void Press(int lane) => PressAt(lane, SongHeard);

    /// <summary>A lane let go of while recording.</summary>
    public void Release(int lane) => ReleaseAt(lane, SongHeard);

    private void PressAt(int lane, double heard)
    {
        if (!_recording || lane < 1 || lane > LaneCount || !double.IsFinite(heard) || _held.ContainsKey(lane))
            return;
        _held[lane] = heard;
        UpdatePending();
    }

    private void ReleaseAt(int lane, double heard)
    {
        if (!_recording || !_held.Remove(lane, out var pressed))
            return;
        _taps.Add(new ChartTap(lane, pressed, double.IsFinite(heard) ? heard : pressed));
        UpdatePending();
    }

    private void UpdatePending()
    {
        var now = SongHeard;
        _pending = ChartRecording.Notes(_taps.Concat(_held.Select(h => new ChartTap(h.Key, h.Value, double.IsFinite(now) ? now : h.Value))),
            Timing, _snap, _audioOffset, _recordFrom, LaneCount);
        OnPropertyChanged(nameof(Pending));
    }

    /// <summary>
    /// Records a take of <paramref name="taps"/> without playing, as if tapped along to the song from the playhead: each
    /// "lane@seconds" (a lane pressed that many seconds into the song, as it reached the speakers), or "lane@seconds-seconds"
    /// for one held until then, separated by commas ("1@2.0, 2@2.5, 3@3.0-4.2"). The player's audio offset, the snap, the
    /// difficulty and Overdub or Replace (to the last tap's beat) go as for a take recorded by hand; it's one undo step. Says
    /// what went in.
    /// </summary>
    public string RecordTake(string taps)
    {
        Editable();
        Stop(keep: false);
        var list = new List<ChartTap>();
        foreach (var part in (taps ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var at = part.IndexOf('@');
            if (at <= 0 || !int.TryParse(part[..at], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lane))
                throw new FormatException($"\"{part}\" isn't a tap: write lane@seconds, or lane@seconds-seconds for a hold.");
            var times = part[(at + 1)..].Split('-', 2, StringSplitOptions.TrimEntries);
            if (!double.TryParse(times[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var pressed) ||
                times.Length > 1 && !double.TryParse(times[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                throw new FormatException($"\"{part}\" isn't a tap: write lane@seconds, or lane@seconds-seconds for a hold.");
            var released = times.Length > 1 ? double.Parse(times[1], CultureInfo.InvariantCulture) : pressed + 0.08;
            list.Add(new ChartTap(lane, pressed, released));
        }

        var from = _snap.Floor(_playhead);
        var to = list.Count == 0 ? from : list.Max(t => Timing.BeatAt(t.Released - _audioOffset / 1000.0));
        return PutIn(list, from, to);
    }

    // ----- A controller's buttons while recording

    private void StartPad()
    {
        StopPad();
        var actions = LaneActions;
        var input = Controls();
        _padPolling = true;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _padThread = new Thread(() =>
        {
            var down = new bool[actions.Count];
            while (_padPolling)
            {
                XInputGamepad.Poll(input);
                var heard = SongHeard;
                for (var i = 0; i < actions.Count; i++)
                {
                    var now = actions[i].Length > 0 && input.BindingsOf(actions[i]).Any(b => b.Gamepad is { } button && input.IsDown(button));
                    if (now == down[i])
                        continue;
                    down[i] = now;
                    var lane = i + 1;
                    dispatcher.BeginInvoke(now ? () => PressAt(lane, heard) : () => ReleaseAt(lane, heard));
                }

                Thread.Sleep(1);
            }
        })
        { IsBackground = true, Name = "Chart recording controller" };
        _padThread.Start();
    }

    private void StopPad()
    {
        _padPolling = false;
        _padThread = null;
    }

    // ----- Keeping up

    private void Track(SceneDocument? document)
    {
        if (_document is not null)
            _document.Changed -= OnSceneChanged;
        _document = document;
        if (_document is not null)
            _document.Changed += OnSceneChanged;
        if (_chart is not null && LanesNow is null)
            _lanesEntity = LanesPlaying(_chart)?.Entity?.Name;
        Refresh();
    }

    // A change to the scene may be to the lanes or their clock: what the chart is shown with.
    private void OnSceneChanged(object? sender, EditEventArgs e)
    {
        foreach (var name in (string[])[nameof(LanesEntity), nameof(Timing), nameof(TimingText), nameof(LaneCount), nameof(LaneColors), nameof(LaneActions), nameof(LaneKeys), nameof(LengthInBeats), nameof(FirstBeat)])
            OnPropertyChanged(name);
    }

    private void Refresh()
    {
        if (_chart is not null && _tables.Entry(_chart) is null)
            (_chart, _selected) = (null, null);
        if (_chart is null && !_recording && _projectSession.Project is not null)
        {
            // A chart to show: the selected lanes', the scene's lanes', or the project's only one.
            var lanes = _selection.Entity?.GetComponent<NoteLanes>() ?? SceneLanes().FirstOrDefault();
            var name = lanes is null ? null : !string.IsNullOrWhiteSpace(lanes.Chart) ? lanes.Chart.Trim() : Path.GetFileNameWithoutExtension(lanes.Song?.Trim() ?? "");
            var charts = ChartNames;
            name = charts.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase)) ?? (charts.Count == 1 ? charts[0] : null);
            if (name is not null)
            {
                Open(name);
                return;
            }
        }

        _rows = Table is { } table && ChartTable.IsChart(table) ? ChartTable.Rows(table, _difficulty) : [];
        if (_selected is not null && !_rows.Any(r => r.Row == _selected && r.InDifficulty))
            _selected = null;
        SetTicks();
        OnPropertyChanged(string.Empty);
    }

    private string? SongNamed(string chart) =>
        SongNames.FirstOrDefault(s => string.Equals(Path.GetFileNameWithoutExtension(s), chart, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        StopPad();
        StopVoice();
    }
}
