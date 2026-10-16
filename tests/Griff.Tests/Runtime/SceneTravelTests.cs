using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// Going between scenes: doors that arrive at a named spot, scenes that remember how they were left, entities kept between
/// scenes, and the inventory, health and counts carried from one scene to the next and kept in saved games.
/// </summary>
public class SceneTravelTests
{
    private const double Frame = 1 / 60.0;
    private readonly InputState _input = new();

    // Two rooms joined by doors: the west room's East Door goes to the east room, arriving at its West Door, and back.
    // Each has a player (a body on the Player layer), a crate and a coin counter.
    private static Scene West() => Room("East Door", new Vector2(8, 0), "East", "West Door", new Vector2(-2, 0));

    private static Scene East() => Room("West Door", new Vector2(-8, 0), "West", "East Door", new Vector2(3, 3));

    private static Scene Room(string door, Vector2 doorAt, string goesTo, string arriveAt, Vector2 playerAt)
    {
        var scene = new Scene();
        var exit = Box(door, doorAt, trigger: true);
        var component = exit.AddComponent<Door>();
        (component.Scene, component.ArriveAt, component.Fade) = (goesTo, arriveAt, 0);
        scene.Add(exit);
        var player = Box("Player", playerAt);
        player.GetComponent<BoxCollider>()!.Layer = "Player";
        player.AddComponent<Body>();
        player.AddComponent<TopDownMovement>();
        scene.Add(player);
        scene.Add(Box("Crate", new Vector2(0, -3)));
        var coins = new Entity("Coins");
        coins.AddComponent<Counter>().Message = "Coin";
        scene.Add(coins);
        return scene;

        static Entity Box(string name, Vector2 at, bool trigger = false)
        {
            var entity = new Entity(name);
            entity.Transform.Position = new Vector3(at, 0);
            var collider = entity.AddComponent<BoxCollider>();
            (collider.Size, collider.IsTrigger) = (new Vector2(1, 1), trigger);
            return entity;
        }
    }

    private Game Start(Func<Scene, Scene>? west = null, Func<Scene, Scene>? east = null)
    {
        Scene MakeWest() => west is null ? West() : west(West());
        Scene MakeEast() => east is null ? East() : east(East());
        var game = new Game(MakeWest(), _input)
        {
            SceneName = "West",
            SceneLoader = name => name switch
            {
                "West" => MakeWest(),
                "East" => MakeEast(),
                _ => throw new FileNotFoundException(name),
            },
        };
        game.Physics.Gravity = Vector2.Zero;
        game.Tick(Frame);
        return game;
    }

