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
}

/// <summary>Starter content for new projects: a playable scene and the scripts that drive it.</summary>
public static class ProjectTemplates
{
    public static string Describe(ProjectTemplate template) => template switch
    {
        ProjectTemplate.Platformer => "Run and jump across platforms collecting coins. Arrow keys or WASD, Space to jump.",
        ProjectTemplate.TopDown => "Move in eight directions around a walled room collecting gems. Arrow keys or WASD.",
        ProjectTemplate.Arcade => "Blast waves of enemies in an arena, with a menu, game over and high score. WASD to fly, mouse to aim, click to fire.",
        _ => "A scene with a camera. Start from scratch.",
    };

    /// <summary>The start scene for a template. Script components are added as missing components until compiled.</summary>
    internal static Scene CreateScene(ProjectTemplate template) => template switch
    {
        ProjectTemplate.Platformer => Platformer(),
        ProjectTemplate.TopDown => TopDown(),
        ProjectTemplate.Arcade => ArcadeMenu(),
        _ => Empty(),
    };

    /// <summary>Scenes besides the start scene (relative to Assets) for a template.</summary>
    internal static IReadOnlyDictionary<string, Scene> MoreScenes(ProjectTemplate template) => template switch
    {
        ProjectTemplate.Arcade => new Dictionary<string, Scene>
        {
            ["Scenes/Game.scene"] = ArcadeGame(),
        },
        _ => new Dictionary<string, Scene>(),
    };

    /// <summary>Script files (relative to Assets) for a template.</summary>
    internal static IReadOnlyDictionary<string, string> Scripts(ProjectTemplate template) => template switch
    {
        ProjectTemplate.Platformer => new Dictionary<string, string>
        {
            ["Scripts/PlayerController.cs"] = PlayerControllerScript,
            ["Scripts/Collectible.cs"] = CollectibleScript,
            ["Scripts/CameraFollow.cs"] = CameraFollowScript,
            ["Scripts/ScoreCounter.cs"] = ScoreCounterScript,
        },
        ProjectTemplate.TopDown => new Dictionary<string, string>
        {
            ["Scripts/TopDownController.cs"] = TopDownControllerScript,
            ["Scripts/Collectible.cs"] = CollectibleScript,
            ["Scripts/CameraFollow.cs"] = CameraFollowScript,
            ["Scripts/ScoreCounter.cs"] = ScoreCounterScript,
        },
        ProjectTemplate.Arcade => new Dictionary<string, string>
        {
            ["Scripts/Ship.cs"] = ShipScript,
            ["Scripts/Bullet.cs"] = BulletScript,
            ["Scripts/Enemy.cs"] = EnemyScript,
            ["Scripts/EnemySpawner.cs"] = EnemySpawnerScript,
            ["Scripts/ScoreCounter.cs"] = ScoreCounterScript,
            ["Scripts/GameOver.cs"] = GameOverScript,
            ["Scripts/BestScore.cs"] = BestScoreScript,
        },
        _ => new Dictionary<string, string>(),
    };

