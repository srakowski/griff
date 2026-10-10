using System.Numerics;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Projects;

public enum ProjectTemplate
{
    Empty,
    Platformer,
    TopDown,
    Arcade,
    Strategy,
    Shooter,
}

/// <summary>Starter content for new projects: a playable scene and the scripts that drive it.</summary>
public static partial class ProjectTemplates
{
    public static string Describe(ProjectTemplate template) => template switch
    {
        ProjectTemplate.Platformer => "Run, jump and dash across platforms collecting coins, ride a lift, bounce off a spring, fight a slime with your sword, and come back at the last checkpoint. Arrow keys or WASD, Space to jump, Shift to dash, X to swing, E to read signs.",
        ProjectTemplate.TopDown => "Explore a dim, torch-lit dungeon in eight directions collecting gems, with a bat that chases you on sight, a sword to fight it with, portals, a crate to push and someone to talk to. Arrow keys or WASD, Shift to dash, X to swing, E to talk.",
        ProjectTemplate.Arcade => "Blast waves of enemies in an arena, with three hearts, a menu, options, game over and high score. WASD to fly, mouse to aim, click to fire.",
        ProjectTemplate.Strategy => "Command an army in real time against the computer: gather gold and wood with workers, put up farms, barracks and towers, train soldiers and archers, and destroy the red base. Click or drag to select, right-click to send, hotkeys on the command card, arrow keys or the minimap to look around.",
        ProjectTemplate.Shooter => "Fight your way out of a dark base in first person: grunts that charge, robots that throw fireballs, sliding doors, a blue key for the way out and a shotgun in the armory. The level is a tilemap you paint top-down. WASD to move, the mouse or arrows to turn, click to fire, 1 and 2 to change weapon.",
        _ => "A scene with a camera. Start from scratch.",
    };

    /// <summary>The start scene for a template. Script components are added as missing components until compiled.</summary>
    internal static Scene CreateScene(ProjectTemplate template) => WithSounds(template, BuildScene(template));

    /// <summary>Sound and music files (relative to Assets) for a template, as WAV bytes.</summary>
    internal static IReadOnlyDictionary<string, byte[]> Sounds(ProjectTemplate template) => TemplateSounds.Files(template);

    /// <summary>Image files (relative to Assets) for a template, as PNG bytes.</summary>
    internal static IReadOnlyDictionary<string, byte[]> Images(ProjectTemplate template) => TemplateArt.Files(template);

    // Templates that come with sounds have them hooked up to everything that makes a noise, and music playing.
    private static Scene WithSounds(ProjectTemplate template, Scene scene)
    {
        if (template == ProjectTemplate.Empty)
            return scene;
        foreach (var entity in scene.Entities)
            TemplateSounds.Wire(entity);
        TemplateSounds.AddMusic(scene);
        return scene;
    }

    private static Scene BuildScene(ProjectTemplate template) => template switch
    {
        ProjectTemplate.Platformer => Platformer(),
        ProjectTemplate.TopDown => TopDown(),
        ProjectTemplate.Arcade => ArcadeMenu(),
        ProjectTemplate.Strategy => Strategy(),
        ProjectTemplate.Shooter => Shooter(),
        _ => Empty(),
    };

    /// <summary>Scenes besides the start scene (relative to Assets) for a template.</summary>
    internal static IReadOnlyDictionary<string, Scene> MoreScenes(ProjectTemplate template) => template switch
    {
        ProjectTemplate.Arcade => new Dictionary<string, Scene>
        {
            ["Scenes/Game.scene"] = WithSounds(template, ArcadeGame()),
            ["Scenes/Options.scene"] = WithSounds(template, ArcadeOptions()),
        },
        _ => new Dictionary<string, Scene>(),
    };

    /// <summary>Script files (relative to Assets) for a template.</summary>
    internal static IReadOnlyDictionary<string, string> Scripts(ProjectTemplate template) => template switch
    {
        ProjectTemplate.Arcade => new Dictionary<string, string>
        {
            ["Scripts/Ship.cs"] = ShipScript,
            ["Scripts/Bullet.cs"] = BulletScript,
            ["Scripts/Enemy.cs"] = EnemyScript,
            ["Scripts/EnemySpawner.cs"] = EnemySpawnerScript,
            ["Scripts/GameOver.cs"] = GameOverScript,
        },
        _ => new Dictionary<string, string>(),
    };

    /// <summary>Prefab files (relative to Assets) for a template.</summary>
    internal static IReadOnlyDictionary<string, Entity> Prefabs(ProjectTemplate template)
    {
        var prefabs = BuildPrefabs(template);
        foreach (var entity in prefabs.Values.SelectMany(p => p.SelfAndDescendants()))
            TemplateSounds.Wire(entity);
        return prefabs;
    }

    private static IReadOnlyDictionary<string, Entity> BuildPrefabs(ProjectTemplate template) => template switch
    {
        ProjectTemplate.Platformer or ProjectTemplate.TopDown => new Dictionary<string, Entity>
        {
            ["Prefabs/Sparkle.prefab"] = Sparkle(),
        },
        ProjectTemplate.Arcade => new Dictionary<string, Entity>
        {
            ["Prefabs/Sparkle.prefab"] = Sparkle(),
            ["Prefabs/Bullet.prefab"] = ArcadeBullet(),
            ["Prefabs/Enemy.prefab"] = ArcadeEnemy(),
        },
        ProjectTemplate.Strategy => StrategyPrefabs(),
        ProjectTemplate.Shooter => ShooterPrefabs(),
        _ => new Dictionary<string, Entity>(),
    };

    // A one-shot burst that cleans itself up; pickups spawn it, in their own color, when collected.
    private static Entity Sparkle()
    {
        var entity = new Entity("Sparkle");
        entity.Transform.Position = new Vector3(0, 0, 0.5f);
        var sparks = entity.AddComponent<ParticleEmitter>();
        sparks.Emitting = false;
        sparks.BurstOnStart = 14;
        sparks.Lifetime = 0.45f;
        sparks.Speed = 4;
        sparks.Spread = 360;
        sparks.StartSize = 0.18f;
        sparks.EndSize = 0;
        sparks.Order = 10;
        sparks.DestroyWhenFinished = true;
        return entity;
    }

