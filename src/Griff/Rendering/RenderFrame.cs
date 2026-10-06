using System.Numerics;
using Griff.Scenes;

namespace Griff.Rendering;

/// <summary>
/// One quad to draw: a unit quad (-0.5..0.5) placed in the world by <see cref="World"/>, which already
/// includes the sprite's size and flips. For text, <see cref="Text"/> is set, the quad is one line high,
/// and the renderer widens it to the rasterized text's aspect ratio.
/// </summary>
public readonly record struct SpriteDraw(Matrix4x4 World, string? Image, Color Tint, Entity Entity, string? Text = null)
{
    /// <summary>Part of the image to draw, (u, v, width, height) in 0–1 image space. The whole image by default.</summary>
    public Vector4 Source { get; init; } = new(0, 0, 1, 1);
}

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
            Sprites = DrawOrder(scene).SelectMany(Draws).ToList(),
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

    /// <summary>Drawable entities back to front: Order, then Z (higher is closer), then hierarchy order.</summary>
    public static IEnumerable<Entity> DrawOrder(Scene scene) =>
        scene.Entities
            .Where(e => (e.GetComponent<Sprite>() is not null || e.GetComponent<Text>() is not null) && IsActive(e))
            .Select((entity, index) => (entity, index))
            .OrderBy(x => x.entity.GetComponent<Sprite>()?.Order ?? x.entity.GetComponent<Text>()!.Order)
            .ThenBy(x => x.entity.Transform.WorldPosition.Z)
            .ThenBy(x => x.index)
            .Select(x => x.entity);

    // An entity with both draws its sprite, then its text on top.
    private static IEnumerable<SpriteDraw> Draws(Entity entity)
    {
        if (entity.GetComponent<Sprite>() is { } sprite)
            yield return new SpriteDraw(QuadMatrix(entity, sprite), sprite.Image, sprite.Tint, entity) { Source = sprite.FrameRect };

        if (entity.GetComponent<Text>() is { Value.Length: > 0 } text)
        {
            var world = Matrix4x4.CreateScale(text.Size, text.Size, 1) * entity.Transform.WorldMatrix;
            yield return new SpriteDraw(world, null, text.Color, entity, text.Value);
        }
    }

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
