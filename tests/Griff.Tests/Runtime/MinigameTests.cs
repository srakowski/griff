using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// Minigames: another scene played on top of this one, under an entity of its own beside the rest of the world, with the
/// screen and the player's input, its result sent back as a message and outputs; rewinds and saved games take it in.
/// </summary>
public class MinigameTests
{
    private const double Frame = 1 / 60.0;
    private readonly InputState _input = new();

    // A room with the player (who moves with the arrow keys), a camera, a counter of coins and an arcade cabinet whose
    // trigger plays the "Arcade" minigame on Play.
    private static Scene Room()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>();
        scene.Add(camera);
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(2, 0, 0);
        player.AddComponent<Sprite>();
        player.AddComponent<TopDownMovement>();
        scene.Add(player);
        var cabinet = new Entity("Cabinet");
        cabinet.Transform.Position = new Vector3(5, 0, 0);
        cabinet.AddComponent<Sprite>();
        var trigger = cabinet.AddComponent<Trigger>();
        (trigger.Message, trigger.Do, trigger.With) = ("Play", "Play Minigame", "Arcade");
        scene.Add(cabinet);
        var mover = new Entity("Clock");
        mover.AddComponent<Counter>().Message = "Tick";
        scene.Add(mover);
        return scene;
    }

    // The minigame: a camera following its own "Player", a score counter and triggers that win or lose it.
    private static Scene Arcade()
    {
        var scene = new Scene();
        var camera = new Entity("Arcade Camera");
        camera.AddComponent<Camera>();
        camera.AddComponent<Follow>().Target = "Player";
        scene.Add(camera);
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(1, 1, 0);
        player.AddComponent<Sprite>();
        player.AddComponent<TopDownMovement>();
        scene.Add(player);
        var score = new Entity("Score");
        score.AddComponent<Counter>().Message = "Points";
        scene.Add(score);
        var rules = new Entity("Rules");
        var win = rules.AddComponent<Trigger>();
        (win.Message, win.Do) = ("Beat It", "Win Minigame");
        var lose = rules.AddComponent<Trigger>();
        (lose.Message, lose.Do, lose.With) = ("Give Up", "Lose Minigame", "3");
        scene.Add(rules);
        return scene;
    }

    private Game Start(Scene? room = null)
    {
        var game = new Game(room ?? Room(), _input)
        {
            SceneName = "Room",
            SceneLoader = name => name switch
            {
                "Arcade" => Arcade(),
                "Room" => Room(),
                _ => throw new FileNotFoundException(name),
            },
        };
        game.Tick(Frame);
        return game;
    }

    private void Run(Game game, int frames = 2)
    {
        for (var i = 0; i < frames; i++)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private static Entity Named(Game game, string name) => game.Scene.Entities.First(e => e.Name == name);

    [Fact]
    public void PlayMinigame_LoadsItBesideTheWorld_PausesTheRest_AndTakesTheInput()
    {
        var game = Start();
        var player = Named(game, "Player");

        game.Send("Play");
        Run(game);

        var root = game.Minigame!;
        Assert.Equal("Minigame: Arcade", root.Name);
        Assert.Equal("Arcade", root.GetComponent<MinigameRoot>()!.Scene);
        Assert.Contains(root, game.Scene.Roots);
        var inside = root.Children.Select(c => c.Name).ToList();
        Assert.Equal(["Arcade Camera", "Player", "Score", "Rules"], inside);

        // Beside everything else: past the cabinet, the furthest thing in the room.
        Assert.True(root.Transform.Position.X > 5 + 32, $"The minigame is at {root.Transform.Position.X}.");
        Assert.True(player.IsPaused);
        Assert.False(root.IsPaused);
        Assert.False(player.GetComponent<TopDownMovement>()!.Controllable);
        Assert.True(root.Children[1].GetComponent<TopDownMovement>()!.Controllable);

        // Its camera has the screen, following its own player, not the room's.
        Assert.Equal("Arcade Camera", SceneRenderer.FindCamera(game.Scene)!.Name);
        Assert.Same(root.Children[1], root.Children[0].GetComponent<Follow>()!.TargetEntity);
    }

    [Fact]
    public void WinningIt_SendsTheMessageWithItsScore_AndGivesEverythingBack()
    {
        var game = Start();
        var player = Named(game, "Player");
        var heard = new List<(string, object?)>();
        game.On(MinigameRoot.DefaultWonMessage, data => heard.Add(("won", data)));
        game.On(MinigameRoot.DefaultLostMessage, data => heard.Add(("lost", data)));

        game.Send("Play");
        Run(game);
        game.Send("Points", 40);
        game.Send("Beat It");
        Run(game);

        Assert.Equal([("won", (object?)40)], heard);
        Assert.Null(game.Minigame);
        Assert.DoesNotContain(game.Scene.Entities, e => e.Name.StartsWith("Minigame", StringComparison.Ordinal) || e.Name == "Rules");
        Assert.False(player.IsPaused);
        Assert.True(player.GetComponent<TopDownMovement>()!.Controllable);
        Assert.Equal("Camera", SceneRenderer.FindCamera(game.Scene)!.Name);

        // And again: lost with the score the trigger gives.
        game.Send("Play");
        Run(game);
        game.Send("Give Up");
        Run(game);
        Assert.Equal(("lost", (object?)3), heard[^1]);
    }

    [Fact]
    public void AMinigameComponent_SaysHow_AndShowsHowItWentAsOutputs()
    {
        var room = Room();
        var cabinet = room.Entities.First(e => e.Name == "Cabinet");
        var minigame = cabinet.AddComponent<Minigame>();
        (minigame.Scene, minigame.PauseBelow, minigame.Fade, minigame.WonMessage) = ("Arcade", false, 0, "Cabinet Won");
        cabinet.GetComponent<Trigger>()!.With = null;
        var game = Start(room);
        var won = 0;
        game.On("Cabinet Won", _ => won++);

        game.Send("Play");
        Run(game);
        Assert.True(minigame.Playing);
        Assert.False(Named(game, "Clock").IsPaused);
        game.Send("Tick");
        Assert.Equal(1, Named(game, "Clock").GetComponent<Counter>()!.Value);

        game.Send("Points", 25);
        game.Send("Beat It");
        Run(game);
        game.Do(cabinet, "Play");
        Run(game);
        game.Send("Points", 10);
        game.Send("Beat It");
        Run(game);

        Assert.Equal(2, won);
        Assert.False(minigame.Playing);
        Assert.Equal((true, 10, 25, 2, 2), (minigame.Won, minigame.Score, minigame.Best, minigame.Plays, minigame.Wins));
        Assert.Equal(25f, ComponentOutputs.Read(cabinet, "Best"));
    }

    [Fact]
    public void WhatsSpawnedInIt_GoesIntoIt_AndWhatsBehindItIsntDrawn()
    {
        var game = Start();
        game.PrefabLoader = _ =>
        {
            var spark = new Entity("Spark");
            spark.AddComponent<Sprite>();
            return spark;
        };
        game.Send("Play");
        Run(game);
        var root = game.Minigame!;
        var at = root.Transform.Position.X + 1;

        var spark = game.Spawn("Spark", new Vector2(at, 2));
        var outside = game.Spawn("Spark", new Vector2(0, 0));

        Assert.Same(root, spark.Parent);
        Assert.Equal(at, spark.Transform.WorldPosition.X, 3);
        Assert.Null(outside.Parent);

        Run(game, 1);
        var names = SceneRenderer.Build(game, 16f / 9).Sprites.Select(d => d.Entity.Name).ToList();
        Assert.Contains("Player", names);
        Assert.DoesNotContain("Cabinet", names);
        Assert.Single(names, n => n == "Spark");
    }

    [Fact]
    public void ItsQuitButton_LosesIt_AndRestart_StartsItOver()
    {
        var game = Start();
        var lost = 0;
        game.On(MinigameRoot.DefaultLostMessage, _ => lost++);
        game.Send("Play");
        Run(game);
        var root = game.Minigame!;
        var score = root.Children.First(c => c.Name == "Score").GetComponent<Counter>()!;
        game.Send("Points", 7);
        Assert.Equal(7, score.Value);

        game.Do(root, "Restart");
        Run(game);
        var again = game.Minigame!;
        Assert.NotSame(root, again);
        Assert.Equal(0, again.Children.First(c => c.Name == "Score").GetComponent<Counter>()!.Value);
        Assert.Equal(0, lost);
        Assert.False(Named(game, "Player").GetComponent<TopDownMovement>()!.Controllable);

        var quit = new Entity("Quit");
        var button = quit.AddComponent<Button>();
        (button.Action, button.Shortcut) = (ButtonAction.Quit, Griff.Input.Key.Q);
        game.Scene.Add(quit, again);
        var quitting = false;
        game.QuitRequested += (_, _) => quitting = true;
        _input.SetKey(Griff.Input.Key.Q, true);
        Run(game, 1);
        _input.SetKey(Griff.Input.Key.Q, false);
        Run(game);
        Assert.Equal(1, lost);
        Assert.False(quitting);
        Assert.Null(game.Minigame);
    }

    [Fact]
    public void ARewind_GoesBackIntoIt_OrToBeforeItOpened()
    {
        var game = Start();
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var timeline = new Timeline(serializer, framesPerSnapshot: 1);
        timeline.Record(game.Scene, game.Time.Total, force: true);
        game.Send("Play");
        Run(game);
        game.Send("Points", 12);
        Run(game);
        timeline.Record(game.Scene, game.Time.Total, force: true);
        timeline.Complete();

        // Back into the middle of it: it's there, with the screen, the room paused and the player's input taken.
        var restored = timeline.Restore(1);
        var input = new InputState();
        var again = new Game(restored, input) { SceneName = "Room", SceneLoader = _ => Arcade() };
        again.Tick(Frame);
        Assert.NotNull(again.Minigame);
        Assert.Equal("Arcade Camera", SceneRenderer.FindCamera(restored)!.Name);
        Assert.True(restored.Entities.First(e => e.Name == "Cabinet").IsPaused);
        var won = new List<object?>();
        again.On(MinigameRoot.DefaultWonMessage, won.Add);
        again.Send("Beat It");
        again.Tick(Frame);
        Assert.Equal([(object?)12], won);
        Assert.True(restored.Entities.First(e => e.Name == "Player" && e.Parent is null).GetComponent<TopDownMovement>()!.Controllable);

        // Back to before it: no minigame.
        var before = timeline.Restore(0);
        Assert.Null(MinigameRoot.Shown(before));
        Assert.Equal("Camera", SceneRenderer.FindCamera(before)!.Name);
    }

    [Fact]
    public void ASavedGame_KeepsTheMinigameBeingPlayed()
    {
        var game = Start();
        game.Send("Play");
        Run(game);
        game.Send("Points", 5);
        Assert.True(game.SaveGame("1"));
        game.Send("Beat It");
        Run(game);
        Assert.Null(game.Minigame);

        Assert.True(game.LoadGame("1"));
        Run(game);

        var root = game.Minigame!;
        Assert.Equal(5, root.Children.First(c => c.Name == "Score").GetComponent<Counter>()!.Value);
        Assert.True(game.Scene.Entities.First(e => e.Name == "Cabinet").IsPaused);
    }

    [Fact]
    public void PlayedOnItsOwn_WinningJustSaysSo()
    {
        var game = new Game(Arcade(), _input);
        var won = 0;
        game.On(MinigameRoot.DefaultWonMessage, _ => won++);
        game.Tick(Frame);

        game.Send("Beat It");

        Assert.Equal(1, won);
        Assert.Null(game.Minigame);
    }

    [Fact]
    public void OnlyOneAtATime_FromTheSameThing()
    {
        var game = Start();
        game.Send("Play");
        Run(game, 1);
        game.Send("Play");
        Run(game, 1);

        Assert.Single(game.Scene.Roots, e => e.GetComponent<MinigameRoot>() is not null);
    }
}
