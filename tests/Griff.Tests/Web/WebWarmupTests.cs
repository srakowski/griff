using Griff.Input;
using Griff.Modules.PhysicsGames;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Web;

namespace Griff.Tests.Web;

/// <summary>
/// The web player's warm-up (WebWarmup): a scene played out of sight before the game starts, so .NET's interpreter readies
/// Griff's steps (and, in a game with bodies that turn, their physics and breaking) before the game's frames need them.
/// </summary>
public sealed class WebWarmupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-warmup-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Only_a_game_with_bodies_that_turn_warms_up_their_physics()
    {
        var physics = await Project.CreateAsync(_root, "Topple", ProjectTemplates.Get("Physics"));
        Assert.True(WebWarmup.HasTurningBodies(physics));

        var platformer = await Project.CreateAsync(_root, "Hop", ProjectTemplates.Get("Platformer"));
        Assert.False(WebWarmup.HasTurningBodies(platformer));

        // A body that turns only in a prefab counts too.
        var prefabs = Path.Combine(platformer.AssetsPath, "Prefabs");
        Directory.CreateDirectory(prefabs);
        await File.WriteAllTextAsync(Path.Combine(prefabs, "Crate.prefab"), """{ "components": [ { "type": "Body", "mass": 1, "rotates": true } ] }""");
        Assert.True(WebWarmup.HasTurningBodies(platformer));
    }

    [Fact]
    public void The_pile_tumbles_and_breaks_the_tower_before_it_is_taken_away()
    {
        var scene = WebWarmup.Scene(turning: true);
        var game = new Game(scene, new InputState());
        var shapes = scene.Entities.Where(e => e.Name == "Shape").ToList();
        var dropped = shapes.Select(e => e.Transform.Position.Y).ToList();
        var pieces = scene.All<Breakable>().Count;
        Assert.Equal(8, shapes.Count);
        Assert.Equal(6, pieces);
        for (var i = 0; i < WebWarmup.PileSteps; i++)
            game.Tick(1 / 60.0);

        // The shapes fell into the bin and came to rest on each other (some turned as they landed), and the ball broke the
        // tower: its posts and planks broke into pieces and were taken away.
        Assert.All(shapes.Zip(dropped), s => Assert.True(s.First.Transform.Position.Y < s.Second + 0.01f));
        Assert.Contains(shapes, s => MathF.Abs(s.Transform.Rotation.Z - shapes.IndexOf(s) * 37 % 360) > 1);
        Assert.True(scene.All<Breakable>().Count < pieces, "nothing in the tower broke");
        game.Stop();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void It_plays_its_steps_and_says_how_long_it_took(bool turning)
    {
        WebWarmup.Run(turning);
        Assert.True(WebWarmup.Milliseconds > 0);
    }

    [Fact]
    public void Without_the_pile_it_is_a_floor_a_camera_and_a_box_that_falls()
    {
        var scene = WebWarmup.Scene(turning: false);
        Assert.Equal(["Main Camera", "Floor", "Faller"], scene.Entities.Select(e => e.Name));
        Assert.DoesNotContain(scene.All<Body>(), b => b.Rotates);
    }
}
