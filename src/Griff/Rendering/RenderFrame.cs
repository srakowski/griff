using System.Numerics;
using Griff.Scenes;

namespace Griff.Rendering;

/// <summary>
/// One quad to draw: a unit quad (-0.5..0.5) placed in the world by <see cref="World"/>, which already
/// includes the sprite's size and flips. For text, <see cref="Text"/> is set, the quad is one line high,
/// and the renderer widens it to the rasterized text's aspect ratio (and shifts it for <see cref="Align"/>).
/// </summary>
public readonly record struct SpriteDraw(Matrix4x4 World, string? Image, Color Tint, Entity Entity, string? Text = null)
{
    /// <summary>Part of the image to draw, (u, v, width, height) in 0–1 image space. The whole image by default.</summary>
    public Vector4 Source { get; init; } = new(0, 0, 1, 1);

    /// <summary>For text: the font (see <see cref="Scenes.Text.Font"/>), or null for the built-in one.</summary>
    public string? Font { get; init; }

    /// <summary>For text: where the quad sits relative to the origin. The renderer shifts it once it knows the width.</summary>
    public TextAlign Align { get; init; }
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

    // Stands in for the entity of draws that belong to the screen rather than the scene, like the scene-change fade.
    private static readonly Entity ScreenEntity = new("Screen");

    /// <param name="aspect">Viewport width divided by height.</param>
    public static RenderFrame Build(Scene scene, float aspect) => Build(scene, aspect, Color.Transparent);

    /// <summary>Builds a running game's frame: its scene, with the screen fade of a scene change on top.</summary>
    public static RenderFrame Build(Runtime.Game game, float aspect) =>
        Build(game.Scene, aspect, game.FadeColor with { A = game.FadeColor.A * game.FadeAmount });

    private static RenderFrame Build(Scene scene, float aspect, Color overlay)
    {
        var camera = FindCamera(scene);
        var component = camera?.GetComponent<Camera>();
        var viewProjection = ViewProjection(camera, component?.Size ?? DefaultCameraSize, aspect);
        var sprites = DrawOrder(scene).SelectMany(Draws).ToList();

        // A quad over the whole view: twice the unit quad in clip space, taken back into the world.
        if (overlay.A > 0 && Matrix4x4.Invert(viewProjection, out var clipToWorld))
            sprites.Add(new SpriteDraw(Matrix4x4.CreateScale(2, 2, 1) * clipToWorld, null, overlay, ScreenEntity));

        return new RenderFrame
        {
            Background = component?.Background ?? DefaultBackground,
            ViewProjection = viewProjection,
            Sprites = sprites,
            Camera = camera,
        };
    }

    /// <summary>The first enabled entity with a Camera, in hierarchy order.</summary>
    public static Entity? FindCamera(Scene scene) =>
        scene.Entities.FirstOrDefault(e => IsActive(e) && e.GetComponent<Camera>() is not null);

    /// <summary>
    /// Orthographic projection of the camera's view. <paramref name="size"/> is half the visible height;
    /// the camera's position, rotation and shake apply, its scale doesn't.
    /// </summary>
    public static Matrix4x4 ViewProjection(Entity? camera, float size, float aspect)
    {
        var view = Matrix4x4.Identity;
        if (camera is not null)
        {
            var transform = camera.Transform;
            var shake = camera.GetComponent<Camera>()?.ShakeOffset ?? Vector2.Zero;
            var world = Matrix4x4.CreateFromQuaternion(transform.RotationQuaternion) *
                        Matrix4x4.CreateTranslation(transform.WorldPosition + new Vector3(shake, 0));
            Matrix4x4.Invert(world, out view);
        }

        var height = size * 2;
        var projection = Matrix4x4.CreateOrthographic(height * aspect, height, -1000f, 1000f);
        return view * projection;
    }

    /// <summary>Drawable entities back to front: Order, then Z (higher is closer), then hierarchy order.</summary>
    public static IEnumerable<Entity> DrawOrder(Scene scene) =>
        scene.Entities
            .Where(e => OrderOf(e) is not null && IsActive(e))
            .Select((entity, index) => (entity, index))
            .OrderBy(x => OrderOf(x.entity))
            .ThenBy(x => x.entity.Transform.WorldPosition.Z)
            .ThenBy(x => x.index)
            .Select(x => x.entity);

    /// <summary>The draw order of an entity (its sprite's, tilemap's, text's or particles', in that preference), or null if it draws nothing.</summary>
    public static int? OrderOf(Entity entity) =>
        entity.GetComponent<Sprite>()?.Order ?? entity.GetComponent<Tilemap>()?.Order ?? entity.GetComponent<Text>()?.Order ??
        entity.GetComponent<ParticleEmitter>()?.Order;

    // An entity with several draws its tilemap, then its sprite, its text, and its particles on top.
    // A button shades its own sprite and text to show it's under the mouse or held.
    private static IEnumerable<SpriteDraw> Draws(Entity entity)
    {
        var button = entity.GetComponent<Button>();

        if (entity.GetComponent<Tilemap>() is { } tilemap)
        {
            foreach (var draw in TileDraws(entity, tilemap))
                yield return draw;
        }

        if (entity.GetComponent<Sprite>() is { } sprite)
            yield return new SpriteDraw(QuadMatrix(entity, sprite), sprite.Image, button?.Shade(sprite.Tint) ?? sprite.Tint, entity) { Source = sprite.FrameRect };

        if (entity.GetComponent<Text>() is { Value.Length: > 0 } text)
        {
            var world = Matrix4x4.CreateScale(text.Size, text.Size, 1) * entity.Transform.WorldMatrix;
            yield return new SpriteDraw(world, null, button?.Shade(text.Color) ?? text.Color, entity, text.Value)
            {
                Font = string.IsNullOrWhiteSpace(text.Font) ? null : text.Font,
                Align = text.Align,
            };
        }

        if (entity.GetComponent<ParticleEmitter>() is { Particles.Count: > 0 } emitter)
        {
            foreach (var draw in ParticleDraws(entity, emitter))
                yield return draw;
        }
    }

    /// <summary>One quad per live particle, in world space at the emitter's depth.</summary>
    public static IEnumerable<SpriteDraw> ParticleDraws(Entity entity, ParticleEmitter emitter)
    {
        var z = entity.Transform.WorldPosition.Z;
        var image = string.IsNullOrEmpty(emitter.Image) ? null : emitter.Image;
        foreach (var particle in emitter.Particles)
        {
            var progress = particle.Progress;
            var size = emitter.SizeAt(progress);
            if (size <= 0)
                continue;
            var world = Matrix4x4.CreateScale(size, size, 1) * Matrix4x4.CreateTranslation(particle.Position.X, particle.Position.Y, z);
            yield return new SpriteDraw(world, image, emitter.ColorAt(progress), entity);
        }
    }

    /// <summary>One quad per tile, in the tilemap's space. Without an image, tiles are solid squares of the tint.</summary>
    public static IEnumerable<SpriteDraw> TileDraws(Entity entity, Tilemap tilemap)
    {
        var world = entity.Transform.WorldMatrix;
        var size = Matrix4x4.CreateScale(tilemap.TileSize, tilemap.TileSize, 1);
        var image = string.IsNullOrEmpty(tilemap.Image) ? null : tilemap.Image;
        foreach (var (x, y, tile) in tilemap.Cells())
        {
            var center = tilemap.CellCenter(x, y);
            var matrix = size * Matrix4x4.CreateTranslation(center.X, center.Y, 0) * world;
            yield return new SpriteDraw(matrix, image, tilemap.Tint, entity) { Source = image is null ? new Vector4(0, 0, 1, 1) : tilemap.TileRect(tile) };
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
