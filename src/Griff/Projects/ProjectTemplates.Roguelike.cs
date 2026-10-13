using System.Numerics;
using Griff.Audio;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Projects;

// The Roguelike template: a menu with the deepest floor reached, and a crypt made anew from a seed each game, floor after
// floor, where everything moves when the hero moves. All of it no-code components: a Dungeon Generator that lays out the
// floors and scatters the monsters and treasure, a hero who steps tile by tile and hits by walking into things (Bump
// Attack) and drinks potions with 1, slimes and bats that hunt the hero by turns, counters, and a panel that opens on
// "Game Over".
public static partial class ProjectTemplates
{
    private sealed class RoguelikeTemplate : ProjectTemplate
    {
        public override string Description => "Go down through a crypt made anew every game, floor after floor: find the stairs in each, fight the slimes and bats by walking into them, and pick up gold and potions. Everything moves when you move. The deepest floor reached is kept. Arrow keys or WASD to walk and fight, 1 to drink a potion, Tab to wait a turn.";

        public override int Order => 110;

        protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Spooky, seed: 14) with { Volume = 0.5f };

        protected override Scene BuildStartScene() => RogueMenu();

        protected override Dictionary<string, Scene> BuildMoreScenes() => new() { ["Scenes/Crypt.scene"] = RogueGame() };

        protected override Dictionary<string, Entity> BuildPrefabs() => RoguePrefabs();

