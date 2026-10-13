using System.Numerics;
using Griff.Audio;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Projects;

// The Strategy template: a small real-time strategy map with two bases, gold and trees to gather, buildings to put up,
// units to train, and the computer playing the red team. All of it no-code components; units and buildings are prefabs.
public static partial class ProjectTemplates
{
    private sealed class StrategyTemplate : ProjectTemplate
    {
        public override string Description => "Command an army in real time against the computer: gather gold and wood with workers, put up farms, barracks and towers, train soldiers and archers, and destroy the red base. Click or drag to select, right-click to send, hotkeys on the command card, arrow keys or the minimap to look around.";

        public override int Order => 40;

        protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Heroic, seed: 5) with { Volume = 0.6f };

        protected override Scene BuildStartScene() => Strategy();

        protected override Dictionary<string, Entity> BuildPrefabs() => StrategyPrefabs();
    }

    private static readonly Color PlayerBlue = Color.FromBytes(0x4C, 0x9A, 0xFF);
    private static readonly Color EnemyRed = Color.FromBytes(0xE5, 0x53, 0x4B);
    private static readonly Color GoldColor = Color.FromBytes(0xE3, 0xB3, 0x41);
    private static readonly Color TreeColor = Color.FromBytes(0x2C, 0x6E, 0x3F);
    private static readonly Color RockColor = Color.FromBytes(0x5C, 0x5F, 0x66);

    private static Scene Strategy()
    {
        var scene = new Scene();

        // The camera is the player's command post: selecting, orders, the HUD and minimap, and the fog of war.
        var camera = new Entity("Main Camera");
        camera.Transform.Position = new Vector3(-12, -6, 0);
        var view = camera.AddComponent<Camera>();
        view.Size = 8;
        view.Background = Color.FromBytes(0x3B, 0x5A, 0x34);
        var commander = camera.AddComponent<Commander>();
        commander.MapSize = new Vector2(48, 32);
        camera.AddComponent<FogOfWar>();
        scene.Add(camera);

        var player = new Entity("Player Team");
        var blue = player.AddComponent<Faction>();
        blue.Team = "Player";
        blue.Color = PlayerBlue;
        blue.Resources = "Gold 400, Wood 150";
        scene.Add(player);

        var enemy = new Entity("Enemy Team");
        var red = enemy.AddComponent<Faction>();
        red.Team = "Enemy";
        red.Color = EnemyRed;
        red.Resources = "Gold 400, Wood 150";
        var opponent = enemy.AddComponent<Opponent>();
        opponent.Workers = 6;
        opponent.Builds = "Farm, Barracks, Tower";
        opponent.AttackWith = 5;
        opponent.FirstAttack = 150;
        opponent.TrainEvery = 12;
        scene.Add(enemy);

        var ground = new Entity("Ground");
        scene.Add(ground);
        foreach (var (position, size) in new[]
                 {
                     (new Vector2(-14, -8), new Vector2(14, 11)), (new Vector2(14, 8), new Vector2(14, 11)),
                     (new Vector2(0, 0), new Vector2(9, 7)), (new Vector2(-6, 10), new Vector2(8, 5)), (new Vector2(6, -10), new Vector2(8, 5)),
                 })
        {
            var patch = new Entity("Clearing");
            patch.Transform.Position = new Vector3(position, -1);
            var sprite = patch.AddComponent<Sprite>();
            sprite.Size = size;
            sprite.Tint = Color.FromBytes(0x4A, 0x6B, 0x3E);
            sprite.Order = -20;
            scene.Add(patch, ground);
        }

        // Rocks across the middle make two ways round, and trees edge the map.
        var rocks = new Entity("Rocks");
        scene.Add(rocks);
        foreach (var (position, size) in new[]
                 {
                     (new Vector2(-4, 6), new Vector2(2, 7)), (new Vector2(4, -6), new Vector2(2, 7)),
                     (new Vector2(-9, 1), new Vector2(3, 2)), (new Vector2(9, -1), new Vector2(3, 2)),
                 })
            scene.Add(Box("Rock", position, size, RockColor), rocks);

        var resources = new Entity("Resources");
        scene.Add(resources);
        scene.Add(GoldMine(new Vector2(-21, -3.5f), 2500), resources);
        scene.Add(GoldMine(new Vector2(21, 3.5f), 2500), resources);
        scene.Add(GoldMine(new Vector2(0, 0), 1500), resources);
        foreach (var at in Forest(new Vector2(-23, -15), new Vector2(-21, -8)).Concat(Forest(new Vector2(-15, -15.5f), new Vector2(-9, -14.5f)))
                     .Concat(Forest(new Vector2(21, 8), new Vector2(23, 15))).Concat(Forest(new Vector2(9, 14.5f), new Vector2(15, 15.5f)))
                     .Concat(Forest(new Vector2(-2, 12), new Vector2(2, 14))).Concat(Forest(new Vector2(-2, -14), new Vector2(2, -12))))
            scene.Add(Tree(at), resources);

        // Blue's base, bottom left: a town hall, workers and a few soldiers to start with.
        var blueBase = new Entity("Blue Base");
        scene.Add(blueBase);
        scene.Add(Placed(TownHall(), "Player", PlayerBlue, new Vector2(-16, -8)), blueBase);
        scene.Add(Placed(Farm(), "Player", PlayerBlue, new Vector2(-19.5f, -12)), blueBase);
        for (var i = 0; i < 5; i++)
            scene.Add(Placed(WorkerUnit(), "Player", PlayerBlue, new Vector2(-18.5f + i * 0.9f, -5)), blueBase);
        for (var i = 0; i < 3; i++)
            scene.Add(Placed(Soldier(), "Player", PlayerBlue, new Vector2(-12.5f + i * 0.9f, -6)), blueBase);
        scene.Add(Placed(Archer(), "Player", PlayerBlue, new Vector2(-11.5f, -7)), blueBase);

        // Red's base, top right, played by the computer: it's a little ahead, with barracks already up.
        var redBase = new Entity("Red Base");
        scene.Add(redBase);
        scene.Add(Placed(TownHall(), "Enemy", EnemyRed, new Vector2(16, 8)), redBase);
        scene.Add(Placed(Farm(), "Enemy", EnemyRed, new Vector2(19.5f, 12)), redBase);
        scene.Add(Placed(Barracks(), "Enemy", EnemyRed, new Vector2(11.5f, 11)), redBase);
        for (var i = 0; i < 4; i++)
            scene.Add(Placed(WorkerUnit(), "Enemy", EnemyRed, new Vector2(18.5f - i * 0.9f, 5)), redBase);
        for (var i = 0; i < 2; i++)
            scene.Add(Placed(Soldier(), "Enemy", EnemyRed, new Vector2(12.5f - i * 0.9f, 6.5f)), redBase);
        scene.Add(Placed(Archer(), "Enemy", EnemyRed, new Vector2(11.5f, 7.5f)), redBase);

        AddPauseMenu(scene, menuScene: null);
        scene.Add(EndPanel("Victory", "Victory!", "The red team has fallen.", Color.FromBytes(0x57, 0xD3, 0x6B)));
        scene.Add(EndPanel("Defeat", "Defeat", "Your last building has fallen.", EnemyRed));
        return scene;
    }

    private static Dictionary<string, Entity> StrategyPrefabs() => new Dictionary<string, Entity>
    {
        ["Prefabs/Worker.prefab"] = WorkerUnit(),
        ["Prefabs/Soldier.prefab"] = Soldier(),
        ["Prefabs/Archer.prefab"] = Archer(),
        ["Prefabs/Arrow.prefab"] = Arrow(),
        ["Prefabs/Town Hall.prefab"] = TownHall(),
        ["Prefabs/Farm.prefab"] = Farm(),
        ["Prefabs/Barracks.prefab"] = Barracks(),
        ["Prefabs/Tower.prefab"] = Tower(),
    };

    // A prefab put in the scene for a team: its Team set, its team-colored parts painted.
    private static Entity Placed(Entity entity, string team, Color color, Vector2 position)
    {
        entity.Transform.Position = new Vector3(position, entity.Transform.Position.Z);
        if (entity.GetComponent<Unit>() is { } unit)
            unit.Team = team;
        if (entity.GetComponent<Building>() is { } building)
            building.Team = team;
        foreach (var part in entity.SelfAndDescendants().Where(e => e.Name == "Team Color"))
            part.GetComponent<Sprite>()!.Tint = color;
        return entity;
    }

    // A unit: a team-colored square with its kind's color in the middle, health, and whatever else it does.
    private static Entity StrategyUnit(string name, Color color, float size, float health)
    {
        var entity = new Entity(name);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = new Vector2(size * 0.62f);
        sprite.Tint = color;
        sprite.Order = 2;
        var hp = entity.AddComponent<Health>();
        hp.Max = health;
        hp.InvincibleTime = 0;
        var unit = entity.AddComponent<Unit>();
        unit.Radius = size / 2;

        var team = new Entity("Team Color");
        var band = team.AddComponent<Sprite>();
        band.Size = new Vector2(size);
        band.Tint = PlayerBlue;
        band.Order = 1;
        entity.Transform.Position = new Vector3(0, 0, 0.1f);
        team.Transform.Position = new Vector3(0, 0, -0.01f);
        WithChild(entity, team);
        return entity;
    }

    private static Entity WorkerUnit()
    {
        var entity = StrategyUnit("Worker", Color.FromBytes(0xD8, 0xC0, 0x8C), 0.7f, 30);
        var unit = entity.GetComponent<Unit>()!;
        unit.Speed = 3.2f;
        unit.Cost = "50 Gold";
        unit.TrainTime = 4;
        unit.Hotkey = "W";
        var worker = entity.AddComponent<Worker>();
        worker.Carry = 8;
        worker.GatherTime = 1.2f;
        worker.Builds = "Farm, Barracks, Tower";
        return entity;
    }

    private static Entity Soldier()
    {
        var entity = StrategyUnit("Soldier", Color.FromBytes(0xB4, 0xB9, 0xC4), 0.8f, 60);
        var unit = entity.GetComponent<Unit>()!;
        unit.Speed = 2.8f;
        unit.Cost = "60 Gold";
        unit.TrainTime = 5;
        unit.Hotkey = "S";
        var fighter = entity.AddComponent<Fighter>();
        fighter.Damage = 6;
        fighter.Range = 0.3f;
        fighter.Cooldown = 1;
        return entity;
    }

    private static Entity Archer()
    {
        var entity = StrategyUnit("Archer", Color.FromBytes(0x8F, 0xD0, 0x7A), 0.7f, 35);
        var unit = entity.GetComponent<Unit>()!;
        unit.Speed = 3;
        unit.Sight = 7;
        unit.Cost = "50 Gold, 25 Wood";
        unit.TrainTime = 5;
        unit.Hotkey = "A";
        var fighter = entity.AddComponent<Fighter>();
        fighter.Damage = 5;
        fighter.Range = 4.5f;
        fighter.Cooldown = 1.4f;
        fighter.Projectile = "Arrow";
        fighter.ProjectileSpeed = 14;
        return entity;
    }

    private static Entity Arrow()
    {
        var entity = new Entity("Arrow");
        entity.Transform.Position = new Vector3(0, 0, 0.5f);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = new Vector2(0.45f, 0.08f);
        sprite.Tint = Color.FromBytes(0xF0, 0xE6, 0xC8);
        sprite.Order = 5;
        entity.AddComponent<Lifetime>().Seconds = 3;
        return entity;
    }

    // A building: a team-colored base with the building's color on top, health, and the ground it covers.
    private static Entity StrategyBuilding(string name, Vector2 size, Color color, float health, string cost, float buildTime, string hotkey)
    {
        var entity = new Entity(name);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size - new Vector2(0.5f);
        sprite.Tint = color;
        sprite.Order = 1;
        var hp = entity.AddComponent<Health>();
        hp.Max = health;
        hp.InvincibleTime = 0;
        var building = entity.AddComponent<Building>();
        building.Size = size;
        building.Cost = cost;
        building.BuildTime = buildTime;
        building.Hotkey = hotkey;

        var team = new Entity("Team Color");
        var band = team.AddComponent<Sprite>();
        band.Size = size;
        band.Tint = PlayerBlue;
        band.Order = 0;
        team.Transform.Position = new Vector3(0, 0, -0.01f);
        WithChild(entity, team);
        return entity;
    }

    private static Entity TownHall()
    {
        var entity = StrategyBuilding("Town Hall", new Vector2(4, 4), Color.FromBytes(0xC9, 0xA8, 0x6A), 800, "400 Gold", 40, "H");
        var building = entity.GetComponent<Building>()!;
        building.Depot = true;
        building.Supply = 10;
        building.Sight = 8;
        entity.AddComponent<Producer>().Trains = "Worker";
        return entity;
    }

    private static Entity Farm()
    {
        var entity = StrategyBuilding("Farm", new Vector2(2, 2), Color.FromBytes(0xA7, 0xC9, 0x6A), 200, "50 Gold, 50 Wood", 8, "F");
        entity.GetComponent<Building>()!.Supply = 6;
        return entity;
    }

    private static Entity Barracks()
    {
        var entity = StrategyBuilding("Barracks", new Vector2(3, 3), Color.FromBytes(0x8E, 0x6B, 0x5A), 500, "150 Gold, 50 Wood", 12, "B");
        entity.AddComponent<Producer>().Trains = "Soldier, Archer";
        return entity;
    }

    private static Entity Tower()
    {
        var entity = StrategyBuilding("Tower", new Vector2(2, 2), Color.FromBytes(0x9A, 0x9E, 0xA8), 300, "100 Gold, 75 Wood", 12, "T");
        var fighter = entity.AddComponent<Fighter>();
        fighter.Damage = 6;
        fighter.Range = 5;
        fighter.Cooldown = 1.2f;
        fighter.Projectile = "Arrow";
        fighter.ProjectileSpeed = 16;
        return entity;
    }

    private static Entity GoldMine(Vector2 position, int amount)
    {
        var entity = new Entity("Gold Mine");
        entity.Transform.Position = new Vector3(position, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = new Vector2(2.5f, 2.5f);
        sprite.Tint = GoldColor;
        var resource = entity.AddComponent<Resource>();
        resource.Kind = "Gold";
        resource.Amount = amount;
        return entity;
    }

    private static Entity Tree(Vector2 position)
    {
        var entity = new Entity("Tree");
        entity.Transform.Position = new Vector3(position, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = new Vector2(0.9f, 0.9f);
        sprite.Tint = TreeColor;
        var resource = entity.AddComponent<Resource>();
        resource.Kind = "Wood";
        resource.Amount = 40;
        return entity;
    }

    // Trees a cell apart filling a box, with a gap here and there.
    private static IEnumerable<Vector2> Forest(Vector2 from, Vector2 to)
    {
        var i = 0;
        for (var y = from.Y; y <= to.Y + 1e-3f; y += 1)
        {
            for (var x = from.X; x <= to.X + 1e-3f; x += 1)
            {
                if (i++ % 7 != 3)
                    yield return new Vector2(x, y);
            }
        }
    }

    // Puts a child inside an entity that isn't in a scene yet (a prefab, or something about to be added).
    private static void WithChild(Entity parent, Entity child)
    {
        var scratch = new Scene();
        scratch.Add(parent);
        scratch.Add(child, parent);
        scratch.Remove(parent);
    }

    // Shown when the game's won or lost: what happened, and a button to play again.
    private static Entity EndPanel(string name, string heading, string detail, Color color)
    {
        var panel = new Entity(name) { IsEnabled = false };
        panel.Transform.Position = new Vector3(0, 1.5f, 2);
        var backdrop = panel.AddComponent<Sprite>();
        backdrop.Size = new Vector2(12, 5.5f);
        backdrop.Tint = new Color(0.07f, 0.08f, 0.1f, 0.94f);
        backdrop.Order = 400;
        var anchor = panel.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Center;
        anchor.Offset = new Vector2(0, 1.5f);
        var end = panel.AddComponent<Panel>();
        end.PauseGame = false;
        end.CloseOnBack = false;

        var title = Label("Heading", new Vector2(0, 1.4f), heading, 1.4f, color);
        title.GetComponent<Text>()!.Order = 401;
        WithChild(panel, title);
        var line = Label("Detail", new Vector2(0, 0.1f), detail, 0.5f, Color.FromBytes(0xB4, 0xB9, 0xC4));
        line.GetComponent<Text>()!.Order = 401;
        WithChild(panel, line);
        WithChild(panel, MenuButton("Play Again", new Vector2(0, -1.6f), Scenes.ButtonAction.RestartScene, null, Key.Enter, PlayerBlue, order: 410));
        return panel;
    }
}
