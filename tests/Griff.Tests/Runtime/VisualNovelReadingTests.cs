using System.Numerics;
using Griff.Art;
using Griff.Input;
using Griff.Modules.VisualNovels;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>
/// Reading a visual novel: saved games in slots (the stage, the talk and the flags put back exactly, a screenshot, kept
/// from one run to the next), the lines read before (remembered too), skipping them, reading on by itself, the log of
/// lines said, the settings for both, and Create Reading Menus.
/// </summary>
public sealed class VisualNovelReadingTests : IDisposable
{
    private const float Frame = 1 / 60f;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "griff-vn-reading-" + Guid.NewGuid().ToString("N"));
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly List<string> _warnings = [];

    public VisualNovelReadingTests()
    {
        _game = new Game(_scene, _input) { SceneName = "Story" };
        _game.Warning += (_, message) => _warnings.Add(message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private string SavePath => Path.Combine(_folder, "save.json");

    private void Run(float seconds, Game? game = null, InputState? input = null)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            (game ?? _game).Tick(Frame);
            (input ?? _input).EndFrame();
        }
    }

    private void Start() => Run(2 * Frame);

    private void Click()
    {
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(Frame);
    }

    private void Press(Key key)
    {
        _input.SetKey(key, true);
        Run(Frame);
        _input.SetKey(key, false);
        Run(Frame);
    }

    // A stage with Ava (Neutral, Happy, Sad) off stage, a box whose lines type out (or show at once), a log, and a dialogue
    // on the stage that starts with the scene, saying `lines`.
    private (Stage Stage, Dialogue Dialogue, Text Text) Build(float typing, params string[] lines) => Build(_scene, typing, lines);

    private static (Stage Stage, Dialogue Dialogue, Text Text) Build(Scene scene, float typing, params string[] lines)
    {
        var holder = new Entity("Stage");
        var stage = holder.AddComponent<Stage>();
        stage.Backgrounds = ["Park: Stage/park.png", "Night: #000000 #202040"];
        stage.Background = "Park";
        var dialogue = holder.AddComponent<Dialogue>();
        (dialogue.Start, dialogue.PauseGame, dialogue.Lines) = (DialogueStart.SceneStart, false, lines);
        scene.Add(holder);

        var ava = new Entity("Ava");
        ava.AddComponent<Speaker>();
        ava.AddComponent<Sprite>().Image = "ava-neutral.png";
        ava.AddComponent<Character>().Expressions = ["Neutral: ava-neutral.png", "Happy: ava-happy.png", "Sad: ava-sad.png"];
        ava.IsEnabled = false;
        scene.Add(ava, holder);

        var box = new Entity("Dialogue Box");
        box.AddComponent<DialogueBox>();
        var text = box.AddComponent<Text>();
        box.AddComponent<Typewriter>().Speed = typing;
        scene.Add(box);
        return (stage, dialogue, text);
    }

    private Character Ava(Scene? scene = null) => (scene ?? _game.Scene).Entities.Single(e => e.Name == "Ava").GetComponent<Character>()!;

    private static T The<T>(Scene scene) where T : Component => scene.Entities.Select(e => e.GetComponent<T>()).OfType<T>().First();

    // ----- Saved games -----

    [Fact]
    public void Loading_a_saved_game_puts_the_stage_the_talk_and_the_flags_back_exactly()
    {
        Build(0,
            "[set Met] [show Ava left happy cut] Ava: Hello!",
            "[clear Met] [set Late] [background Night cut] [move Ava right] Ava (sad): It's late.",
            "Ava: Goodnight.");
        Start();
        Assert.Equal("Hello!", The<Dialogue>(_scene).Said);
        Assert.True(_game.SaveGame("1"));

        Click();
        Run(1);
        Assert.Equal("Night", The<Stage>(_scene).Background);
        Assert.Equal("Right", Ava().Position);
        Assert.True(_game.HasFlag("Late"));
        Assert.False(_game.HasFlag("Met"));
        var loaded = 0;
        _game.GameLoaded += _ => loaded++;

        Assert.True(_game.LoadGame("1"));
        // It switches at the end of the frame, and the talk starts again on the next.
        Run(3 * Frame);
        var scene = _game.Scene;
        Assert.NotSame(_scene, scene);
        Assert.Equal("Story", _game.SceneName);
        Assert.Equal(1, loaded);
        var dialogue = The<Dialogue>(scene);
        Assert.Same(dialogue, _game.Talking);
        Assert.Equal(0, dialogue.Line);
        Assert.Equal("Hello!", dialogue.Said);
        Assert.Equal("Park", The<Stage>(scene).Background);
        Assert.Equal("Left", Ava(scene).Position);
        Assert.Equal("Happy", Ava(scene).Expression);
        Assert.Equal(-4.5f, Ava(scene).Entity!.Transform.WorldPosition.X, 3);
        Assert.True(_game.HasFlag("Met"));
        Assert.False(_game.HasFlag("Late"));

        // And it goes on from there, its tags done as the next line is said.
        Click();
        Assert.Equal("It's late.", dialogue.Said);
        Assert.True(_game.HasFlag("Late"));
        Assert.Empty(_warnings);
    }

    [Fact]
    public void A_game_saved_from_a_save_screen_comes_back_without_it_and_with_a_screenshot()
    {
        Build(0, "Ava: One.", "Ava: Two.");
        var screen = new Entity("Save Screen");
        screen.AddComponent<Panel>();
        _scene.Add(screen);
        Start();
        _game.OpenPanel("Save Screen");
        Run(Frame);
        Assert.True(_game.IsPaused);

        Assert.True(_game.SaveGame("2"));
        Assert.True(_game.PictureWanted);
        // The host draws the game without the panel and hands over its pixels: a red picture, twice as wide as kept.
        var pixels = Enumerable.Repeat<byte[]>([200, 30, 30, 255], 480 * 270).SelectMany(p => p).ToArray();
        _game.TakePicture(pixels, 480, 270);
        Assert.False(_game.PictureWanted);

        var saved = _game.SavedGameIn("2")!;
        Assert.Equal("@save/2", saved.Picture);
        Assert.Equal("Story", saved.SceneName);
        Assert.Equal("Ava: One.", saved.Line);
        Assert.Equal(16 / 9f, saved.PictureAspect, 2);
        var (png, _) = _game.Pictures.Get("@save/2");
        var (rgba, width, height) = Png.Decode(png!)!.Value;
        Assert.Equal((Game.PictureWidth, 135), (width, height));
        Assert.Equal(200, rgba[0]);

        _game.LoadGame("2");
        Run(2 * Frame);
        Assert.False(_game.IsPaused);
        Assert.Empty(_game.OpenPanels);
        Assert.False(_game.Scene.Entities.Single(e => e.Name == "Save Screen").IsEnabled);
        Assert.Equal("One.", The<Dialogue>(_game.Scene).Said);
    }

    [Fact]
    public void A_frame_drawn_for_the_screenshot_leaves_out_the_panels_open_over_the_game()
    {
        Build(0, "Ava: One.");
        var screen = new Entity("Save Screen");
        screen.AddComponent<Panel>();
        screen.AddComponent<Sprite>().Tint = Color.Black;
        _scene.Add(screen);
        Start();
        _game.OpenPanel("Save Screen");
        Run(Frame);

        Assert.Contains(SceneRenderer.Build(_game, 16 / 9f).Sprites, d => d.Entity == screen);
        var picture = SceneRenderer.BuildPicture(_game, 16 / 9f);
        Assert.DoesNotContain(picture.Sprites, d => d.Entity == screen);
        Assert.Contains(picture.Sprites, d => d.Entity.Name == "Stage");
        Assert.Same(_game.Pictures, picture.Pictures);
    }

    [Fact]
    public void Saved_games_are_kept_from_one_run_of_the_game_to_the_next_and_can_be_deleted()
    {
        _game.SaveData = new SaveData(SavePath);
        _game.Clock = () => new DateTimeOffset(2026, 10, 15, 14, 3, 0, TimeSpan.Zero);
        Build(0, "Ava: Hello!", "Ava: Again.");
        Start();
        _game.SaveGame("10");
        _game.SaveGame("2");
        Click();
        _game.QuickSave();

        // The next time the game runs.
        var scene = new Scene();
        Build(scene, 0, "Ava: Hello!", "Ava: Again.");
        var input = new InputState();
        var next = new Game(scene, input) { SaveData = new SaveData(SavePath), SceneName = "Story" };
        Assert.Equal(["2", "10", Game.QuickSlot], next.SavedGames.Select(g => g.Slot));
        var saved = next.SavedGameIn("2")!;
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 14, 3, 0, TimeSpan.Zero), saved.Saved);
        Assert.Equal("Ava: Hello! in Story at 2", saved.Describe("{line} in {scene} at {slot}"));
        Assert.Equal("Ava: Again.", next.SavedGameIn(Game.QuickSlot)!.Line);

        Run(2 * Frame, next, input);
        Assert.True(next.QuickLoad());
        Run(2 * Frame, next, input);
        Assert.Equal("Again.", The<Dialogue>(next.Scene).Said);

        Assert.True(next.DeleteSave("2"));
        Assert.False(next.HasSave("2"));
        Assert.Null(next.SavedGameIn("2"));
        Assert.False(next.LoadGame("2"));
        Assert.Equal(["10", Game.QuickSlot], new Game(new Scene(), new InputState()) { SaveData = new SaveData(SavePath) }.SavedGames.Select(g => g.Slot));
    }

    [Fact]
    public void Save_load_and_delete_buttons_work_on_their_slot_and_fade_while_its_empty()
    {
        _game.ViewAspect = 16 / 9f;
        Build(0, "Ava: Hello!", "Ava: Again.");
        var card = new Entity("Slot");
        card.Transform.Position = new Vector3(-5, -3, 0);
        card.AddComponent<SaveSlot>().Slot = "3";
        var save = card.AddComponent<Button>();
        (save.Action, save.Size) = (ButtonAction.SaveGame, new Vector2(2, 2));
        _scene.Add(card);
        var delete = new Entity("Delete");
        delete.Transform.Position = new Vector3(0.8f, 0.8f, 0);
        var x = delete.AddComponent<Button>();
        (x.Action, x.Size) = (ButtonAction.DeleteSave, new Vector2(0.3f, 0.3f));
        // Over the card (whose save slot draws at 320).
        delete.AddComponent<Sprite>().Order = 400;
        _scene.Add(delete, card);
        var load = new Entity("Load");
        load.Transform.Position = new Vector3(5, -3, 0);
        var loader = load.AddComponent<Button>();
        (loader.Action, loader.Slot, loader.Size) = (ButtonAction.LoadGame, "3", new Vector2(2, 2));
        _scene.Add(load);
        var quick = new Entity("Quick Load");
        quick.AddComponent<Button>().Action = ButtonAction.QuickLoad;
        _scene.Add(quick, card);

        Start();
        Assert.False(loader.IsUsable);
        Assert.False(x.IsUsable);
        Assert.Equal("Empty", card.GetComponent<SaveSlot>()!.Description);

        ClickAt(new Vector2(-5, -3));
        Assert.True(_game.HasSave("3"));
        Run(Frame);
        Assert.True(loader.IsUsable);
        Assert.True(x.IsUsable);
        Assert.False(quick.GetComponent<Button>()!.IsUsable);
        Assert.Contains("Ava: Hello!", card.GetComponent<SaveSlot>()!.Description);

        _game.AdvanceDialogue(_game.Talking!);
        Assert.Equal("Again.", _game.Talking!.Said);
        ClickAt(new Vector2(5, -3));
        Run(2 * Frame);
        Assert.Equal("Hello!", The<Dialogue>(_game.Scene).Said);

        var slot = _game.Scene.Entities.Single(e => e.Name == "Slot");
        ClickAt(new Vector2(-4.2f, -2.2f));
        Assert.False(_game.HasSave("3"));
        Assert.Equal("Empty", slot.GetComponent<SaveSlot>()!.Description);
    }

    [Fact]
    public void A_continue_button_loads_the_game_saved_last_in_any_slot_and_is_faded_while_there_is_none()
    {
        _game.ViewAspect = 16 / 9f;
        Build(0, "Ava: One.", "Ava: Two.", "Ava: Three.");
        var resume = new Entity("Continue");
        resume.Transform.Position = new Vector3(5, -3, 0);
        var button = resume.AddComponent<Button>();
        (button.Action, button.Size) = (ButtonAction.Continue, new Vector2(2, 2));
        _scene.Add(resume);
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        _game.Clock = () => now;

        Start();
        Assert.Null(_game.LatestSave);
        Assert.False(button.IsUsable);
        Assert.False(_game.ContinueGame());

        // Saved in slot 2 on line Two, then quick saved later on line Three: Continue loads the quick save.
        _game.AdvanceDialogue(_game.Talking!);
        Run(Frame);
        _game.SaveGame("2");
        now = now.AddMinutes(5);
        _game.AdvanceDialogue(_game.Talking!);
        Run(Frame);
        _game.QuickSave();
        Run(Frame);
        Assert.Equal((Game.QuickSlot, "Ava: Three.", "Ava: Two."), (_game.LatestSave?.Slot, _game.LatestSave?.Line, _game.SavedGameIn("2")?.Line));
        Assert.True(button.IsUsable);

        _game.EndDialogue(finished: false);
        _game.StartDialogue(The<Dialogue>(_scene));
        Run(Frame);
        Assert.Equal("One.", _game.Talking?.Said);
        ClickAt(new Vector2(5, -3));
        Run(2 * Frame);
        Assert.Equal("Three.", _game.Talking?.Said);

        // Slot 2 saved again, later still: now it's the one.
        now = now.AddMinutes(5);
        _game.SaveGame("2");
        Assert.Equal("2", _game.LatestSave?.Slot);
        _game.DeleteSave("2");
        _game.DeleteSave(Game.QuickSlot);
        Run(Frame);
        Assert.Null(_game.LatestSave);
        Assert.False(_game.Scene.Entities.Single(e => e.Name == "Continue").GetComponent<Button>()!.IsUsable);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void A_pause_menu_over_choices_takes_the_arrow_keys_and_the_line_stops_typing_under_it()
    {
        _game.ViewAspect = 16 / 9f;
        var (_, dialogue, text) = Build(0, "Which?", "> A", "> B", "> C", "A much longer line, typing out slowly.");
        var pause = new Entity("Paused");
        pause.AddComponent<Panel>().OpenOn = "Pause";
        _scene.Add(pause);
        foreach (var (name, y) in new[] { ("Resume", 1f), ("Quit", -1f) })
        {
            var entity = new Entity(name);
            entity.Transform.Position = new Vector3(0, y, 0);
            entity.AddComponent<Button>().Action = name == "Resume" ? ButtonAction.ClosePanel : ButtonAction.None;
            _scene.Add(entity, pause);
        }

        Start();
        Assert.Equal(3, dialogue.Choices.Count);
        Press(Key.Escape);
        Assert.Equal("Paused", _game.TopPanel?.Entity!.Name);
        Assert.Equal("Resume", _game.Focused?.Entity!.Name);
        Press(Key.Down);
        Assert.Equal("Quit", _game.Focused?.Entity!.Name);
        Assert.Equal(0, dialogue.Picked);

        // Closed again, the arrows pick the choices.
        _game.ClosePanel(_game.TopPanel);
        Run(Frame);
        Press(Key.Down);
        Assert.Equal(1, dialogue.Picked);

        // A line typing out holds still under the menu.
        var typing = text.Entity!.GetComponent<Typewriter>()!;
        typing.Speed = 10;
        Press(Key.D1);
        Assert.Equal("A much longer line, typing out slowly.", dialogue.Said);
        Press(Key.Escape);
        var shown = typing.Shown;
        Run(1);
        Assert.Equal(shown, typing.Shown);
        _game.ClosePanel(_game.TopPanel);
        Run(1);
        Assert.True(typing.Shown > shown);
    }

    private void ClickAt(Vector2 at)
    {
        _input.MouseWorld = at;
        Run(Frame);
        Click();
    }

    [Fact]
    public void A_network_game_cant_be_saved_or_loaded_and_reading_stays_each_machines_own()
    {
        var world = new NetWorld(() =>
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            Build(scene, 0, "One.", "Two.", "Three.");
            return scene;
        });
        var warnings = new List<string>();
        world.Client.Warning += (_, w) => warnings.Add(w);
        world.Run(0.2);
        Assert.False(world.Client.SaveGame("1"));
        Assert.False(world.Host.SaveGame("1"));
        Assert.Contains(warnings, w => w.Contains("network game"));

        // The client reads on by itself; the host's talk stays where it was.
        world.Client.AutoReading = true;
        world.Run(1.5);
        Assert.Equal("Two.", world.Client.Talking?.Said ?? "");
        Assert.Equal("One.", world.Host.Talking?.Said);
    }

    [Fact]
    public void A_button_on_a_panel_just_closed_doesnt_stop_a_click_reading_on_or_the_wheel_opening_the_log()
    {
        Build(0, "One.", "Two.", "Three.");
        var menu = new Entity("Menu");
        menu.AddComponent<Panel>();
        _scene.Add(menu);
        var button = new Entity("Button");
        button.AddComponent<Button>().Size = new Vector2(4, 4);
        _scene.Add(button, menu);
        var log = new Entity("Log");
        log.AddComponent<Panel>();
        log.AddComponent<Backlog>();
        _scene.Add(log);
        Start();
        _game.OpenPanel("Menu");
        _input.MouseWorld = Vector2.Zero;
        Run(2 * Frame);
        Assert.True(button.GetComponent<Button>()!.IsHovered);
        Press(Key.Escape);
        Assert.Null(_game.TopPanel);

        Click();
        Assert.Equal("Two.", _game.Talking!.Said);
        _input.AddScroll(1);
        Run(Frame);
        Assert.Equal("Log", _game.TopPanel?.Entity?.Name);
    }

    // ----- Lines read before, skipping and reading on -----

    [Fact]
    public void Lines_read_are_remembered_from_one_run_to_the_next()
    {
        _game.SaveData = new SaveData(SavePath);
        var (_, dialogue, _) = Build(0, "One.", "Two.", "Three.");
        Start();
        Click();
        Assert.True(_game.HasRead(dialogue, 0));
        Assert.True(_game.HasRead(dialogue, 1));
        Assert.False(_game.HasRead(dialogue, 2));
        Run(1.5f);

        // The same scene as its file has it (the same entities), with a line changed since.
        var serializer = new Griff.Scenes.Serialization.SceneSerializer(Griff.Scenes.Serialization.ComponentRegistry.CreateDefault());
        var scene = serializer.LoadFromString(serializer.SaveToString(_scene));
        var again = The<Dialogue>(scene);
        again.Lines = ["One.", "Two changed.", "Three."];
        var next = new Game(scene, new InputState()) { SaveData = new SaveData(SavePath) };
        Assert.True(next.HasRead(again, 0));
        Assert.False(next.HasRead(again, 1));
        Assert.False(next.HasRead(again, 2));
    }

    [Fact]
    public void Skipping_goes_through_lines_read_before_and_stops_at_one_not_read_yet()
    {
        var (_, dialogue, _) = Build(20, "One.", "Two.", "Three.", "Four.", "Five.");
        Start();
        Run(0.5f);
        Click();
        Run(0.5f);
        Click();
        Assert.Equal(2, dialogue.Line);

        // Read again from the start: Skip goes through what was read, and stops at Four.
        _game.EndDialogue(finished: false);
        _game.StartDialogue(dialogue);
        Run(Frame);
        Assert.Equal(0, dialogue.Line);
        _game.Skipping = true;
        Run(0.5f);
        Assert.Equal(3, dialogue.Line);
        Assert.False(_game.Skipping);

        // Skip Unread goes on through the rest.
        _game.SkipUnread = true;
        _game.Skipping = true;
        Run(0.3f);
        Assert.True(dialogue.IsDone);
    }

    [Fact]
    public void Skipping_stops_at_choices_and_a_click_stops_it_too()
    {
        var (_, dialogue, _) = Build(0, "One.", "Two.", "Pick:", "> A", "> B", "After.", "More.");
        _game.SkipUnread = true;
        Start();
        _game.Skipping = true;
        Run(0.5f);
        Assert.Equal(2, dialogue.Line);
        Assert.Equal(2, dialogue.Choices.Count);
        Assert.False(_game.Skipping);

        Press(Key.D1);
        Assert.Equal("After.", dialogue.Said);
        _game.Skipping = true;
        _input.SetKey(Key.E, true);
        Run(Frame);
        _input.SetKey(Key.E, false);
        Assert.False(_game.Skipping);
    }

    [Fact]
    public void The_stage_skips_while_its_skip_action_is_held_with_transitions_done_at_once()
    {
        var (stage, dialogue, _) = Build(0, "One.", "Two.", "[background Night fade] [show Ava left] Three.", "Four.");
        _game.SkipUnread = true;
        Start();
        _input.SetKey(Key.LeftControl, true);
        Run(4 * SkipFrames);
        Assert.Equal(3, dialogue.Line);
        Assert.Null(stage.LeavingBackground);
        Assert.Equal(1, Ava().Shown, 3);

        // Let go, and it stays.
        _input.SetKey(Key.LeftControl, false);
        Run(0.5f);
        Assert.Equal(3, dialogue.Line);
        Assert.False(_game.Skipping);
    }

    private static float SkipFrames => Game.SkipEvery + 2 * Frame;

    [Fact]
    public void Auto_reads_on_after_a_pause_as_long_as_the_line_and_waits_at_choices()
    {
        var (_, dialogue, _) = Build(0, "Hi.", "This line is a good deal longer than the one before it.", "Which?", "> A", "> B");
        Start();
        _game.AutoReading = true;
        var shortWait = _game.AutoDelay("Hi.");
        var longWait = _game.AutoDelay(dialogue.Lines[1]);
        Assert.True(longWait > shortWait + 1);

        Run(shortWait - 0.1f);
        Assert.Equal(0, dialogue.Line);
        Run(0.2f);
        Assert.Equal(1, dialogue.Line);
        Run(longWait - 0.2f);
        Assert.Equal(1, dialogue.Line);
        Run(0.3f);
        Assert.Equal(2, dialogue.Line);
        Run(10);
        Assert.Equal(2, dialogue.Line);
        Assert.True(_game.AutoReading);

        // Quicker at a higher speed.
        _game.AutoSpeed = 1;
        Assert.True(_game.AutoDelay("Hi.") < shortWait / 1.5f);
    }

    [Fact]
    public void Auto_waits_for_the_line_to_type_out_and_the_stages_auto_action_turns_it_on_and_off()
    {
        var (_, dialogue, _) = Build(10, "Twenty letters long..", "Next.");
        Start();
        Press(Key.A);
        Assert.True(_game.AutoReading);
        Run(_game.AutoDelay(dialogue.Said) + 0.5f);
        // Two seconds of typing first.
        Assert.Equal(0, dialogue.Line);
        Run(2);
        Assert.Equal(1, dialogue.Line);
        Press(Key.A);
        Assert.False(_game.AutoReading);
    }

    [Fact]
    public void Skip_unread_and_auto_speed_are_the_players_settings_kept_in_the_save_data()
    {
        _game.SaveData = new SaveData(SavePath);
        var slider = new Entity("Auto Speed");
        var speed = slider.AddComponent<Slider>();
        speed.Setting = GameSetting.AutoSpeed;
        _scene.Add(slider);
        var toggle = new Entity("Skip Unread");
        toggle.AddComponent<Button>();
        toggle.AddComponent<Toggle>().Setting = GameSetting.SkipUnread;
        _scene.Add(toggle);
        Run(Frame);
        Assert.Equal(0.5f, speed.Value, 3);
        Assert.False(toggle.GetComponent<Toggle>()!.IsOn);

        _game.AutoSpeed = 0.8f;
        _game.SkipUnread = true;
        Run(Frame);
        Assert.Equal(0.8f, speed.Value, 3);
        Assert.True(toggle.GetComponent<Toggle>()!.IsOn);

        var next = new Game(new Scene(), new InputState()) { SaveData = new SaveData(SavePath) };
        Assert.Equal(0.8f, next.AutoSpeed, 3);
        Assert.True(next.SkipUnread);
    }

    // ----- The log -----

    [Fact]
    public void The_log_keeps_the_lines_said_with_who_said_them_and_the_choices_picked()
    {
        Build(0, "[show Ava left] Ava: Hello!", "It was a quiet day.", "Ava: Stay?", "> Yes", "> No", "Ava: Good.");
        var holder = new Entity("Log");
        var log = holder.AddComponent<Backlog>();
        log.Keep = 4;
        holder.AddComponent<Panel>();
        _scene.Add(holder);
        Start();
        Click();
        Click();
        Press(Key.D1);
        Assert.Equal(["\\It was a quiet day.", "Ava: Stay?", "> Yes", "Ava: Good."], log.Lines);
        Assert.Equal(("Ava", "Stay?", false), Backlog.Entry(log.Lines[1]));
        Assert.Equal(((string?)null, "Yes", true), Backlog.Entry(log.Lines[2]));
        Assert.Equal(((string?)null, "It was a quiet day.", false), Backlog.Entry(log.Lines[0]));

        // Saved with the scene, as the stage is.
        _game.SaveGame("1");
        _game.LoadGame("1");
        Run(2 * Frame);
        Assert.Equal(log.Lines, The<Backlog>(_game.Scene).Lines);
    }

    [Fact]
    public void The_wheel_opens_the_log_scrolls_it_and_closes_it_past_the_newest_line()
    {
        var lines = Enumerable.Range(1, 30).Select(i => $"Ava: Line {i}.").ToArray();
        Build(0, lines);
        var holder = new Entity("Log");
        holder.AddComponent<Panel>();
        _scene.Add(holder);
        var inside = new Entity("Lines");
        var log = inside.AddComponent<Backlog>();
        _scene.Add(inside, holder);
        Start();
        while (_game.Talking!.Line < 29)
            _game.AdvanceDialogue(_game.Talking!);
        Assert.Equal(30, log.Lines.Count);
        Assert.True(log.MostScroll > 5);

        _input.AddScroll(1);
        Run(Frame);
        Assert.Same(holder.GetComponent<Panel>(), _game.TopPanel);
        Assert.Equal(0, log.Scroll);
        _input.AddScroll(1);
        Run(Frame);
        Assert.Equal(3, log.Scroll);

        // The keys, a page, and dragging with a finger.
        Press(Key.Up);
        Assert.Equal(4, log.Scroll);
        Press(Key.PageUp);
        Assert.Equal(Math.Min(log.MostScroll, 4 + log.RowsShown), log.Scroll);
        Press(Key.End);
        Assert.Equal(0, log.Scroll);
        _input.MouseWorld = new Vector2(0, 0);
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        _input.MouseWorld = new Vector2(0, -log.RowHeight * 2);
        Run(Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(Frame);
        Assert.Equal(2, log.Scroll, 3);

        Press(Key.End);
        _input.AddScroll(-1);
        Run(Frame);
        Assert.Null(_game.TopPanel);
        // Reading went nowhere meanwhile.
        Assert.Equal("Line 30.", _game.Talking!.Said);
    }

    [Fact]
    public void The_log_opens_with_its_key_on_the_reading_menus()
    {
        Build(0, "Ava: Hello!");
        ReadingMenus.AddTo(_scene);
        Start();
        Press(Key.H);
        Assert.Equal(ReadingMenus.Log, _game.TopPanel?.Entity?.Name);
        Assert.Equal(["Ava: Hello!"], The<Backlog>(_scene).Lines);
        Press(Key.H);
        Assert.Null(_game.TopPanel);
    }

    // ----- Create Reading Menus -----

    [Fact]
    public void Create_Reading_Menus_makes_the_quick_menu_and_its_screens_and_they_work()
    {
        _game.ViewAspect = 16 / 9f;
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>().Size = 5;
        _scene.Add(camera);
        Build(0, "Ava: One.", "Ava: Two.", "Ava: Three.");
        var preset = new ReadingMenusPreset();
        var root = new Entity(preset.EntityName(_scene));
        preset.Configure(root, _scene);
        _scene.Add(root);
        foreach (var edit in preset.EditScene(_scene))
            edit.Apply();

        var names = _scene.Entities.Select(e => e.Name).ToHashSet();
        foreach (var name in (string[])["Quick Menu", ReadingMenus.SaveScreen, ReadingMenus.LoadScreen, ReadingMenus.Log, ReadingMenus.Settings])
            Assert.Contains(name, names);
        Assert.Equal(12, _scene.Entities.Count(e => e.GetComponent<SaveSlot>() is not null));
        Start();
        Assert.Empty(_game.OpenPanels);

        // Save in slot 1 from the save screen, read on, then load it from the load screen.
        Button(ReadingMenus.SaveScreen + "/Slot 1", out var save);
        _game.OpenPanel(ReadingMenus.SaveScreen);
        Run(Frame);
        ClickAt(Flat(save.Entity!.Transform.WorldPosition));
        Assert.Equal("Ava: One.", _game.SavedGameIn("1")?.Line);
        Press(Key.Escape);
        Assert.Null(_game.TopPanel);
        _game.AdvanceDialogue(_game.Talking!);

        _game.OpenPanel(ReadingMenus.LoadScreen);
        Run(Frame);
        Button(ReadingMenus.LoadScreen + "/Slot 1", out var load);
        Assert.True(load.IsUsable);
        Assert.False(Find(ReadingMenus.LoadScreen + "/Slot 2").GetComponent<Button>()!.IsUsable);
        ClickAt(Flat(load.Entity!.Transform.WorldPosition));
        Run(1);
        Assert.Equal("One.", _game.Talking?.Said);

        // The quick menu's Auto and Skip are toggles of the game's own.
        var auto = _game.Scene.Entities.Single(e => e.Name == "Auto").GetComponent<Button>()!;
        ClickAt(Flat(auto.Entity!.Transform.WorldPosition));
        Assert.True(_game.AutoReading);
        Assert.Equal("Auto >", auto.Entity.GetComponent<Text>()!.Value);
        Assert.Equal("One.", _game.Talking?.Said);
        Assert.Empty(_warnings);

        void Button(string path, out Button button) => button = Find(path).GetComponent<Button>()!;
    }

    [Fact]
    public void Create_Stage_comes_with_the_reading_menus_once()
    {
        var preset = new StagePreset();
        var holder = new Entity(preset.EntityName(_scene));
        preset.Configure(holder, _scene);
        _scene.Add(holder);
        foreach (var edit in preset.EditScene(_scene))
            edit.Apply();
        Assert.Single(_scene.Entities, e => e.GetComponent<Backlog>() is not null);
        Assert.Contains(_scene.Entities, e => e.Name == ReadingMenus.Name);

        Assert.DoesNotContain(preset.EditScene(_scene).OfType<Griff.Editing.AddEntityEdit>(), e => e.Entity.Name == ReadingMenus.Name);
    }

    private Entity Find(string path)
    {
        var parts = path.Split('/');
        var entity = _game.Scene.Entities.Single(e => e.Name == parts[0]);
        foreach (var part in parts.Skip(1))
            entity = entity.Children.Single(c => c.Name == part);
        return entity;
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
}
