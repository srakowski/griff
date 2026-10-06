using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

public class SceneFlowTests
{
    private const double Frame = 1 / 60.0;

    private static Scene Title()
    {
        var scene = new Scene();
        scene.Add(new Entity("Logo"));
        return scene;
    }

    [Fact]
    public void ReloadScene_StartsTheCurrentSceneOver()
    {
        var loads = new List<string>();
        var game = new Game(Title(), new InputState())
        {
            SceneName = "Title",
            SceneLoader = name =>
            {
                loads.Add(name);
                return Title();
            },
        };
        var before = game.Scene;

        game.ReloadScene();
        game.Tick(Frame);

        Assert.Equal(["Title"], loads);
        Assert.NotSame(before, game.Scene);
        Assert.Equal("Title", game.SceneName);
    }

    [Fact]
    public void ReloadScene_WithoutAName_Throws()
    {
        var game = new Game(Title(), new InputState()) { SceneLoader = _ => Title() };

        Assert.Throws<InvalidOperationException>(game.ReloadScene);
    }

    [Theory]
    [InlineData("Level2", "Level2")]
    [InlineData("Scenes/Level2.scene", "Level2")]
    [InlineData("Scenes\\Boss.Fight.scene", "Boss.Fight")]
    public void LoadScene_RemembersTheScenesName(string requested, string expected)
    {
        var game = new Game(Title(), new InputState()) { SceneLoader = _ => Title() };

        game.LoadScene(requested);
        game.Tick(Frame);

        Assert.Equal(expected, game.SceneName);
    }

    [Fact]
    public void KeepBetweenScenes_CarriesTheEntityAcross_StillRunning()
    {
        var title = Title();
        var music = new Entity("Music");
        var probe = music.AddComponent<GameTests.Probe>();
        var child = new Entity("Child");
        title.Add(music);
        title.Add(child, music);

        var level = new Scene();
        var game = new Game(title, new InputState()) { SceneLoader = _ => level };
        game.Tick(Frame);
        var timerFired = false;
        game.After(0.05f, () => timerFired = true, owner: music);

        game.KeepBetweenScenes(music);
        game.LoadScene("Level");
        game.Tick(Frame);
        Run(game, 0.1);

        Assert.Same(level, music.Scene);
        Assert.Same(level, child.Scene);
        Assert.Same(music, child.Parent);
        Assert.DoesNotContain("OnDestroy", probe.Calls);
        Assert.Equal(1, probe.Calls.Count(c => c == "Start"));
        Assert.True(probe.Calls.Count(c => c == "Update") > 3, "keeps updating in the new scene");
        Assert.True(timerFired, "its timers keep running");
        Assert.Null(title.Find(music.Id));
    }

    [Fact]
    public void KeepBetweenScenes_AKeptChild_BecomesTopLevel()
    {
        var title = Title();
        var parent = new Entity("Parent");
        var score = new Entity("Score");
        title.Add(parent);
        title.Add(score, parent);
        var level = new Scene();
        var game = new Game(title, new InputState()) { SceneLoader = _ => level };

        game.KeepBetweenScenes(score);
        game.LoadScene("Level");
        game.Tick(Frame);

        Assert.Same(level, score.Scene);
        Assert.Null(score.Parent);
        Assert.Contains(score, level.Roots);
    }

    [Fact]
    public void KeepBetweenScenes_ReturningToTheSceneItCameFrom_DoesNotDuplicateIt()
    {
        // Every load is a fresh copy of the same file: the same entity ids each time.
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var saved = Title();
        saved.Add(new Entity("Music"));
        var file = serializer.SaveToString(saved);
        var musicId = saved.Entities.Single(e => e.Name == "Music").Id;

        var game = new Game(serializer.LoadFromString(file), new InputState())
        {
            SceneLoader = name => name == "Title" ? serializer.LoadFromString(file) : new Scene(),
        };
        var music = game.Scene.Find(musicId)!;
        game.KeepBetweenScenes(music);

        game.LoadScene("Level");
        game.Tick(Frame);
        game.LoadScene("Title");
        game.Tick(Frame);

        Assert.Same(music, Assert.Single(game.Scene.Entities, e => e.Id == musicId));
        Assert.Same(game.Scene, music.Scene);
    }

    [Fact]
    public void KeepBetweenScenes_ADestroyedEntity_IsForgotten()
    {
        var title = Title();
        var music = new Entity("Music");
        title.Add(music);
        var level = new Scene();
        var game = new Game(title, new InputState()) { SceneLoader = _ => level };

        game.KeepBetweenScenes(music);
        game.Destroy(music);
        game.Tick(Frame);
        game.LoadScene("Level");
        game.Tick(Frame);

        Assert.Null(music.Scene);
        Assert.Empty(level.Entities);
    }

    [Fact]
    public void FindAll_FindsEveryEntityWithTheName_AndComponentsByType()
    {
        var scene = new Scene();
        var a = new Entity("Enemy");
        var b = new Entity("Enemy");
        var other = new Entity("Player");
        a.AddComponent<GameTests.Probe>();
        b.AddComponent<GameTests.Probe>();
        scene.Add(a);
        scene.Add(other);
        scene.Add(b, other);
        var game = new Game(scene, new InputState());

        Assert.Equal([a, b], game.FindAll("Enemy"));
        Assert.Empty(game.FindAll("Nobody"));
        Assert.Equal([a, b], game.FindAll<GameTests.Probe>().Select(p => p.Entity));
        Assert.Same(a, game.Find<GameTests.Probe>()!.Entity);
        Assert.Null(game.Find<Camera>());
    }

    private static void Run(Game game, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Frame)
            game.Tick(Frame);
    }
}