    private static Scene Empty()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>();
        scene.Add(camera);
        return scene;
    }

    private static Scene Platformer()
    {
        var scene = new Scene();

        // A vertical dead zone keeps the camera still through a jump; it never shows past the backdrop.
        var follow = AddCameraWithScore(scene);
        follow.DeadZone = new Vector2(0, 3);
        follow.LookAhead = 0.15f;
        follow.StayInside = "Backdrop";

        var backdrop = Box("Backdrop", new Vector2(0, 2), new Vector2(32, 14), Color.FromBytes(0x26, 0x2B, 0x36));
        backdrop.RemoveComponent(backdrop.GetComponent<BoxCollider>()!);
        backdrop.GetComponent<Sprite>()!.Order = -10;
        scene.Add(backdrop);

        // Running, jumping and dashing need no script; its Inspector tunes how it feels.
        var player = Box("Player", new Vector2(-6, -2), new Vector2(0.8f, 1), Color.FromBytes(0xF0, 0x8C, 0x2E));
        player.GetComponent<BoxCollider>()!.Layer = "Player";
        player.AddComponent<Body>();
        player.AddComponent<PlatformerMovement>().Dash = true;
        AddDashTrail(player, minSpeed: 10);
        AddRespawningHealth(player);
        scene.Add(player);
        AddSword(scene, player);

        var level = new Entity("Level");
        scene.Add(level);
        var gray = Color.FromBytes(0x48, 0x4B, 0x55);
        scene.Add(Box("Ground", new Vector2(0, -4.5f), new Vector2(30, 1), gray), level);
        scene.Add(Platform(new Vector2(-1, -1.5f), new Vector2(4, 0.5f), gray), level);
        scene.Add(Platform(new Vector2(4.5f, 0.5f), new Vector2(3, 0.5f), gray), level);
        scene.Add(Box("Wall", new Vector2(-15.5f, 0), new Vector2(1, 10), gray), level);
        scene.Add(Box("Wall", new Vector2(15.5f, 0), new Vector2(1, 10), gray), level);

        // Up on the left: a lift (a one-way platform with a Mover, so you can walk through it at the bottom) to a ledge
        // patrolled by a slime that turns at the edges.
        var lift = Platform(new Vector2(-13, -3.75f), new Vector2(2.5f, 0.5f), Color.FromBytes(0x6B, 0x70, 0x7D));
        lift.Name = "Lift";
        var mover = lift.AddComponent<Mover>();
        mover.Points = "0 6";
        mover.Speed = 1.5f;
        mover.Wait = 1;
        scene.Add(lift, level);
        scene.Add(Platform(new Vector2(-8.5f, 2), new Vector2(5, 0.5f), gray), level);

        var slime = Box("Slime", new Vector2(-8, 2.65f), new Vector2(0.8f, 0.8f), Color.FromBytes(0x57, 0xAB, 0x5A));
        slime.GetComponent<BoxCollider>()!.Layer = "Enemy";
        slime.AddComponent<Body>();
        slime.AddComponent<Patrol>().Speed = 1.5f;
        var touch = slime.AddComponent<DamageOnTouch>();
        touch.Layers = "Player";
        touch.Knockback = 8;
        AddEnemyHealth(slime, hits: 2);
        scene.Add(slime);

        // The lever on the ledge brings out a bridge with two more coins on it; until then the bridge isn't there.
        AddLever(scene, new Vector2(-10.3f, 2.6f), "Bridge");
        var bridge = Platform(new Vector2(-3, 2), new Vector2(5, 0.5f), Color.FromBytes(0x9C, 0x6B, 0x3C));
        bridge.Name = "Bridge";
        scene.Add(bridge, level);
        // Children are placed relative to the bridge: on top of it, either side of its middle.
        scene.Add(Collectible("Coin", new Vector2(-1, 0.75f), Color.FromBytes(0xE3, 0xB3, 0x41)), bridge);
        scene.Add(Collectible("Coin", new Vector2(1, 0.75f), Color.FromBytes(0xE3, 0xB3, 0x41)), bridge);

        // On the right, a spring up to two ledges that give way a moment after you land, each with a coin.
        var spring = Box("Spring", new Vector2(13.5f, -3.75f), new Vector2(1.2f, 0.5f), Color.FromBytes(0x4C, 0x9A, 0xFF));
        spring.AddComponent<Bouncer>();
        scene.Add(spring, level);
        foreach (var position in new[] { new Vector2(10.5f, 3), new Vector2(7, 4.5f) })
        {
            var ledge = Box("Crumbling Ledge", position, new Vector2(2, 0.5f), Color.FromBytes(0x9C, 0x6B, 0x3C));
            ledge.AddComponent<Crumble>();
            scene.Add(ledge, level);
            scene.Add(Collectible("Coin", new Vector2(0, 0.75f), Color.FromBytes(0xE3, 0xB3, 0x41)), ledge);
        }

        // Reach the flag and that's where you come back after losing your hearts.
        scene.Add(Flag("Checkpoint", new Vector2(1.5f, -3)));
        AddSign(scene, new Vector2(-3.5f, -3.65f),
            "Arrow keys or WASD to run, Space to jump, Shift to dash.",
            "The lift on the left goes up to a ledge. Mind the slime, or swing your sword at it with X!",
            "Pull the lever up there with E, and see what appears.",
            "Touch the flag, and that's where you come back after losing your hearts.");
        scene.Add(DialogueBox());

        var coins = new Entity("Coins");
        scene.Add(coins);
        foreach (var position in new[] { new Vector2(-1, -0.5f), new Vector2(4.5f, 1.5f), new Vector2(9, -3.5f), new Vector2(-6.6f, 2.8f) })
            scene.Add(Collectible("Coin", position, Color.FromBytes(0xE3, 0xB3, 0x41)), coins);

        AddPauseMenu(scene, menuScene: null);
        return scene;
    }

    private static Scene TopDown()
    {
        var scene = new Scene();

        // A dim dungeon: the camera's Ambient light is a deep blue, lit up by torches in the corners and the player's lantern,
        // which cast shadows from the walls and whatever stands in the way.
        var follow = AddCameraWithScore(scene);
        var camera = follow.Entity!.GetComponent<Camera>()!;
        camera.SortByY = true;
        camera.Ambient = Color.FromBytes(0x4A, 0x52, 0x70);

        var player = Box("Player", Vector2.Zero, new Vector2(0.8f, 0.8f), Color.FromBytes(0x4C, 0x9A, 0xFF));
        player.GetComponent<BoxCollider>()!.Layer = "Player";
        player.AddComponent<Body>().GravityScale = 0;
        player.AddComponent<TopDownMovement>().Dash = true;
        AddDashTrail(player, minSpeed: 9);
        AddRespawningHealth(player);
        var lantern = player.AddComponent<Light>();
        lantern.Radius = 5;
        lantern.Intensity = 0.8f;
        lantern.Color = Color.FromBytes(0xFF, 0xF1, 0xD6);
        lantern.Shadows = true;
        scene.Add(player);
        AddSword(scene, player);
        foreach (var position in new[] { new Vector2(-10.5f, 6.5f), new Vector2(10.5f, 6.5f), new Vector2(-10.5f, -6.5f), new Vector2(10.5f, -6.5f) })
            scene.Add(Torch(position));

        var walls = new Entity("Walls");
        scene.Add(walls);
        var gray = Color.FromBytes(0x48, 0x4B, 0x55);
        scene.Add(Box("Wall", new Vector2(0, 7.5f), new Vector2(24, 1), gray), walls);
        scene.Add(Box("Wall", new Vector2(0, -7.5f), new Vector2(24, 1), gray), walls);
        scene.Add(Box("Wall", new Vector2(-11.5f, 0), new Vector2(1, 14), gray), walls);
        scene.Add(Box("Wall", new Vector2(11.5f, 0), new Vector2(1, 14), gray), walls);
        scene.Add(Box("Pillar", new Vector2(-4, 2), new Vector2(2, 2), gray), walls);
        scene.Add(Box("Pillar", new Vector2(5, -3), new Vector2(2, 2), gray), walls);

        var gems = new Entity("Gems");
        scene.Add(gems);
        foreach (var position in new[] { new Vector2(-8, 5), new Vector2(8, 5), new Vector2(-8, -5), new Vector2(8, -5) })
            scene.Add(Collectible("Gem", position, Color.FromBytes(0x57, 0xAB, 0x5A)), gems);

        // A bat that goes after the player once it can see them, and gives up when they get away.
        var bat = new Entity("Bat");
        bat.Transform.Position = new Vector3(-7, -2, 0);
        var batSprite = bat.AddComponent<Sprite>();
        batSprite.Size = new Vector2(0.7f, 0.5f);
        batSprite.Tint = Color.FromBytes(0x9B, 0x6B, 0xD6);
        var batCollider = bat.AddComponent<CircleCollider>();
        batCollider.Radius = 0.3f;
        batCollider.Layer = "Enemy";
        bat.AddComponent<Body>().GravityScale = 0;
        var chase = bat.AddComponent<Chase>();
        chase.Range = 5;
        chase.Speed = 2.5f;
        chase.NeedsSight = true;
        var bite = bat.AddComponent<DamageOnTouch>();
        bite.Layers = "Player";
        bite.Knockback = 6;
        AddEnemyHealth(bat, hits: 2);
        scene.Add(bat);

        // Step into one portal and come out of the other, across the room.
        var portalColor = Color.FromBytes(0x4C, 0x9A, 0xFF) with { A = 0.6f };
        scene.Add(Portal("Portal A", new Vector2(-9.5f, -3), "Portal B", portalColor));
        scene.Add(Portal("Portal B", new Vector2(9.5f, 3), "Portal A", portalColor));

        // A vault in the top wall, locked; the key is across the room. The lock takes the key and disappears.
        scene.Add(Box("Wall", new Vector2(4, 5.625f), new Vector2(0.5f, 2.75f), gray), walls);
        scene.Add(Box("Wall", new Vector2(7.25f, 5.625f), new Vector2(0.5f, 2.75f), gray), walls);
        scene.Add(Box("Wall", new Vector2(4.4f, 4), new Vector2(1.3f, 0.5f), gray), walls);
        scene.Add(Box("Wall", new Vector2(6.975f, 4), new Vector2(1.05f, 0.5f), gray), walls);
        var vaultDoor = Box("Vault Door", new Vector2(5.75f, 4), new Vector2(1.4f, 0.5f), Color.FromBytes(0xE3, 0xB3, 0x41));
        var vaultLock = vaultDoor.AddComponent<Switch>();
        vaultLock.Mode = SwitchMode.Button;
        vaultLock.Key = "Key";
        vaultLock.Hides = "Vault Door";
        scene.Add(vaultDoor, walls);
        var treasure = Collectible("Treasure", new Vector2(5.75f, 5.6f), Color.FromBytes(0xE3, 0xB3, 0x41));
        treasure.GetComponent<Pickup>()!.Amount = 5;
        scene.Add(treasure);

        var key = Collectible("Key", new Vector2(8.5f, -1), Color.FromBytes(0xE3, 0xB3, 0x41));
        key.GetComponent<Sprite>()!.Size = new Vector2(0.35f, 0.6f);
        var keyPickup = key.GetComponent<Pickup>()!;
        keyPickup.Message = null;
        keyPickup.Item = "Key";
        scene.Add(key);

        // How many keys the player holds, under the hearts.
        var keys = new Entity("Keys");
        keys.Transform.Position = new Vector3(0, 3.8f, 1);
        var keysText = keys.AddComponent<Text>();
        keysText.Value = "Keys: 0";
        keysText.Size = 0.5f;
        keysText.Order = 100;
        var keysCounter = keys.AddComponent<Counter>();
        keysCounter.Message = null;
        keysCounter.Item = "Key";
        keysCounter.Format = "Keys: {0}";
        scene.Add(keys, follow.Entity);

        // A crate to push onto the plate below it, which brings out a bonus gem. Only the crate presses the plate.
        var crate = Box("Crate", new Vector2(-1, -2.5f), new Vector2(1, 1), Color.FromBytes(0x9C, 0x6B, 0x3C));
        crate.GetComponent<BoxCollider>()!.Layer = "Crate";
        crate.AddComponent<Body>().GravityScale = 0;
        crate.AddComponent<Pushable>();
        scene.Add(crate);
        var plate = new Entity("Plate");
        plate.Transform.Position = new Vector3(-1, -5.5f, 0);
        var plateSprite = plate.AddComponent<Sprite>();
        plateSprite.Size = new Vector2(1.1f, 1.1f);
        plateSprite.Tint = Color.FromBytes(0x3A, 0x3F, 0x4B);
        plateSprite.Order = -1;
        var plateCollider = plate.AddComponent<BoxCollider>();
        plateCollider.Size = new Vector2(0.6f, 0.6f);
        plateCollider.IsTrigger = true;
        var plateSwitch = plate.AddComponent<Switch>();
        plateSwitch.Mode = SwitchMode.Plate;
        plateSwitch.Layers = "Crate";
        plateSwitch.Targets = "Bonus Gem";
        scene.Add(plate);
        var bonus = Collectible("Bonus Gem", new Vector2(2, -5.5f), Color.FromBytes(0x4C, 0x9A, 0xFF));
        bonus.GetComponent<Pickup>()!.Amount = 3;
        scene.Add(bonus);

        var guide = AddSign(scene, new Vector2(3, 4),
            "Hello! Gems are a point each.",
            "Bats chase you once they see you. Get far enough away and they give up, or swing your sword with X.",
            "The portals take you across the room.",
            "There's a key about. It opens the vault next to me.",
            "Push the crate onto the plate down there, and see what turns up.");
        guide.Name = "Guide";
        var guideSprite = guide.GetComponent<Sprite>()!;
        guideSprite.Size = new Vector2(0.8f, 0.8f);
        guideSprite.Tint = Color.FromBytes(0xE3, 0xB3, 0x41);
        scene.Add(DialogueBox());
        AddPauseMenu(scene, menuScene: null);

        return scene;
    }

    // The camera follows the player; the score and health bar are children of the camera, so they stay on screen as the
    // camera moves. The score adds up the pickups' "Score" messages by itself.
    private static Follow AddCameraWithScore(Scene scene)
    {
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>().Size = 6;
        var follow = camera.AddComponent<Follow>();
        follow.Target = "Player";
        scene.Add(camera);

        var score = new Entity("Score");
        score.Transform.Position = new Vector3(0, 5.2f, 1);
        var text = score.AddComponent<Text>();
        text.Value = "Score: 0";
        text.Size = 0.8f;
        text.Order = 100;
        score.AddComponent<Counter>();
        scene.Add(score, camera);

        // The player's hearts, as a bar under the score that follows their health by itself.
        var hearts = new Entity("Health Bar");
        hearts.Transform.Position = new Vector3(0, 4.4f, 1);
        var bar = hearts.AddComponent<Bar>();
        bar.Track = "Player";
        bar.Size = new Vector2(2.4f, 0.25f);
        scene.Add(hearts, camera);
        return follow;
    }

    // The Arcade menu: a title, the best score, and buttons that need no code (Play loads Game, Options its scene, Quit
    // quits). The arrow keys, D-pad or stick pick a button.
    private static Scene ArcadeMenu()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var menuCamera = camera.AddComponent<Camera>();
        menuCamera.Size = 6;
        menuCamera.Transition = SceneTransition.Wipe;
        scene.Add(camera);

        scene.Add(Label("Title", new Vector2(0, 2.8f), "ARCADE", 1.6f, Color.FromBytes(0xF0, 0x8C, 0x2E)));

        // The best score the game's counter saved: this counter just shows it.
        var best = Label("Best", new Vector2(0, 1.5f), "Best: 0", 0.5f, Color.FromBytes(0xB4, 0xB9, 0xC4));
        var bestCounter = best.AddComponent<Counter>();
        bestCounter.Message = null;
        bestCounter.Format = "Best: {1}";
        bestCounter.SaveBestAs = "best";
        scene.Add(best);

        scene.Add(MenuButton("Play", new Vector2(0, -0.1f), Scenes.ButtonAction.LoadScene, "Game", Runtime.Key.Enter, Color.FromBytes(0x4C, 0x9A, 0xFF)));
        scene.Add(MenuButton("Options", new Vector2(0, -1.4f), Scenes.ButtonAction.LoadScene, "Options", Runtime.Key.None, Color.FromBytes(0x3A, 0x3F, 0x4B)));
        scene.Add(MenuButton("Quit", new Vector2(0, -2.7f), Scenes.ButtonAction.Quit, null, Runtime.Key.Escape, Color.FromBytes(0x3A, 0x3F, 0x4B)));

        // Pinned to the bottom of the screen, whatever its shape.
        var hint = Label("Hint", Vector2.Zero, "Enter to play  ·  Arrows to choose  ·  Esc to quit", 0.4f, Color.FromBytes(0x7D, 0x83, 0x90));
        var anchor = hint.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Bottom;
        anchor.Offset = new Vector2(0, 0.6f);
        scene.Add(hint);
        return scene;
    }

    // Options with no code: sliders and toggles bound to the game's settings, which it remembers next time.
    private static Scene ArcadeOptions()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var optionsCamera = camera.AddComponent<Camera>();
        optionsCamera.Size = 6;
        optionsCamera.Transition = SceneTransition.Wipe;
        scene.Add(camera);

        var light = Color.FromBytes(0xB4, 0xB9, 0xC4);
        var gray = Color.FromBytes(0x3A, 0x3F, 0x4B);
        scene.Add(Label("Title", new Vector2(0, 4.9f), "Options", 1.2f, Color.FromBytes(0xF0, 0x8C, 0x2E)));
        AddSetting("Music", 3.5f, GameSetting.MusicVolume);
        AddSetting("Sounds", 2.4f, GameSetting.SoundVolume);

        scene.Add(ToggleButton("Sound", new Vector2(0, 1.15f), GameSetting.Sound, "Sound: On", "Sound: Off"));
        scene.Add(ToggleButton("Fullscreen", new Vector2(0, -0.1f), GameSetting.Fullscreen, "Fullscreen: On", "Fullscreen: Off"));

        // Left and right pick one; the enemy spawner reads it back with Game.SaveData.GetString("difficulty").
        var difficulty = MenuButton("Difficulty", new Vector2(0, -1.35f), Scenes.ButtonAction.None, null, Runtime.Key.None, gray);
        var choice = difficulty.AddComponent<Choice>();
        choice.Format = "< {0} >";
        choice.SaveAs = "difficulty";
        scene.Add(difficulty);

        // Click, then press a key or button: it fires from then on. "{Fire}" always shows what does.
        var fire = MenuButton("Fire", new Vector2(0, -2.6f), Scenes.ButtonAction.Rebind, null, Runtime.Key.None, gray);
        fire.GetComponent<Scenes.Button>()!.InputAction = "Fire";
        fire.GetComponent<Text>()!.Value = "Fire: {Fire}";
        scene.Add(fire);

        scene.Add(MenuButton("Back", new Vector2(0, -4.2f), Scenes.ButtonAction.LoadScene, "Main", Runtime.Key.Escape, Color.FromBytes(0x4C, 0x9A, 0xFF)));
        return scene;

        // A label on the left and a slider on the right.
        void AddSetting(string name, float y, GameSetting setting)
        {
            var label = Label(name, new Vector2(-1.6f, y), name, 0.55f, light);
            label.GetComponent<Text>()!.Align = TextAlign.Right;
            scene.Add(label);

            var entity = new Entity($"{name} Volume");
            entity.Transform.Position = new Vector3(1.3f, y, 0);
            var slider = entity.AddComponent<Slider>();
            slider.Size = new Vector2(5, 0.45f);
            slider.Setting = setting;
            scene.Add(entity);
        }
    }

    /// <summary>A button that switches a setting on and off, saying which way it's set.</summary>
    private static Entity ToggleButton(string name, Vector2 position, GameSetting setting, string onText, string offText)
    {
        var entity = MenuButton(name, position, Scenes.ButtonAction.None, null, Runtime.Key.None, Color.FromBytes(0x3A, 0x3F, 0x4B));
        entity.GetComponent<Text>()!.Value = onText;
        var toggle = entity.AddComponent<Toggle>();
        toggle.Setting = setting;
        toggle.OnText = onText;
        toggle.OffText = offText;
        return entity;
    }

    // The Arcade game: a walled arena, the ship, a spawner, the score in the corner and a hidden game over panel.
    private static Scene ArcadeGame()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var gameCamera = camera.AddComponent<Camera>();
        gameCamera.Size = 8;
        gameCamera.Transition = SceneTransition.Iris;
        scene.Add(camera);

        var arena = new Entity("Arena");
        scene.Add(arena);
        var gray = Color.FromBytes(0x48, 0x4B, 0x55);
        scene.Add(Box("Wall", new Vector2(0, 7.5f), new Vector2(25, 1), gray), arena);
        scene.Add(Box("Wall", new Vector2(0, -7.5f), new Vector2(25, 1), gray), arena);
        scene.Add(Box("Wall", new Vector2(-12.5f, 0), new Vector2(1, 16), gray), arena);
        scene.Add(Box("Wall", new Vector2(12.5f, 0), new Vector2(1, 16), gray), arena);

        var ship = Box("Player", Vector2.Zero, new Vector2(0.9f, 0.9f), Color.FromBytes(0x4C, 0x9A, 0xFF));
        ship.GetComponent<BoxCollider>()!.Size = new Vector2(0.8f, 0.8f);
        ship.GetComponent<BoxCollider>()!.Layer = "Player";
        ship.AddComponent<Body>().GravityScale = 0;
        var shipHealth = ship.AddComponent<Health>();
        shipHealth.Max = 3;
        shipHealth.InvincibleTime = 1.2f;
        ship.AddComponent(Script("Ship"));
        scene.Add(ship);
        var nose = new Entity("Nose");
        nose.Transform.Position = new Vector3(0.6f, 0, 0);
        var noseSprite = nose.AddComponent<Sprite>();
        noseSprite.Size = new Vector2(0.35f, 0.3f);
        noseSprite.Tint = Color.FromBytes(0xB4, 0xD5, 0xFF);
        scene.Add(nose, ship);

        var spawner = new Entity("Spawner");
        spawner.AddComponent(Script("EnemySpawner"));
        scene.Add(spawner);

        // Enemies send "Score" as they die; the counter adds them up and saves the best for the menu.
        var score = Label("Score", Vector2.Zero, "Score: 0", 0.8f, Color.White);
        score.Transform.Position = new Vector3(0, 0, 1);
        score.GetComponent<Text>()!.Align = TextAlign.Left;
        var corner = score.AddComponent<ScreenAnchor>();
        corner.Offset = new Vector2(0.6f, -0.7f);
        score.AddComponent<Counter>().SaveBestAs = "best";
        scene.Add(score);

        // The ship's hit points, kept up to date by the Ship script.
        var hearts = Label("Hearts", Vector2.Zero, "♥♥♥", 0.8f, Color.FromBytes(0xE5, 0x53, 0x4B));
        hearts.Transform.Position = new Vector3(0, 0, 1);
        hearts.GetComponent<Text>()!.Align = TextAlign.Right;
        var topRight = hearts.AddComponent<ScreenAnchor>();
        topRight.Anchor = Anchor.TopRight;
        topRight.Offset = new Vector2(-0.6f, -0.7f);
        scene.Add(hearts);

        // Shown by the ship when it's destroyed. Its buttons only work once it's visible.
        var panel = new Entity("Game Over") { IsEnabled = false };
        panel.Transform.Position = new Vector3(0, 0, 2);
        var backdrop = panel.AddComponent<Sprite>();
        backdrop.Size = new Vector2(12, 6);
        backdrop.Tint = new Color(0.07f, 0.08f, 0.1f, 0.9f);
        backdrop.Order = 200;
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var gameOver = panel.AddComponent<Panel>();
        gameOver.PauseGame = false;   // the ship's last moments play out in slow motion behind it
        gameOver.CloseOnBack = false;
        panel.AddComponent(Script("GameOver"));
        scene.Add(panel);

        var heading = Label("Heading", new Vector2(0, 1.5f), "Game Over", 1.3f, Color.FromBytes(0xF0, 0x8C, 0x2E));
        heading.GetComponent<Text>()!.Order = 201;
        scene.Add(heading, panel);
        var result = Label("Result", new Vector2(0, 0.2f), "", 0.5f, Color.FromBytes(0xB4, 0xB9, 0xC4));
        result.GetComponent<Text>()!.Order = 201;
        scene.Add(result, panel);
        scene.Add(MenuButton("Retry", new Vector2(-2.2f, -1.6f), Scenes.ButtonAction.RestartScene, null, Runtime.Key.Enter, Color.FromBytes(0x4C, 0x9A, 0xFF), order: 210), panel);
        scene.Add(MenuButton("Menu", new Vector2(2.2f, -1.6f), Scenes.ButtonAction.LoadScene, "Main", Runtime.Key.Escape, Color.FromBytes(0x3A, 0x3F, 0x4B), order: 210), panel);

        AddPauseMenu(scene, menuScene: "Main");
        return scene;
    }

    /// <summary>
    /// A pause menu with no code: Pause (Esc, P or Start) opens it and stops the game, and Resume, Esc or B closes it.
    /// Restart starts the level over and, with a <paramref name="menuScene"/>, Menu goes back to it.
    /// </summary>
    private static void AddPauseMenu(Scene scene, string? menuScene)
    {
        var panel = new Entity("Paused");
        panel.Transform.Position = new Vector3(0, 0, 3);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var pause = panel.AddComponent<Panel>();
        pause.OpenOn = "Pause";
        var backdrop = panel.AddComponent<Sprite>();
        backdrop.Size = new Vector2(7, menuScene is null ? 5.2f : 6.4f);
        backdrop.Tint = new Color(0.07f, 0.08f, 0.1f, 0.92f);
        backdrop.Order = 300;
        scene.Add(panel);

        var top = menuScene is null ? 1.6f : 2.2f;
        var heading = Label("Heading", new Vector2(0, top), "Paused", 1, Color.FromBytes(0xF0, 0x8C, 0x2E));
        heading.GetComponent<Text>()!.Order = 301;
        AddChild(heading);
        AddChild(MenuButton("Resume", new Vector2(0, top - 1.3f), Scenes.ButtonAction.ClosePanel, null, Runtime.Key.None, Color.FromBytes(0x4C, 0x9A, 0xFF), order: 310));
        AddChild(MenuButton("Restart", new Vector2(0, top - 2.55f), Scenes.ButtonAction.RestartScene, null, Runtime.Key.None, Color.FromBytes(0x3A, 0x3F, 0x4B), order: 310));
        if (menuScene is not null)
            AddChild(MenuButton("Menu", new Vector2(0, top - 3.8f), Scenes.ButtonAction.LoadScene, menuScene, Runtime.Key.None, Color.FromBytes(0x3A, 0x3F, 0x4B), order: 310));

        // "{Pause}" shows Esc on the keyboard and Start on a gamepad.
        var hint = Label("Hint", new Vector2(0, -backdrop.Size.Y / 2 + 0.45f), "{Pause} to resume", 0.35f, Color.FromBytes(0x7D, 0x83, 0x90));
        hint.GetComponent<Text>()!.Order = 301;
        AddChild(hint);

        void AddChild(Entity child) => scene.Add(child, panel);
    }

    private static Entity ArcadeBullet()
    {
        var entity = new Entity("Bullet");
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = new Vector2(0.3f, 0.3f);
        sprite.Tint = Color.FromBytes(0xFF, 0xE0, 0x7A);
        sprite.Order = 5;
        var collider = entity.AddComponent<CircleCollider>();
        collider.Radius = 0.15f;
        collider.IsTrigger = true;
        // Shots leave from inside the ship and fly in streams: they pass through both.
        collider.Layer = "Bullet";
        collider.IgnoreLayers = "Player, Bullet";
        entity.AddComponent<Body>().GravityScale = 0;
        var damage = entity.AddComponent<DamageOnTouch>();
        damage.Layers = "Enemy";
        damage.DestroyOnHit = true;
        entity.AddComponent<Lifetime>().Seconds = 1.5f;
        var trail = entity.AddComponent<Trail>();
        trail.Interval = 0.02f;
        trail.Fade = 0.12f;
        trail.Opacity = 0.6f;
        entity.AddComponent(Script("Bullet"));
        return entity;
    }

    private static Entity ArcadeEnemy()
    {
        var entity = new Entity("Enemy");
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = new Vector2(0.8f, 0.8f);
        sprite.Tint = Color.FromBytes(0xE5, 0x53, 0x4B);
        var collider = entity.AddComponent<CircleCollider>();
        collider.Radius = 0.4f;
        collider.IsTrigger = true;
        collider.Layer = "Enemy";
        entity.AddComponent<Body>().GravityScale = 0;
        var health = entity.AddComponent<Health>();
        health.Max = 1;
        health.InvincibleTime = 0;
        entity.AddComponent<DamageOnTouch>().Layers = "Player";
        entity.AddComponent(Script("Enemy"));
        return entity;
    }

    private static Entity Label(string name, Vector2 position, string value, float size, Color color)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var text = entity.AddComponent<Text>();
        text.Value = value;
        text.Size = size;
        text.Color = color;
        text.Order = 100;
        return entity;
    }

    /// <summary>A button with a colored background and a label, which does <paramref name="action"/> by itself.</summary>
    private static Entity MenuButton(string label, Vector2 position, Scenes.ButtonAction action, string? scene, Runtime.Key shortcut, Color color, int order = 100)
    {
        var size = new Vector2(4, 1.1f);
        var entity = new Entity(label);
        entity.Transform.Position = new Vector3(position, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = color;
        sprite.Order = order;
        var text = entity.AddComponent<Text>();
        text.Value = label;
        text.Size = 0.6f;
        var button = entity.AddComponent<Scenes.Button>();
        button.Size = size;
        button.Action = action;
        button.Scene = scene;
        button.Shortcut = shortcut;
        button.Fade = 0.3f;
        return entity;
    }

    // Streaks behind the player while it dashes (faster than it runs).
    private static void AddDashTrail(Entity player, float minSpeed)
    {
        var trail = player.AddComponent<Trail>();
        trail.MinSpeed = minSpeed;
        trail.Interval = 0.02f;
        trail.Fade = 0.2f;
    }

    // Three hearts, and back at the last checkpoint (or the start) a moment after losing them.
    // A sword for the player: an Attack that swings a hitbox (a child to its right, turned whichever way it faces) at enemies.
    private static void AddSword(Scene scene, Entity player)
    {
        var attack = player.AddComponent<Attack>();
        attack.Lunge = 3;
        var hitbox = new Entity("Hitbox");
        hitbox.Transform.Position = new Vector3(0.9f, 0, 0);
        var blade = hitbox.AddComponent<Sprite>();
        blade.Size = new Vector2(1, 0.25f);
        blade.Tint = Color.FromBytes(0xE6, 0xE9, 0xEF);
        blade.Order = 6;
        var collider = hitbox.AddComponent<BoxCollider>();
        collider.Size = new Vector2(1, 0.8f);
        collider.IsTrigger = true;
        collider.Layer = "Attack";
        var damage = hitbox.AddComponent<DamageOnTouch>();
        damage.Layers = "Enemy";
        damage.Knockback = 8;
        scene.Add(hitbox, player);
    }

    // An enemy that a few sword hits defeat, with a burst of sparkles.
    private static void AddEnemyHealth(Entity enemy, int hits)
    {
        var health = enemy.AddComponent<Health>();
        health.Max = hits;
        health.Current = hits;
        health.InvincibleTime = 0.3f;
    }

    // A torch on the wall: a little flame with a warm, flickering light.
    private static Entity Torch(Vector2 position)
    {
        var torch = new Entity("Torch");
        torch.Transform.Position = new Vector3(position, 0);
        var flame = torch.AddComponent<Sprite>();
        flame.Size = new Vector2(0.3f, 0.45f);
        flame.Tint = Color.FromBytes(0xFF, 0xA9, 0x3A);
        flame.Order = 2;
        var light = torch.AddComponent<Light>();
        light.Radius = 6;
        light.Color = Color.FromBytes(0xFF, 0xB8, 0x6A);
        light.Flicker = 0.2f;
        light.Shadows = true;
        return torch;
    }

    private static void AddRespawningHealth(Entity player)
    {
        var health = player.AddComponent<Health>();
        health.Max = 3;
        health.InvincibleTime = 1;
        health.Respawn = true;
        health.RespawnDelay = 0.8f;
    }

    /// <summary>A checkpoint: a thin flag that turns green once reached.</summary>
    private static Entity Flag(string name, Vector2 position)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = new Vector2(0.25f, 2);
        sprite.Tint = Color.FromBytes(0xB4, 0xB9, 0xC4);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = new Vector2(0.8f, 2);
        collider.IsTrigger = true;
        entity.AddComponent<Checkpoint>();
        return entity;
    }

    /// <summary>
    /// Something to read or talk to: its lines show in the dialogue box when the player presses Interact beside it, and
    /// an "E" floats above it while they're close enough.
    /// </summary>
    private static Entity AddSign(Scene scene, Vector2 position, params string[] lines)
    {
        var entity = new Entity("Sign");
        entity.Transform.Position = new Vector3(position, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = new Vector2(0.9f, 0.7f);
        sprite.Tint = Color.FromBytes(0x9C, 0x6B, 0x3C);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = new Vector2(1.8f, 1.4f);
        collider.IsTrigger = true;
        var dialogue = entity.AddComponent<Dialogue>();
        dialogue.Lines = lines;
        dialogue.Prompt = "Hint";
        scene.Add(entity);

        var hint = Label("Hint", new Vector2(0, 0.9f), "{Interact}", 0.45f, Color.White);
        hint.IsEnabled = false;
        scene.Add(hint, entity);
        return entity;
    }

    /// <summary>
    /// A lever that switches on <paramref name="targets"/> when the player presses Interact beside it; an "E" floats above
    /// it while they're close enough.
    /// </summary>
    private static Entity AddLever(Scene scene, Vector2 position, string targets)
    {
        var entity = new Entity("Lever");
        entity.Transform.Position = new Vector3(position, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = new Vector2(0.25f, 0.7f);
        sprite.Tint = Color.FromBytes(0xC2, 0x4E, 0x4E);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = new Vector2(1.2f, 1.2f);
        collider.IsTrigger = true;
        var lever = entity.AddComponent<Switch>();
        lever.Targets = targets;
        lever.Prompt = "Hint";
        scene.Add(entity);

        var hint = Label("Hint", new Vector2(0, 0.8f), "{Interact}", 0.45f, Color.White);
        hint.IsEnabled = false;
        scene.Add(hint, entity);
        return entity;
    }

    /// <summary>
    /// The panel dialogue is shown in, pinned to the bottom of the screen. It starts hidden (so it isn't in the way in the
    /// Scene view), and shows while someone talks.
    /// </summary>
    private static Entity DialogueBox()
    {
        var entity = new Entity("Dialogue Box") { IsEnabled = false };
        entity.Transform.Position = new Vector3(0, 0, 2);
        var anchor = entity.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Bottom;
        anchor.Offset = new Vector2(0, 1.4f);
        var panel = entity.AddComponent<Sprite>();
        panel.Size = new Vector2(16, 2.2f);
        panel.Tint = new Color(0.07f, 0.08f, 0.1f, 0.9f);
        panel.Order = 200;
        var text = entity.AddComponent<Text>();
        text.Value = "...";
        text.Size = 0.4f;
        text.Order = 201;
        entity.AddComponent<Typewriter>();
        return entity;
    }

    /// <summary>A door to <paramref name="target"/> in the same room, used by walking into it.</summary>
    private static Entity Portal(string name, Vector2 position, string target, Color color)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = new Vector2(1, 1.4f);
        sprite.Tint = color;
        sprite.Order = -1;
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = new Vector2(0.8f, 1.2f);
        collider.IsTrigger = true;
        var door = entity.AddComponent<Door>();
        door.Target = target;
        door.Fade = 0.2f;
        return entity;
    }

    /// <summary>A solid, visible box: sprite plus matching collider.</summary>
    private static Entity Box(string name, Vector2 position, Vector2 size, Color color)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = color;
        entity.AddComponent<BoxCollider>().Size = size;
        return entity;
    }

    /// <summary>A one-way platform: jump up through it, land on top.</summary>
    private static Entity Platform(Vector2 position, Vector2 size, Color color)
    {
        var entity = Box("Platform", position, size, color);
        entity.GetComponent<BoxCollider>()!.OneWay = true;
        return entity;
    }

    /// <summary>A spinning pickup worth a point to the player, which bursts into sparkles of its color.</summary>
    private static Entity Collectible(string name, Vector2 position, Color color)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.Transform.Rotation = new Vector3(0, 0, 45);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = new Vector2(0.5f, 0.5f);
        sprite.Tint = color;
        var collider = entity.AddComponent<CircleCollider>();
        collider.Radius = 0.3f;
        collider.IsTrigger = true;
        var pickup = entity.AddComponent<Pickup>();
        pickup.Layers = "Player";
        pickup.Effect = "Sparkle";
        pickup.Spin = 90;
        return entity;
    }

    // Scripts aren't compiled yet when the template is written; save their references as missing components,
    // which become the real scripts as soon as the project opens and compiles.
    private static MissingComponent Script(string typeName)
    {
        using var document = System.Text.Json.JsonDocument.Parse($$"""{ "type": "{{typeName}}" }""");
        return new MissingComponent(typeName, document.RootElement);
    }

    private const string ShipScript = """
        /// <summary>
        /// Fly with WASD, the arrow keys or a gamepad. Aim with the mouse; hold the left button or Space to fire. Its Health
        /// takes the hits: each one costs a heart.
        /// </summary>
        [Category("Arcade")]
        [Requires(typeof(Body), Note = "flies by setting its velocity")]
        [Requires(typeof(Health), Note = "the hearts it shows")]
        public class Ship : Behavior
        {
            public float Speed { get; set; } = 8;

            /// <summary>Shots per second.</summary>
            public float FireRate { get; set; } = 8;

            public float BulletSpeed { get; set; } = 18;

            private float _cooldown;

            protected override void Start()
            {
                // Fire is a built-in action (left mouse, J, gamepad X or right trigger); here Space fires too.
                Input.Bind("Fire", MouseButton.Left, Key.Space, Key.J, GamepadButton.X, GamepadButton.RightShoulder, GamepadButton.RightTrigger);
                ShowHearts();
            }

            protected override void Update()
            {
                GetComponent<Body>()!.Velocity = Input.Move * Speed;

                // Turn to face the mouse, or the way a gamepad's right stick (or left, with the right one at rest) points.
                var position = new Vector2(Transform.Position.X, Transform.Position.Y);
                var aim = Input.AimFrom(position);
                if (aim != Vector2.Zero)
                    Transform.Rotation = Transform.Rotation with { Z = MathF.Atan2(aim.Y, aim.X) * 180 / MathF.PI };

                _cooldown -= Time.Delta;
                if (Input.IsDown("Fire") && _cooldown <= 0 && aim != Vector2.Zero)
                {
                    _cooldown = 1 / FireRate;
                    var bullet = Game.Spawn("Bullet", position);
                    bullet.GetComponent<Body>()!.Velocity = aim * BulletSpeed;
                    Audio.Play("Sounds/shoot.wav");
                }
            }

            // Health flashes and blinks the ship by itself; a freeze, a red flash and a kick make the hit land.
            protected override void OnDamage(float amount, Entity? source)
            {
                ShowHearts();
                Game.HitStop(0.08f);
                Game.FlashScreen(new Color(0.9f, 0.2f, 0.2f, 0.35f), 0.25f);
                Game.ShakeCamera(0.35f, 0.3f);
                Input.Rumble(0.6f, 0.25f);
            }

            // Health destroys the ship once this returns.
            protected override void OnDeath()
            {
                var position = Transform.WorldPosition;
                var burst = Game.Spawn("Sparkle", new Vector2(position.X, position.Y));
                if (burst.GetComponent<ParticleEmitter>() is { } sparks && GetComponent<Sprite>() is { } sprite)
                {
                    sparks.StartColor = sprite.Tint;
                    sparks.EndColor = sprite.Tint with { A = 0 };
                    sparks.Burst(30);
                }

                Game.ShakeCamera(0.5f, 0.4f);
                Game.SlowMotion(0.25f, 1.2f);
                Game.OpenPanel("Game Over");
            }

            private void ShowHearts()
            {
                if (Game.Find("Hearts")?.GetComponent<Text>() is { } hearts)
                    hearts.Value = new string('♥', (int)MathF.Ceiling(GetComponent<Health>()!.Current));
            }
        }

        """;

    private const string BulletScript = """
        /// <summary>Stops at walls. Its DamageOnTouch hurts enemies, and its Lifetime clears it away if it misses.</summary>
        [Category("Arcade")]
        [WorksWith(typeof(DamageOnTouch), Note = "hurts what it hits")]
        [WorksWith(typeof(Lifetime), Note = "clears it away if it misses")]
        public class Bullet : Behavior
        {
            protected override void OnTriggerEnter(Entity other)
            {
                if (other.Name == "Wall")
                    Game.Destroy(Entity!);
            }
        }

        """;

    private const string EnemyScript = """
        /// <summary>Chases the ship, hurting it on contact (DamageOnTouch). One hit destroys it, for a point.</summary>
        [Category("Arcade")]
        [Requires(typeof(Body), Note = "chases by setting its velocity")]
        public class Enemy : Behavior
        {
            public float Speed { get; set; } = 2.5f;

            private Entity? _target;

            protected override void Start()
            {
                _target = Game.Find("Player");
                Speed *= Rand.Range(0.8f, 1.4f);

                // Pop in.
                Transform.Scale = new Vector3(0, 0, 1);
                ScaleTo(Vector2.One, 0.3f, Ease.OutBack);
            }

            protected override void Update()
            {
                Transform.Rotation += new Vector3(0, 0, 120 * Time.Delta);
                if (_target?.Scene is null)
                    return;

                var toTarget = _target.Transform.Position - Transform.Position;
                var direction = new Vector2(toTarget.X, toTarget.Y);
                if (direction != Vector2.Zero)
                    GetComponent<Body>()!.Velocity = Vector2.Normalize(direction) * Speed;
            }

            // Health destroys it once this returns.
            protected override void OnDeath()
            {
                Game.Send("Score", 1);
                var position = Transform.WorldPosition;
                var burst = Game.Spawn("Sparkle", new Vector2(position.X, position.Y));
                if (burst.GetComponent<ParticleEmitter>() is { } sparks && GetComponent<Sprite>() is { } sprite)
                {
                    sparks.StartColor = sprite.Tint;
                    sparks.EndColor = sprite.Tint with { A = 0 };
                }

                Game.ShakeCamera(0.15f, 0.12f);
            }
        }

        """;

    private const string EnemySpawnerScript = """
        /// <summary>Spawns enemies around the edge of the arena, faster and faster, until the ship is gone.</summary>
        [Category("Arcade")]
        public class EnemySpawner : Behavior
        {
            /// <summary>Enemies appear on the edge of a box this size around the middle.</summary>
            public Vector2 ArenaSize { get; set; } = new(22, 12);

            public float StartInterval { get; set; } = 1.5f;

            public float MinInterval { get; set; } = 0.35f;

            /// <summary>Seconds taken off the interval for every second played.</summary>
            public float Ramp { get; set; } = 0.02f;

            protected override void Start() => StartCoroutine(SpawnWaves());

            // The Difficulty picked in Options: easy spawns less often, hard more.
            private float Pace => Game.SaveData.GetString("difficulty") switch
            {
                "Easy" => 1.4f,
                "Hard" => 0.7f,
                _ => 1,
            };

            private IEnumerator SpawnWaves()
            {
                yield return Wait(1);
                while (Game.Find("Player") is { } player)
                {
                    Game.Spawn("Enemy", PickSpot(player));
                    yield return Wait(Pace * MathF.Max(MinInterval, StartInterval - Ramp * (float)Time.Total));
                }
            }

            // Somewhere on the edge, not right on top of the player.
            private Vector2 PickSpot(Entity player)
            {
                var half = ArenaSize / 2;
                var playerAt = new Vector2(player.Transform.Position.X, player.Transform.Position.Y);
                var spot = Vector2.Zero;
                for (var i = 0; i < 10; i++)
                {
                    spot = Rand.Chance(0.5f)
                        ? new Vector2(Rand.Sign() * half.X, Rand.Range(-half.Y, half.Y))
                        : new Vector2(Rand.Range(-half.X, half.X), Rand.Sign() * half.Y);
                    if (Vector2.Distance(spot, playerAt) > 5)
                        break;
                }

                return spot;
            }
        }

        """;

    private const string GameOverScript = """
        /// <summary>The game over panel: its Panel keeps it hidden until the ship's destroyed, then it shows the score against the best.</summary>
        [Category("Arcade")]
        [Requires(typeof(Panel))]
        public class GameOver : Behavior
        {
            // Runs the first frame the panel is shown. The score's Counter has already saved a new best.
            protected override void Start()
            {
                var counter = Game.Find<Counter>();
                var (score, best) = (counter?.Value ?? 0, counter?.Best ?? 0);

                var result = Entity!.Children.FirstOrDefault(c => c.Name == "Result")?.GetComponent<Text>();
                if (result is not null)
                    result.Value = score >= best && score > 0 ? $"New best: {score}!" : $"Score: {score}   Best: {best}";

                Transform.Scale = new Vector3(0.6f, 0.6f, 1);
                ScaleTo(Vector2.One, 0.35f, Ease.OutBack);
            }
        }

        """;
}
