using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Rendering;

public class SceneRendererTests
{
    private const float Tolerance = 1e-4f;
    private readonly Scene _scene = new();

    private Entity AddSprite(string name, int order = 0, float z = 0, Entity? parent = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(0, 0, z);
        entity.AddComponent<Sprite>().Order = order;
        _scene.Add(entity, parent);
        return entity;
    }

    [Fact]
    public void Sprites_AreSortedByOrderThenZThenHierarchy()
    {
        var top = AddSprite("Top", order: 5);
        var first = AddSprite("First");
        var near = AddSprite("Near", z: 1);
        var second = AddSprite("Second");

        var frame = SceneRenderer.Build(_scene, 1);

        Assert.Equal([first, second, near, top], frame.Sprites.Select(s => s.Entity));
    }

    [Fact]
    public void DisabledEntities_AndTheirChildren_AreNotDrawn()
    {
        var parent = AddSprite("Parent");
        parent.IsEnabled = false;
        AddSprite("Child", parent: parent);
        var visible = AddSprite("Visible");

        var frame = SceneRenderer.Build(_scene, 1);

        Assert.Equal([visible], frame.Sprites.Select(s => s.Entity));
    }

    [Fact]
    public void QuadMatrix_IncludesSizeFlipAndTransform()
    {
        var entity = AddSprite("Sprite");
        var sprite = entity.GetComponent<Sprite>()!;
        sprite.Size = new Vector2(2, 3);
        sprite.FlipX = true;
        entity.Transform.Position = new Vector3(10, 0, 0);

        var world = SceneRenderer.Build(_scene, 1).Sprites[0].World;

        // The quad's right edge (0.5, 0) lands at x = 10 - 1 because of the flip.
        AssertNear(new Vector3(9, 0, 0), Vector3.Transform(new Vector3(0.5f, 0, 0), world));
        AssertNear(new Vector3(10, 1.5f, 0), Vector3.Transform(new Vector3(0, 0.5f, 0), world));
    }

    [Fact]
    public void Camera_MapsItsFrameToClipSpace()
    {
        var cameraEntity = new Entity("Camera");
        cameraEntity.Transform.Position = new Vector3(3, 4, 0);
        cameraEntity.AddComponent<Camera>().Size = 2;
        _scene.Add(cameraEntity);

        var frame = SceneRenderer.Build(_scene, aspect: 2);

        Assert.Same(cameraEntity, frame.Camera);
        AssertNear(Vector3.Zero, Project(frame, new Vector3(3, 4, 0)));
        AssertNear(new Vector3(0, 1, 0), Project(frame, new Vector3(3, 6, 0)));   // top edge: y + size
        AssertNear(new Vector3(1, 0, 0), Project(frame, new Vector3(7, 4, 0)));   // right edge: x + size * aspect
    }

    [Fact]
    public void Camera_RotationRotatesTheView()
    {
        var cameraEntity = new Entity("Camera");
        cameraEntity.Transform.Rotation = new Vector3(0, 0, 90);
        cameraEntity.AddComponent<Camera>().Size = 1;
        _scene.Add(cameraEntity);

        var frame = SceneRenderer.Build(_scene, aspect: 1);

        // With the camera turned 90° left, a point to its world-left appears at the top of the screen.
        AssertNear(new Vector3(0, -1, 0), Project(frame, new Vector3(1, 0, 0)));
    }

    [Fact]
    public void NoCamera_UsesDefaults()
    {
        var frame = SceneRenderer.Build(_scene, aspect: 1);

        Assert.Null(frame.Camera);
        Assert.Equal(Views.DefaultBackground, frame.Background);
        AssertNear(new Vector3(0, 1, 0), Project(frame, new Vector3(0, Views.DefaultCameraSize, 0)));
    }

    private static Vector3 Project(RenderFrame frame, Vector3 world)
    {
        var clip = Vector4.Transform(new Vector4(world, 1), frame.ViewProjection);
        return new Vector3(clip.X, clip.Y, 0) / clip.W;
    }

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < Tolerance, $"Expected {expected}, got {actual}");
}
