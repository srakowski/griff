using System.Numerics;
using Griff.Scenes;

namespace Griff.Rendering;

/// <summary>
/// One sprite to draw: a unit quad (-0.5..0.5) placed in the world by <see cref="World"/>,
/// which already includes the sprite's size and flips.
/// </summary>
public readonly record struct SpriteDraw(Matrix4x4 World, string? Image, Color Tint, Entity Entity);

/// <summary>Everything a renderer needs for one frame, in draw order (back to front).</summary>
public sealed class RenderFrame
{
    public required Color Background { get; init; }

    /// <summary>World to clip space (-1..1) for the active camera.</summary>
    public required Matrix4x4 ViewProjection { get; init; }

    public required IReadOnlyList<SpriteDraw> Sprites { get; init; }

    /// <summary>The camera used, or null if the scene has none (a default camera at the origin is used).</summary>
    public Entity? Camera { get; init; }
}

/// <summary>Builds <see cref="RenderFrame"/>s from scenes. Shared by the editor and the game so they always agree.</summary>
public static class SceneRenderer
{
    public const float DefaultCameraSize = 5f;

    public static readonly Color DefaultBackground = Color.FromBytes(0x1E, 0x22, 0x2B);

    /// <param name="aspect">Viewport width divided by height.</param>
    public static RenderFrame Build(Scene scene, float aspect)
    {
        var camera = FindCamera(scene);
        var component = camera?.GetComponent<Camera>();

        return new RenderFrame
        {
            Background = component?.Background ?? DefaultBackground,
            ViewProjection = ViewProjection(camera, component?.Size ?? DefaultCameraSize, aspect),
            Sprites = DrawOrder(scene)
                .Select(e => (Entity: e, Sprite: e.GetComponent<Sprite>()!))
                .Select(x => new SpriteDraw(QuadMatrix(x.Entity, x.Sprite), x.Sprite.Image, x.Sprite.Tint, x.Entity))
                .ToList(),
            Camera = camera,
        };
    }

    /// <summary>The first enabled entity with a Camera, in hierarchy order.</summary>
    public static Entity? FindCamera(Scene scene) =>
        scene.Entities.FirstOrDefault(e => IsActive(e) && e.GetComponent<Camera>() is not null);

    /// <summary>
    /// Orthographic projection of the camera's view. <paramref name="size"/> is half the visible height;
    /// the camera's position and rotation apply, its scale doesn't.
    /// </summary>
    public static Matrix4x4 ViewProjection(Entity? camera, float size, float aspect)
    {
        var view = Matrix4x4.Identity;
        if (camera is not null)
        {
            var transform = camera.Transform;
            var world = Matrix4x4.CreateFromQuaternion(transform.RotationQuaternion) * Matrix4x4.CreateTranslation(transform.WorldPosition);
            Matrix4x4.Invert(world, out view);
        }

        var height = size * 2;
        var projection = Matrix4x4.CreateOrthographic(height * aspect, height, -1000f, 1000f);
        return view * projection;
    }

    /// <summary>Sprites back to front: Order, then Z (higher is closer), then hierarchy order.</summary>
    public static IEnumerable<Entity> DrawOrder(Scene scene) =>
        scene.Entities
            .Where(e => e.GetComponent<Sprite>() is not null && IsActive(e))
            .Select((entity, index) => (entity, index))
            .OrderBy(x => x.entity.GetComponent<Sprite>()!.Order)
            .ThenBy(x => x.entity.Transform.WorldPosition.Z)
            .ThenBy(x => x.index)
            .Select(x => x.entity);

    /// <summary>True if the entity and all its ancestors are enabled.</summary>
    public static bool IsActive(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (!e.IsEnabled)
                return false;
        }

        return true;
    }

    private static Matrix4x4 QuadMatrix(Entity entity, Sprite sprite) =>
        Matrix4x4.CreateScale(sprite.Size.X * (sprite.FlipX ? -1 : 1), sprite.Size.Y * (sprite.FlipY ? -1 : 1), 1) *
        entity.Transform.WorldMatrix;
}
