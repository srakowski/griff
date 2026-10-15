using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;
using Griff.Input;
using Griff.Modules.PhysicsGames;
using Griff.Projects;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Web;

/// <summary>
/// Gets .NET in the browser ready for the game while its loading screen still shows, so the game's first moments and its
/// first big collapse play smoothly. .NET for WebAssembly runs Griff in an interpreter that prepares each method the first
/// time it's called, prepares it again, faster, once it has been called a thousand times, and turns the busiest stretches
/// into WebAssembly after that. Left to the game, that work lands in its frames: every step's work is prepared again at
/// once a thousand steps in (a frame of 50 to 110 ms, about 17 seconds into every game), and the first pile to collapse
/// prepares the physics of bodies that turn as it goes (frames of 20 to 45 ms). So a scene of its own is played first,
/// out of sight, for <see cref="Steps"/> steps, each built into a picture (not drawn); in a game with bodies that turn
/// (<see cref="HasTurningBodies"/>), it starts with crates, balls and polygons tumbling into a bin and a tower of posts
/// and planks that break, knocked down by a ball. It adds 0.3 to 0.5 s to loading, 1 to 1.5 s with the pile.
/// </summary>
public static partial class WebWarmup
{
    /// <summary>How many steps it plays: as many calls as the interpreter counts before preparing a method again.</summary>
    public const int Steps = 1000;

    /// <summary>How many of them the pile of bodies that turn plays for, before it's taken away.</summary>
    public const int PileSteps = 90;

    /// <summary>How long the last warm-up took, in milliseconds (0 before one).</summary>
    public static double Milliseconds { get; private set; }

    /// <summary>
    /// Whether any of <paramref name="project"/>'s scenes or prefabs has a <see cref="Body"/> that turns (Rotates), read
    /// straight from their files (a scene loaded just to look would cost more than the warm-up saves).
    /// </summary>
    public static bool HasTurningBodies(Project project)
    {
        if (!Directory.Exists(project.AssetsPath))
            return false;
        foreach (var file in Directory.EnumerateFiles(project.AssetsPath, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if ((extension.Equals(Project.SceneExtension, StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(Project.PrefabExtension, StringComparison.OrdinalIgnoreCase)) && Turning().IsMatch(File.ReadAllText(file)))
                return true;
        }

        return false;
    }

    /// <summary>Plays the warm-up scene, with the pile of bodies that turn if <paramref name="turning"/>.</summary>
    public static void Run(bool turning)
    {
        var started = Stopwatch.GetTimestamp();
        var game = new Game(Scene(turning), new InputState());
        RenderFrame? frame = null;
        for (var i = 0; i < Steps; i++)
        {
            // The pile has done its work: the rest of the steps are of the scene's little else, quickly.
            if (i == PileSteps)
            {
                foreach (var body in game.Scene.All<Body>().Where(b => b.Rotates).ToList())
                    game.Scene.Remove(body.Entity!);
            }

            game.Tick(1 / 60.0);
            frame = SceneRenderer.Build(game, 16f / 9, frame);
        }

        game.Stop();
        Milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    /// <summary>The warm-up's scene: a floor, a camera, a box that falls onto it (as a platformer's player does) and, if <paramref name="turning"/>, the pile.</summary>
    internal static Scene Scene(bool turning)
    {
        var scene = new Scene();
        Add(scene, "Main Camera", new Vector2(0, 3)).AddComponent<Camera>();
        Add(scene, "Floor", new Vector2(0, -0.5f)).AddComponent<BoxCollider>().Size = new Vector2(80, 1);
        var faller = Add(scene, "Faller", new Vector2(8, 2));
        faller.AddComponent<Sprite>().Size = Vector2.One;
        faller.AddComponent<BoxCollider>().Size = Vector2.One;
        faller.AddComponent<Body>();
        if (!turning)
            return scene;

        // A bin of crates, balls and polygons (a triangle, a hexagon and an L, which is two pieces), dropped in a little off true.
        foreach (var x in new[] { -15.5f, -4.5f })
            Add(scene, "Side", new Vector2(x, 3)).AddComponent<BoxCollider>().Size = new Vector2(1, 6);
        for (var i = 0; i < 8; i++)
        {
            var entity = Add(scene, "Shape", new Vector2(-14 + i % 8 * 1.2f, 1 + i % 2 * 1.2f));
            entity.Transform.Rotation = new Vector3(0, 0, i * 37 % 360);
            switch (i % 4)
            {
                case 0:
                    entity.AddComponent<BoxCollider>().Size = new Vector2(0.9f, 0.9f);
                    break;
                case 1:
                    entity.AddComponent<CircleCollider>().Radius = 0.45f;
                    break;
                case 2:
                    entity.AddComponent<PolygonCollider>().Points = i == 2 ? "0 0.5; -0.45 -0.3; 0.45 -0.3" : "0.45 0; 0.22 0.39; -0.22 0.39; -0.45 0; -0.22 -0.39; 0.22 -0.39";
                    break;
                default:
                    entity.AddComponent<PolygonCollider>().Points = "-0.45 -0.45; 0.45 -0.45; 0.45 -0.1; -0.1 -0.1; -0.1 0.45; -0.45 0.45";
                    break;
            }

            entity.AddComponent<Sprite>().Size = new Vector2(0.9f, 0.9f);
            entity.AddComponent<Body>().Rotates = true;
        }

        // A tower of posts and planks, two storeys, asleep, that break, and a heavy ball thrown through it.
        for (var i = 0; i < 6; i++)
        {
            var (storey, part) = (i / 3, i % 3);
            var (at, size) = part switch
            {
                0 => (new Vector2(-1.1f, storey * 2.35f + 1), new Vector2(0.35f, 2)),
                1 => (new Vector2(1.1f, storey * 2.35f + 1), new Vector2(0.35f, 2)),
                _ => (new Vector2(0, storey * 2.35f + 2.175f), new Vector2(3, 0.35f)),
            };
            var entity = Add(scene, part == 2 ? "Plank" : "Post", at);
            entity.AddComponent<Sprite>().Size = size;
            entity.AddComponent<BoxCollider>().Size = size;
            var body = entity.AddComponent<Body>();
            (body.Rotates, body.Asleep, body.Mass) = (true, true, size.X * size.Y);
            var piece = entity.AddComponent<Breakable>();
            (piece.Strength, piece.MinHit) = (6, 1.5f);
        }

        var ball = Add(scene, "Ball", new Vector2(-3, 2));
        ball.AddComponent<CircleCollider>().Radius = 0.6f;
        ball.AddComponent<Sprite>().Size = new Vector2(1.2f, 1.2f);
        var thrown = ball.AddComponent<Body>();
        (thrown.Rotates, thrown.Mass, thrown.Velocity) = (true, 40, new Vector2(30, 2));
        return scene;
    }

    private static Entity Add(Scene scene, string name, Vector2 at)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        scene.Add(entity);
        return entity;
    }

    // A body's Rotates ticked, as scene and prefab files save it.
    [GeneratedRegex("\"rotates\"\\s*:\\s*true")]
    private static partial Regex Turning();
}
