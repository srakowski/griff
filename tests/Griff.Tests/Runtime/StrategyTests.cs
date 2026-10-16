using System.Numerics;
using Griff.Input;
using Griff.Modules.Strategy;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public sealed class StrategyTests
{
    private const double Frame = 1 / 60.0;

    // ----- Building blocks -----

    private static Entity Team(Scene scene, string team, string resources)
    {
        var entity = new Entity(team + " Team");
        var faction = entity.AddComponent<Faction>();
        faction.Team = team;
        faction.Resources = [.. resources.Split(",", StringSplitOptions.TrimEntries)];
        scene.Add(entity);
        return entity;
    }

    private static Entity Soldier(string team = "Player", float damage = 5, float health = 20)
    {
        var entity = new Entity("Soldier");
        entity.AddComponent<Sprite>();
        var unit = entity.AddComponent<Unit>();
        unit.Team = team;
        unit.Cost = "60 Gold";
        unit.TrainTime = 1;
        var hp = entity.AddComponent<Health>();
        hp.Max = health;
        hp.InvincibleTime = 0;
        var fighter = entity.AddComponent<Fighter>();
        fighter.Damage = damage;
        fighter.Range = 0.3f;
        fighter.Cooldown = 0.5f;
        return entity;
    }

    private static Entity WorkerUnit(string team = "Player")
    {
        var entity = new Entity("Worker");
        entity.AddComponent<Sprite>();
        entity.AddComponent<Unit>().Team = team;
        var worker = entity.AddComponent<Worker>();
        worker.GatherTime = 0.5f;
        worker.Carry = 10;
        worker.Builds = "Farm";
        return entity;
    }

    private static Entity Hall(string team = "Player")
    {
        var entity = new Entity("Hall");
        entity.AddComponent<Sprite>().Size = new Vector2(2, 2);
        var building = entity.AddComponent<Building>();
        building.Team = team;
        building.Size = new Vector2(2, 2);
        building.Depot = true;
        building.Supply = 5;
        entity.AddComponent<Health>().Max = 50;
        return entity;
    }

    private static Entity Farm()
    {
        var entity = new Entity("Farm");
        entity.AddComponent<Sprite>().Size = new Vector2(2, 2);
        var building = entity.AddComponent<Building>();
        building.Size = new Vector2(2, 2);
        building.Cost = "30 Gold";
        building.BuildTime = 1;
        building.Supply = 4;
        entity.AddComponent<Health>().Max = 20;
        return entity;
    }

    private static Entity Mine(Vector2 at, int amount = 100)
    {
        var entity = new Entity("Gold Mine");
        entity.Transform.Position = new Vector3(at, 0);
        entity.AddComponent<Sprite>().Size = new Vector2(1, 1);
        var resource = entity.AddComponent<Resource>();
        resource.Kind = "Gold";
        resource.Amount = amount;
        return entity;
    }

    private static Entity At(Entity entity, float x, float y)
    {
        entity.Transform.Position = new Vector3(x, y, entity.Transform.Position.Z);
        return entity;
    }

    private static Game NewGame(Scene scene, InputState input) => new(scene, input)
    {
        PrefabLoader = name => name switch
        {
            "Soldier" => Soldier(),
            "Worker" => WorkerUnit(),
            "Farm" => Farm(),
            _ => throw new KeyNotFoundException(name),
        },
    };

    private static void Run(Game game, InputState input, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Frame)
        {
            game.Tick(Frame);
            input.EndFrame();
        }
    }

    private static Vector2 Flat(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    // ----- Resources -----

    [Fact]
    public void Costs_ReadEitherWayRound_AndFactionsSpendThem()
    {
        Assert.Equal(new Dictionary<string, int> { ["Gold"] = 150, ["Wood"] = 50 }, Costs.Parse("150 Gold, Wood 50"));
        Assert.Equal("150 Gold, 50 Wood", Costs.Describe("Gold 150, 50 Wood"));
        Assert.Equal("Free", Costs.Describe(""));

        var faction = new Faction { Resources = ["Gold 100", "Wood 20"] };
        Assert.True(faction.CanAfford("60 Gold"));
        Assert.Equal("Wood", faction.Lacking("60 Gold, 30 Wood"));
        Assert.True(faction.Spend("60 Gold, 10 Wood"));
        Assert.Equal(40, faction.Amount("Gold"));
        Assert.False(faction.Spend("50 Gold"));
        faction.Add("Gold", 25);
        Assert.Equal(["Gold 65", "Wood 10"], faction.Resources);
    }

    [Fact]
    public void Workers_GatherAndBringItBack_UntilTheMineRunsOut()
    {
        var scene = new Scene();
        var team = Team(scene, "Player", "Gold 0");
        scene.Add(At(Hall(), 0, 0));
        var mine = Mine(new Vector2(5, 0), amount: 30);
        scene.Add(mine);
        var worker = At(WorkerUnit(), 2, 2);
        scene.Add(worker);
        var input = new InputState();
        var game = NewGame(scene, input);
        Run(game, input, 0.1);

        worker.GetComponent<Worker>()!.Gather(mine);
        Run(game, input, 4);
        Assert.True(team.GetComponent<Faction>()!.Amount("Gold") >= 10);

        Run(game, input, 12);
        Assert.Equal(30, team.GetComponent<Faction>()!.Amount("Gold"));
        Assert.Null(mine.Scene);
        Assert.Equal(UnitOrder.Idle, worker.GetComponent<Unit>()!.Order);
    }

    // ----- Moving -----

    [Fact]
    public void Units_WalkAroundBuildings_ToWhereTheyreSent()
    {
        var scene = new Scene();
        var wall = At(Hall("Enemy"), 0, 0);
        wall.GetComponent<Building>()!.Size = new Vector2(2, 6);
        scene.Add(wall);
        var walker = At(WorkerUnit(), -3, 0);
        scene.Add(walker);
        var input = new InputState();
        var game = NewGame(scene, input);
        Run(game, input, 0.1);

        walker.GetComponent<Unit>()!.MoveTo(new Vector2(3, 0));
        var arrived = false;
        walker.GetComponent<Unit>()!.Arrived += _ => arrived = true;
        var box = new Box(Vector2.Zero, new Vector2(1, 3));
        for (var t = 0.0; t < 8; t += Frame)
        {
            game.Tick(Frame);
            input.EndFrame();
            Assert.False(box.Contains(Flat(walker)), $"walked through the building at {Flat(walker)}");
        }

        Assert.True(arrived);
        Assert.True(Vector2.Distance(Flat(walker), new Vector2(3, 0)) < 0.3f);
    }

    [Fact]
    public void Units_SentTogether_SpreadOutRatherThanStack()
    {
        var scene = new Scene();
        var soldiers = Enumerable.Range(0, 6).Select(i => At(Soldier(), -4 + i * 0.1f, 0)).ToList();
        foreach (var soldier in soldiers)
            scene.Add(soldier);
        var input = new InputState();
        var game = NewGame(scene, input);
        Run(game, input, 0.1);

        foreach (var soldier in soldiers)
            soldier.GetComponent<Unit>()!.MoveTo(new Vector2(4, 0));
        Run(game, input, 6);

        var positions = soldiers.Select(Flat).ToList();
        for (var i = 0; i < positions.Count; i++)
        {
            for (var j = i + 1; j < positions.Count; j++)
                Assert.True(Vector2.Distance(positions[i], positions[j]) > 0.5f, "two units ended up on top of each other");
        }
    }

    // ----- Fighting -----

    [Fact]
    public void Enemies_InSight_FightUntilOneFalls()
    {
        var scene = new Scene();
        var strong = At(Soldier("Player", damage: 10, health: 40), 0, 0);
        var weak = At(Soldier("Enemy", damage: 2, health: 20), 4, 0);
        scene.Add(strong);
        scene.Add(weak);
        var input = new InputState();
        var game = NewGame(scene, input);

        Run(game, input, 6);

        Assert.Null(weak.Scene);
        Assert.NotNull(strong.Scene);
        Assert.True(strong.GetComponent<Health>()!.Current < 40);
    }

    // Teams are every genre's: a platformer's hero on the Enemy team (no unit, no building) is fought like any enemy,
    // and one on the Player team is left alone.
    [Fact]
    public void Units_Fight_AnythingWithHealth_OnAnotherTeam()
    {
        var scene = new Scene();
        scene.Add(At(Soldier("Player", damage: 10), 0, 0));
        Entity Hero(string name, string team, float x)
        {
            var hero = At(new Entity(name), x, 0);
            hero.AddComponent(new Team { Name = team });
            hero.AddComponent(new Health { Max = 10, DestroyOnDeath = false });
            scene.Add(hero);
            return hero;
        }

        var foe = Hero("Rogue", "Enemy", 3);
        var friend = Hero("Squire", "Player", -2);
        var input = new InputState();
        var game = NewGame(scene, input);

        Run(game, input, 4);

        Assert.True(foe.GetComponent<Health>()!.IsDead);
        Assert.Equal(10, friend.GetComponent<Health>()!.Current);
    }

    [Fact]
    public void HoldingUnits_DontChase_ButAttackOrdersDo()
    {
        var scene = new Scene();
        var guard = At(Soldier(), 0, 0);
        var enemy = At(Soldier("Enemy", damage: 0), 4, 0);
        scene.Add(guard);
        scene.Add(enemy);
        var input = new InputState();
        var game = NewGame(scene, input);
        enemy.GetComponent<Unit>()!.Hold();
        guard.GetComponent<Unit>()!.Hold();
        Run(game, input, 2);
        Assert.Equal(Vector2.Zero, Flat(guard));

        guard.GetComponent<Unit>()!.Attack(enemy);
        Run(game, input, 5);
        Assert.Null(enemy.Scene);
    }

    [Fact]
    public void Soldiers_FightBackAgainstATowerShootingThem_BeforeHittingHarmlessBuildings()
    {
        var scene = new Scene();
        var farm = At(Hall("Enemy"), 2.5f, 0);
        farm.GetComponent<Building>()!.Depot = false;
        scene.Add(farm);
        var tower = At(Hall("Enemy"), -1, 4);
        tower.GetComponent<Health>()!.Max = 30;
        var guns = tower.AddComponent<Fighter>();
        guns.Damage = 1;
        guns.Range = 5;
        scene.Add(tower);
        var soldier = At(Soldier(damage: 5, health: 100), 0.9f, 0);
        scene.Add(soldier);
        var input = new InputState();
        var game = NewGame(scene, input);

        Run(game, input, 8);

        Assert.Null(tower.Scene);
        Assert.NotNull(farm.Scene);
    }

    [Fact]
    public void MeleeUnits_ReachABuildingFromItsCorner()
    {
        var scene = new Scene();
        var hall = At(Hall("Enemy"), 0, 0);
        scene.Add(hall);
        var soldier = At(Soldier(damage: 5), 2, 2);
        scene.Add(soldier);
        var input = new InputState();
        var game = NewGame(scene, input);

        Run(game, input, 3);

        Assert.True(hall.GetComponent<Health>()!.Current < 50, "the soldier never got close enough to hit it");
    }

    [Fact]
    public void UnitsSentToARallyPoint_GatherInAClump_NotALine()
    {
        var scene = new Scene();
        Team(scene, "Player", "Gold 1000");
        var hall = At(Hall(), 0, 0);
        hall.GetComponent<Building>()!.Supply = 20;
        hall.AddComponent<Producer>().Trains = "Soldier";
        hall.GetComponent<Building>()!.Rally = new Vector2(6, 0);
        scene.Add(hall);
        var input = new InputState();
        var game = NewGame(scene, input);
        Run(game, input, 0.1);

        for (var i = 0; i < 5; i++)
            Assert.Null(game.Train(hall.GetComponent<Producer>()!, "Soldier"));
        Run(game, input, 10);

        var soldiers = game.FindAll<Unit>().Select(u => Flat(u.Entity!)).ToList();
        Assert.Equal(5, soldiers.Count);
        Assert.All(soldiers, at => Assert.True(Vector2.Distance(at, new Vector2(6, 0)) < 2.7f, $"a soldier stopped at {at}, far from the rally point"));
    }

    // ----- Training and building -----

    [Fact]
    public void Producers_TrainUnits_ForTheirCost_AndNeedSupply()
    {
        var scene = new Scene();
        var team = Team(scene, "Player", "Gold 200");
        var hall = At(Hall(), 0, 0);
        hall.GetComponent<Building>()!.Supply = 1;
        var producer = hall.AddComponent<Producer>();
        producer.Trains = "Soldier";
        scene.Add(hall);
        var input = new InputState();
        var game = NewGame(scene, input);
        Run(game, input, 0.1);

        Assert.Null(game.Train(producer, "Soldier"));
        Assert.Equal(140, team.GetComponent<Faction>()!.Amount("Gold"));
        Assert.Equal("Not enough supply: build more farms.", game.Train(producer, "Soldier"));
        Assert.Equal("It doesn't train Archer.", game.Train(producer, "Archer"));

        hall.GetComponent<Building>()!.Rally = new Vector2(0, -4);
        Run(game, input, 1.2);
        var soldier = Assert.Single(game.FindAll<Unit>()).Entity!;
        Assert.Equal("Player", soldier.GetComponent<Unit>()!.Team);
        Assert.Equal(UnitOrder.Move, soldier.GetComponent<Unit>()!.Order);
        Assert.Equal(1, game.SupplyUsed("Player"));
    }

    [Fact]
    public void Cancelling_Training_GivesTheCostBack()
    {
        var scene = new Scene();
        var team = Team(scene, "Player", "Gold 200");
        var hall = At(Hall(), 0, 0);
        var producer = hall.AddComponent<Producer>();
        producer.Trains = "Soldier";
        scene.Add(hall);
        var input = new InputState();
        var game = NewGame(scene, input);
        Run(game, input, 0.1);

        game.Train(producer, "Soldier");
        game.Train(producer, "Soldier");
        Assert.Equal(80, team.GetComponent<Faction>()!.Amount("Gold"));
        Assert.True(game.CancelTraining(producer));
        Assert.Equal(140, team.GetComponent<Faction>()!.Amount("Gold"));
        Assert.Single(producer.Queue);
    }

    // What another genre sets off (a trigger, a card, a wire): Train and Cancel, and the queue read as an output.
    [Fact]
    public void ProducersActions_TrainAndCancel_AndItsQueueIsAnOutput()
    {
        var scene = new Scene();
        var team = Team(scene, "Player", "Gold 200");
        var hall = At(Hall(), 0, 0);
        var producer = hall.AddComponent<Producer>();
        producer.Trains = "Soldier";
        scene.Add(hall);
        var input = new InputState();
        var game = NewGame(scene, input);
        Run(game, input, 0.1);

        game.Do(hall, "Train");
        game.Do(hall, "Train", "Soldier");
        Run(game, input, Frame);
        Assert.Equal(2, producer.Queued);
        Assert.Equal(80, team.GetComponent<Faction>()!.Amount("Gold"));
        Assert.Equal(2, ComponentOutputs.Read(hall, "Queued"));

        game.Do(hall, "Cancel");
        Run(game, input, Frame);
        Assert.Equal(1, producer.Queued);
        Assert.Equal(140, team.GetComponent<Faction>()!.Amount("Gold"));
    }

    [Fact]
    public void Workers_PutUpBuildings_ThatProvideSupplyOnceFinished()
    {
        var scene = new Scene();
        var team = Team(scene, "Player", "Gold 100");
        scene.Add(At(Hall(), 0, 0));
        var worker = At(WorkerUnit(), 2, 0);
        scene.Add(worker);
        var input = new InputState();
        var game = NewGame(scene, input);
        Run(game, input, 0.1);

        Assert.Null(game.PlaceBuilding(worker.GetComponent<Worker>()!, "Farm", new Vector2(0, 0), out var blocked));
        Assert.Equal("Can't build there.", blocked);
        var site = game.PlaceBuilding(worker.GetComponent<Worker>()!, "Farm", new Vector2(5, 0), out _)!;
        Assert.NotNull(site);
        Assert.Equal(70, team.GetComponent<Faction>()!.Amount("Gold"));
        Assert.False(site.GetComponent<Building>()!.IsBuilt);
        Assert.Equal(5, game.SupplyProvided("Player"));

        Run(game, input, 4);
        Assert.True(site.GetComponent<Building>()!.IsBuilt);
        Assert.Equal(9, game.SupplyProvided("Player"));
        Assert.Equal(UnitOrder.Idle, worker.GetComponent<Unit>()!.Order);
    }

    // ----- The player's controls -----

    private static (Game Game, InputState Input, Entity Camera) Commanded(Scene scene)
    {
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>().Size = 8;
        var commander = camera.AddComponent<Commander>();
        commander.EdgeScroll = false;
        commander.MapSize = new Vector2(40, 30);
        scene.Add(camera);
        var input = new InputState();
        var game = NewGame(scene, input);
        Run(game, input, 0.1);
        return (game, input, camera);
    }

    private static void Click(Game game, InputState input, Vector2 at, MouseButton button = MouseButton.Left)
    {
        input.MouseWorld = at;
        input.SetMouseButton(button, true);
        Run(game, input, Frame);
        input.SetMouseButton(button, false);
        Run(game, input, Frame);
    }

    [Fact]
    public void Dragging_ABox_SelectsTheTeamsUnits_AndRightClickSendsThem()
    {
        var scene = new Scene();
        var mine = new[] { At(Soldier(), -1, 0), At(Soldier(), 0, 0), At(Soldier(), 4, 0) };
        var theirs = At(Soldier("Enemy", damage: 0), 0, 1.5f);
        foreach (var e in mine)
            scene.Add(e);
        scene.Add(theirs);
        var (game, input, _) = Commanded(scene);
        theirs.GetComponent<Unit>()!.Hold();

        input.MouseWorld = new Vector2(-2, -1);
        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, Frame);
        input.MouseWorld = new Vector2(1, 2);
        Run(game, input, Frame);
        input.SetMouseButton(MouseButton.Left, false);
        Run(game, input, Frame);

        Assert.Equal([mine[0], mine[1]], game.Selected.OrderBy(e => e.Transform.Position.X));
        Assert.True(mine[0].GetComponent<Unit>()!.IsSelected);

        Click(game, input, new Vector2(-3, -4), MouseButton.Right);
        Assert.All(game.Selected, e => Assert.Equal(UnitOrder.Move, e.GetComponent<Unit>()!.Order));
        Assert.Equal(UnitOrder.Idle, mine[2].GetComponent<Unit>()!.Order);

        // Right-clicking an enemy attacks it.
        Click(game, input, new Vector2(0, 1.5f), MouseButton.Right);
        Assert.All(game.Selected, e => Assert.Equal(UnitOrder.Attack, e.GetComponent<Unit>()!.Order));
    }

    [Fact]
    public void Hotkeys_TrainUnits_AndPlaceBuildings()
    {
        var scene = new Scene();
        var team = Team(scene, "Player", "Gold 300");
        var hall = At(Hall(), 0, 0);
        hall.AddComponent<Producer>().Trains = "Soldier";
        scene.Add(hall);
        var worker = At(WorkerUnit(), 2.5f, 0);
        scene.Add(worker);
        var (game, input, _) = Commanded(scene);

        Click(game, input, new Vector2(0, 0));
        Assert.Equal([hall], game.Selected);
        input.SetKey(Key.S, true);
        Run(game, input, Frame);
        input.SetKey(Key.S, false);
        Assert.Single(hall.GetComponent<Producer>()!.Queue);
        Assert.Equal(240, team.GetComponent<Faction>()!.Amount("Gold"));

        Click(game, input, new Vector2(2.5f, 0));
        Assert.Equal([worker], game.Selected);
        input.SetKey(Key.F, true);
        Run(game, input, Frame);
        input.SetKey(Key.F, false);
        Click(game, input, new Vector2(6, 3));
        var farm = Assert.Single(game.FindAll<Building>(), b => b.Entity!.Name == "Farm");
        Assert.Equal(new Vector2(6, 3), Flat(farm.Entity!));
        Assert.Equal(UnitOrder.Build, worker.GetComponent<Unit>()!.Order);
    }

    [Fact]
    public void ControlGroups_RememberASelection()
    {
        var scene = new Scene();
        var a = At(Soldier(), 0, 0);
        var b = At(Soldier(), 3, 0);
        scene.Add(a);
        scene.Add(b);
        var (game, input, _) = Commanded(scene);

        Click(game, input, new Vector2(0, 0));
        input.SetKey(Key.LeftControl, true);
        input.SetKey(Key.D1, true);
        Run(game, input, Frame);
        input.SetKey(Key.D1, false);
        input.SetKey(Key.LeftControl, false);
        Click(game, input, new Vector2(3, 0));
        Assert.Equal([b], game.Selected);

        input.SetKey(Key.D1, true);
        Run(game, input, Frame);
        input.SetKey(Key.D1, false);
        Assert.Equal([a], game.Selected);
    }

    [Fact]
    public void ArrowKeys_PanTheCamera_WithinTheMap()
    {
        var scene = new Scene();
        var (game, input, camera) = Commanded(scene);

        input.SetKey(Key.Right, true);
        Run(game, input, 5);
        input.SetKey(Key.Right, false);

        var x = camera.Transform.Position.X;
        Assert.True(x > 3, "the camera didn't move");
        Assert.True(x + 8 * game.ViewAspect <= 20.01f, "the camera went past the map's edge");
    }

    // ----- Fog, winning and losing -----

    [Fact]
    public void FogOfWar_HidesEnemiesTheTeamCantSee()
    {
        var scene = new Scene();
        scene.Add(At(Soldier(), -10, 0));
        var near = At(Soldier("Enemy", damage: 0), -7, 0);
        var far = At(Soldier("Enemy", damage: 0), 10, 0);
        scene.Add(near);
        scene.Add(far);
        var (game, input, camera) = Commanded(scene);
        camera.AddComponent<FogOfWar>();
        Run(game, input, 0.3);

        Assert.True(game.IsVisible(new Vector2(-10, 0)));
        Assert.False(game.IsVisible(new Vector2(10, 0)));
        var frame = SceneRenderer.Build(game, 16f / 9);
        Assert.Contains(frame.Sprites, d => d.Entity == near);
        Assert.DoesNotContain(frame.Sprites, d => d.Entity == far);
    }

    [Fact]
    public void DestroyingTheLastEnemyBuilding_Wins()
    {
        var scene = new Scene();
        Team(scene, "Player", "Gold 0");
        var enemy = Team(scene, "Enemy", "Gold 0");
        scene.Add(At(Hall(), -10, 0));
        var theirs = At(Hall("Enemy"), 4, 0);
        theirs.GetComponent<Health>()!.Max = 10;
        scene.Add(theirs);
        var victory = new Entity("Victory") { IsEnabled = false };
        victory.AddComponent<Panel>().PauseGame = false;
        scene.Add(victory);
        var soldier = At(Soldier(damage: 5), 1, 0);
        scene.Add(soldier);
        var (game, input, _) = Commanded(scene);
        var defeated = new List<object?>();
        game.On("Defeated", defeated.Add);

        soldier.GetComponent<Unit>()!.Attack(theirs);
        Run(game, input, 4);

        Assert.Null(theirs.Scene);
        Assert.True(enemy.GetComponent<Faction>()!.IsDefeated);
        Assert.Equal("Enemy", Assert.Single(defeated));
        Assert.True(victory.IsEnabled);
    }

    [Fact]
    public void TheHud_ShowsResourcesSupplyAndTheCommandCard()
    {
        var scene = new Scene();
        Team(scene, "Player", "Gold 120, Wood 30");
        scene.Add(At(Hall(), 0, 0));
        var worker = At(WorkerUnit(), 3, 0);
        scene.Add(worker);
        var (game, input, _) = Commanded(scene);
        game.Select([worker]);
        Run(game, input, Frame);

        var texts = SceneRenderer.Build(game, 16f / 9).Sprites.Select(d => d.Text).OfType<string>().ToList();
        Assert.Contains(texts, t => t.Contains("Gold 120") && t.Contains("Wood 30") && t.Contains("Supply 1 / 5"));
        Assert.Contains("Farm", texts);
        Assert.Contains("Worker", texts);
    }

    // ----- Sharing a scene -----

    // A commander on its own camera over a map of its own: 20 by 20 round `center`.
    private static Entity CommanderAt(Scene scene, string name, Vector2 center, string team = "Player", Viewport? viewport = null)
    {
        var camera = new Entity(name);
        camera.Transform.Position = new Vector3(center, 0);
        camera.AddComponent<Camera>().Size = 6;
        var commander = camera.AddComponent<Commander>();
        (commander.EdgeScroll, commander.MapCenter, commander.MapSize, commander.Team) = (false, center, new Vector2(20, 20), team);
        if (viewport is not null)
            camera.AddComponent(viewport);
        scene.Add(camera);
        return camera;
    }

    [Fact]
    public void TwoCommanders_OnMapsOfTheirOwn_PlayGamesOfTheirOwn()
    {
        var scene = new Scene();
        Team(scene, "Player", "Gold 0");
        var west = CommanderAt(scene, "West", new Vector2(-50, 0));
        var east = CommanderAt(scene, "East", new Vector2(50, 0));
        west.AddComponent<FogOfWar>();
        var westSoldier = At(Soldier(), -50, 0);
        var eastSoldier = At(Soldier(), 50, 0);
        var stranger = At(Soldier("Enemy", damage: 0), 0, 0);
        scene.Add(westSoldier);
        scene.Add(eastSoldier);
        scene.Add(stranger);
        var input = new InputState();
        var game = NewGame(scene, input);
        Run(game, input, 0.3);

        var strategy = StrategyGame.Of(game);
        Assert.Equal(2, strategy.Games.Count);
        Assert.Equal([westSoldier], strategy.Games[0].Units.Select(u => u.Entity));
        Assert.Equal([eastSoldier], strategy.Games[1].Units.Select(u => u.Entity));

        // Each walks on its own map.
        eastSoldier.GetComponent<Unit>()!.MoveTo(new Vector2(55, 5));
        Run(game, input, 3);
        Assert.True(Vector2.Distance(Flat(eastSoldier), new Vector2(55, 5)) < 0.3f);

        // The west's fog hides the west's enemies only: one off its map is no business of its fog.
        var frame = SceneRenderer.Build(game, 16f / 9);
        Assert.Contains(frame.Sprites, d => d.Entity == stranger);
        Assert.Null(strategy.Games[1].Fog);
        Assert.NotNull(strategy.Games[0].Fog);
    }

    [Fact]
    public void ACommanderInAViewport_ShowsItsHudThere_AndTheMainViewHasNone()
    {
        var scene = new Scene();
        Team(scene, "Player", "Gold 120");
        var main = new Entity("Main Camera");
        main.AddComponent<Camera>().Size = 5;
        scene.Add(main);
        CommanderAt(scene, "Strategy Camera", new Vector2(100, 0), viewport: new Viewport { Left = 0.5f, Top = 0, Width = 0.5f, Height = 1 });
        scene.Add(At(Hall(), 100, 0));
        var input = new InputState();
        var game = NewGame(scene, input);
        Run(game, input, 0.1);

        var frame = SceneRenderer.Build(game, 16f / 9);
        var view = Assert.Single(frame.Views).Frame;
        Assert.Contains(view.Sprites.Select(d => d.Text).OfType<string>(), t => t.Contains("Gold 120"));
        Assert.DoesNotContain(frame.Sprites.Select(d => d.Text).OfType<string>(), t => t.Contains("Gold 120"));
    }

    [Fact]
    public void WithNoCommanderOnTheirMap_UnitsAreNoStrategyGamesBusiness()
    {
        var scene = new Scene();
        CommanderAt(scene, "Commander", new Vector2(-50, 0));
        var loose = At(Soldier(), 50, 0);
        scene.Add(loose);
        var input = new InputState();
        var game = NewGame(scene, input);
        Run(game, input, 0.1);

        loose.GetComponent<Unit>()!.MoveTo(new Vector2(55, 0));
        Run(game, input, 1);
        Assert.Equal(new Vector2(50, 0), Flat(loose));
        Assert.Empty(StrategyGame.Of(game).Games[0].Units);
    }

    // ----- The computer -----

    [Fact]
    public void Opponents_KeepTheirWorkersBusy_AndTrainMore()
    {
        var scene = new Scene();
        var enemy = Team(scene, "Enemy", "Gold 200");
        var opponent = enemy.AddComponent<Opponent>();
        opponent.Workers = 3;
        opponent.Builds = "";
        opponent.Army = "";
        var hall = At(Hall("Enemy"), 0, 0);
        hall.AddComponent<Producer>().Trains = "Worker";
        hall.GetComponent<Building>()!.Supply = 10;
        scene.Add(hall);
        scene.Add(Mine(new Vector2(5, 0), 1000));
        scene.Add(At(WorkerUnit("Enemy"), 2, 2));
        var input = new InputState();
        var game = NewGame(scene, input);

        Run(game, input, 15);

        var workers = game.FindAll<Worker>().Where(w => w.Entity!.GetComponent<Unit>()!.Team == "Enemy").ToList();
        Assert.Equal(3, workers.Count);
        Assert.All(workers, w => Assert.Contains(w.Entity!.GetComponent<Unit>()!.Order, new[] { UnitOrder.Gather, UnitOrder.Return }));
    }
}
