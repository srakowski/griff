using System.Numerics;
using Griff.Input;
using Griff.Modules.Dungeons;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Templates;

namespace Griff.Tests.Projects;

/// <summary>The Roguelike template: a menu with the deepest floor, and a crypt made anew each game where the hero fights by turns, floor after floor.</summary>
public sealed class RoguelikeTemplateTests : IDisposable
{
    private const double Frame = 1 / 60.0;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-rogue-" + Guid.NewGuid().ToString("N"));
    private readonly InputState _input = new();
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // The template, with the crypt's generator set as the test needs (its seed, or no monsters in the way).
    private async Task<Game> Create(string? scene = null, Action<DungeonGenerator>? setUp = null)
    {
        var project = await Project.CreateAsync(_root, "Crypt", ProjectTemplates.Get("Roguelike"));
        Scene Load(string name)
        {
            var loaded = GameLoader.LoadScene(project, _registry, name);
            foreach (var dungeon in loaded.Entities.SelectMany(e => e.GetComponents<DungeonGenerator>()))
                setUp?.Invoke(dungeon);
            return loaded;
        }

        var game = new Game(Load(scene ?? Project.DefaultSceneName), _input)
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            SceneLoader = Load,
            SceneName = scene ?? Project.DefaultSceneName,
            ViewAspect = 16 / 9f,
        };
        Run(game, 0.1);
        return game;
    }

    private void Run(Game game, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Frame)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Tap(Game game, Key key, double after = 0.25)
    {
        _input.SetKey(key, true);
        Run(game, Frame);
        _input.SetKey(key, false);
        Run(game, after);
    }

    private static string TextOf(Game game, string name) => game.Find(name)!.GetComponent<Text>()!.Value!;

    private static DungeonGenerator Dungeon(Game game) => game.Find("Crypt")!.GetComponent<DungeonGenerator>()!;

    private static (int X, int Y) Cell(Entity entity) => entity.GetComponent<GridMovement>()!.Cell;

    // Steps the hero a cell at a time to a cell, by the arrow keys, along the shortest way there (or until it goes down a floor).
    private void WalkTo(Game game, (int X, int Y) goal)
    {
        var hero = game.Find("Player")!;
        var map = game.Find("Crypt")!.GetComponent<Tilemap>()!;
        var floor = Dungeon(game).Floor;
        foreach (var corner in map.FindCellPath(Cell(hero), goal, diagonal: false))
        {
            for (var steps = 0; Cell(hero) != corner && steps < 80 && Dungeon(game).Floor == floor; steps++)
            {
                var (x, y) = (corner.X - Cell(hero).X, corner.Y - Cell(hero).Y);
                Tap(game, x > 0 ? Key.Right : x < 0 ? Key.Left : y > 0 ? Key.Down : Key.Up);
            }
        }
    }

    // Puts a prefab in the cell next to the hero (right, or another free side), returning it.
    private static Entity NextToHero(Game game, string prefab, out Key toward)
    {
        var hero = game.Find("Player")!;
        var map = game.Find("Crypt")!.GetComponent<Tilemap>()!;
        var cell = Cell(hero);
        foreach (var (dx, dy, key) in new[] { (1, 0, Key.Right), (-1, 0, Key.Left), (0, 1, Key.Down), (0, -1, Key.Up) })
        {
            if (map.IsBlocked(cell.X + dx, cell.Y + dy))
                continue;
            toward = key;
            var at = hero.GetComponent<GridMovement>()!.CellCenter((cell.X + dx, cell.Y + dy));
            return game.Spawn(prefab, at);
        }

        throw new InvalidOperationException("The hero is walled in.");
    }

    [Fact]
    public async Task Play_MakesACrypt_WithTheHeroInTheFirstRoom_TheStairsInTheFurthest_AndMonstersAndTreasureAbout()
    {
        var game = await Create();
        Assert.Equal("Deepest floor: 0", TextOf(game, "Deepest"));
        Tap(game, Key.Enter, after: 0.8);
        Assert.Equal("Crypt", game.SceneName);

        var dungeon = Dungeon(game);
        Assert.Equal(1, dungeon.Floor);
        Assert.Equal(dungeon.RoomList[0].Center, Cell(game.Find("Player")!));
        var stairs = game.Find("Stairs")!.Transform.Position;
        Assert.Equal(dungeon.ExitCell, game.Find("Player")!.GetComponent<GridMovement>()!.CellAt(new Vector2(stairs.X, stairs.Y)));
        var spawns = game.Find("Crypt")!.Children.Single(c => c.Name == DungeonGenerator.SpawnsName).Children;
        Assert.InRange(spawns.Count(e => e.Name == "Slime"), 3, 4);
        Assert.InRange(spawns.Count(e => e.Name == "Bat"), 1, 2);
        Assert.InRange(spawns.Count(e => e.Name == "Potion"), 1, 2);
        Assert.InRange(spawns.Count(e => e.Name == "Gold"), 4, 6);
        Assert.Equal(("Floor 1", "Gold: 0", "Potions: 0", "Turn 1"), (TextOf(game, "Floor"), TextOf(game, "Gold Count"), TextOf(game, "Potion Count"), TextOf(game, "Turns")));
    }

    [Fact]
    public async Task TheStairs_TakeTheHeroDownAFloor_AndTheMenuRemembersTheDeepest()
    {
        var game = await Create(setUp: d => (d.Seed, d.Spawns) = (77, []));
        Tap(game, Key.Enter, after: 0.8);
        var hero = game.Find("Player")!;
        WalkTo(game, Dungeon(game).ExitCell!.Value);
        Run(game, 0.3);

        Assert.Equal(2, Dungeon(game).Floor);
        Assert.Equal("Floor 2", TextOf(game, "Floor"));
        Assert.Equal(Dungeon(game).RoomList[0].Center, Cell(hero));

        // Falling there, the Game Over panel's Menu (Esc) goes back to the menu.
        hero.GetComponent<Health>()!.Kill();
        Run(game, 0.5);
        Assert.Equal("You reached floor 2", TextOf(game, "Reached"));
        Tap(game, Key.Escape, after: 0.8);
        Assert.Equal("Main", game.SceneName);
        Assert.Equal("Deepest floor: 2", TextOf(game, "Deepest"));
    }

    [Fact]
    public async Task WalkingIntoASlime_HitsIt_ItHitsBack_AndTwoHitsKillIt()
    {
        var game = await Create(setUp: d => d.Spawns = []);
        Tap(game, Key.Enter, after: 0.8);
        var hero = game.Find("Player")!;
        var slime = NextToHero(game, "Slime", out var toward);
        Run(game, 0.1);

        Tap(game, toward, after: 0.4);
        Assert.Equal(1, slime.GetComponent<Health>()!.Current);
        Assert.Equal(9, hero.GetComponent<Health>()!.Current);

        Tap(game, toward, after: 0.4);
        Assert.Null(slime.Scene);
        Assert.Equal(9, hero.GetComponent<Health>()!.Current);
    }

    [Fact]
    public async Task APotion_PickedUp_ShowsOnTheHud_AndOneDrinksIt()
    {
        var game = await Create(setUp: d => d.Spawns = []);
        Tap(game, Key.Enter, after: 0.8);
        var hero = game.Find("Player")!;
        NextToHero(game, "Potion", out var toward);
        NextToHero(game, "Gold", out _);
        Run(game, 0.1);

        Tap(game, toward, after: 0.4);
        Assert.Equal(("Potions: 1", "Gold: 1"), (TextOf(game, "Potion Count"), TextOf(game, "Gold Count")));

        var health = hero.GetComponent<Health>()!;
        health.Damage(6);
        Tap(game, Key.D1, after: 0.3);
        Assert.Equal(8, health.Current);
        Assert.Equal("Potions: 0", TextOf(game, "Potion Count"));
    }

    [Fact]
    public async Task TheHeroFalling_OpensYouDied_WithTheFloorReached_AndAgainStartsANewCrypt()
    {
        var game = await Create(setUp: d => d.Spawns = []);
        Tap(game, Key.Enter, after: 0.8);
        game.Find("Player")!.GetComponent<Health>()!.Kill();
        Run(game, 0.5);

        Assert.True(game.Find("Game Over")!.IsEnabled);
        Assert.Equal("You reached floor 1", TextOf(game, "Reached"));
        Tap(game, Key.Enter, after: 0.8);
        Assert.Equal("Crypt", game.SceneName);
        Assert.Equal(10, game.Find("Player")!.GetComponent<Health>()!.Current);
    }
}
