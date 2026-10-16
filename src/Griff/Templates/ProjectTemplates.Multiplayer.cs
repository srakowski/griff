using System.Numerics;
using Griff.Audio;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Templates;

// The Multiplayer template: a menu to host or join, and an arena for up to eight players, made only of components.
public static partial class ProjectTemplates
{
    private sealed class MultiplayerTemplate : ProjectTemplate
    {
        public override string Description => "Play with friends: up to eight players in an arena, on one network or over the internet. Host from the menu and others join with your address (in the editor, Game > Play with 2 Players). Shoot each other and the slimes, grab hearts, and top the scoreboard. WASD to move, the mouse to aim, click to fire, Shift to dash.";

        public override int Order => 130;

        protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Tense, seed: 11) with { Volume = 0.55f };

        protected override Scene BuildStartScene() => MultiplayerMenu();

        protected override Dictionary<string, Scene> BuildMoreScenes() => new() { ["Scenes/Arena.scene"] = MultiplayerArena() };

        protected override Dictionary<string, Entity> BuildPrefabs() => MultiplayerPrefabs();

        public override IReadOnlyDictionary<string, byte[]> Images() => TemplateArt.ArenaFiles();
    }

    private static readonly Color ArenaBlue = Color.FromBytes(0x4C, 0x8D, 0xFF);
    private static readonly Color ArenaGray = Color.FromBytes(0x3A, 0x3F, 0x4B);
    private static readonly Color ArenaLight = Color.FromBytes(0xB4, 0xB9, 0xC4);
    private static readonly Color ArenaOrange = Color.FromBytes(0xF0, 0x8C, 0x2E);

    // The menu: your name, the address to join, and Host, Join, Practice and Quit. The line under the buttons says how
    // joining is going (or why it didn't work).
    private static Scene MultiplayerMenu()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var menuCamera = camera.AddComponent<Camera>();
        menuCamera.Size = 6;
        menuCamera.Transition = SceneTransition.Wipe;
        scene.Add(camera);

        scene.Add(Label("Title", new Vector2(0, 4.3f), "ARENA", 1.6f, ArenaOrange));
        scene.Add(Label("Subtitle", new Vector2(0, 3.2f), "Up to 8 players, on your network or over the internet", 0.42f, ArenaLight));

        AddField(scene, "Name", new Vector2(0, 2.0f), "Your name", TextFieldUse.PlayerName, 16);
        AddField(scene, "Address", new Vector2(0, 0.8f), "Host's address (empty: find one nearby)", TextFieldUse.JoinAddress, 40);

        scene.Add(MenuButton("Host Game", new Vector2(-2.2f, -0.6f), Scenes.ButtonAction.HostGame, "Arena", Key.Enter, ArenaBlue));
        scene.Add(MenuButton("Join Game", new Vector2(2.2f, -0.6f), Scenes.ButtonAction.JoinGame, null, Key.None, ArenaBlue));
        scene.Add(MenuButton("Practice", new Vector2(-2.2f, -1.9f), Scenes.ButtonAction.LoadScene, "Arena", Key.None, ArenaGray));
        scene.Add(MenuButton("Quit", new Vector2(2.2f, -1.9f), Scenes.ButtonAction.Quit, null, Key.Escape, ArenaGray));

        // Joining's progress: "Joining 192.168.1.5...", or why it didn't work.
        var status = Label("Status", new Vector2(0, -3.2f), "", 0.42f, ArenaLight);
        var board = status.AddComponent<Scoreboard>();
        board.Title = null;
        board.Offline = null;
        board.ShowScore = false;
        scene.Add(status);

        var hint = Label("Hint", Vector2.Zero, "Host, and others join with your address  ·  In the editor: Game > Play with 2 Players", 0.36f,
            Color.FromBytes(0x7D, 0x83, 0x90));
        var anchor = hint.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Bottom;
        anchor.Offset = new Vector2(0, 0.6f);
        scene.Add(hint);
        return scene;
    }

    // A label to the left of a box to type in.
    private static void AddField(Scene scene, string name, Vector2 position, string placeholder, TextFieldUse use, int maxLength)
    {
        var label = Label($"{name} Label", position + new Vector2(-3.9f, 0), name, 0.5f, ArenaLight);
        label.GetComponent<Text>()!.Align = TextAlign.Right;
        scene.Add(label);

        var size = new Vector2(7, 0.9f);
        var field = new Entity($"{name} Field");
        field.Transform.Position = new Vector3(position + new Vector2(0.4f, 0), 0);
        var box = field.AddComponent<Sprite>();
        box.Size = size;
        box.Tint = Color.FromBytes(0x22, 0x26, 0x30);
        box.Order = 90;
        var text = field.AddComponent<Text>();
        text.Value = "";
        text.Size = 0.42f;
        text.Order = 100;
        var input = field.AddComponent<TextField>();
        input.Size = size;
        input.Placeholder = placeholder;
        input.Use = use;
        input.MaxLength = maxLength;
        scene.Add(field);
    }

    // The arena: walls and crates to hide behind, slimes that come for the nearest player, hearts to heal with, and a
    // spawn point for each player. Defeat another player or a slime for a point.
    private static Scene MultiplayerArena()
    {
        var scene = new Scene();
        var game = new Entity("Game");
        var settings = game.AddComponent<Multiplayer>();
        settings.MaxPlayers = 8;
        settings.Player = "Player";
        settings.ExitScene = "Main";
        scene.Add(game);

        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 8;
        view.Transition = SceneTransition.Iris;
        view.Background = Color.FromBytes(0x16, 0x18, 0x1F);
        var follow = camera.AddComponent<Follow>();
        follow.Target = "Player";
        follow.Smoothing = 8;
        follow.StayInside = "Floor";
        scene.Add(camera);

        scene.Add(ArenaFloor());
        scene.Add(ArenaWalls());

        foreach (var at in new[] { new Vector2(-6, 4), new Vector2(6, 4), new Vector2(-6, -4), new Vector2(6, -4), new Vector2(0, 7), new Vector2(0, -7) })
            scene.Add(ArenaCrate(at));

        // Around the edge, as far apart as the arena allows: everyone gets one to themselves.
        var spawns = new Entity("Spawns");
        scene.Add(spawns);
        Vector2[] spots = [new(-15, 8), new(15, -8), new(15, 8), new(-15, -8), new(0, 9), new(0, -9), new(-15, 0), new(15, 0)];
        for (var i = 0; i < spots.Length; i++)
        {
            var spawn = new Entity($"Spawn {i + 1}");
            spawn.Transform.Position = new Vector3(spots[i], 0);
            spawn.AddComponent<PlayerSpawn>();
            scene.Add(spawn, spawns);
        }

        scene.Add(ArenaPlayer(spots[0]));

        // Slimes crawl out of the middle, a few at a time, after whoever is nearest.
        var pit = new Entity("Slime Pit");
        var slimes = pit.AddComponent<Spawner>();
        slimes.Prefab = "Slime";
        slimes.Interval = 4;
        slimes.Delay = 3;
        slimes.MaxAlive = 4;
        slimes.Radius = 1;
        scene.Add(pit);

        foreach (var at in new[] { new Vector2(-10, 0), new Vector2(10, 0) })
        {
            var hearts = new Entity("Heart Spot");
            hearts.Transform.Position = new Vector3(at, 0);
            var spawner = hearts.AddComponent<Spawner>();
            spawner.Prefab = "Heart";
            spawner.Interval = 8;
            spawner.Delay = 2;
            spawner.MaxAlive = 1;
            scene.Add(hearts);
        }

        AddArenaHud(scene, camera);
        AddArenaMenu(scene);
        return scene;
    }

    private static Entity ArenaPlayer(Vector2 at)
    {
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(at, 0);
        var sprite = player.AddComponent<Sprite>();
        sprite.Image = TemplateArt.Fighter;
        sprite.Size = new Vector2(1.2f, 1.2f);
        sprite.Order = 10;
        var collider = player.AddComponent<CircleCollider>();
        collider.Radius = 0.45f;
        collider.Layer = "Player";
        player.AddComponent<Body>().GravityScale = 0;
        var move = player.AddComponent<TopDownMovement>();
        move.Speed = 6;
        move.Dash = true;
        var health = player.AddComponent<Health>();
        health.Max = 3;
        health.Current = 3;
        health.InvincibleTime = 1;
        health.Respawn = true;
        health.RespawnDelay = 2;
        var gun = player.AddComponent<Shooter>();
        gun.Action = "Fire";
        gun.Prefab = "Shot";
        gun.Speed = 16;
        gun.Interval = 0.22f;
        AddDashTrail(player, 7);

        var tag = new Entity("Name");
        tag.Transform.Position = new Vector3(0, 0.95f, 0);
        var text = tag.AddComponent<Text>();
        text.Value = "";
        text.Size = 0.34f;
        text.Order = 20;
        tag.AddComponent<NameTag>().Offline = "You";
        player.ChildList.Add(tag);
        tag.Parent = player;
        return player;
    }

    // A checkerboard of slate tiles under everything.
    private static Entity ArenaFloor()
    {
        var rows = new List<string>();
        for (var y = 0; y < 22; y++)
            rows.Add(new string(Enumerable.Range(0, 36).Select(x => (char)('0' + ((x + y) % 2 == 0 ? TemplateArt.FloorTile : TemplateArt.FloorTileAlt))).ToArray()));
        var floor = new Entity("Floor");
        floor.Transform.Position = new Vector3(-18, 11, 0);
        var map = floor.AddComponent<Tilemap>();
        map.Image = TemplateArt.ArenaTiles;
        map.Columns = TemplateArt.ArenaColumns;
        map.Tiles = rows;
        map.Solid = false;
        map.Order = -10;
        return floor;
    }

    // The wall round the edge, and four pillars.
    private static Entity ArenaWalls()
    {
        const int width = 36, height = 22;
        var grid = new char[height, width];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                grid[y, x] = x == 0 || y == 0 || x == width - 1 || y == height - 1 ? (char)('0' + TemplateArt.ArenaWallTile) : Tilemap.Empty;
        }

        foreach (var (px, py) in new[] { (9, 6), (26, 6), (9, 15), (26, 15) })
        {
            for (var y = py - 1; y <= py; y++)
            {
                for (var x = px - 1; x <= px; x++)
                    grid[y, x] = (char)('0' + TemplateArt.ArenaWallTile);
            }
        }

        var rows = new List<string>();
        for (var y = 0; y < height; y++)
            rows.Add(new string(Enumerable.Range(0, width).Select(x => grid[y, x]).ToArray()));
        var walls = new Entity("Walls");
        walls.Transform.Position = new Vector3(-18, 11, 0);
        var map = walls.AddComponent<Tilemap>();
        map.Image = TemplateArt.ArenaTiles;
        map.Columns = TemplateArt.ArenaColumns;
        map.Tiles = rows;
        return walls;
    }

    private static Entity ArenaCrate(Vector2 at)
    {
        var crate = new Entity("Crate");
        crate.Transform.Position = new Vector3(at, 0);
        var sprite = crate.AddComponent<Sprite>();
        sprite.Image = TemplateArt.CrateArt;
        sprite.Size = new Vector2(1.4f, 1.4f);
        crate.AddComponent<BoxCollider>().Size = new Vector2(1.4f, 1.4f);
        return crate;
    }

    // Scores and pings in the corner (with how to join, while hosting), your health along the top, and how to play.
    private static void AddArenaHud(Scene scene, Entity camera)
    {
        var scores = Label("Scores", Vector2.Zero, "", 0.4f, Color.White);
        scores.Transform.Position = new Vector3(0, 0, 1);
        scores.GetComponent<Text>()!.Align = TextAlign.Left;
        var corner = scores.AddComponent<ScreenAnchor>();
        corner.Anchor = Anchor.TopLeft;
        corner.Offset = new Vector2(0.5f, -0.5f);
        var board = scores.AddComponent<Scoreboard>();
        board.Title = "Scores";
        board.Offline = "Practice: Esc for the menu";
        scene.Add(scores);

        var hearts = new Entity("Health Bar");
        hearts.Transform.Position = new Vector3(0, 7.3f, 1);
        var bar = hearts.AddComponent<Bar>();
        bar.Track = "Player";
        bar.Size = new Vector2(3, 0.3f);
        bar.Fill = Color.FromBytes(0xE5, 0x45, 0x55);
        scene.Add(hearts, camera);

        var hint = Label("Hint", Vector2.Zero, "WASD to move  ·  Mouse to aim, click to fire  ·  Shift to dash  ·  Esc for the menu", 0.34f,
            Color.FromBytes(0x9A, 0xA0, 0xAA));
        var bottom = hint.AddComponent<ScreenAnchor>();
        bottom.Anchor = Anchor.Bottom;
        bottom.Offset = new Vector2(0, 0.5f);
        scene.Add(hint);
    }

    // A network game doesn't stop for one player's menu: it's Resume or Leave.
    private static void AddArenaMenu(Scene scene)
    {
        var panel = new Entity("Paused");
        panel.Transform.Position = new Vector3(0, 0, 3);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        panel.AddComponent<Panel>().OpenOn = "Pause";
        var backdrop = panel.AddComponent<Sprite>();
        backdrop.Size = new Vector2(7, 5.2f);
        backdrop.Tint = new Color(0.07f, 0.08f, 0.1f, 0.92f);
        backdrop.Order = 300;
        scene.Add(panel);

        var heading = Label("Heading", new Vector2(0, 1.6f), "Menu", 1, ArenaOrange);
        heading.GetComponent<Text>()!.Order = 301;
        scene.Add(heading, panel);
        scene.Add(MenuButton("Resume", new Vector2(0, 0.3f), Scenes.ButtonAction.ClosePanel, null, Key.None, ArenaBlue, order: 310), panel);
        scene.Add(MenuButton("Leave Game", new Vector2(0, -0.95f), Scenes.ButtonAction.LeaveGame, "Main", Key.None, ArenaGray, order: 310), panel);
        var hint = Label("Hint", new Vector2(0, -2.15f), "The game goes on while this is open", 0.33f, Color.FromBytes(0x7D, 0x83, 0x90));
        hint.GetComponent<Text>()!.Order = 301;
        scene.Add(hint, panel);
    }

    private static Dictionary<string, Entity> MultiplayerPrefabs() => new Dictionary<string, Entity>
    {
        ["Prefabs/Shot.prefab"] = ArenaShot(),
        ["Prefabs/Slime.prefab"] = ArenaSlime(),
        ["Prefabs/Heart.prefab"] = ArenaHeart(),
        ["Prefabs/Sparkle.prefab"] = Sparkle(),
    };

    // A shot in its player's color: it hurts other players and slimes, and stops at walls.
    private static Entity ArenaShot()
    {
        var shot = new Entity("Shot");
        var sprite = shot.AddComponent<Sprite>();
        sprite.Size = new Vector2(0.32f, 0.32f);
        sprite.Tint = Color.FromBytes(0xFF, 0xE0, 0x7A);
        sprite.Order = 12;
        var collider = shot.AddComponent<CircleCollider>();
        collider.Radius = 0.16f;
        collider.IsTrigger = true;
        collider.Layer = "Shot";
        collider.IgnoreLayers = "Shot";
        shot.AddComponent<Body>().GravityScale = 0;
        var damage = shot.AddComponent<DamageOnTouch>();
        damage.Layers = "Player, Enemy";
        damage.DestroyOnHit = true;
        damage.DestroyOnWall = true;
        damage.Knockback = 5;
        shot.AddComponent<Lifetime>().Seconds = 1.2f;
        var trail = shot.AddComponent<Trail>();
        trail.Interval = 0.02f;
        trail.Fade = 0.12f;
        trail.Opacity = 0.6f;
        return shot;
    }

    private static Entity ArenaSlime()
    {
        var slime = new Entity("Slime");
        var sprite = slime.AddComponent<Sprite>();
        sprite.Image = TemplateArt.Slime;
        sprite.Size = new Vector2(1.1f, 1.1f);
        sprite.Order = 8;
        var collider = slime.AddComponent<CircleCollider>();
        collider.Radius = 0.45f;
        collider.Layer = "Enemy";
        slime.AddComponent<Body>().GravityScale = 0;
        var chase = slime.AddComponent<Chase>();
        chase.Target = "Player";
        chase.Speed = 2.6f;
        chase.Range = 40;
        var health = slime.AddComponent<Health>();
        health.Max = 2;
        health.Current = 2;
        health.InvincibleTime = 0.15f;
        var damage = slime.AddComponent<DamageOnTouch>();
        damage.Layers = "Player";
        damage.Knockback = 7;
        return slime;
    }

    private static Entity ArenaHeart()
    {
        var heart = new Entity("Heart");
        var sprite = heart.AddComponent<Sprite>();
        sprite.Image = TemplateArt.Heart;
        sprite.Size = new Vector2(0.7f, 0.7f);
        var collider = heart.AddComponent<CircleCollider>();
        collider.Radius = 0.35f;
        collider.IsTrigger = true;
        var pickup = heart.AddComponent<Pickup>();
        pickup.Layers = "Player";
        pickup.Heal = 1;
        // Hearts heal; points come from defeating things.
        pickup.Message = null;
        pickup.Effect = "Sparkle";
        pickup.Sound = TemplateSounds.PowerUp;
        return heart;
    }
}
