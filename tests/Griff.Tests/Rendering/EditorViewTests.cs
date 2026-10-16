using System.Numerics;
using Griff.Projects;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Rendering;

/// <summary>
/// The frame the editor's Scene view is drawn from (<see cref="SceneRenderer.BuildView"/>) is the game's frame of the same
/// part of the world, draw for draw, with none of what a camera adds: so every drawable component, a board drawing its
/// tilemap among them, looks the same in the Scene view as in the game, from its one implementation.
/// </summary>
public class EditorViewTests
{
    public static TheoryData<string> Templates => [.. ProjectTemplates.All.Select(t => t.Name)];

    [Theory]
    [MemberData(nameof(Templates))]
    public void EveryTemplateScene_IsTheGamesFrameOfIt_AsSeenFromItsCamera(string name)
    {
        var template = ProjectTemplates.Get(name);
        foreach (var (path, scene) in template.MoreScenes().Prepend(new("Start", template.StartScene())))
        {
            var game = SceneRenderer.Build(scene, 16f / 9);
            // The part of the world the game's camera sees: where it is, its size up and down, and that times the aspect across.
            var camera = Views.FindCamera(scene);
            var size = camera?.GetComponent<Camera>()?.Size ?? Views.DefaultCameraSize;
            var at = camera?.Transform.WorldPosition ?? Vector3.Zero;
            var view = new Box(new Vector2(at.X, at.Y), new Vector2(size * 16f / 9, size));
            Assert.Equal(game.ViewProjection, Views.ViewProjection(camera, size, 16f / 9));

            var editor = SceneRenderer.BuildView(scene, view, size / 180, Views.DefaultBackground);

            // Parallax layers are drawn where they're placed in the editor, rather than shifted for a camera.
            Assert.Equal(Drawn(game, scene), Drawn(editor, scene));
            Assert.Null(editor.Effects);
            Assert.Empty(editor.Lights);
            Assert.Equal(view.Center, Vector2.Transform(Vector2.Zero, Inverse(editor.ViewProjection)), new Vector2Comparer());
        }
    }

    [Fact]
    public void AParallaxLayer_IsDrawnWhereItsPlaced_Once()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>();
        camera.Transform.Position = new Vector3(40, 0, 0);
        scene.Add(camera);
        var hills = new Entity("Hills");
        hills.AddComponent<Sprite>();
        hills.Transform.Position = new Vector3(40, 0, 0);
        var parallax = hills.AddComponent<Parallax>();
        (parallax.Factor, parallax.RepeatX) = (new Vector2(0.5f, 0.5f), true);
        scene.Add(hills);

        // A camera's view would shift it by half the camera's place, and repeat it across the view.
        var frame = SceneRenderer.BuildView(scene, new Box(new Vector2(40, 0), new Vector2(16, 9)), 0.05f, Views.DefaultBackground);

        var draw = Assert.Single(frame.Sprites);
        Assert.Equal(new Vector2(40, 0), new Vector2(draw.World.M41, draw.World.M42));
    }

    [Fact]
    public void AnEntitysOpacity_FadesItsDraws_OrLeavesThemOut()
    {
        var scene = new Scene();
        var (shown, faded, hidden) = (Add("Shown"), Add("Faded"), Add("Hidden"));

        var frame = SceneRenderer.BuildView(scene, new Box(Vector2.Zero, new Vector2(16, 9)), 0.05f, Views.DefaultBackground,
            opacity: e => e == faded ? 0.25f : e == hidden ? 0 : 1);

        Assert.Equal([shown, faded], frame.Sprites.Select(d => d.Entity));
        Assert.Equal([1f, 0.25f], frame.Sprites.Select(d => d.Tint.A));

        Entity Add(string entityName)
        {
            var entity = new Entity(entityName);
            entity.AddComponent<Sprite>();
            scene.Add(entity);
            return entity;
        }
    }

    // A scene's draws, less those of parallax layers and of the screen.
    private static List<SpriteDraw> Drawn(RenderFrame frame, Scene scene) =>
        [.. frame.Sprites.Where(d => d.Entity.Scene == scene && !InParallax(d.Entity))];

    private static bool InParallax(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Parallax>() is not null)
                return true;
        }

        return false;
    }

    private static Matrix4x4 Inverse(Matrix4x4 matrix) => Matrix4x4.Invert(matrix, out var inverse) ? inverse : throw new InvalidOperationException();

    private sealed class Vector2Comparer : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 1e-3f;

        public int GetHashCode(Vector2 v) => 0;
    }
}
