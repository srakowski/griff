using System.Numerics;
using Griff.Input;
using Griff.Modules.FirstPerson;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public sealed class FirstPersonTests
{
    private const double Frame = 1 / 60.0;
    private const float Aspect = 16f / 9f;

    // A camera looking through "Player", who stands at (0.5, -0.5) facing right (+X) down a corridor, with a wall
    // `distance` cells ahead: a tilemap at the origin, one row of floor between rows of wall.
    private static (Game Game, InputState Input, Entity Player, Tilemap Walls) Corridor(int distance = 5, int columns = 64)
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>();
        var view = camera.AddComponent<FirstPersonView>();
        view.Columns = columns;
        view.Minimap = false;
        view.Fog = Color.Transparent;
        view.Crosshair = Color.Transparent;
        scene.Add(camera);

        var walls = new Entity("Walls");
        walls.Transform.Position = new Vector3(0, 1, 0);
        var tilemap = walls.AddComponent<Tilemap>();
        var floor = new string('.', distance) + "0";
        tilemap.Tiles = [new string('0', distance + 1), floor, new string('0', distance + 1)];
        scene.Add(walls);

        var player = new Entity("Player");
        player.Transform.Position = new Vector3(0.5f, -0.5f, 0);
        player.AddComponent<CircleCollider>().Radius = 0.25f;
        player.AddComponent<Body>().GravityScale = 0;
        player.AddComponent<FirstPersonMovement>();
        scene.Add(player);

        var input = new InputState();
        return (new Game(scene, input), input, player, tilemap);
    }

    private static Entity Target(Scene scene, Vector2 at, float health = 3)
    {
        var entity = new Entity("Target");
        entity.Transform.Position = new Vector3(at, 0);
        entity.AddComponent<Sprite>().Size = new Vector2(0.8f, 1);
        entity.AddComponent<CircleCollider>().Radius = 0.3f;
        var hp = entity.AddComponent<Health>();
        hp.Max = health;
        hp.Current = health;
        hp.InvincibleTime = 0.05f;
        scene.Add(entity);
        return entity;
    }

    private static void Run(Game game, InputState input, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Frame)
        {
            game.Tick(Frame);
            input.EndFrame();
        }
    }

    [Fact]
    public void Walls_StandUpOutOfTheMap_ShorterTheFurtherAway()
    {
        var (near, _, _, _) = Corridor(distance: 2);
        var (far, _, _, _) = Corridor(distance: 6);

        // The strip in the middle of the screen is the wall straight ahead: twice as far, half as tall.
        var nearStrip = MiddleStrip(SceneRenderer.Build(near, Aspect), 64);
        var farStrip = MiddleStrip(SceneRenderer.Build(far, Aspect), 64);
        Assert.True(nearStrip.World.M22 > farStrip.World.M22 * 2.5f, $"{nearStrip.World.M22} vs {farStrip.World.M22}");

        // The wall 1.5 units ahead: as tall on screen (out of the view's 10 units) as perspective says.
        var focal = 1 / MathF.Tan(70 * MathF.PI / 360);
        Assert.Equal(1 / 1.5f * focal * Aspect * 5, nearStrip.World.M22, 2);
    }

    // The columns' strips are drawn left to right after the floor and ceiling: find the one in the middle.
    private static SpriteDraw MiddleStrip(RenderFrame frame, int columns)
    {
        var strips = frame.Sprites.Skip(80).Take(columns + 10).Where(d => MathF.Abs(d.World.M11) < 0.5f).ToList();
        return strips.OrderBy(d => MathF.Abs(d.World.M41 - frame.Sprites[0].World.M41)).First();
    }

    [Fact]
    public void ASprite_BehindAWall_IsHidden_AndInFront_Shows()
    {
        var (game, _, _, _) = Corridor(distance: 3);
        var front = Target(game.Scene, new Vector2(2.5f, -0.5f));
        var behind = Target(game.Scene, new Vector2(6, -0.5f));

        var frame = SceneRenderer.Build(game, Aspect);

        Assert.Contains(frame.Sprites, d => d.Entity == front);
        Assert.DoesNotContain(frame.Sprites, d => d.Entity == behind);
    }

    [Fact]
    public void TheViewer_IsNotDrawn_InItsOwnView()
    {
        var (game, _, player, _) = Corridor();
        player.AddComponent<Sprite>();

        Assert.DoesNotContain(SceneRenderer.Build(game, Aspect).Sprites, d => d.Entity == player);
    }

    [Fact]
    public void ABlock_IsDrawnAsWall_AndHidesWhatsBehindIt()
    {
        var (game, _, _, _) = Corridor(distance: 6);
        var door = new Entity("Door");
        door.Transform.Position = new Vector3(2.5f, -0.5f, 0);
        door.AddComponent<Sprite>().Tint = Color.Red;
        door.AddComponent<BoxCollider>();
        door.AddComponent<Block>();
        game.Scene.Add(door);
        var behind = Target(game.Scene, new Vector2(4.5f, -0.5f));

        var frame = SceneRenderer.Build(game, Aspect);

        Assert.Contains(frame.Sprites, d => d.Tint.R > 0.5f && d.Tint.G < 0.1f && MathF.Abs(d.World.M11) < 0.5f);
        Assert.DoesNotContain(frame.Sprites, d => d.Entity == behind);
        Assert.DoesNotContain(frame.Sprites, d => d.Entity == door);
    }

    [Fact]
    public void W_WalksTheWayItFaces_AndTheArrowsTurn()
    {
        var (game, input, player, _) = Corridor(distance: 8);

        input.SetKey(Key.W, true);
        Run(game, input, 0.5);
        input.SetKey(Key.W, false);
        Assert.True(player.Transform.Position.X > 1.6f, $"x = {player.Transform.Position.X}");
        Assert.Equal(-0.5f, player.Transform.Position.Y, 2);

        input.SetKey(Key.Left, true);
        Run(game, input, 0.5);
        input.SetKey(Key.Left, false);
        Assert.InRange(player.Transform.Rotation.Z, 60, 90);
    }

    [Fact]
    public void D_Strafes_ToTheRight()
    {
        var (game, input, player, _) = Corridor();
        player.Transform.Rotation = new Vector3(0, 0, 90); // facing up the map: right is +X

        input.SetKey(Key.D, true);
        Run(game, input, 0.3);

        Assert.True(player.Transform.Position.X > 0.9f, $"x = {player.Transform.Position.X}");
    }

    [Fact]
    public void TheMouse_OnlyTurns_WhileTheGameHasIt()
    {
        var (game, input, player, _) = Corridor();
        Run(game, input, Frame);
        Assert.True(input.WantsMouseLock);

        input.AddMouseDelta(new Vector2(100, 0));
        Run(game, input, Frame);
        Assert.Equal(0, player.Transform.Rotation.Z, 3);

        input.IsMouseLocked = true;
        input.AddMouseDelta(new Vector2(100, 0));
        Run(game, input, Frame);
        Assert.Equal(-15, player.Transform.Rotation.Z, 2);
    }

    [Fact]
    public void Firing_HitsWhatsInFront_UsingAmmo()
    {
        var (game, input, player, _) = Corridor(distance: 8);
        var weapon = player.AddComponent<Weapon>();
        weapon.Ammo = "Bullets";
        weapon.Spread = 0;
        player.AddComponent<Inventory>().Items = ["Bullets 2"];
        var target = Target(game.Scene, new Vector2(4.5f, -0.5f));
        var health = target.GetComponent<Health>()!;

        for (var shot = 0; shot < 3; shot++)
        {
            input.SetMouseButton(MouseButton.Left, true);
            Run(game, input, Frame);
            input.SetMouseButton(MouseButton.Left, false);
            Run(game, input, 0.5);
        }

        // Two bullets, two hits; the third press just clicks.
        Assert.Equal(1, health.Current);
        Assert.Equal(0, player.GetComponent<Inventory>()!.Count("Bullets"));
    }

    [Fact]
    public void Firing_DoesntHit_ThroughWalls()
    {
        var (game, input, player, _) = Corridor(distance: 3);
        player.AddComponent<Weapon>().Spread = 0;
        var target = Target(game.Scene, new Vector2(6, -0.5f));

        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, Frame);

        Assert.Equal(3, target.GetComponent<Health>()!.Current);
    }

    [Fact]
    public void AShotgunsPellets_LandTogether_AsOneBigHit()
    {
        var (game, input, player, _) = Corridor(distance: 8);
        var shotgun = player.AddComponent<Weapon>();
        shotgun.Pellets = 6;
        shotgun.Spread = 2;
        var target = Target(game.Scene, new Vector2(2.5f, -0.5f), health: 10);

        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, Frame);

        Assert.Equal(4, target.GetComponent<Health>()!.Current);
    }

    [Fact]
    public void AWeaponThatNeedsAnItem_IsTakenOut_AsSoonAsItsPickedUp_AndNumberKeysSwitch()
    {
        var (game, input, player, _) = Corridor(distance: 8);
        var pistol = player.AddComponent<Weapon>();
        var shotgun = player.AddComponent<Weapon>();
        shotgun.Name = "Shotgun";
        shotgun.Slot = 2;
        shotgun.NeedsItem = "Shotgun";
        Run(game, input, Frame);
        Assert.Same(pistol, game.CurrentWeapon);

        input.SetKey(Key.D2, true);
        Run(game, input, Frame);
        input.SetKey(Key.D2, false);
        Assert.Same(pistol, game.CurrentWeapon);

        var pickup = new Entity("Shotgun");
        pickup.Transform.Position = new Vector3(1.5f, -0.5f, 0);
        var area = pickup.AddComponent<CircleCollider>();
        area.IsTrigger = true;
        area.Radius = 0.4f;
        var item = pickup.AddComponent<Pickup>();
        item.Item = "Shotgun";
        item.Message = null;
        game.Scene.Add(pickup);
        input.SetKey(Key.W, true);
        Run(game, input, 0.5);
        input.SetKey(Key.W, false);
        Assert.Same(shotgun, game.CurrentWeapon);

        input.SetKey(Key.D1, true);
        Run(game, input, Frame);
        Assert.Same(pistol, game.CurrentWeapon);
    }

    [Fact]
    public void AProjectileWeapon_LaunchesItsPrefab()
    {
        var (game, input, player, _) = Corridor(distance: 8);
        game.PrefabLoader = _ =>
        {
            var rocket = new Entity("Rocket");
            rocket.AddComponent<CircleCollider>().Radius = 0.1f;
            rocket.AddComponent<Body>().GravityScale = 0;
            return rocket;
        };
        var launcher = player.AddComponent<Weapon>();
        launcher.Projectile = "Rocket";
        launcher.Spread = 0;

        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, Frame);

        var rocket = Assert.Single(game.FindAll("Rocket"));
        Assert.Equal(launcher.ProjectileSpeed, rocket.GetComponent<Body>()!.Velocity.X, 1);
    }

    [Fact]
    public void BeingHurt_FlashesTheScreen()
    {
        var (game, input, player, _) = Corridor();
        var health = player.AddComponent<Health>();
        Run(game, input, Frame);

        health.Damage(1);
        Run(game, input, Frame);

        Assert.True(game.ScreenFlash.A > 0);
    }

    [Fact]
    public void AHealingPickup_Heals_ButWaitsWhileHealthIsFull()
    {
        var (game, input, player, _) = Corridor(distance: 8);
        var health = player.AddComponent<Health>();
        health.Max = 5;
        health.Current = 5;
        var medkit = new Entity("Medkit");
        medkit.Transform.Position = new Vector3(0.5f, -0.5f, 0);
        var area = medkit.AddComponent<CircleCollider>();
        area.IsTrigger = true;
        medkit.AddComponent<Pickup>().Heal = 2;
        game.Scene.Add(medkit);

        Run(game, input, 0.1);
        Assert.NotNull(game.Find("Medkit"));

        health.Current = 2;
        Run(game, input, 0.1);
        Assert.Null(game.Find("Medkit"));
        Assert.Equal(4, health.Current);
    }

    [Fact]
    public void AShotThatBreaksOnWalls_IsGoneWhenItHitsOne()
    {
        var (game, input, _, _) = Corridor(distance: 3);
        var shot = new Entity("Shot");
        shot.Transform.Position = new Vector3(1.5f, -0.5f, 0);
        shot.AddComponent<CircleCollider>().Radius = 0.1f;
        var body = shot.AddComponent<Body>();
        body.GravityScale = 0;
        body.Velocity = new Vector2(8, 0);
        shot.AddComponent<DamageOnTouch>().DestroyOnWall = true;
        game.Scene.Add(shot);

        Run(game, input, 0.5);

        Assert.Null(game.Find("Shot"));
    }

    [Fact]
    public void AShooterThatNeedsSight_HoldsFire_BehindAWall()
    {
        var (game, input, player, _) = Corridor(distance: 3);
        var spawned = 0;
        game.PrefabLoader = _ =>
        {
            spawned++;
            return new Entity("Bullet");
        };
        var turret = new Entity("Turret");
        turret.Transform.Position = new Vector3(6, -0.5f, 0);
        var shooter = turret.AddComponent<Shooter>();
        shooter.Target = "Player";
        shooter.NeedsSight = true;
        shooter.Interval = 0.1f;
        game.Scene.Add(turret);

        Run(game, input, 1);
        Assert.Equal(0, spawned);

        turret.Transform.Position = new Vector3(2.5f, -0.5f, 0);
        Run(game, input, 1);
        Assert.True(spawned > 0);
    }

    [Fact]
    public void ThePlayersWeapon_IsHeldAtTheBottomOfTheScreen()
    {
        var (game, input, player, _) = Corridor();
        var weapon = player.AddComponent<Weapon>();
        weapon.Image = "Textures/pistol.png";
        Run(game, input, 0.5);

        var frame = SceneRenderer.Build(game, Aspect);
        var held = Assert.Single(frame.Sprites, d => d.Image == "Textures/pistol.png");
        Assert.True(held.World.M42 < -2, $"y = {held.World.M42}");
    }
}
