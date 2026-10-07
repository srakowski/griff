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
    public static RenderFrame Build(Scene scene, float aspect) => Build(scene, aspect, Color.Transparent, []);

    /// <summary>
    /// Builds a running game's frame: its scene, with the screen fade of a scene change on top, and its
    /// <see cref="Runtime.Game.Debug"/> shapes over that.
    /// </summary>
    public static RenderFrame Build(Runtime.Game game, float aspect) =>
        Build(game.Scene, aspect, game.FadeColor with { A = game.FadeColor.A * game.FadeAmount }, game.Debug.Shapes);

    private static RenderFrame Build(Scene scene, float aspect, Color overlay, IEnumerable<Runtime.DebugShape> debug)
    {
        var camera = FindCamera(scene);
        var component = camera?.GetComponent<Camera>();
        var size = component?.Size ?? DefaultCameraSize;
        var viewProjection = ViewProjection(camera, size, aspect);
        var view = new View(ViewCenter(camera), new Vector2(size * aspect, size));
        var sprites = DrawOrder(scene).SelectMany(e => Draws(e, view)).ToList();

        // A quad over the whole view: twice the unit quad in clip space, taken back into the world.
        if (overlay.A > 0 && Matrix4x4.Invert(viewProjection, out var clipToWorld))
            sprites.Add(new SpriteDraw(Matrix4x4.CreateScale(2, 2, 1) * clipToWorld, null, overlay, ScreenEntity));

        sprites.AddRange(DebugDraws(debug, size));

        return new RenderFrame
        {
            Background = component?.Background ?? DefaultBackground,
            ViewProjection = viewProjection,
            Sprites = sprites,
            Camera = camera,
        };
    }

    // The part of the world the camera sees (ignoring its rotation): for parallax layers and repeating them across the view.
    private readonly record struct View(Vector2 Center, Vector2 Half);

    private static Vector2 ViewCenter(Entity? camera)
    {
        if (camera is null)
            return Vector2.Zero;
        var position = camera.Transform.WorldPosition;
        return new Vector2(position.X, position.Y) + (camera.GetComponent<Camera>()?.ShakeOffset ?? Vector2.Zero);
    }

    /// <summary>Quads for debug shapes. Lines are about two pixels thick on a 720-pixel-high view, whatever the zoom.</summary>
    public static IEnumerable<SpriteDraw> DebugDraws(IEnumerable<Runtime.DebugShape> shapes, float cameraSize)
    {
        var thickness = cameraSize / 180;
        foreach (var shape in shapes)
        {
            switch (shape)
            {
                case Runtime.DebugLine line:
                    yield return LineDraw(line.From, line.To, thickness, line.Color);
                    break;
                case Runtime.DebugCircle circle:
                    const int segments = 32;
                    for (var i = 0; i < segments; i++)
                    {
                        var a = MathF.Tau * i / segments;
                        var b = MathF.Tau * (i + 1) / segments;
                        yield return LineDraw(circle.Center + circle.Radius * new Vector2(MathF.Cos(a), MathF.Sin(a)),
                            circle.Center + circle.Radius * new Vector2(MathF.Cos(b), MathF.Sin(b)), thickness, circle.Color);
                    }

                    break;
                case Runtime.DebugText text:
                    yield return new SpriteDraw(Matrix4x4.CreateScale(text.Size, text.Size, 1) *
                                                Matrix4x4.CreateTranslation(text.Position.X, text.Position.Y, 0), null, text.Color, ScreenEntity, text.Text);
                    break;
            }
        }
    }

    // A thin quad from one point to another, overlapping by half the thickness at each end so joined lines meet cleanly.
    private static SpriteDraw LineDraw(Vector2 from, Vector2 to, float thickness, Color color)
    {
        var delta = to - from;
        var center = (from + to) / 2;
        var world = Matrix4x4.CreateScale(delta.Length() + thickness, thickness, 1) *
                    Matrix4x4.CreateRotationZ(MathF.Atan2(delta.Y, delta.X)) *
                    Matrix4x4.CreateTranslation(center.X, center.Y, 0);
        return new SpriteDraw(world, null, color, ScreenEntity);
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

    /// <summary>
    /// Drawable entities back to front: Order, then Z (higher is closer), then hierarchy order. With the camera's
    /// <see cref="Camera.SortByY"/>, things lower down draw in front of things above them, before hierarchy order.
    /// </summary>
    public static IEnumerable<Entity> DrawOrder(Scene scene)
    {
        var sortByY = FindCamera(scene)?.GetComponent<Camera>()?.SortByY == true;
        return scene.Entities
            .Where(e => OrderOf(e) is not null && IsActive(e))
            .Select((entity, index) => (entity, index))
            .OrderBy(x => OrderOf(x.entity))
            .ThenBy(x => x.entity.Transform.WorldPosition.Z)
            .ThenByDescending(x => sortByY ? x.entity.Transform.WorldPosition.Y : 0)
            .ThenBy(x => x.index)
            .Select(x => x.entity);
    }

    /// <summary>The draw order of an entity (its sprite's, tilemap's, text's or particles', in that preference), or null if it draws nothing.</summary>
    public static int? OrderOf(Entity entity) =>
        entity.GetComponent<Sprite>()?.Order ?? entity.GetComponent<Tilemap>()?.Order ?? entity.GetComponent<Text>()?.Order ??
        entity.GetComponent<ParticleEmitter>()?.Order;

    // A parallax layer (the entity or an ancestor) shifts everything it draws, and may repeat its sprite across the view.
    private static IEnumerable<SpriteDraw> Draws(Entity entity, View view)
    {
        var parallax = ParallaxOf(entity);
        if (parallax is null)
            return Draws(entity);

        var shift = parallax.OffsetFor(view.Center);
        var draws = Draws(entity).Select(d => d with { World = d.World * Matrix4x4.CreateTranslation(shift.X, shift.Y, 0) });
        if (parallax.Entity != entity || parallax is { RepeatX: false, RepeatY: false } || entity.GetComponent<Sprite>() is null)
            return draws;

        // Only the sprite repeats; the entity's text and particles are drawn once.
        var sprite = entity.GetComponent<Tilemap>() is { } tilemap ? tilemap.Cells().Count() : 0;
        return draws.SelectMany((d, i) => i == sprite ? Repeated(d, parallax, view) : [d]);
    }

    private static Parallax? ParallaxOf(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Parallax>() is { } parallax)
                return parallax;
        }

        return null;
    }

    // Copies of a sprite quad, one quad apart, from one edge of the view to the other along the repeated axes.
    private static IEnumerable<SpriteDraw> Repeated(SpriteDraw draw, Parallax parallax, View view)
    {
        var center = new Vector2(draw.World.M41, draw.World.M42);
        var step = new Vector2(Math.Abs(draw.World.M11) + Math.Abs(draw.World.M21), Math.Abs(draw.World.M12) + Math.Abs(draw.World.M22));
        var (fromX, toX) = parallax.RepeatX ? Span(center.X, step.X, view.Center.X, view.Half.X) : (0, 0);
        var (fromY, toY) = parallax.RepeatY ? Span(center.Y, step.Y, view.Center.Y, view.Half.Y) : (0, 0);
        for (var y = fromY; y <= toY; y++)
        {
            for (var x = fromX; x <= toX; x++)
                yield return draw with { World = draw.World * Matrix4x4.CreateTranslation(x * step.X, y * step.Y, 0) };
        }

        // Which copies, counted from the original, overlap the view along one axis. A tiny sprite is capped, not repeated forever.
        static (int From, int To) Span(float center, float step, float viewCenter, float viewHalf)
        {
            if (!(step > 1e-3f))
                return (0, 0);
            const int cap = 256;
            var from = (int)MathF.Floor((viewCenter - viewHalf - center) / step + 0.5f);
            var to = (int)MathF.Ceiling((viewCenter + viewHalf - center) / step - 0.5f);
            return (Math.Max(from, -cap), Math.Min(to, cap));
        }
    }

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
