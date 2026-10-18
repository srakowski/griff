using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// A Show When shows its entity only while its flag holds, and a flag named "run." lasts one run of the game: a HUD that
/// reveals its tools as goals are met, and starts hidden again in a New Game or the next time the game starts.
/// </summary>
public sealed class ShowWhenTests
{
    private const double Frame = 1 / 60.0;
    private readonly InputState _input = new();

    public ShowWhenTests() =>
        _input.UseControls(new Dictionary<string, IReadOnlyList<InputBinding>> { ["OpenContracts"] = [Key.C] });

    // A HUD with a Contracts button shown while run.Contracts is set, clicked by the OpenContracts action.
    private static Scene Hud()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 10;
        scene.Add(camera);
        var entity = new Entity("Contracts");
        var button = entity.AddComponent<Button>();
        button.ShortcutAction = "OpenContracts";
        entity.AddComponent<ShowWhen>().NeedsFlag = "run.Contracts";
        scene.Add(entity);
        return scene;
    }

    private void Tick(Game game)
    {
        game.Tick(Frame);
        _input.EndFrame();
    }

    private void Press(Game game, Key key)
    {
        _input.SetKey(key, true);
        Tick(game);
        _input.SetKey(key, false);
        Tick(game);
    }

    private static Entity Contracts(Game game) => game.Find("Contracts")!;

    [Fact]
    public void AButton_IsHiddenAndItsShortcutDoesNothing_UntilItsFlagIsSet_AndHiddenAgainWhenItsCleared()
    {
        var game = new Game(Hud(), _input);
        var clicks = 0;
        Contracts(game).GetComponent<Button>()!.Clicked += _ => clicks++;

        Tick(game);
        Assert.False(Contracts(game).IsActive);
        Press(game, Key.C);
        Assert.Equal(0, clicks);

        game.SetFlag("run.Contracts");
        Tick(game);
        Assert.True(Contracts(game).IsActive);
        Press(game, Key.C);
        Assert.Equal(1, clicks);

        game.SetFlag("run.Contracts", on: false);
        Tick(game);
        Assert.False(Contracts(game).IsActive);
    }

    [Fact]
    public void NotAFlag_ShowsItWhileTheFlagIsntSet_AndEmptyLeavesTheEntityAlone()
    {
        var scene = new Scene();
        var tip = new Entity("Tip");
        tip.AddComponent<ShowWhen>().NeedsFlag = "not SeenTip";
        scene.Add(tip);
        var hidden = new Entity("Hidden") { IsEnabled = false };
        hidden.AddComponent<ShowWhen>();
        scene.Add(hidden);
        var game = new Game(scene, _input);

        Tick(game);
        Assert.True(tip.IsActive);
        Assert.False(hidden.IsEnabled);

        game.SetFlag("seentip"); // capitals don't matter
        Tick(game);
        Assert.False(tip.IsActive);
    }

    [Fact]
    public void ANewGame_StartsWithItHiddenAgain()
    {
        var game = new Game(Hud(), _input) { SceneLoader = _ => Hud() };
        game.SetFlag("run.Contracts");
        game.SetFlag("Unlocked");
        Tick(game);
        Assert.True(Contracts(game).IsActive);

        game.NewGame("Main");
        for (var i = 0; i < 3; i++)
            Tick(game);

        Assert.False(game.HasFlag("run.Contracts"));
        Assert.False(game.HasFlag("Unlocked"));
        Assert.False(Contracts(game).IsActive);
    }

    [Fact]
    public void RunFlags_AreKeptWhileTheGameRuns_ButNotWritten_SoTheNextRunStartsWithoutThem()
    {
        var path = Path.Combine(Path.GetTempPath(), $"griff-run-flags-{Guid.NewGuid():N}", "save.json");
        try
        {
            var game = new Game(Hud(), _input) { SaveData = new SaveData(path) };
            game.SetFlag("run.Contracts");
            game.SetFlag("SeenIntro");
            game.SaveData.Set("Run.money", 50);
            Tick(game);
            Assert.True(game.HasFlag("run.Contracts"));
            Assert.Equal(50, game.SaveData.GetInt("Run.money"));
            game.SaveData.Flush();

            var written = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            Assert.DoesNotContain(written, p => SaveData.LastsOneRun(p.Key));
            Assert.True(written.ContainsKey("SeenIntro"));
            var next = new Game(Hud(), _input) { SaveData = new SaveData(path) };
            Tick(next);
            Assert.False(next.HasFlag("run.Contracts"));
            Assert.True(next.HasFlag("SeenIntro"));
            Assert.False(Contracts(next).IsActive);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void RunFlags_AreKeptInASavedGame_AndLoadingItPutsThemBack()
    {
        var game = new Game(Hud(), _input) { SceneLoader = _ => Hud() };
        Tick(game);
        game.SetFlag("run.Contracts");
        Assert.True(game.SaveGame("1"));

        game.SetFlag("run.Contracts", on: false);
        Assert.True(game.LoadGame("1"));
        for (var i = 0; i < 3; i++)
            Tick(game);

        Assert.True(game.HasFlag("run.Contracts"));
        Assert.True(Contracts(game).IsActive);
    }

    [Fact]
    public void LastsOneRun_IsForNamesStartingRunDot()
    {
        Assert.True(SaveData.LastsOneRun("run.Contracts"));
        Assert.True(SaveData.LastsOneRun("Run.Contracts"));
        Assert.False(SaveData.LastsOneRun("runner"));
        Assert.False(SaveData.LastsOneRun("Contracts"));
    }

    [Fact]
    public void Flags_JoinedWithOr_ShowItWhileAnyHolds()
    {
        var game = new Game(Hud(), _input);
        var show = Contracts(game).GetComponent<ShowWhen>()!;
        show.NeedsFlag = "run.Contracts OR run.Work or not run.Started";
        Tick(game);
        // Not started yet: the last holds.
        Assert.True(Contracts(game).IsActive);

        game.SetFlag("run.Started");
        Tick(game);
        Assert.False(Contracts(game).IsActive);

        game.SetFlag("run.Work");
        Tick(game);
        Assert.True(Contracts(game).IsActive);
        Assert.Equal("Shown only while run.Contracts is set or run.Work is set or run.Started isn't set.", show.SetupNote());
        Assert.True(game.FlagHolds("x or run.Work"));
        Assert.False(game.FlagHolds("x or y"));
        // "or" inside a name isn't a join.
        Assert.False(game.FlagHolds("Ordered"));
    }
}