    private void Run(Game game, int frames = 3)
    {
        for (var i = 0; i < frames; i++)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private static Entity Named(Game game, string name) => game.Scene.Entities.First(e => e.Name == name);

    private static Entity Put(Scene scene, string name)
    {
        var entity = new Entity(name);
        scene.Add(entity);
        return entity;
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    // Walks the player into the door called `door` and lets the game go through it.
    private void GoThrough(Game game, string door)
    {
        var player = Named(game, "Player");
        player.Transform.Teleport(Named(game, door).Transform.WorldPosition);
        Run(game);
    }

    [Fact]
    public void ADoor_ArrivesAtTheSpotItNames_AndDoesntGoStraightBack()
    {
        var game = Start();

        GoThrough(game, "East Door");
        Assert.Equal("East", game.SceneName);
        Assert.Equal("West Door", game.ArrivedAt!.Name);
        Assert.Equal(new Vector2(-8, 0), At(Named(game, "Player")), new Vector2Comparer(0.01f));

        // Standing in the door back, it stays put until it steps off and on again.
        Run(game, 10);
        Assert.Equal("East", game.SceneName);
        var player = Named(game, "Player");
        player.Transform.Teleport(new Vector3(0, 0, 0));
        Run(game);
        GoThrough(game, "West Door");
        Assert.Equal("West", game.SceneName);
        Assert.Equal(new Vector2(8, 0), At(Named(game, "Player")), new Vector2Comparer(0.01f));
    }

    [Fact]
    public void LoadScene_CanArriveAtASpot_FromAScript()
    {
        var game = Start();

        game.LoadScene("East", 0, null, arriveAt: "West Door");
        Run(game, 1);

        Assert.Equal(new Vector2(-8, 0), At(Named(game, "Player")), new Vector2Comparer(0.01f));
    }

    [Fact]
    public void ARememberedScene_IsAsItWasLeft_WhenComingBack()
    {
        static Scene Remembering(Scene scene)
        {
            Put(scene, "Memory").AddComponent<RememberScene>();
            return scene;
        }

        var game = Start(Remembering);
        Named(game, "Crate").Transform.Teleport(new Vector3(4, -4, 0));
        game.Send("Coin", 3);

        GoThrough(game, "East Door");
        Assert.True(game.Remembers("West"));
        Assert.False(game.Remembers("East"));
        Named(game, "Crate").Transform.Teleport(new Vector3(-5, -5, 0));
        Named(game, "Player").Transform.Teleport(new Vector3(0, 0, 0));
        Run(game);
        GoThrough(game, "West Door");

        // The west room as it was left (the player arriving at the door), the east room afresh next time.
        Assert.Equal("West", game.SceneName);
        Assert.Equal(new Vector2(4, -4), At(Named(game, "Crate")), new Vector2Comparer(0.01f));
        Assert.Equal(3, Named(game, "Coins").GetComponent<Counter>()!.Value);
        Assert.Equal(new Vector2(8, 0), At(Named(game, "Player")), new Vector2Comparer(0.01f));
        Assert.Single(game.Scene.Entities, e => e.Name == "Player");
        Named(game, "Player").Transform.Teleport(new Vector3(0, 0, 0));
        Run(game);
        GoThrough(game, "East Door");
        Assert.Equal(new Vector2(0, -3), At(Named(game, "Crate")), new Vector2Comparer(0.01f));
    }

    [Fact]
    public void WhatItForgets_AndWhatsOnItsWayOut_ComeBackAsTheFileHasThem()
    {
        static Scene Remembering(Scene scene)
        {
            Put(scene, "Memory").AddComponent<RememberScene>().Forget = "Crate";
            return scene;
        }

        var game = Start(Remembering);
        Named(game, "Crate").Transform.Teleport(new Vector3(4, -4, 0));
        var spark = new Entity("Spark");
        spark.AddComponent<Lifetime>().Seconds = 10;
        game.Spawn(spark);
        Run(game, 1);

        GoThrough(game, "East Door");
        GoThrough(game, "West Door");

        Assert.Equal(new Vector2(0, -3), At(Named(game, "Crate")), new Vector2Comparer(0.01f));
        Assert.DoesNotContain(game.Scene.Entities, e => e.Name == "Spark");
    }

    [Fact]
    public void Restarting_NewGame_AndForgetScene_StartItAfresh()
    {
        static Scene Remembering(Scene scene)
        {
            Put(scene, "Memory").AddComponent<RememberScene>();
            return scene;
        }

        var game = Start(Remembering, Remembering);
        Named(game, "Crate").Transform.Teleport(new Vector3(4, -4, 0));
        GoThrough(game, "East Door");
        Assert.True(game.Remembers("West"));

        // A trigger's Forget Scene.
        var trigger = Put(game.Scene, "Reset").AddComponent<Trigger>();
        (trigger.Message, trigger.Do, trigger.With) = ("Reset", "Forget Scene", "West");
        game.Send("Reset");
        Assert.False(game.Remembers("West"));

        // Restarting this room doesn't come back to how it was.
        Named(game, "Crate").Transform.Teleport(new Vector3(-5, -5, 0));
        game.ReloadScene();
        Run(game, 1);
        Assert.Equal(new Vector2(0, -3), At(Named(game, "Crate")), new Vector2Comparer(0.01f));
        Assert.False(game.Remembers("East"));

        // New Game forgets every room.
        Named(game, "Player").Transform.Teleport(new Vector3(0, 0, 0));
        Run(game);
        GoThrough(game, "West Door");
        Assert.True(game.Remembers("East"));
        game.NewGame("West");
        Run(game, 1);
        Assert.Empty(game.RememberedScenes);
    }

    [Fact]
    public void KeepBetweenScenes_TakesThePlaceOfTheOneThere_AndArrivesAtTheDoor()
    {
        static Scene Kept(Scene scene)
        {
            var player = scene.Entities.First(e => e.Name == "Player");
            player.AddComponent<KeepBetweenScenes>();
            player.AddComponent<Health>().Max = 5;
            return scene;
        }

        var game = Start(Kept, Kept);
        var player = Named(game, "Player");
        player.GetComponent<Health>()!.Damage(2);

        GoThrough(game, "East Door");

        Assert.Same(player, Assert.Single(game.Scene.Entities, e => e.Name == "Player"));
        Assert.Equal(3, player.GetComponent<Health>()!.Current);
        Assert.Equal(new Vector2(-8, 0), At(player), new Vector2Comparer(0.01f));

        // Without a door to arrive at, it stands where the one it replaced stood.
        game.LoadScene("West");
        Run(game, 1);
        Assert.Same(player, Assert.Single(game.Scene.Entities, e => e.Name == "Player"));
        Assert.Equal(new Vector2(-2, 0), At(player), new Vector2Comparer(0.01f));
    }

    [Fact]
    public void HealthAndCounts_CarryOverByTheirSaveAs()
    {
        static Scene Carrying(Scene scene)
        {
            var player = scene.Entities.First(e => e.Name == "Player");
            var health = player.AddComponent<Health>();
            (health.Max, health.SaveAs) = (5, "Hearts");
            scene.Entities.First(e => e.Name == "Coins").GetComponent<Counter>()!.SaveAs = "Coins";
            return scene;
        }

        var game = Start(Carrying, Carrying);
        Named(game, "Player").GetComponent<Health>()!.Damage(2);
        game.Send("Coin", 4);

        GoThrough(game, "East Door");

        Assert.Equal(3, Named(game, "Player").GetComponent<Health>()!.Current);
        Assert.Equal(4, Named(game, "Coins").GetComponent<Counter>()!.Value);
        Assert.Equal(4, game.SaveData.GetInt("Coins"));
        Assert.Equal(3f, game.SaveData.GetFloat("Hearts"));

        // Saved at zero (it died), it starts full.
        Named(game, "Player").GetComponent<Health>()!.Kill();
        Named(game, "Player").GetComponent<Health>()!.DestroyOnDeath = false;
        game.ReloadScene();
        Run(game, 2);
        Assert.Equal(5, Named(game, "Player").GetComponent<Health>()!.Current);
    }

    [Fact]
    public void ARememberedScene_ReadsWhatItCarriesAgain_AsTheOtherScenesLeftIt()
    {
        static Scene Carrying(Scene scene)
        {
            Put(scene, "Memory").AddComponent<RememberScene>();
            var player = scene.Entities.First(e => e.Name == "Player");
            player.AddComponent<Inventory>().SaveAs = "Items";
            scene.Entities.First(e => e.Name == "Coins").GetComponent<Counter>()!.SaveAs = "Coins";
            return scene;
        }

        var game = Start(Carrying, Carrying);
        game.Send("Coin", 2);
        GoThrough(game, "East Door");
        game.Send("Coin", 5);
        Named(game, "Player").GetComponent<Inventory>()!.Add("Key");
        Run(game, 1);
        GoThrough(game, "West Door");

        Assert.Equal(7, Named(game, "Coins").GetComponent<Counter>()!.Value);
        Assert.True(Named(game, "Player").GetComponent<Inventory>()!.Has("Key"));
    }

    [Fact]
    public void ASavedGame_KeepsTheRememberedScenes_AndWhatsCarried_AsTheyWere()
    {
        static Scene Carrying(Scene scene)
        {
            Put(scene, "Memory").AddComponent<RememberScene>();
            scene.Entities.First(e => e.Name == "Coins").GetComponent<Counter>()!.SaveAs = "Coins";
            return scene;
        }

        var game = Start(Carrying, Carrying);
        Named(game, "Crate").Transform.Teleport(new Vector3(4, -4, 0));
        game.Send("Coin", 2);
        GoThrough(game, "East Door");
        Assert.True(game.SaveGame("1"));

        // Later: more coins, and the west room changed again.
        game.Send("Coin", 10);
        Named(game, "Player").Transform.Teleport(new Vector3(0, 0, 0));
        Run(game);
        GoThrough(game, "West Door");
        Named(game, "Crate").Transform.Teleport(new Vector3(-6, -6, 0));
        Named(game, "Player").Transform.Teleport(new Vector3(0, 0, 0));
        Run(game);
        GoThrough(game, "East Door");
        Assert.Equal(12, game.SaveData.GetInt("Coins"));

        Assert.True(game.LoadGame("1"));
        Run(game, 1);
        Assert.Equal("East", game.SceneName);
        Assert.Equal(2, Named(game, "Coins").GetComponent<Counter>()!.Value);
        Assert.Equal(2, game.SaveData.GetInt("Coins"));
        Named(game, "Player").Transform.Teleport(new Vector3(0, 0, 0));
        Run(game);
        GoThrough(game, "West Door");
        Assert.Equal(new Vector2(4, -4), At(Named(game, "Crate")), new Vector2Comparer(0.01f));
    }

    private sealed class Vector2Comparer(float tolerance) : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector2 obj) => 0;
    }
}
