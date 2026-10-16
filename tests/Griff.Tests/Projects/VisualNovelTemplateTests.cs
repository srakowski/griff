using System.Numerics;
using Griff.Input;
using Griff.Modules.VisualNovels;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Projects;

/// <summary>
/// The Visual Novel template, Starfall Café: a title screen and a story told on a stage with no code, read to each of its
/// three endings with the mouse, the keys or a gamepad; saved mid-story and loaded (the stage, the line and the flags put
/// back) from the pause menu and the title screen's Continue; skipped on a second read up to the first choice; and
/// exported for the desktop and the web.
/// </summary>
public sealed class VisualNovelTemplateTests : IDisposable
{
    private const double Frame = 1 / 60.0;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-vn-" + Guid.NewGuid().ToString("N"));
    private readonly InputState _input = new();
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();
    private readonly List<string> _warnings = [];

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<Project> CreateProject() => await Project.CreateAsync(_root, "Starfall", ProjectTemplates.Get("VisualNovel"));

    private Game Start(Project project, string scene)
    {
        Scene Load(string name) => GameLoader.LoadScene(project, _registry, name);
        var game = new Game(Load(scene), _input)
        {
            SceneLoader = Load,
            SceneName = scene,
            Components = _registry,
            ViewAspect = 16 / 9f,
        };
        game.Problems.Reported += p => _warnings.Add(p.Message);
        game.UseProjectSettings(project);
        // Off the buttons and the choices, until a test points it somewhere.
        _input.MouseWorld = new Vector2(0, 1.5f);
        Run(game, 0.1);
        return game;
    }