        public override IReadOnlyDictionary<string, byte[]> Images() => TemplateArt.CryptFiles();
    }

    private static readonly Color RogueGold = Color.FromBytes(0xF2, 0xC9, 0x4C);
    private static readonly Color RogueRed = Color.FromBytes(0xB8, 0x3A, 0x48);
    private static readonly Color RogueGray = Color.FromBytes(0x34, 0x32, 0x3E);
    private static readonly Color RogueLight = Color.FromBytes(0xD8, 0xD0, 0xC4);
    private static readonly Color RogueDim = Color.FromBytes(0x84, 0x7C, 0x8C);
    private static readonly Color RogueNight = Color.FromBytes(0x0A, 0x08, 0x0E);

    // The deepest floor reached is kept under this, shown on the menu and when the hero falls.
    private const string RogueDeepestKey = "deepest";

    private static Scene RogueMenu()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 6;
        view.Background = RogueNight;
        view.Transition = SceneTransition.Fade;
        scene.Add(camera);

        scene.Add(Label("Title", new Vector2(0, 3.6f), "THE CRYPT", 1.5f, RogueGold));
        scene.Add(Label("Subtitle", new Vector2(0, 2.5f), "Find the stairs. Go deeper. Don't die.", 0.5f, RogueLight));

        var deepest = Label("Deepest", new Vector2(0, 1.3f), "Deepest floor: 0", 0.55f, RogueDim);
        var counter = deepest.AddComponent<Counter>();
        counter.Message = null;
        counter.SaveBestAs = RogueDeepestKey;
        counter.Format = "Deepest floor: {1}";
        scene.Add(deepest);

        scene.Add(MenuButton("Play", new Vector2(0, -0.2f), Scenes.ButtonAction.LoadScene, "Crypt", Key.Enter, RogueRed));
        scene.Add(MenuButton("Quit", new Vector2(0, -1.6f), Scenes.ButtonAction.Quit, null, Key.Escape, RogueGray));

        var hint = Label("Hint", Vector2.Zero, "Enter to play  ·  Arrows to choose", 0.4f, RogueDim);
        var anchor = hint.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Bottom;
        anchor.Offset = new Vector2(0, 0.6f);
        scene.Add(hint);
        return scene;
    }

    private static Scene RogueGame()
    {
        var scene = new Scene();

        // A dark crypt: the camera follows the hero, lit by the lantern they carry.
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 6.5f;
        view.Background = RogueNight;
        view.Ambient = Color.FromBytes(0x40, 0x3C, 0x58);
        view.Transition = SceneTransition.Fade;
        camera.AddComponent<Follow>().Target = "Player";
        scene.Add(camera);

        // A new crypt every game (Seed 0): its floors are background tiles to walk on, its walls solid. The hero starts in
        // the first room, the stairs wait in the furthest, and monsters, potions and gold are scattered through the rest.
        var crypt = new Entity("Crypt");
        var map = crypt.AddComponent<Tilemap>();
        map.Image = TemplateArt.CryptTiles;
        map.Columns = TemplateArt.CryptColumns;
        map.BackgroundTiles = TemplateArt.CryptFloorTile.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var dungeon = crypt.AddComponent<DungeonGenerator>();
        (dungeon.Width, dungeon.Height, dungeon.Rooms, dungeon.MinRoomSize, dungeon.MaxRoomSize) = (40, 28, 8, 4, 8);
        (dungeon.WallTile, dungeon.FloorTile, dungeon.Exit) = (TemplateArt.CryptWallTile, TemplateArt.CryptFloorTile, "Stairs");
        dungeon.Spawns = ["Slime 3-4", "Bat 1-2", "Potion 1-2", "Gold 4-6"];
        scene.Add(crypt);

        // A crypt to look at in the editor, with the hero and the stairs in place; the game makes its own.
        var sample = Runtime.DungeonGen.Generate(dungeon, seed: 1);
        map.Tiles = sample.Tiles;
        static Vector3 Middle((int X, int Y) cell) => new(cell.X + 0.5f, -cell.Y - 0.5f, 0);

        var stairs = new Entity("Stairs");
        stairs.Transform.Position = Middle(sample.Exit);
        var steps = stairs.AddComponent<Sprite>();
        (steps.Image, steps.Size, steps.Order) = (TemplateArt.Stairs, Vector2.One, -1);
        scene.Add(stairs);

        var hero = RogueHero();
        hero.Transform.Position = Middle(sample.Players[0]);
        scene.Add(hero);
        camera.Transform.Position = hero.Transform.Position;

        // Everything moves when the hero moves: the hero's side, then the monsters', one step (or hit) each.
        var turns = new Entity("Turns");
        var order = turns.AddComponent<TurnOrder>();
        (order.Between, order.Format) = (0, "Turn {1}");
        var turnText = turns.AddComponent<Text>();
        (turnText.Value, turnText.Size, turnText.Align, turnText.Color, turnText.Order) = ("Turn 1", 0.5f, TextAlign.Right, RogueDim, 150);
        var corner = turns.AddComponent<ScreenAnchor>();
        (corner.Anchor, corner.Offset) = (Anchor.TopRight, new Vector2(-0.6f, -0.7f));
        scene.Add(turns);

        AddRogueHud(scene);
        scene.Add(RogueGameOverPanel());
        AddPauseMenu(scene, menuScene: "Main");
        return scene;
    }

    // The hero: steps tile by tile, hits what it walks into, carries and drinks potions, and lights the way.
    private static Entity RogueHero()
    {
        var hero = new Entity("Player");
        var sprite = hero.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (TemplateArt.Hero, Vector2.One, 5);
        var collider = hero.AddComponent<BoxCollider>();
        (collider.Size, collider.Layer) = (new Vector2(0.8f), "Player");
        hero.AddComponent<Body>().GravityScale = 0;
        hero.AddComponent<GridMovement>().StepTime = 0.12f;
        var health = hero.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime) = (10, 10, 0);
        health.DeathMessage = "Game Over";
        var bump = hero.AddComponent<BumpAttack>();
        (bump.Layers, bump.Effect) = ("Enemy", "Sparkle");
        var inventory = hero.AddComponent<Inventory>();
        inventory.ItemUses = ["Potion: heal 4"];
        var taker = hero.AddComponent<TurnTaker>();
        (taker.Side, taker.Actions) = ("Player", 1);
        var lantern = hero.AddComponent<Light>();
        (lantern.Radius, lantern.Intensity, lantern.Color) = (6, 0.9f, Color.FromBytes(0xFF, 0xE2, 0xB0));
        return hero;
    }

    // Down the left: health, the floor, gold and potions. The keys along the bottom.
    private static void AddRogueHud(Scene scene)
    {
        var health = new Entity("Health Bar");
        var bar = health.AddComponent<Bar>();
        (bar.Track, bar.Size, bar.Fill) = ("Player", new Vector2(3.2f, 0.35f), Color.FromBytes(0xD0, 0x40, 0x50));
        var top = health.AddComponent<ScreenAnchor>();
        top.Offset = new Vector2(2.4f, -0.6f);
        scene.Add(health);

        var y = -1.3f;
        Counter Line(string name, string text, Color color)
        {
            var label = Label(name, Vector2.Zero, text, 0.6f, color);
            label.GetComponent<Text>()!.Align = TextAlign.Left;
            var anchor = label.AddComponent<ScreenAnchor>();
            anchor.Offset = new Vector2(0.8f, y);
            y -= 0.9f;
            scene.Add(label);
            var counter = label.AddComponent<Counter>();
            counter.Pop = 1.15f;
            return counter;
        }

        // Count Floor reads the crypt's floor, and keeps the deepest yet.
        var floor = Line("Floor", "Floor 1", RogueLight);
        (floor.Message, floor.CountFloor, floor.Format, floor.SaveBestAs) = (null, true, "Floor {0}", RogueDeepestKey);

        var gold = Line("Gold Count", "Gold: 0", RogueGold);
        (gold.Message, gold.Format) = ("Gold", "Gold: {0}");

        var potions = Line("Potion Count", "Potions: 0", Color.FromBytes(0xE8, 0x70, 0x88));
        (potions.Message, potions.Item, potions.Format) = (null, "Potion", "Potions: {0}");

        // "{EndTurn}" shows Tab on the keyboard and Back on a gamepad.
        var hint = Label("Hint", Vector2.Zero, "Arrows to walk, and into monsters to fight  ·  1 to drink a potion  ·  {EndTurn} to wait  ·  {Pause} for the menu", 0.45f, RogueDim);
        var bottom = hint.AddComponent<ScreenAnchor>();
        bottom.Anchor = Anchor.Bottom;
        bottom.Offset = new Vector2(0, 0.6f);
        scene.Add(hint);
    }

    // Opens on "Game Over" (the hero's Death Message): how deep they got and the deepest yet, then again or the menu.
    private static Entity RogueGameOverPanel()
    {
        var panel = new Entity("Game Over");
        panel.Transform.Position = new Vector3(0, 0, 2);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var over = panel.AddComponent<Panel>();
        over.OpenMessage = "Game Over";
        over.CloseOnBack = false;
        var backdrop = panel.AddComponent<Sprite>();
        backdrop.Size = new Vector2(10, 6.4f);
        backdrop.Tint = new Color(0.05f, 0.04f, 0.07f, 0.94f);
        backdrop.Order = 400;

        var heading = Label("Heading", new Vector2(0, 2.1f), "You Died", 1.3f, RogueRed);
        heading.GetComponent<Text>()!.Order = 401;
        WithChild(panel, heading);

        var reached = Label("Reached", new Vector2(0, 0.9f), "", 0.6f, RogueLight);
        reached.GetComponent<Text>()!.Order = 401;
        var floor = reached.AddComponent<Counter>();
        (floor.Message, floor.CountFloor, floor.Format, floor.Pop) = (null, true, "You reached floor {0}", 1);
        WithChild(panel, reached);

        var deepestLine = Label("Deepest", new Vector2(0, 0.1f), "", 0.5f, RogueGold);
        deepestLine.GetComponent<Text>()!.Order = 401;
        var deepest = deepestLine.AddComponent<Counter>();
        (deepest.Message, deepest.SaveBestAs, deepest.Format, deepest.Pop) = (null, RogueDeepestKey, "Deepest: {1}", 1);
        WithChild(panel, deepestLine);

        WithChild(panel, MenuButton("Again", new Vector2(-2.2f, -1.6f), Scenes.ButtonAction.RestartScene, null, Key.Enter, RogueRed, order: 410));
        WithChild(panel, MenuButton("Menu", new Vector2(2.2f, -1.6f), Scenes.ButtonAction.LoadScene, "Main", Key.Escape, RogueGray, order: 410));
        return panel;
    }

    private static Dictionary<string, Entity> RoguePrefabs() => new()
    {
        ["Prefabs/Sparkle.prefab"] = Sparkle(),
        ["Prefabs/Slime.prefab"] = RogueMonster("Slime", TemplateArt.Slime, hitPoints: 2, range: 5, sight: true, diagonal: false),
        ["Prefabs/Bat.prefab"] = RogueMonster("Bat", TemplateArt.Bat, hitPoints: 1, range: 8, sight: false, diagonal: true),
        ["Prefabs/Potion.prefab"] = RogueTreasure("Potion", TemplateArt.Potion, item: "Potion"),
        ["Prefabs/Gold.prefab"] = RogueTreasure("Gold", TemplateArt.Gold, item: null),
    };

    // A monster that hunts the hero once it notices them, a step a turn, and hits them by walking into them.
    private static Entity RogueMonster(string name, string image, float hitPoints, float range, bool sight, bool diagonal)
    {
        var monster = new Entity(name);
        var sprite = monster.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (image, Vector2.One, 4);
        var collider = monster.AddComponent<BoxCollider>();
        (collider.Size, collider.Layer) = (new Vector2(0.8f), "Enemy");
        monster.AddComponent<Body>().GravityScale = 0;
        var movement = monster.AddComponent<GridMovement>();
        (movement.StepTime, movement.Diagonal) = (0.12f, diagonal);
        var chase = monster.AddComponent<GridChase>();
        (chase.Range, chase.GiveUp, chase.NeedsSight, chase.StopDistance) = (range, 3, sight, 0);
        var health = monster.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime) = (hitPoints, hitPoints, 0);
        monster.AddComponent<BumpAttack>().Layers = "Player";
        var taker = monster.AddComponent<TurnTaker>();
        (taker.Side, taker.Actions) = ("Enemy", 1);
        return monster;
    }

    // Something to pick up by stepping onto it: a potion for the inventory, or gold for the count.
    private static Entity RogueTreasure(string name, string image, string? item)
    {
        var treasure = new Entity(name);
        var sprite = treasure.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (image, new Vector2(0.6f), 2);
        var collider = treasure.AddComponent<BoxCollider>();
        (collider.Size, collider.IsTrigger) = (new Vector2(0.6f), true);
        var pickup = treasure.AddComponent<Pickup>();
        (pickup.Layers, pickup.Item, pickup.Message, pickup.Effect) = ("Player", item, item is null ? "Gold" : null, "Sparkle");
        return treasure;
    }
}
