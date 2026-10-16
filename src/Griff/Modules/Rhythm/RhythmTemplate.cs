using System.Numerics;
using Griff.Audio;
using Griff.Data;
using Griff.Input;
using Griff.Projects;
using Griff.Scenes;
using Griff.Templates;
using static Griff.Templates.ProjectTemplates;

namespace Griff.Modules.Rhythm;

// The Rhythm template, "Neon Pulse": a rhythm game with no code. A title, a song select of three songs made in Griff (each
// charted at Easy, Normal and Hard, previewed as it's picked out, with its best score and grade), the song played on four
// lanes in a city at night (judgments, the combo, score, accuracy, how far through it is), results with the grade and the
// best kept, a pause menu, and settings for the note speed, the audio and visual offsets (with a calibration to tap along
// to) and the volumes. Keys, a controller, the mouse and fingers all play.
internal sealed class RhythmTemplate : ProjectTemplate
{
    public override string Description => "Neon Pulse: a rhythm game with no code. Pick one of three songs made in Griff at Easy, Normal or Hard, and press each lane as its notes cross the line, holding the long ones: Perfect, Great, Good or Miss, a combo, a score and a grade, and your best kept for each song and difficulty. Settings for the note speed, the audio offset (tap along to calibrate it) and the volumes. D F J K or the arrows, a controller, a click or a finger on a lane.";

    // After the fighting game.
    public override int Order => 126;

    protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Bright, seed: 17) with { Volume = 0.5f, Tempo = 120 };

    protected override Scene BuildStartScene() => TitleScreen();

    protected override Dictionary<string, Scene> BuildMoreScenes() => new()
    {
        [$"Scenes/{SelectScene}.scene"] = SelectScreen(),
        [$"Scenes/{PlayScene}.scene"] = PlayScreen(),
    };

    public override IReadOnlyDictionary<string, byte[]> Images() => RhythmArt.Files();

    // The songs' charts: each song's table, named like it.
    public override IReadOnlyDictionary<string, Table> Tables() =>
        Songs.ToDictionary(s => $"{TableFile.Folder}/{s.Title}{TableFile.Extension}", s => ChartWriter.Chart(s.Title, s.Music));

    // The songs, made in Griff: they open in the Sound Maker.
    protected override IReadOnlyDictionary<string, MusicRecipe> MoreMusic => Songs.ToDictionary(s => SongPath(s.Title), s => s.Music);

    // The theme plays on the title; the song select and the lanes play the songs.
    protected override bool PlaysTheme(Scene scene) =>
        !scene.Entities.Any(e => e.GetComponent<NoteLanes>() is not null || e.GetComponent<SongSelect>() is not null);

    // Its own sounds: a song starting, a note missed, and the results coming up.
    protected override IReadOnlyDictionary<string, SoundRecipe> MoreSounds => new Dictionary<string, SoundRecipe>
    {
        [StartSound] = new() { Wave = Waveform.Square, SquareDuty = 0.3f, Frequency = 0.42f, Slide = 0.2f, ChangeAmount = 0.3f, ChangeSpeed = 0.5f, Sustain = 0.08f, Decay = 0.25f, Punch = 0.4f, Volume = 0.4f },
        [MissSound] = new() { Wave = Waveform.Noise, Frequency = 0.12f, Slide = -0.1f, Sustain = 0.02f, Decay = 0.12f, LowPassCutoff = 0.35f, Volume = 0.22f },
        [ResultsSound] = new() { Wave = Waveform.Triangle, Frequency = 0.38f, ChangeAmount = 0.45f, ChangeSpeed = 0.35f, VibratoDepth = 0.15f, VibratoSpeed = 0.6f, Sustain = 0.2f, Decay = 0.5f, Punch = 0.3f, Volume = 0.45f },
    };

    public const string StartSound = "Sounds/start.wav";
    public const string MissSound = "Sounds/miss.wav";
    public const string ResultsSound = "Sounds/results.wav";

    protected internal override void WireSounds(Entity entity)
    {
        foreach (var component in entity.Components)
        {
            switch (component)
            {
                case SongSelect select:
                    TemplateSounds.Set(select.MoveSound, s => select.MoveSound = s, TemplateSounds.Select);
                    TemplateSounds.Set(select.PickSound, s => select.PickSound = s, StartSound);
                    break;
                case NoteLanes lanes:
                    TemplateSounds.Set(lanes.MissSound, s => lanes.MissSound = s, MissSound);
                    break;
                case Panel panel when entity.Name == ResultsName:
                    TemplateSounds.Set(panel.Sound, s => panel.Sound = s, ResultsSound);
                    break;
            }
        }

        base.WireSounds(entity);
    }

    // ----- The songs -----

    /// <summary>A song to pick: its title (its file's and its chart's name), the tune, how it's described, and its color.</summary>
    public sealed record Song(string Title, MusicRecipe Music, string Style, Color Color);

    /// <summary>The songs, in the order of the song select's cards: easiest first.</summary>
    public static IReadOnlyList<Song> Songs { get; } =
    [
        new("First Light", MusicRecipe.ForMood(MusicMood.Chill, seed: 5) with { Tempo = 100, Bars = 16, Volume = 0.8f }, "Chill  ·  100 BPM", Color.FromBytes(0x4B, 0xD8, 0xF2)),
        new("Neon Avenue", MusicRecipe.ForMood(MusicMood.Bright, seed: 5) with { Tempo = 120, Bars = 16, Volume = 0.8f }, "Bright  ·  120 BPM", Color.FromBytes(0xFF, 0x4F, 0xA3)),
        new("Overdrive", MusicRecipe.ForMood(MusicMood.Tense, seed: 12) with { Tempo = 140, Bars = 16, Volume = 0.8f }, "Tense  ·  140 BPM", Color.FromBytes(0xFF, 0xC8, 0x3A)),
    ];

    /// <summary>Where a song is kept in Assets: "Music/First Light.wav".</summary>
    public static string SongPath(string title) => $"Music/{title}.wav";

    /// <summary>The scenes besides the title: the song select and the lanes.</summary>
    public const string SelectScene = "Select", PlayScene = "Play";

    /// <summary>The entities the template's pieces are on: the song select, the lanes, the results, the settings and the pause menu.</summary>
    public const string SelectName = "Songs", LanesName = "Note Lanes", ResultsName = "Results", SettingsName = "Settings", PausedName = "Paused";

    /// <summary>The names in the save data the song and difficulty picked are kept under, and the lanes' bests.</summary>
    public const string SavedSong = "Song", SavedDifficulty = "Difficulty", SavedBest = "Best";

    // ----- Colors -----

    private static readonly Color Pink = Color.FromBytes(0xFF, 0x4F, 0xA3);
    private static readonly Color Cyan = Color.FromBytes(0x7F, 0xF0, 0xFF);
    private static readonly Color Gold = Color.FromBytes(0xFF, 0xD2, 0x4A);
    private static readonly Color Light = Color.FromBytes(0xF4, 0xF0, 0xFF);
    private static readonly Color Dim = Color.FromBytes(0xB8, 0xB0, 0xD8);
    private static readonly Color Violet = Color.FromBytes(0x6A, 0x3D, 0xF0);
    private static readonly Color Gray = Color.FromBytes(0x3A, 0x34, 0x58);
    private static readonly Color Card = new(0.06f, 0.04f, 0.14f, 0.88f);
    private static readonly Color Shade = new(0.04f, 0.03f, 0.1f, 0.94f);
    private static readonly Color Ink = Color.FromBytes(0x0A, 0x08, 0x20);

    // ----- The title -----

    private static Scene TitleScreen()
    {
        var scene = new Scene();
        AddCamera(scene);
        AddBackdrop(scene);

        // A beat clock keeping time with the theme, for the title and the speakers to bop to.
        var beat = new Entity("Beat");
        beat.AddComponent<BeatClock>();
        scene.Add(beat);
        AddSpeakers(scene, "Beat", -6.4f);

        var title = Label("Title", new Vector2(0, 2.6f), "NEON PULSE", 1.7f, Pink);
        var pulse = title.AddComponent<BeatPulse>();
        (pulse.Size, pulse.Clock) = (0.06f, "Beat");
        scene.Add(title);
        scene.Add(Label("Subtitle", new Vector2(0, 1.55f), "Press each lane as its notes cross the line", 0.4f, Dim));

        scene.Add(Fit(MenuButton("Play", new Vector2(0, 0.2f), ButtonAction.LoadScene, SelectScene, Key.Enter, Pink), 4.4f));
        var settings = Fit(MenuButton("Settings", new Vector2(0, -0.85f), ButtonAction.OpenPanel, null, Key.None, Violet), 4.4f);
        (settings.GetComponent<Button>()!.Panel, settings.Name) = (SettingsName, "Open Settings");
        scene.Add(settings);
        scene.Add(Fit(MenuButton("Quit", new Vector2(0, -1.9f), ButtonAction.Quit, null, Key.Escape, Gray), 4.4f));

        scene.Add(Hint("Arrows to choose  ·  Enter to pick  ·  Play with D F J K, the arrows, a controller or a finger on the lanes"));
        AddSettings(scene);
        return scene;
    }

    // ----- The song select -----

    private static Scene SelectScreen()
    {
        var scene = new Scene();
        AddCamera(scene);
        AddBackdrop(scene);
        var band = new Entity("Band");
        var shade = band.AddComponent<Sprite>();
        (shade.Size, shade.Tint, shade.Order) = (new Vector2(40, 8.2f), new Color(0.03f, 0.02f, 0.08f, 0.6f), -5);
        band.Transform.Position = new Vector3(0, -0.3f, 0);
        scene.Add(band);
        scene.Add(Label("Heading", new Vector2(0, 4.2f), "Choose a song", 0.75f, Light));

        // The cards, one a song, down the left, and the frame over the one picked out.
        var songs = new Entity(SelectName);
        songs.Transform.Position = new Vector3(-3.7f, -0.2f, 0);
        var select = songs.AddComponent<SongSelect>();
        (select.Songs, select.Scene, select.Back) = ([.. Songs.Select(s => SongPath(s.Title))], PlayScene, Project.DefaultSceneName);
        (select.SaveAs, select.SaveDifficultyAs, select.BestFrom, select.Cursor) = (SavedSong, SavedDifficulty, SavedBest, "Cursor");
        scene.Add(songs);
        for (var i = 0; i < Songs.Count; i++)
        {
            var song = Songs[i];
            var card = new Entity(song.Title);
            card.Transform.Position = new Vector3(0, 2.1f - i * 2.1f, 0);
            var plateOf = card.AddComponent<Sprite>();
            (plateOf.Size, plateOf.Tint, plateOf.Order) = (new Vector2(7.2f, 1.5f), Card, 20);
            scene.Add(card, songs);

            var stripe = new Entity("Stripe");
            stripe.Transform.Position = new Vector3(-3.45f, 0, 0);
            var bar = stripe.AddComponent<Sprite>();
            (bar.Size, bar.Tint, bar.Order) = (new Vector2(0.18f, 1.3f), song.Color, 21);
            scene.Add(stripe, card);
            scene.Add(Left(Label("Name", new Vector2(-3.05f, 0.22f), song.Title, 0.55f, Light), 22), card);
            scene.Add(Left(Label("Style", new Vector2(-3.05f, -0.36f), song.Style, 0.3f, Dim), 22), card);
            var grade = Label("Grade", new Vector2(2.75f, 0), "{0}", 0.85f, song.Color);
            (grade.GetComponent<Text>()!.Track, grade.GetComponent<Text>()!.Output, grade.GetComponent<Text>()!.Order) = (SelectName, $"Grade:{song.Title}", 22);
            scene.Add(grade, card);
        }

        var cursor = new Entity("Cursor");
        cursor.Transform.Position = new Vector3(-3.7f, 1.9f, 0);
        var frame = cursor.AddComponent<Sprite>();
        (frame.Image, frame.Size, frame.Order) = (RhythmArt.Frame, RhythmArt.FrameSize, 25);
        scene.Add(cursor);

        // What's picked out: its title, the difficulty (with arrows to click), its notes, best and grade, and Play.
        const float x = 5.1f;
        var panel = new Entity("Details");
        panel.Transform.Position = new Vector3(x, -0.2f, 0);
        var plate = panel.AddComponent<Sprite>();
        (plate.Size, plate.Tint, plate.Order) = (new Vector2(6.2f, 6.2f), Card, 20);
        scene.Add(panel);
        scene.Add(Tracking("Title", new Vector2(x, 2.15f), "{0}", 0.65f, Light, SelectName, nameof(SongSelect.Title)));
        scene.Add(Tracking("Difficulty", new Vector2(x, 1.0f), "{0}", 0.6f, Gold, SelectName, nameof(SongSelect.Difficulty)));
        scene.Add(Arrow("Easier", x - 2.2f, "<", nameof(SongSelect.PreviousDifficulty)));
        scene.Add(Arrow("Harder", x + 2.2f, ">", nameof(SongSelect.NextDifficulty)));
        scene.Add(Tracking("Notes", new Vector2(x, 0.05f), "{0} notes", 0.38f, Dim, SelectName, nameof(SongSelect.Notes)));
        scene.Add(Tracking("Best", new Vector2(x, -0.65f), "Best  {0:#,0}", 0.42f, Light, SelectName, nameof(SongSelect.Best)));
        scene.Add(Tracking("Best Grade", new Vector2(x, -1.3f), "{0}", 0.5f, Cyan, SelectName, "Best Grade"));
        var play = Fit(MenuButton("Play", new Vector2(x, -2.45f), ButtonAction.Do, null, Key.None, Pink), 3.6f);
        var playButton = play.GetComponent<Button>()!;
        (playButton.Navigable, playButton.Do, playButton.Targets) = (false, nameof(SongSelect.Play), SelectName);
        play.GetComponent<Text>()!.Value = "Play  {Jump}";
        scene.Add(play);

        var back = Fit(MenuButton("Back", Vector2.Zero, ButtonAction.LoadScene, Project.DefaultSceneName, Key.None, Gray), 2.6f);
        back.GetComponent<Button>()!.Navigable = false;
        (back.GetComponent<Text>()!.Value, back.GetComponent<Text>()!.Size) = ("Back  Esc", 0.36f);
        var corner = back.AddComponent<ScreenAnchor>();
        (corner.Anchor, corner.Offset) = (Anchor.TopLeft, new Vector2(1.6f, -0.6f));
        scene.Add(back);

        scene.Add(Hint("Up and down: song  ·  left and right: difficulty  ·  Enter or Space: play  ·  or click a song, and click it again to play"));
        return scene;
    }

    // An arrow beside the difficulty that steps it, for the mouse and fingers.
    private static Entity Arrow(string name, float x, string label, string action)
    {
        var arrow = MenuButton(name, new Vector2(x, 1.0f), ButtonAction.Do, null, Key.None, Gray);
        var button = arrow.GetComponent<Button>()!;
        (button.Navigable, button.Do, button.Targets, button.Fade) = (false, action, SelectName, 0);
        var size = new Vector2(0.8f, 0.8f);
        (arrow.GetComponent<Sprite>()!.Size, button.Size) = (size, size);
        (arrow.GetComponent<Text>()!.Value, arrow.GetComponent<Text>()!.Size) = (label, 0.5f);
        return arrow;
    }

    // ----- Playing -----

    private static Scene PlayScreen()
    {
        var scene = new Scene();
        AddCamera(scene);
        AddBackdrop(scene);

        // The lanes, in the middle: the song picked (or the first, played from here in the editor) at the difficulty picked.
        var entity = new Entity(LanesName);
        entity.AddComponent<BeatClock>();
        var lanes = entity.AddComponent<NoteLanes>();
        (lanes.Song, lanes.SavedSong, lanes.Difficulty, lanes.SavedDifficulty, lanes.SaveBestAs) = (SongPath(Songs[0].Title), SavedSong, "Normal", SavedDifficulty, SavedBest);
        scene.Add(entity);
        AddSpeakers(scene, LanesName, -6.6f);

        // Each lane's key under it, as the player plays ("D", or a controller's button).
        for (var lane = 0; lane < 4; lane++)
        {
            var key = Label($"Key {lane + 1}", new Vector2((lane - 1.5f) * lanes.LaneWidth, -4.05f), $"{{Lane{lane + 1}}}", 0.32f, Dim);
            key.GetComponent<Text>()!.Order = 305;
            scene.Add(key);
        }

        AddHud(scene);
        scene.Add(ResultsPanel());
        AddPause(scene);
        AddSettings(scene);
        return scene;
    }

    // Along the top: the song and difficulty and how far through it is (left), the score, accuracy and best (right), and a
    // pause button for fingers; the combo big on the left.
    private static void AddHud(Scene scene)
    {
        scene.Add(Pinned(Tracking("Song Title", Vector2.Zero, "{0}", 0.55f, Light, LanesName, nameof(NoteLanes.Title)), Anchor.TopLeft, new Vector2(0.5f, -0.6f), TextAlign.Left));
        scene.Add(Pinned(Tracking("Playing", Vector2.Zero, "{0}", 0.36f, Gold, LanesName, "Playing"), Anchor.TopLeft, new Vector2(0.5f, -1.15f), TextAlign.Left));
        var progress = new Entity("Progress");
        var anchor = progress.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.TopLeft, new Vector2(2.3f, -1.6f));
        var bar = progress.AddComponent<Bar>();
        (bar.Track, bar.Output, bar.Size, bar.Max, bar.Order) = (LanesName, nameof(NoteLanes.Progress), new Vector2(3.6f, 0.14f), 1, 160);
        (bar.Fill, bar.Background, bar.Trail) = (Cyan, new Color(1, 1, 1, 0.15f), Color.Transparent);
        scene.Add(progress);

        scene.Add(Pinned(Tracking("Score", Vector2.Zero, "{0:#,0}", 0.7f, Light, LanesName, nameof(NoteLanes.Score)), Anchor.TopRight, new Vector2(-1.5f, -0.62f), TextAlign.Right));
        scene.Add(Pinned(Tracking("Accuracy", Vector2.Zero, "{0:0.00}%", 0.4f, Cyan, LanesName, nameof(NoteLanes.Accuracy)), Anchor.TopRight, new Vector2(-0.5f, -1.35f), TextAlign.Right));
        scene.Add(Pinned(Tracking("Best", Vector2.Zero, "Best {0:#,0}", 0.3f, Dim, LanesName, nameof(NoteLanes.Best)), Anchor.TopRight, new Vector2(-0.5f, -1.85f), TextAlign.Right));

        var pause = MenuButton("Pause", Vector2.Zero, ButtonAction.OpenPanel, null, Key.None, Gray);
        var button = pause.GetComponent<Button>()!;
        (button.Navigable, button.Panel, button.Fade) = (false, PausedName, 0);
        var size = new Vector2(0.8f, 0.8f);
        (pause.GetComponent<Sprite>()!.Size, button.Size) = (size, size);
        (pause.GetComponent<Text>()!.Value, pause.GetComponent<Text>()!.Size) = ("II", 0.4f);
        var corner = pause.AddComponent<ScreenAnchor>();
        (corner.Anchor, corner.Offset) = (Anchor.TopRight, new Vector2(-0.65f, -0.65f));
        scene.Add(pause);

        // The combo, from the second hit in a row.
        scene.Add(Tracking("Combo", new Vector2(-5.3f, 1.4f), "{0:0;;#}", 1.1f, Light, LanesName, nameof(NoteLanes.Combo)));
        scene.Add(Tracking("Combo Label", new Vector2(-5.3f, 0.55f), "{0:'combo';;#}", 0.36f, Dim, LanesName, nameof(NoteLanes.Combo)));
    }

    // Opens as the song's over: the grade, the score and how it was made up, the best, and Retry, Songs and Title.
    private static Entity ResultsPanel()
    {
        var panel = new Entity(ResultsName);
        panel.Transform.Position = new Vector3(0, 0, 2);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var shown = panel.AddComponent<Panel>();
        (shown.OpenMessage, shown.CloseOnBack, shown.PauseGame, shown.First) = ("Song Complete", false, false, "Retry");
        var backdrop = panel.AddComponent<Sprite>();
        (backdrop.Size, backdrop.Tint, backdrop.Order) = (new Vector2(12, 7.6f), Shade, 350);

        void Add(Entity child, int order = 351)
        {
            if (child.GetComponent<Text>() is { } text && child.GetComponent<Button>() is null)
                text.Order = order;
            WithChild(panel, child);
        }

        Add(Tracking("Title", new Vector2(0, 3.0f), "{0}", 0.6f, Light, LanesName, nameof(NoteLanes.Title)));
        Add(Tracking("Playing", new Vector2(0, 2.45f), "{0}", 0.38f, Gold, LanesName, "Playing"));
        Add(Tracking("Grade", new Vector2(-3.2f, 0.55f), "{0}", 2.6f, Gold, LanesName, nameof(NoteLanes.Grade)));
        Add(Tracking("New Best", new Vector2(-3.2f, -1.1f), "{0:'NEW BEST!';;''}", 0.42f, Pink, LanesName, "New Best"));
        Add(Tracking("Full Combo", new Vector2(-3.2f, -1.65f), "{0:'FULL COMBO';;''}", 0.36f, Cyan, LanesName, "Full Combo"));

        (string Name, string Format, string Output, float Size, Color Color)[] lines =
        [
            ("Score", "Score  {0:#,0}", nameof(NoteLanes.Score), 0.5f, Light),
            ("Accuracy", "Accuracy  {0:0.00}%", nameof(NoteLanes.Accuracy), 0.38f, Light),
            ("Best Combo", "Best combo  {0}", nameof(NoteLanes.BestCombo), 0.38f, Light),
            ("Perfects", "Perfect  {0}", nameof(NoteLanes.Perfects), 0.34f, NoteLanes.JudgmentColor(NoteJudgment.Perfect)),
            ("Greats", "Great  {0}", nameof(NoteLanes.Greats), 0.34f, NoteLanes.JudgmentColor(NoteJudgment.Great)),
            ("Goods", "Good  {0}", nameof(NoteLanes.Goods), 0.34f, NoteLanes.JudgmentColor(NoteJudgment.Good)),
            ("Misses", "Miss  {0}", nameof(NoteLanes.Misses), 0.34f, NoteLanes.JudgmentColor(NoteJudgment.Miss)),
            ("Best", "Best  {0:#,0}", nameof(NoteLanes.Best), 0.34f, Dim),
        ];
        for (var i = 0; i < lines.Length; i++)
        {
            var (name, format, output, size, color) = lines[i];
            var line = Tracking(name, new Vector2(-0.4f, 1.55f - i * 0.48f - (i >= 3 ? 0.1f : 0) - (i >= 7 ? 0.1f : 0)), format, size, color, LanesName, output);
            line.GetComponent<Text>()!.Align = TextAlign.Left;
            Add(line);
        }

        Add(Fit(MenuButton("Retry", new Vector2(-3.6f, -2.9f), ButtonAction.RestartScene, null, Key.Enter, Pink, order: 360), 3.2f));
        Add(Fit(MenuButton("Songs", new Vector2(0, -2.9f), ButtonAction.LoadScene, SelectScene, Key.None, Violet, order: 360), 3.2f));
        Add(Fit(MenuButton("Title", new Vector2(3.6f, -2.9f), ButtonAction.LoadScene, Project.DefaultSceneName, Key.Escape, Gray, order: 360), 3.2f));
        return panel;
    }

    // A pause menu with no code: Pause (Esc, P or Start, or the button in the corner) stops the song where it is; Resume,
    // Restart, Settings and back to the songs.
    private static void AddPause(Scene scene)
    {
        var panel = new Entity(PausedName);
        panel.Transform.Position = new Vector3(0, 0, 3);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var pause = panel.AddComponent<Panel>();
        pause.OpenOn = "Pause";
        var backdrop = panel.AddComponent<Sprite>();
        (backdrop.Size, backdrop.Tint, backdrop.Order) = (new Vector2(7, 7.2f), Shade, 400);
        scene.Add(panel);

        var heading = Label("Heading", new Vector2(0, 2.65f), "Paused", 1, Pink);
        heading.GetComponent<Text>()!.Order = 401;
        scene.Add(heading, panel);
        scene.Add(MenuButton("Resume", new Vector2(0, 1.4f), ButtonAction.ClosePanel, null, Key.None, Pink, order: 410), panel);
        scene.Add(MenuButton("Restart", new Vector2(0, 0.2f), ButtonAction.RestartScene, null, Key.None, Gray, order: 410), panel);
        var settings = MenuButton("Settings", new Vector2(0, -1.0f), ButtonAction.OpenPanel, null, Key.None, Violet, order: 410);
        (settings.GetComponent<Button>()!.Panel, settings.Name) = (SettingsName, "Open Settings");
        scene.Add(settings, panel);
        scene.Add(MenuButton("Songs", new Vector2(0, -2.2f), ButtonAction.LoadScene, SelectScene, Key.None, Gray, order: 410), panel);

        // "{Pause}" shows Esc on the keyboard and Start on a gamepad.
        var hint = Label("Hint", new Vector2(0, -3.2f), "{Pause} to resume", 0.35f, Dim);
        hint.GetComponent<Text>()!.Order = 401;
        scene.Add(hint, panel);
    }

    // ----- Settings -----

    // The settings, a panel the title and the pause menu open: the note speed, the audio and visual offsets (and a calibration
    // to tap along to), and the volumes, all kept with the player's settings.
    private static void AddSettings(Scene scene)
    {
        var panel = new Entity(SettingsName);
        panel.Transform.Position = new Vector3(0, 0, 4);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        panel.AddComponent<Panel>();
        var backdrop = panel.AddComponent<Sprite>();
        (backdrop.Size, backdrop.Tint, backdrop.Order) = (new Vector2(12, 8.4f), Shade, 500);
        scene.Add(panel);

        var heading = Label("Heading", new Vector2(0, 3.45f), "Settings", 0.9f, Pink);
        heading.GetComponent<Text>()!.Order = 501;
        scene.Add(heading, panel);

        (string Name, GameSetting Setting, float Min, float Max, float Step, string Format)[] rows =
        [
            ("Note Speed", GameSetting.NoteSpeed, 0.5f, 3, 0.25f, "{0:0.00}x"),
            ("Audio Offset", GameSetting.AudioOffset, -200, 500, 5, "{0:0} ms"),
            ("Visual Offset", GameSetting.VisualOffset, -200, 200, 5, "{0:0} ms"),
            ("Music Volume", GameSetting.MusicVolume, 0, 1, 0.1f, "{0:0%}"),
            ("Effects Volume", GameSetting.SoundVolume, 0, 1, 0.1f, "{0:0%}"),
        ];
        for (var i = 0; i < rows.Length; i++)
        {
            var (name, setting, min, max, step, format) = rows[i];
            var y = 2.25f - i * 0.95f;
            var label = Label($"{name} Label", new Vector2(-1.9f, y), name.Replace(" Volume", "", StringComparison.Ordinal), 0.42f, Light);
            (label.GetComponent<Text>()!.Align, label.GetComponent<Text>()!.Order) = (TextAlign.Right, 501);
            scene.Add(label, panel);

            var row = new Entity(name);
            row.Transform.Position = new Vector3(0.6f, y, 0);
            var slider = row.AddComponent<Slider>();
            (slider.Setting, slider.Min, slider.Max, slider.Step, slider.Size, slider.Order) = (setting, min, max, step, new Vector2(4.2f, 0.36f), 510);
            (slider.Fill, slider.Value) = (Violet, setting == GameSetting.NoteSpeed ? 1 : setting is GameSetting.MusicVolume or GameSetting.SoundVolume ? 1 : 0);
            scene.Add(row, panel);
            var value = Label("Value", new Vector2(2.35f, 0), format, 0.36f, Dim);
            (value.GetComponent<Text>()!.Output, value.GetComponent<Text>()!.Align, value.GetComponent<Text>()!.Order) = (nameof(Slider.Value), TextAlign.Left, 501);
            scene.Add(value, row);
        }

        var calibrate = MenuButton("Calibrate", new Vector2(-2.1f, -2.95f), ButtonAction.OpenPanel, null, Key.None, Violet, order: 510);
        (calibrate.GetComponent<Button>()!.Panel, calibrate.Name) = (CalibrationScreen.Name, "Open Calibration");
        scene.Add(Fit(calibrate, 3.8f), panel);
        scene.Add(Fit(MenuButton("Back", new Vector2(2.1f, -2.95f), ButtonAction.ClosePanel, null, Key.Escape, Gray, order: 510), 3.8f), panel);

        // Tap along to clicks to set the audio offset, over the settings.
        var tap = new Entity(CalibrationScreen.Name);
        CalibrationScreen.Configure(tap);
        tap.Transform.Position = new Vector3(0, 0, 5);
        scene.Add(tap);
        foreach (var child in CalibrationScreen.Children())
            scene.Add(child, tap);
        Raise(tap, 300);
    }

    // Draws an entity and what's inside it `by` higher: a panel over another.
    private static void Raise(Entity entity, int by)
    {
        foreach (var e in entity.SelfAndDescendants())
        {
            foreach (var component in e.Components)
            {
                switch (component)
                {
                    case Sprite sprite:
                        sprite.Order += by;
                        break;
                    case Text text:
                        text.Order += by;
                        break;
                    case Slider slider:
                        slider.Order += by;
                        break;
                }
            }
        }
    }

    // ----- Pieces -----

    private static void AddCamera(Scene scene)
    {
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        (view.Size, view.Background, view.Transition) = (5, Ink, SceneTransition.Fade);
        scene.Add(camera);
    }

    private static void AddBackdrop(Scene scene)
    {
        var backdrop = new Entity("Backdrop");
        backdrop.Transform.Position = new Vector3(0, 0, -10);
        var sprite = backdrop.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (RhythmArt.Stage, new Vector2(RhythmArt.StageWidth, RhythmArt.StageHeight), -20);
        scene.Add(backdrop);
    }

    // A speaker each side, bopping to the beat of `clock`.
    private static void AddSpeakers(Scene scene, string clock, float x)
    {
        foreach (var side in new[] { -1, 1 })
        {
            var speaker = new Entity(side < 0 ? "Left Speaker" : "Right Speaker");
            speaker.Transform.Position = new Vector3(side * -x, -2.6f, 0);
            var sprite = speaker.AddComponent<Sprite>();
            (sprite.Image, sprite.Size, sprite.Order) = (RhythmArt.Speaker, RhythmArt.SpeakerSize, 5);
            var pulse = speaker.AddComponent<BeatPulse>();
            (pulse.Size, pulse.Clock) = (0.07f, clock);
            scene.Add(speaker);
        }
    }

    private static Entity Tracking(string name, Vector2 at, string format, float size, Color color, string track, string output)
    {
        var label = Label(name, at, format, size, color);
        var text = label.GetComponent<Text>()!;
        (text.Track, text.Output, text.Order) = (track, output, 160);
        return label;
    }

    private static Entity Left(Entity label, int order)
    {
        (label.GetComponent<Text>()!.Align, label.GetComponent<Text>()!.Order) = (TextAlign.Left, order);
        return label;
    }

    private static Entity Pinned(Entity label, Anchor anchor, Vector2 offset, TextAlign align)
    {
        label.GetComponent<Text>()!.Align = align;
        var pin = label.AddComponent<ScreenAnchor>();
        (pin.Anchor, pin.Offset) = (anchor, offset);
        return label;
    }

    private static Entity Hint(string value)
    {
        var hint = Label("Hint", Vector2.Zero, value, 0.3f, Dim);
        hint.GetComponent<Text>()!.Order = 160;
        var anchor = hint.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.Bottom, new Vector2(0, 0.35f));
        return hint;
    }

    private static Entity Fit(Entity button, float width)
    {
        var size = new Vector2(width, 0.85f);
        button.GetComponent<Sprite>()!.Size = size;
        button.GetComponent<Button>()!.Size = size;
        button.GetComponent<Text>()!.Size = 0.45f;
        return button;
    }
}