    private void Run(Game game, double seconds)
    {
        for (var t = 0.0; t < seconds - 1e-6; t += Frame)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Press(Game game, Key key, double after = 0.1)
    {
        _input.SetKey(key, true);
        Run(game, Frame);
        _input.SetKey(key, false);
        Run(game, after);
    }

    private void Pad(Game game, GamepadButton button, double after = 0.1)
    {
        _input.SetGamepad([button], Vector2.Zero);
        Run(game, Frame);
        _input.SetGamepad([], Vector2.Zero);
        Run(game, after);
    }

    private void Click(Game game, Vector2 at, double after = 0.1)
    {
        _input.MouseWorld = at;
        Run(game, Frame);
        _input.SetMouseButton(MouseButton.Left, true);
        Run(game, Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(game, after);
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    private static Dialogue Story(Game game) => game.Find(VisualNovelTemplate.StageName)!.GetComponent<Dialogue>()!;

    private static Stage StageOf(Game game) => game.Find(VisualNovelTemplate.StageName)!.GetComponent<Stage>()!;

    private static Character Cast(Game game, string name) => game.Find(name)!.GetComponent<Character>()!;

    private static string? Ended(Game game) =>
        VisualNovelTemplate.Endings.Select(e => game.Find(e.Panel)?.GetComponent<Panel>()).FirstOrDefault(p => p is { Open: true })?.Entity!.Name;

    // How a player reads on and picks a choice: the mouse (a click, or a tap), the keys or a gamepad.
    public enum Hands
    {
        Mouse,
        Keys,
        Gamepad,
    }

    // Reads on until the story offers choices or ends.
    private void ReadOn(Game game, Hands hands)
    {
        for (var i = 0; i < 200 && Story(game).Choices.Count == 0 && Ended(game) is null; i++)
        {
            switch (hands)
            {
                case Hands.Mouse:
                    Click(game, new Vector2(0, 1.5f));
                    break;
                case Hands.Keys:
                    Press(game, Key.Space);
                    break;
                default:
                    Pad(game, GamepadButton.A);
                    break;
            }
        }
    }

    // Picks choice `index` (from 0): a click on its row, the arrows and Enter, or the D-pad and Y.
    private void Choose(Game game, int index, Hands hands)
    {
        var dialogue = Story(game);
        Assert.NotEmpty(dialogue.Choices);
        var picked = dialogue.Choices[index];
        // The line over them types out first.
        var typing = game.Find("Line")!.GetComponent<Typewriter>()!;
        for (var i = 0; i < 300 && !typing.IsDone; i++)
            Run(game, Frame);
        switch (hands)
        {
            case Hands.Mouse:
                // Each row of the box's text is a line of its size, the block centered on it; the choices are its last rows.
                var text = game.Find("Line")!.GetComponent<Text>()!;
                var lines = (text.Value ?? "").Count(c => c == '\n') + 1;
                var top = text.Entity!.Transform.WorldPosition.Y + text.Size * lines / 2;
                var row = lines - dialogue.Choices.Count + index;
                Click(game, new Vector2(0, top - text.Size * (row + 0.5f)), after: 0.2);
                break;
            case Hands.Keys:
                for (var i = 0; i < index; i++)
                    Press(game, Key.Down);
                Press(game, Key.Enter, after: 0.2);
                break;
            default:
                for (var i = 0; i < index; i++)
                    Pad(game, GamepadButton.DPadDown);
                // A reads on; Y (Interact) picks, so reading on never picks a choice by mistake.
                Pad(game, GamepadButton.Y, after: 0.2);
                break;
        }

        Assert.DoesNotContain(picked, Story(game).Choices);
    }

    // From the title screen: New Game, clicked or pressed.
    private Game NewGame(Project project, Hands hands)
    {
        var game = Start(project, Project.DefaultSceneName);
        switch (hands)
        {
            case Hands.Mouse:
                Click(game, At(game.Find("New Game")!), after: 0.8);
                break;
            case Hands.Keys:
                Press(game, Key.Enter, after: 0.8);
                break;
            default:
                // A controller picks the top button straight away.
                Pad(game, GamepadButton.A, after: 0.8);
                break;
        }

        Assert.Equal(VisualNovelTemplate.StoryScene, game.SceneName);
        _input.MouseWorld = new Vector2(0, 1.5f);
        return game;
    }

    [Fact]
    public async Task TheTitleScreen_StartsANewGame_WithContinueFadedUntilAGameIsSaved()
    {
        var project = await CreateProject();
        var game = Start(project, Project.DefaultSceneName);
        Assert.Equal(["New Game", "Continue", "Load", "Settings", "Quit"],
            game.Scene.Entities.Where(e => e.Parent is null && e.GetComponent<Button>() is not null).Select(e => e.Name));
        Assert.False(game.Find("Continue")!.GetComponent<Button>()!.IsUsable);
        Assert.Equal("Happy", game.Find("Comet")!.GetComponent<Animation>()!.Clip);

        // Load and Settings open their screens, and Esc closes them.
        Click(game, At(game.Find("Load")!));
        Assert.Equal(ReadingMenus.LoadScreen, game.TopPanel?.Entity!.Name);
        Assert.All(game.Scene.All<SaveSlot>(), s => Assert.False(s.Entity!.GetComponent<Button>()!.IsUsable));
        Press(game, Key.Escape);
        Assert.Null(game.TopPanel);
        Click(game, At(game.Find("Settings")!));
        Assert.Equal(ReadingMenus.Settings, game.TopPanel?.Entity!.Name);
        Press(game, Key.Escape);

        // New Game: the story starts by itself, in the café.
        Click(game, At(game.Find("New Game")!), after: 0.8);
        Assert.Equal(VisualNovelTemplate.StoryScene, game.SceneName);
        Assert.True(Story(game).IsTalking);
        Assert.Equal("Cafe", StageOf(game).Background);
        Assert.Equal(VisualNovelTemplate.Script[1], Story(game).Said);
        Assert.Empty(_warnings);
    }

    [Theory]
    [InlineData(0, 0, "Home Ending", Hands.Mouse)]
    [InlineData(1, 0, "Loaf Ending", Hands.Keys)]
    [InlineData(0, 1, "Stay Ending", Hands.Gamepad)]
    [InlineData(1, 1, "Stay Ending", Hands.Mouse)]
    public async Task EachEnding_IsReachedByItsChoices(int warmUp, int sendOff, string ending, Hands hands)
    {
        var project = await CreateProject();
        var game = NewGame(project, hands);
        var stage = StageOf(game);

        // The café: June, Comet in the flour, then Felix running in; the first choice is how to warm Comet up.
        ReadOn(game, hands);
        Assert.Equal(["Make it a mug of hot cocoa", "Tuck it into the warm bread oven"], Story(game).Choices);
        Assert.Equal(("Cafe", 3), (stage.Background, stage.OnStage));
        Assert.Equal(("Center", "Happy"), (Cast(game, "Felix").Position, Cast(game, "Felix").Expression));
        Assert.Equal("Left", Cast(game, "June").Position);
        Choose(game, warmUp, hands);
        Assert.Equal(warmUp == 0, game.HasFlag(VisualNovelTemplate.CocoaFlag));
        Assert.Equal(warmUp == 1, game.HasFlag(VisualNovelTemplate.OvenFlag));

        // The roof: send Comet home, or ask it to stay.
        ReadOn(game, hands);
        Assert.Equal("Roof", stage.Background);
        Assert.Equal(("Center", "Sad"), (Cast(game, "Comet").Position, Cast(game, "Comet").Expression));
        Assert.Equal("Sad", game.Find("Comet")!.GetComponent<Animation>()!.Clip);
        Choose(game, sendOff, hands);
        ReadOn(game, hands);
        Run(game, 0.5);

        Assert.Equal(ending, Ended(game));
        Assert.True(Story(game).IsDone);
        Assert.True(game.IsPaused);
        Assert.Equal(sendOff == 0 ? "Dawn" : "Cafe", stage.Background);
        // Who's on stage at the end: everyone staying, or June and Felix watching Comet go.
        Assert.Equal(sendOff == 0 ? 2 : 3, stage.OnStage);
        Assert.Empty(_warnings);

        // Play Again starts the story over with neither flag set; Title goes back to the title screen.
        Press(game, Key.Enter, after: 0.8);
        Assert.Equal(VisualNovelTemplate.StoryScene, game.SceneName);
        Assert.False(game.HasFlag(VisualNovelTemplate.CocoaFlag) || game.HasFlag(VisualNovelTemplate.OvenFlag));
        Assert.Equal(VisualNovelTemplate.Script[1], Story(game).Said);
    }

    [Fact]
    public async Task SavedMidStory_ALoadPutsTheStageTheLineAndTheFlagsBack_FromThePauseMenuOrTheTitlesContinue()
    {
        var project = await CreateProject();
        var game = NewGame(project, Hands.Keys);
        ReadOn(game, Hands.Keys);
        Choose(game, 1, Hands.Keys);
        // On the roof, Comet sad at the thought of leaving.
        for (var i = 0; i < 40 && Story(game).Said?.StartsWith("It's very far.", StringComparison.Ordinal) != true; i++)
            Press(game, Key.Space);
        var said = Story(game).Said;
        var line = Story(game).Line;
        Assert.Equal("It's very far. And it's nice down here, with the warm bread and the both of you.", said);

        // Saved from the pause menu with the keys: Esc, then Save, then the first slot.
        Press(game, Key.Escape);
        Assert.Equal("Paused", game.TopPanel?.Entity!.Name);
        Press(game, Key.Down);
        Press(game, Key.Enter);
        Assert.Equal(ReadingMenus.SaveScreen, game.TopPanel?.Entity!.Name);
        Press(game, Key.Enter);
        Assert.Equal("Comet: " + said, game.SavedGameIn("1")?.Line);
        Press(game, Key.Escape);
        Press(game, Key.Escape);
        Assert.Null(game.TopPanel);

        // Read on to the end, staying: the café again, everyone on stage.
        Press(game, Key.Space);
        Press(game, Key.Space);
        Choose(game, 1, Hands.Keys);
        ReadOn(game, Hands.Keys);
        Assert.Equal("Stay Ending", Ended(game));

        // Loading slot 1 from the load screen: the roof, the line, Comet sad in the middle, the Oven flag.
        game.ClosePanel(game.TopPanel);
        game.OpenPanel(ReadingMenus.LoadScreen);
        Run(game, 0.1);
        Click(game, At(game.Find(ReadingMenus.LoadScreen)!.Children.Single(c => c.Name == "Slot 1")), after: 0.8);
        AssertBack(game);

        // A New Game clears the flags; the title's Continue loads the latest save, as it was.
        Press(game, Key.Escape);
        // (The arrows go to the pause menu, not the choices under it.)
        for (var i = 0; i < 4; i++)
            Press(game, Key.Down);
        Assert.Equal("Title", game.Focused?.Entity!.Name);
        Press(game, Key.Enter, after: 0.8);
        Assert.Equal(Project.DefaultSceneName, game.SceneName);
        Press(game, Key.Enter, after: 0.8);
        Assert.False(game.HasFlag(VisualNovelTemplate.OvenFlag));
        Assert.Equal("Cafe", StageOf(game).Background);
        game.LoadScene(Project.DefaultSceneName);
        Run(game, 0.2);
        var resume = game.Find("Continue")!.GetComponent<Button>()!;
        Assert.True(resume.IsUsable);
        Click(game, At(resume.Entity!), after: 0.8);
        AssertBack(game);
        Assert.Empty(_warnings);

        void AssertBack(Game loaded)
        {
            Assert.Equal(VisualNovelTemplate.StoryScene, loaded.SceneName);
            Assert.Null(loaded.TopPanel);
            Assert.Equal(said, Story(loaded).Said);
            Assert.Equal(line, Story(loaded).Line);
            Assert.Equal("Roof", StageOf(loaded).Background);
            Assert.Equal(3, StageOf(loaded).OnStage);
            Assert.Equal(("Center", "Sad"), (Cast(loaded, "Comet").Position, Cast(loaded, "Comet").Expression));
            Assert.True(loaded.HasFlag(VisualNovelTemplate.OvenFlag));
            Assert.False(loaded.HasFlag(VisualNovelTemplate.CocoaFlag));
            Assert.Equal(["\"Go on home. The sky needs you.\"", "\"You could always stay here with us.\""], ReadToChoices(loaded));
        }

        IReadOnlyList<string> ReadToChoices(Game loaded)
        {
            Press(loaded, Key.Space);
            Press(loaded, Key.Space);
            return Story(loaded).Choices;
        }
    }

    [Fact]
    public async Task Skip_GoesThroughWhatWasReadBefore_AndStopsAtTheFirstChoice()
    {
        var project = await CreateProject();
        var game = NewGame(project, Hands.Mouse);

        // Nothing's been read yet: Skip stops at once, on the first line.
        Press(game, Key.LeftControl);
        Assert.Equal(1, Story(game).Line);
        ReadOn(game, Hands.Mouse);
        var first = Story(game).Line;
        Assert.NotEmpty(Story(game).Choices);

        // Read again, from the start: holding Ctrl skips to the first choice, and stops there.
        game.LoadScene(VisualNovelTemplate.StoryScene);
        Run(game, 0.2);
        Assert.Equal(1, Story(game).Line);
        _input.SetKey(Key.LeftControl, true);
        Run(game, 3);
        _input.SetKey(Key.LeftControl, false);
        Run(game, Frame);
        Assert.Equal(first, Story(game).Line);
        Assert.NotEmpty(Story(game).Choices);
        Assert.False(game.Skipping);
        Assert.Equal(("Cafe", 3), (StageOf(game).Background, StageOf(game).OnStage));

        // The quick menu's Skip does the same, and stops at the first line not read yet.
        Choose(game, 0, Hands.Mouse);
        Click(game, At(game.Find("Skip")!));
        Run(game, 1);
        Assert.False(game.Skipping);
        Assert.Equal("Hot cocoa, extra marshmallows. Do stars like marshmallows?", Story(game).Said);
        Assert.Empty(_warnings);
    }

    [Fact]
    public async Task TheStory_SaysOnlyNamesTheStageHas_AndItsPicturesAreThere()
    {
        var project = await CreateProject();
        foreach (var file in VisualNovelArt.Files().Keys)
            Assert.True(File.Exists(Path.Combine(project.AssetsPath, file)), file);
        var game = Start(project, VisualNovelTemplate.StoryScene);
        var stage = StageOf(game);
        Assert.Equal(["Cafe", "Roof", "Dawn"], stage.Backgrounds.Select(b => b.Split(':')[0]));
        Assert.Equal(VisualNovelArt.CometFaces, game.Find("Comet")!.GetComponent<Animation>()!.ClipList.Select(c => c.Name));
        Assert.Equal(4, Cast(game, "June").Expressions.Count);
        Assert.Equal(4, Cast(game, "Felix").Expressions.Count);

        // Every way through, the keys reading on, and nothing the stage doesn't know is named.
        foreach (var (warm, send) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
        {
            game.LoadScene(VisualNovelTemplate.StoryScene);
            Run(game, 0.2);
            ReadOn(game, Hands.Keys);
            Choose(game, warm, Hands.Keys);
            ReadOn(game, Hands.Keys);
            Choose(game, send, Hands.Keys);
            ReadOn(game, Hands.Keys);
            Assert.NotNull(Ended(game));
        }

        Assert.Empty(_warnings);
    }

    [Fact]
    public async Task Exports_ForTheDesktopAndTheWeb_CarryTheStoryAndItsPictures()
    {
        var project = await CreateProject();
        string[] wanted = ["Assets/Scenes/Story.scene", "Assets/" + VisualNovelArt.Cafe, "Assets/" + VisualNovelArt.Roof, "Assets/" + VisualNovelArt.CometSheet,
            "Assets/" + VisualNovelArt.Face("June", "Happy"), "Assets/Music/theme.wav"];

        // The desktop: one .exe, holding the game's files, which tells the story.
        var player = Path.Combine(_root, "player", "Griff.Player.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(player)!);
        File.WriteAllText(player, "player exe");
        var desktop = GameExporter.Export(project, player, Path.Combine(_root, "out"));
        Assert.True(desktop.Success, desktop.Error);
        var extracted = GamePackage.Extract(desktop.Executable!, Path.Combine(_root, "cache"), "Starfall")!;
        Assert.All(wanted, file => Assert.True(File.Exists(Path.Combine(extracted, file)), file));
        var loaded = await GameLoader.LoadAsync(extracted);
        Assert.Empty(loaded.Problems);
        var game = (loaded with { Scene = loaded.LoadScene(VisualNovelTemplate.StoryScene), SceneName = VisualNovelTemplate.StoryScene }).NewGame();
        for (var i = 0; i < 30; i++)
            game.Tick(1 / 60f);
        Assert.Equal(VisualNovelTemplate.Script[1], Story(game).Said);

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
}
