using System.Globalization;
using System.Numerics;
using Griff.Audio;
using Griff.Scenes;

namespace Griff.Projects;

// The Shooter template: a first-person shooter through a dark base. The level is an ordinary tilemap painted top-down;
// the camera's First Person View stands it up around the player. Enemies chase and shoot, doors slide open, a blue key
// opens the way out, and a shotgun waits in the armory. All of it no-code components; the art is painted by TemplateArt.
public static partial class ProjectTemplates
{
    private sealed class ShooterTemplate : ProjectTemplate
    {
        public override string Description => "Fight your way out of a dark base in first person: grunts that charge, robots that throw fireballs, sliding doors, a blue key for the way out and a shotgun in the armory. The level is a tilemap you paint top-down. WASD to move, the mouse or arrows to turn, click to fire, 1 and 2 to change weapon.";

        public override int Order => 50;

        protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Tense, seed: 8) with { Volume = 0.5f };

        protected override Scene BuildStartScene() => Shooter();

        protected override Dictionary<string, Entity> BuildPrefabs() => ShooterPrefabs();

        public override IReadOnlyDictionary<string, byte[]> Images() => TemplateArt.ShooterFiles();
    }

    private static readonly Color ShooterGreen = Color.FromBytes(0x57, 0xD3, 0x6B);
    private static readonly Color ShooterBlue = Color.FromBytes(0x8C, 0xB8, 0xFF);
    private static readonly Color LampLight = Color.FromBytes(0xFF, 0xC8, 0x80);

    /// <summary>How many enemies the Shooter template's level has, for its kill counter.</summary>
    internal const int ShooterEnemies = 10;

    private static Scene Shooter()
    {
        var scene = new Scene();

        // The camera looks through the player's eyes; it only places the HUD, so where it is doesn't matter.
        var camera = new Entity("Main Camera");
        camera.Transform.Position = new Vector3(12, -8, 0);
        var view = camera.AddComponent<Camera>();
        view.Size = 5;
        view.Background = Color.Black;
        view.Ambient = new Color(0.55f, 0.55f, 0.62f);
        camera.AddComponent<FirstPersonView>();
        scene.Add(camera);

        scene.Add(ShooterLevel());
        scene.Add(ShooterPlayer(new Vector2(3.5f, -3.5f)));

        // Doors slide into the wall when the player comes near; the blue one only with the blue key.
        var doors = new Entity("Doors");
        scene.Add(doors);
        foreach (var part in SlidingDoor("Hall Door", new Vector2(7.5f, -3.5f), "0 1", new Vector2(2.6f, 1.6f), key: null, Color.White))
            scene.Add(part, doors);
        foreach (var part in SlidingDoor("Blue Door", new Vector2(18.5f, -13.5f), "1 0", new Vector2(1.6f, 2.6f), key: "Key", ShooterBlue))
            scene.Add(part, doors);

        var lights = new Entity("Lamps");
        scene.Add(lights);
        foreach (var at in new[] { new Vector2(3.5f, -2.5f), new Vector2(10.5f, -3.5f), new Vector2(19.5f, -3.5f), new Vector2(5, -9.5f), new Vector2(14, -9.5f), new Vector2(20.5f, -8.5f) })
            scene.Add(Lamp(at, LampLight), lights);
        scene.Add(Lamp(new Vector2(18.5f, -15.5f), ShooterGreen), lights);

        var props = new Entity("Barrels");
        scene.Add(props);
        foreach (var at in new[] { new Vector2(21.5f, -1.5f), new Vector2(21.5f, -5.5f), new Vector2(13, -11.5f), new Vector2(1.5f, -1.5f) })
            scene.Add(ShooterBarrel(at), props);

        var enemies = new Entity("Enemies");
        scene.Add(enemies);
        foreach (var at in new[] { new Vector2(15, -2.5f), new Vector2(20, -4.5f), new Vector2(2.5f, -11.5f), new Vector2(13, -8), new Vector2(20, -11), new Vector2(21, -15.5f) })
            scene.Add(Grunt(at), enemies);
        foreach (var at in new[] { new Vector2(21, -3), new Vector2(7, -10.5f), new Vector2(21.5f, -8), new Vector2(16, -15.5f) })
            scene.Add(Gunner(at), enemies);

        var pickups = new Entity("Pickups");
        scene.Add(pickups);
        scene.Add(ShooterPickup("Bullets", new Vector2(5.5f, -1.5f), TemplateArt.Bullets, item: "Bullets", amount: 12), pickups);
        scene.Add(ShooterPickup("Bullets", new Vector2(9, -5.5f), TemplateArt.Bullets, item: "Bullets", amount: 12), pickups);
        scene.Add(ShooterPickup("Shotgun", new Vector2(8.5f, -8.5f), TemplateArt.Shotgun, item: "Shotgun", amount: 1, size: new Vector2(0.8f, 0.4f)), pickups);
        scene.Add(ShooterPickup("Shells", new Vector2(8.5f, -11.5f), TemplateArt.Shells, item: "Shells", amount: 8), pickups);
        scene.Add(ShooterPickup("Shells", new Vector2(11.5f, -7.5f), TemplateArt.Shells, item: "Shells", amount: 8), pickups);
        scene.Add(ShooterPickup("Medkit", new Vector2(1.5f, -12.5f), TemplateArt.Medkit, heal: 4), pickups);
        scene.Add(ShooterPickup("Medkit", new Vector2(9, -1.5f), TemplateArt.Medkit, heal: 4), pickups);
        scene.Add(ShooterPickup("Medkit", new Vector2(11.5f, -12.5f), TemplateArt.Medkit, heal: 4), pickups);
        var key = ShooterPickup("Blue Key", new Vector2(22, -12.5f), TemplateArt.Key, item: "Key", amount: 1);
        var bob = key.AddComponent<Billboard>();
        bob.Lift = 0.2f;
        bob.Bob = 0.12f;
        scene.Add(key, pickups);

        // Reaching the exit sign finishes the level.
        var exit = new Entity("Exit");
        exit.Transform.Position = new Vector3(18.5f, -16.4f, 0);
        var exitArea = exit.AddComponent<BoxCollider>();
        exitArea.Size = new Vector2(6, 1.2f);
        exitArea.IsTrigger = true;
        var reached = exit.AddComponent<Switch>();
        reached.Mode = SwitchMode.Button;
        reached.Targets = "Victory";
        reached.OnTint = Color.Transparent;
        scene.Add(exit);

        AddShooterHud(scene);
        AddPauseMenu(scene, menuScene: null);
        scene.Add(EndPanel("Victory", "Level Complete!", "You fought your way out of the base.", ShooterGreen));
        return scene;
    }

    private static Dictionary<string, Entity> ShooterPrefabs() => new Dictionary<string, Entity>
    {
        ["Prefabs/Fireball.prefab"] = Fireball(),
        ["Prefabs/Puff.prefab"] = Puff(),
        ["Prefabs/Grunt.prefab"] = Grunt(Vector2.Zero),
        ["Prefabs/Gunner.prefab"] = Gunner(Vector2.Zero),
    };

    // The walls, painted top-down: each room's edge in its own tiles, doorways cut through, pillars and a hazard block.
    // '.' is floor; the numbers are tiles of the wall sheet (see TemplateArt).
    private static Entity ShooterLevel()
    {
        const int width = 24, height = 18;
        var grid = new char[height, width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                grid[y, x] = Tilemap.Empty;
        }

        void Room(int x0, int y0, int x1, int y1, int tile)
        {
            for (var x = x0; x <= x1; x++)
                (grid[y0, x], grid[y1, x]) = (Tile(tile), Tile(tile));
            for (var y = y0; y <= y1; y++)
                (grid[y, x0], grid[y, x1]) = (Tile(tile), Tile(tile));
        }

        void Set(int x, int y, int tile) => grid[y, x] = tile < 0 ? Tilemap.Empty : Tile(tile);

        Room(0, 0, 7, 6, TemplateArt.StoneTile); // the start
        Room(7, 0, 23, 6, TemplateArt.BrickTile); // the hall
        Room(0, 6, 10, 13, TemplateArt.PanelTile); // the armory
        Room(10, 6, 23, 13, TemplateArt.MossTile); // the cave
        Room(14, 13, 23, 17, TemplateArt.PanelTile); // the way out

        Set(7, 3, -1); // the hall door
        Set(3, 6, -1); // start to armory
        Set(4, 6, -1);
        Set(15, 6, -1); // hall to cave
        Set(10, 10, -1); // armory to cave
        Set(10, 11, -1);
        Set(18, 13, -1); // the blue door
        foreach (var (x, y) in new[] { (12, 2), (12, 4), (17, 2), (17, 4) })
            Set(x, y, TemplateArt.StoneTile);
        foreach (var y in new[] { 8, 9, 10 })
            Set(0, y, TemplateArt.ComputerTile);
        Set(16, 9, TemplateArt.HazardTile);
        Set(17, 9, TemplateArt.HazardTile);
        Set(5, 6, TemplateArt.WoodTile);
        Set(6, 6, TemplateArt.WoodTile);
        for (var x = 17; x <= 20; x++)
            Set(x, 17, TemplateArt.ExitTile);

        var rows = new List<string>();
        for (var y = 0; y < height; y++)
            rows.Add(new string(Enumerable.Range(0, width).Select(x => grid[y, x]).ToArray()));

        var entity = new Entity("Walls");
        var tilemap = entity.AddComponent<Tilemap>();
        tilemap.Image = TemplateArt.Walls;
        tilemap.Columns = TemplateArt.WallColumns;
        tilemap.Rows = TemplateArt.WallRows;
        tilemap.Tiles = rows;
        return entity;

        static char Tile(int tile) => (char)('0' + tile);
    }

    private static Entity ShooterPlayer(Vector2 at)
    {
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(at, 0);
        var sprite = player.AddComponent<Sprite>();
        sprite.Image = TemplateArt.Player;
        sprite.Size = new Vector2(0.7f);
        sprite.Order = 5;
        var collider = player.AddComponent<CircleCollider>();
        collider.Radius = 0.28f;
        collider.Layer = "Player";
        player.AddComponent<Body>().GravityScale = 0;
        player.AddComponent<FirstPersonMovement>();
        var health = player.AddComponent<Health>();
        health.Max = 10;
        health.Current = 10;
        health.InvincibleTime = 0.6f;
        health.FlashOnHit = false;
        health.BlinkWhileInvincible = false;
        health.Respawn = true;
        health.RespawnDelay = 1.5f;
        player.AddComponent<Inventory>().Items = ["Bullets 24"];

        var pistol = player.AddComponent<Weapon>();
        pistol.Name = "Pistol";
        pistol.Slot = 1;
        pistol.Ammo = "Bullets";
        pistol.Damage = 1;
        pistol.FireRate = 3.5f;
        pistol.Spread = 1.5f;
        pistol.Image = TemplateArt.PistolView;
        pistol.FireImage = TemplateArt.PistolFire;
        pistol.Color = Color.White;
        pistol.ImpactEffect = "Puff";

        var shotgun = player.AddComponent<Weapon>();
        shotgun.Name = "Shotgun";
        shotgun.Slot = 2;
        shotgun.NeedsItem = "Shotgun";
        shotgun.Ammo = "Shells";
        shotgun.Damage = 1;
        shotgun.Pellets = 7;
        shotgun.Spread = 14;
        shotgun.FireRate = 1.2f;
        shotgun.Knockback = 5;
        shotgun.Range = 18;
        shotgun.Kick = 0.25f;
        shotgun.Shake = 0.12f;
        shotgun.Size = 0.55f;
        shotgun.Image = TemplateArt.ShotgunView;
        shotgun.FireImage = TemplateArt.ShotgunFire;
        shotgun.Color = Color.White;
        shotgun.ImpactEffect = "Puff";
        shotgun.Sound = TemplateSounds.Explode;
        return player;
    }

    // A brute that charges when it sees you and hurts on contact.
    private static Entity Grunt(Vector2 at)
    {
        var entity = ShooterEnemy("Grunt", at, TemplateArt.Grunt, health: 3);
        var chase = entity.AddComponent<Chase>();
        chase.Speed = 2.4f;
        chase.Range = 9;
        chase.NeedsSight = true;
        chase.Around = "Walls";
        chase.FlipSprite = false;
        var bite = entity.AddComponent<DamageOnTouch>();
        bite.Layers = "Player";
        bite.Amount = 1;
        bite.Knockback = 4;
        return entity;
    }

    // A robot that keeps its distance and lobs fireballs when it has a clear shot.
    private static Entity Gunner(Vector2 at)
    {
        var entity = ShooterEnemy("Gunner", at, TemplateArt.Gunner, health: 4);
        var chase = entity.AddComponent<Chase>();
        chase.Speed = 1.6f;
        chase.Range = 11;
        chase.StopDistance = 4.5f;
        chase.NeedsSight = true;
        chase.Around = "Walls";
        chase.FlipSprite = false;
        var gun = entity.AddComponent<Shooter>();
        gun.Prefab = "Fireball";
        gun.Target = "Player";
        gun.Range = 10;
        gun.NeedsSight = true;
        gun.Interval = 1.8f;
        gun.Delay = 1;
        gun.Speed = 6;
        return entity;
    }

    private static Entity ShooterEnemy(string name, Vector2 at, string image, float health)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Image = image;
        sprite.Size = new Vector2(0.8f, 1);
        sprite.Order = 4;
        var collider = entity.AddComponent<CircleCollider>();
        collider.Radius = 0.3f;
        collider.Layer = "Enemy";
        entity.AddComponent<Body>().GravityScale = 0;
        var hp = entity.AddComponent<Health>();
        hp.Max = health;
        hp.Current = health;
        hp.InvincibleTime = 0.05f;
        hp.BlinkWhileInvincible = false;
        hp.DeathMessage = "EnemyDied";
        return entity;
    }

    // An enemy's shot: it flies at chest height, glows on the walls it passes, and bursts on whatever it hits.
    private static Entity Fireball()
    {
        var entity = new Entity("Fireball");
        var sprite = entity.AddComponent<Sprite>();
        sprite.Image = TemplateArt.Fireball;
        sprite.Size = new Vector2(0.35f);
        sprite.Order = 6;
        var billboard = entity.AddComponent<Billboard>();
        billboard.Lift = 0.3f;
        var collider = entity.AddComponent<CircleCollider>();
        collider.Radius = 0.12f;
        collider.Layer = "Shot";
        collider.IgnoreLayers = "Enemy, Shot";
        entity.AddComponent<Body>().GravityScale = 0;
        var burn = entity.AddComponent<DamageOnTouch>();
        burn.Layers = "Player";
        burn.Amount = 2;
        burn.DestroyOnHit = true;
        burn.DestroyOnWall = true;
        entity.AddComponent<Lifetime>().Seconds = 4;
        var glow = entity.AddComponent<Light>();
        glow.Radius = 2;
        glow.Color = Color.FromBytes(0xFF, 0x80, 0x30);
        return entity;
    }

    // Sparks where a bullet hits.
    private static Entity Puff()
    {
        var entity = new Entity("Puff");
        var sparks = entity.AddComponent<ParticleEmitter>();
        sparks.Emitting = false;
        sparks.BurstOnStart = 8;
        sparks.Lifetime = 0.3f;
        sparks.Speed = 1.2f;
        sparks.Spread = 360;
        sparks.StartSize = 0.09f;
        sparks.EndSize = 0;
        sparks.StartColor = Color.FromBytes(0xFF, 0xE0, 0x90);
        sparks.EndColor = Color.FromBytes(0xFF, 0x60, 0x20, 0);
        sparks.Order = 10;
        sparks.DestroyWhenFinished = true;
        return entity;
    }

    private static Entity ShooterPickup(string name, Vector2 at, string image, string? item = null, int amount = 1, float heal = 0, Vector2? size = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Image = image;
        sprite.Size = size ?? new Vector2(0.45f);
        sprite.Order = 3;
        var area = entity.AddComponent<CircleCollider>();
        area.Radius = 0.4f;
        area.IsTrigger = true;
        var pickup = entity.AddComponent<Pickup>();
        pickup.Layers = "Player";
        pickup.Message = null;
        pickup.Item = item;
        pickup.Amount = amount;
        pickup.Heal = heal;
        return entity;
    }

    // A barrel to hide behind, which bursts after a couple of shots.
    private static Entity ShooterBarrel(Vector2 at)
    {
        var entity = new Entity("Barrel");
        entity.Transform.Position = new Vector3(at, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Image = TemplateArt.Barrel;
        sprite.Size = new Vector2(0.6f, 0.8f);
        sprite.Order = 3;
        entity.AddComponent<CircleCollider>().Radius = 0.3f;
        var health = entity.AddComponent<Health>();
        health.Max = 2;
        health.Current = 2;
        health.InvincibleTime = 0;
        health.BlinkWhileInvincible = false;
        return entity;
    }

    // A lamp hanging from the ceiling, lighting the walls around it.
    private static Entity Lamp(Vector2 at, Color color)
    {
        var entity = new Entity("Lamp");
        entity.Transform.Position = new Vector3(at, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Image = TemplateArt.Lamp;
        sprite.Size = new Vector2(0.32f);
        sprite.Tint = Color.Lerp(Color.White, color, 0.4f);
        sprite.Order = 2;
        entity.AddComponent<Billboard>().Lift = 0.6f;
        var light = entity.AddComponent<Light>();
        light.Radius = 5;
        light.Color = color;
        light.Intensity = 0.9f;
        return entity;
    }

    // A door that slides along `path` into the wall when the player comes near: a box of wall (a Block), and a sensor
    // around it (a Switch) that sets its Mover going. With a key, only someone carrying one opens it.
    private static IEnumerable<Entity> SlidingDoor(string name, Vector2 at, string path, Vector2 sensor, string? key, Color tint)
    {
        var door = new Entity(name);
        door.Transform.Position = new Vector3(at, 0);
        var sprite = door.AddComponent<Sprite>();
        sprite.Image = TemplateArt.Door;
        sprite.Tint = tint;
        door.AddComponent<BoxCollider>().Size = Vector2.One;
        door.AddComponent<Block>();
        var mover = door.AddComponent<Mover>();
        mover.Points = path;
        mover.Mode = MoverMode.Once;
        mover.Speed = 1.6f;
        mover.Wait = 0;
        yield return door;

        var trigger = new Entity(name + " Sensor");
        trigger.Transform.Position = new Vector3(at, 0);
        var area = trigger.AddComponent<BoxCollider>();
        area.Size = sensor;
        area.IsTrigger = true;
        var opener = trigger.AddComponent<Switch>();
        opener.Mode = SwitchMode.Button;
        opener.Targets = name;
        opener.Key = key;
        opener.OnTint = Color.Transparent;
        yield return trigger;
    }

    // Health, ammo, kills and the blue key, pinned to the corners; and how to play, fading away after a while.
    private static void AddShooterHud(Scene scene)
    {
        var hud = new Entity("HUD");
        scene.Add(hud);

        var label = Label("Health Label", Vector2.Zero, "HEALTH", 0.35f, Color.FromBytes(0xB4, 0xB9, 0xC4));
        label.GetComponent<Text>()!.Align = TextAlign.Left;
        Pin(label, Anchor.BottomLeft, new Vector2(0.45f, 1.05f));

        var bar = new Entity("Health Bar");
        var health = bar.AddComponent<Bar>();
        health.Track = "Player";
        health.Size = new Vector2(3.2f, 0.32f);
        health.Fill = Color.FromBytes(0xE5, 0x53, 0x4B);
        Pin(bar, Anchor.BottomLeft, new Vector2(0.45f + 1.6f, 0.55f));

        Pin(ItemCounter("Bullets", "Bullets  {0}", TextAlign.Right), Anchor.BottomRight, new Vector2(-0.45f, 1.05f));
        Pin(ItemCounter("Shells", "Shells  {0}", TextAlign.Right), Anchor.BottomRight, new Vector2(-0.45f, 0.5f));
        Pin(ItemCounter("Key", "Blue key  {0}", TextAlign.Left), Anchor.TopLeft, new Vector2(0.45f, -1.1f));

        var kills = Label("Kills", Vector2.Zero, $"Kills  0 / {ShooterEnemies}", 0.45f, Color.FromBytes(0xE6, 0xE8, 0xEC));
        kills.GetComponent<Text>()!.Align = TextAlign.Left;
        var counter = kills.AddComponent<Counter>();
        counter.Message = "EnemyDied";
        counter.Format = "Kills  {0} / " + ShooterEnemies;
        Pin(kills, Anchor.TopLeft, new Vector2(0.45f, -0.55f));

        var hint = Label("How To Play", Vector2.Zero, "WASD to move   Mouse or arrows to turn   Click to fire   1 2 to change weapon", 0.32f,
            Color.FromBytes(0xE6, 0xE8, 0xEC));
        var fade = hint.AddComponent<Lifetime>();
        fade.Seconds = 12;
        fade.FadeOut = 2;
        Pin(hint, Anchor.Top, new Vector2(0, -0.55f));

        void Pin(Entity entity, Anchor anchor, Vector2 offset)
        {
            var pin = entity.AddComponent<ScreenAnchor>();
            pin.Anchor = anchor;
            pin.Offset = offset;
            scene.Add(entity, hud);
        }

        static Entity ItemCounter(string item, string format, TextAlign align)
        {
            var entity = Label(item + " Counter", Vector2.Zero, string.Format(CultureInfo.InvariantCulture, format, 0), 0.45f, Color.FromBytes(0xE6, 0xE8, 0xEC));
            entity.GetComponent<Text>()!.Align = align;
            var counter = entity.AddComponent<Counter>();
            counter.Message = null;
            counter.Item = item;
            counter.Format = format;
            return entity;
        }
    }
}