    /// <summary>Prefab files (relative to Assets) for a template.</summary>
    internal static IReadOnlyDictionary<string, Entity> Prefabs(ProjectTemplate template) => template switch
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
        _ => new Dictionary<string, Entity>(),
    };

    // A one-shot burst that cleans itself up; collectibles spawn it when picked up.
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

        AddCameraWithScore(scene);

        var player = Box("Player", new Vector2(-6, -2), new Vector2(0.8f, 1), Color.FromBytes(0xF0, 0x8C, 0x2E));
        player.AddComponent<Body>();
        player.AddComponent(Script("PlayerController"));
        scene.Add(player);

        var level = new Entity("Level");
        scene.Add(level);
        var gray = Color.FromBytes(0x48, 0x4B, 0x55);
        scene.Add(Box("Ground", new Vector2(0, -4.5f), new Vector2(30, 1), gray), level);
        scene.Add(Platform(new Vector2(-1, -1.5f), new Vector2(4, 0.5f), gray), level);
        scene.Add(Platform(new Vector2(4.5f, 0.5f), new Vector2(3, 0.5f), gray), level);
        scene.Add(Box("Wall", new Vector2(-15.5f, 0), new Vector2(1, 10), gray), level);
        scene.Add(Box("Wall", new Vector2(15.5f, 0), new Vector2(1, 10), gray), level);

        var coins = new Entity("Coins");
        scene.Add(coins);
        foreach (var position in new[] { new Vector2(-1, -0.5f), new Vector2(4.5f, 1.5f), new Vector2(9, -3.5f) })
            scene.Add(Collectible("Coin", position, Color.FromBytes(0xE3, 0xB3, 0x41)), coins);

        return scene;
    }

    private static Scene TopDown()
    {
        var scene = new Scene();

        AddCameraWithScore(scene);

        var player = Box("Player", Vector2.Zero, new Vector2(0.8f, 0.8f), Color.FromBytes(0x4C, 0x9A, 0xFF));
        player.AddComponent<Body>().GravityScale = 0;
        player.AddComponent(Script("TopDownController"));
        scene.Add(player);

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

        return scene;
    }

    // The score is a child of the camera, so it stays on screen as the camera moves.
    private static void AddCameraWithScore(Scene scene)
    {
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>().Size = 6;
        camera.AddComponent(Script("CameraFollow"));
        scene.Add(camera);

        var score = new Entity("Score");
        score.Transform.Position = new Vector3(0, 5.2f, 1);
        var text = score.AddComponent<Text>();
        text.Value = "Score: 0";
        text.Size = 0.8f;
        text.Order = 100;
        score.AddComponent(Script("ScoreCounter"));
        scene.Add(score, camera);
    }

    // The Arcade menu: a title, the best score, and buttons that need no code (Play loads Game, Quit quits).
    private static Scene ArcadeMenu()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>().Size = 6;
        scene.Add(camera);

        scene.Add(Label("Title", new Vector2(0, 2.6f), "ARCADE", 1.6f, Color.FromBytes(0xF0, 0x8C, 0x2E)));
        var best = Label("Best", new Vector2(0, 1.3f), "Best: 0", 0.5f, Color.FromBytes(0xB4, 0xB9, 0xC4));
        best.AddComponent(Script("BestScore"));
        scene.Add(best);

        scene.Add(MenuButton("Play", new Vector2(0, -0.4f), Scenes.ButtonAction.LoadScene, "Game", Runtime.Key.Enter, Color.FromBytes(0x4C, 0x9A, 0xFF)));
        scene.Add(MenuButton("Quit", new Vector2(0, -1.9f), Scenes.ButtonAction.Quit, null, Runtime.Key.Escape, Color.FromBytes(0x3A, 0x3F, 0x4B)));

        // Pinned to the bottom of the screen, whatever its shape.
        var hint = Label("Hint", Vector2.Zero, "Enter to play  ·  Esc to quit", 0.4f, Color.FromBytes(0x7D, 0x83, 0x90));
        var anchor = hint.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Bottom;
        anchor.Offset = new Vector2(0, 0.6f);
        scene.Add(hint);
        return scene;
    }

    // The Arcade game: a walled arena, the ship, a spawner, the score in the corner and a hidden game over panel.
    private static Scene ArcadeGame()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>().Size = 8;
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

        var score = Label("Score", Vector2.Zero, "Score: 0", 0.8f, Color.White);
        score.Transform.Position = new Vector3(0, 0, 1);
        score.GetComponent<Text>()!.Align = TextAlign.Left;
        var corner = score.AddComponent<ScreenAnchor>();
        corner.Offset = new Vector2(0.6f, -0.7f);
        score.AddComponent(Script("ScoreCounter"));
        scene.Add(score);

        // Shown by the ship when it's destroyed. Its buttons only work once it's visible.
        var panel = new Entity("Game Over") { IsEnabled = false };
        panel.Transform.Position = new Vector3(0, 0, 2);
        var backdrop = panel.AddComponent<Sprite>();
        backdrop.Size = new Vector2(12, 6);
        backdrop.Tint = new Color(0.07f, 0.08f, 0.1f, 0.9f);
        backdrop.Order = 200;
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
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
        return scene;
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
        entity.AddComponent(Script("Collectible"));
        return entity;
    }

    // Scripts aren't compiled yet when the template is written; save their references as missing components,
    // which become the real scripts as soon as the project opens and compiles.
    private static MissingComponent Script(string typeName)
    {
        using var document = System.Text.Json.JsonDocument.Parse($$"""{ "type": "{{typeName}}" }""");
        return new MissingComponent(typeName, document.RootElement);
    }

    private const string PlayerControllerScript = """
        /// <summary>Run with A/D, the arrow keys or a gamepad; jump with Space, W, Up or the A button while on the ground.</summary>
        public class PlayerController : Behavior
        {
            public float Speed { get; set; } = 7;

            public float JumpSpeed { get; set; } = 13;

            protected override void Update()
            {
                var body = GetComponent<Body>()!;
                var velocity = body.Velocity;
                velocity.X = Input.Horizontal * Speed;

                var jump = Input.WasPressed(Key.Space) || Input.WasPressed(Key.W) || Input.WasPressed(Key.Up) ||
                           Input.WasPressed(GamepadButton.A);
                if (jump && body.IsGrounded)
                    velocity.Y = JumpSpeed;

                body.Velocity = velocity;
            }
        }

        """;

    private const string TopDownControllerScript = """
        /// <summary>Move in eight directions with WASD or the arrow keys.</summary>
        public class TopDownController : Behavior
        {
            public float Speed { get; set; } = 6;

            protected override void Update()
            {
                var direction = new Vector2(Input.Horizontal, Input.Vertical);
                if (direction != Vector2.Zero)
                    direction = Vector2.Normalize(direction);

                GetComponent<Body>()!.Velocity = direction * Speed;
            }
        }

        """;

    private const string CollectibleScript = """
        /// <summary>Spins in place and disappears when the player touches it.</summary>
        public class Collectible : Behavior
        {
            public float SpinSpeed { get; set; } = 90;

            protected override void Update() =>
                Transform.Rotation += new Vector3(0, 0, SpinSpeed * Time.Delta);

            protected override void OnTriggerEnter(Entity other)
            {
                if (other.Name != "Player")
                    return;

                Game.Find("Score")?.GetComponent<ScoreCounter>()?.Add(1);

                // A burst of sparkles in this collectible's color (Prefabs/Sparkle) and a little camera kick.
                var position = Transform.WorldPosition;
                var sparkle = Game.Spawn("Sparkle", new Vector2(position.X, position.Y));
                if (sparkle.GetComponent<ParticleEmitter>() is { } sparks && GetComponent<Sprite>() is { } sprite)
                {
                    sparks.StartColor = sprite.Tint;
                    sparks.EndColor = sprite.Tint with { A = 0 };
                }

                Game.ShakeCamera(0.12f, 0.15f);
                Game.Destroy(Entity!);
            }
        }

        """;

    private const string ScoreCounterScript = """
        /// <summary>Shows the score in this entity's Text, with a little pop when it goes up. Other scripts call Add.</summary>
        public class ScoreCounter : Behavior
        {
            public int Score { get; set; }

            public void Add(int points)
            {
                Score += points;
                GetComponent<Text>()!.Value = $"Score: {Score}";

                Transform.Scale = new Vector3(1.4f, 1.4f, 1);
                ScaleTo(Vector2.One, 0.3f, Ease.OutBack);
            }
        }

        """;

    private const string ShipScript = """
        /// <summary>Fly with WASD, the arrow keys or a gamepad. Aim with the mouse; hold the left button or Space to fire.</summary>
        public class Ship : Behavior
        {
            public float Speed { get; set; } = 8;

            /// <summary>Shots per second.</summary>
            public float FireRate { get; set; } = 8;

            public float BulletSpeed { get; set; } = 18;

            private float _cooldown;

            protected override void Update()
            {
                var move = new Vector2(Input.Horizontal, Input.Vertical);
                if (move.LengthSquared() > 1)
                    move = Vector2.Normalize(move);
                GetComponent<Body>()!.Velocity = move * Speed;

                // Turn to face the mouse.
                var position = new Vector2(Transform.Position.X, Transform.Position.Y);
                var aim = Input.MouseWorld - position;
                if (aim != Vector2.Zero)
                    Transform.Rotation = Transform.Rotation with { Z = MathF.Atan2(aim.Y, aim.X) * 180 / MathF.PI };

                _cooldown -= Time.Delta;
                var firing = Input.IsDown(MouseButton.Left) || Input.IsDown(Key.Space) || Input.IsDown(GamepadButton.RightShoulder);
                if (firing && _cooldown <= 0 && aim != Vector2.Zero)
                {
                    _cooldown = 1 / FireRate;
                    var bullet = Game.Spawn("Bullet", position);
                    bullet.GetComponent<Body>()!.Velocity = Vector2.Normalize(aim) * BulletSpeed;
                }
            }

            protected override void OnTriggerEnter(Entity other)
            {
                if (other.GetComponent<Enemy>() is not null)
                    Explode();
            }

            private void Explode()
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
                Game.Find<GameOver>()?.Show();
                Game.Destroy(Entity!);
            }
        }

        """;

    private const string BulletScript = """
        /// <summary>Flies until it hits an enemy or a wall, or runs out of time.</summary>
        public class Bullet : Behavior
        {
            public float Lifetime { get; set; } = 1.5f;

            private bool _spent;

            protected override void Start() => After(Lifetime, () => Game.Destroy(Entity!));

            protected override void OnTriggerEnter(Entity other)
            {
                if (_spent)
                    return;

                if (other.GetComponent<Enemy>() is { } enemy)
                {
                    enemy.Hit();
                    Spend();
                }
                else if (other.Name == "Wall")
                    Spend();
            }

            private void Spend()
            {
                _spent = true;
                Game.Destroy(Entity!);
            }
        }

        """;

    private const string EnemyScript = """
        /// <summary>Chases the ship. One hit destroys it, for a point.</summary>
        public class Enemy : Behavior
        {
            public float Speed { get; set; } = 2.5f;

            private Entity? _target;
            private bool _alive;

            protected override void Start()
            {
                _alive = true;
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

            public void Hit()
            {
                if (!_alive)
                    return;
                _alive = false;

                Game.Find<ScoreCounter>()?.Add(1);
                var position = Transform.WorldPosition;
                var burst = Game.Spawn("Sparkle", new Vector2(position.X, position.Y));
                if (burst.GetComponent<ParticleEmitter>() is { } sparks && GetComponent<Sprite>() is { } sprite)
                {
                    sparks.StartColor = sprite.Tint;
                    sparks.EndColor = sprite.Tint with { A = 0 };
                }

                Game.ShakeCamera(0.15f, 0.12f);
                Game.Destroy(Entity!);
            }
        }

        """;

    private const string EnemySpawnerScript = """
        /// <summary>Spawns enemies around the edge of the arena, faster and faster, until the ship is gone.</summary>
        public class EnemySpawner : Behavior
        {
            /// <summary>Enemies appear on the edge of a box this size around the middle.</summary>
            public Vector2 ArenaSize { get; set; } = new(22, 12);

            public float StartInterval { get; set; } = 1.5f;

            public float MinInterval { get; set; } = 0.35f;

            /// <summary>Seconds taken off the interval for every second played.</summary>
            public float Ramp { get; set; } = 0.02f;

            protected override void Start() => StartCoroutine(SpawnWaves());

            private IEnumerator SpawnWaves()
            {
                yield return Wait(1);
                while (Game.Find("Player") is { } player)
                {
                    Game.Spawn("Enemy", PickSpot(player));
                    yield return Wait(MathF.Max(MinInterval, StartInterval - Ramp * (float)Time.Total));
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
        /// <summary>The game over panel: hidden until the ship is destroyed, then shows the score and saves the best.</summary>
        public class GameOver : Behavior
        {
            public void Show() => Entity!.IsEnabled = true;

            // Runs the first frame the panel is shown.
            protected override void Start()
            {
                var score = Game.Find<ScoreCounter>()?.Score ?? 0;
                var best = Math.Max(score, Game.SaveData.GetInt("best"));
                Game.SaveData.Set("best", best);

                var result = Entity!.Children.FirstOrDefault(c => c.Name == "Result")?.GetComponent<Text>();
                if (result is not null)
                    result.Value = score >= best && score > 0 ? $"New best: {score}!" : $"Score: {score}   Best: {best}";

                Transform.Scale = new Vector3(0.6f, 0.6f, 1);
                ScaleTo(Vector2.One, 0.35f, Ease.OutBack);
            }
        }

        """;

    private const string BestScoreScript = """
        /// <summary>Shows the best score saved by GameOver.</summary>
        public class BestScore : Behavior
        {
            protected override void Start() => GetComponent<Text>()!.Value = $"Best: {Game.SaveData.GetInt("best")}";
        }

        """;

    private const string CameraFollowScript = """
        /// <summary>Smoothly follows the entity named Target.</summary>
        public class CameraFollow : Behavior
        {
            public string Target { get; set; } = "Player";

            /// <summary>Higher is snappier.</summary>
            public float Smoothing { get; set; } = 6;

            private Entity? _target;

            protected override void Start() => _target = Game.Find(Target);

            protected override void Update()
            {
                if (_target is null)
                    return;

                var goal = _target.Transform.WorldPosition with { Z = Transform.Position.Z };
                Transform.Position = Vector3.Lerp(Transform.Position, goal, 1 - MathF.Exp(-Smoothing * Time.Delta));
            }
        }

        """;
}
