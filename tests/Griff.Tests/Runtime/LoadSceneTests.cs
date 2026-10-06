using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class LoadSceneTests
{
    [Fact]
    public void LoadScene_SwitchesAtEndOfFrame_AndStartsTheNewScene()
    {
        var menu = new Scene();
        var button = new Entity("Start Button");
        var starter = button.AddComponent<Starter>();
        menu.Add(button);

        var level = new Scene();
        var hero = new Entity("Hero");
        var heroProbe = hero.AddComponent<GameTests.Probe>();
        level.Add(hero);

        var game = new Game(menu, new InputState()) { SceneLoader = name => name == "Level1" ? level : throw new FileNotFoundException() };
        Scene? changed = null;
        game.SceneChanged += (_, scene) => changed = scene;

        game.Tick(0.016);
        Assert.Same(level, game.Scene);
        Assert.Same(level, changed);
        Assert.True(starter.Destroyed);
        Assert.Empty(heroProbe.Calls);

        game.Tick(0.016);
        Assert.Equal(["Start", "Update"], heroProbe.Calls);
    }

    [Fact]
    public void LoadScene_WithoutLoader_Throws()
    {
        var game = new Game(new Scene(), new InputState());

        Assert.Throws<InvalidOperationException>(() => game.LoadScene("Anything"));
    }

    public class Starter : Behavior
    {
        [DoNotSerialize]
        public bool Destroyed { get; private set; }

        protected override void Update() => Game.LoadScene("Level1");

        protected override void OnDestroy() => Destroyed = true;
    }
}
