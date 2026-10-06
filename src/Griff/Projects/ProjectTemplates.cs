using System.Numerics;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Projects;

public enum ProjectTemplate
{
    Empty,
    Platformer,
    TopDown,
}

/// <summary>Starter content for new projects: a playable scene and the scripts that drive it.</summary>
public static class ProjectTemplates
{
    public static string Describe(ProjectTemplate template) => template switch
    {
        ProjectTemplate.Platformer => "Run and jump across platforms collecting coins. Arrow keys or WASD, Space to jump.",
        ProjectTemplate.TopDown => "Move in eight directions around a walled room collecting gems. Arrow keys or WASD.",
        _ => "A scene with a camera. Start from scratch.",
    };

    /// <summary>The start scene for a template. Script components are added as missing components until compiled.</summary>
    internal static Scene CreateScene(ProjectTemplate template) => template switch
    {
        ProjectTemplate.Platformer => Platformer(),
        ProjectTemplate.TopDown => TopDown(),
        _ => Empty(),
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
        _ => new Dictionary<string, string>(),
    };

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
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = new Vector2(0.5f, 0.5f);
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
                Game.Destroy(Entity!);
            }
        }

        """;

    private const string ScoreCounterScript = """
        /// <summary>Shows the score in this entity's Text. Other scripts call Add.</summary>
        public class ScoreCounter : Behavior
        {
            public int Score { get; set; }

            public void Add(int points)
            {
                Score += points;
                GetComponent<Text>()!.Value = $"Score: {Score}";
            }
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
