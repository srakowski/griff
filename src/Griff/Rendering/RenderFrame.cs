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

    /// <summary>A color painted over the quad (keeping its shape), with alpha saying how much; see <see cref="Sprite.Flash"/>.</summary>
    public Color Flash { get; init; } = Color.Transparent;

    /// <summary>
    /// When more than 0, the quad is drawn in <see cref="Tint"/> everywhere except a round hole in its middle this big (as
    /// a fraction of the quad's half-width), with no image: the iris of a scene change.
    /// </summary>
    public float HoleRadius { get; init; }
}

/// <summary>A light to draw: its middle in the world, how far it reaches, its color times its brightness, and its softness.</summary>
public readonly record struct LightDraw(Vector2 Center, float Radius, Color Color, float Softness)
{
    /// <summary>
    /// For a light with <see cref="Scenes.Light.Shadows"/>: the sides that cast its shadows (see
    /// <see cref="Rendering.Shadows.CastersFor"/>). Empty for a light without, or with nothing in its way.
    /// </summary>
    public IReadOnlyList<ShadowEdge> Casters { get; init; } = [];

    /// <summary>How much light it puts on <paramref name="point"/>, per channel.</summary>
    public Vector3 At(Vector2 point)
    {
        if (Casters.Count > 0 && !Shadows.Reaches(Casters, Center, point))
            return Vector3.Zero;
        var amount = Scenes.Light.Falloff(Vector2.Distance(point, Center), Radius, Softness);
        return new Vector3(Color.R, Color.G, Color.B) * amount;
    }
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

    /// <summary>
    /// The light everything gets without a light (see <see cref="Scenes.Camera.Ambient"/>). White means the scene isn't
    /// dark and <see cref="Lights"/> are ignored.
    /// </summary>
    public Color Ambient { get; init; } = Color.White;

    /// <summary>The lights in view, when the scene is dark.</summary>
    public IReadOnlyList<LightDraw> Lights { get; init; } = [];

    /// <summary>
    /// How many of the first <see cref="Sprites"/> the darkness and lights apply to; those after (the HUD, menus, fades,
    /// debug shapes) are drawn over it as they are.
    /// </summary>
    public int LitCount { get; init; }

    /// <summary>The camera's <see cref="ScreenEffects"/>, applied to the whole finished screen; null if it has none in use.</summary>
    public ScreenEffects? Effects { get; init; }

    /// <summary>True if the scene is dark, so the renderer darkens the first <see cref="LitCount"/> sprites and adds lights.</summary>
    public bool IsDark => Ambient.R < 1 || Ambient.G < 1 || Ambient.B < 1;

    /// <summary>The light reaching <paramref name="point"/> in the world: the ambient light plus every light, each channel at most 1.</summary>
    public Color LightAt(Vector2 point)
    {
        if (!IsDark)
            return Color.White;
        var sum = new Vector3(Ambient.R, Ambient.G, Ambient.B);
        foreach (var light in Lights)
            sum += light.At(point);
        sum = Vector3.Clamp(sum, Vector3.Zero, Vector3.One);
        return new Color(sum.X, sum.Y, sum.Z);
    }
}

/// <summary>Builds <see cref="RenderFrame"/>s from scenes. Shared by the editor and the game so they always agree.</summary>
public static partial class SceneRenderer
{
    public const float DefaultCameraSize = 5f;

    public static readonly Color DefaultBackground = Color.FromBytes(0x1E, 0x22, 0x2B);

    // Stands in for the entity of draws that belong to the screen rather than the scene, like the scene-change fade.
    private static readonly Entity ScreenEntity = new("Screen");

    /// <param name="aspect">Viewport width divided by height.</param>
    public static RenderFrame Build(Scene scene, float aspect) => Build(scene, aspect, null);

    /// <summary>
    /// Builds a running game's frame: its scene, with any <see cref="Runtime.Game.FlashScreen"/> and then the screen fade
    /// of a scene change on top (drawn the way the camera's <see cref="Camera.Transition"/> says), and its
    /// <see cref="Runtime.Game.Debug"/> shapes over that.
    /// </summary>
    public static RenderFrame Build(Runtime.Game game, float aspect) => Build(game.Scene, aspect, game);

    private static RenderFrame Build(Scene scene, float aspect, Runtime.Game? game)
    {
        var camera = FindCamera(scene);
        var component = camera?.GetComponent<Camera>();
        var size = component?.Size ?? DefaultCameraSize;
        var viewProjection = ViewProjection(camera, size, aspect);
        var view = new View(ViewCenter(camera), new Vector2(size * aspect, size));

        // In first person the world is seen through the viewer's eyes, lit by itself, and the camera's view (unturned and
        // unshaken) only places the HUD.
        var firstPerson = game?.FirstPersonView is { } fp && game.ViewerOf(fp) is { } viewer ? (View: fp, Viewer: viewer) : default;
        List<SpriteDraw> sprites;
        if (firstPerson.View is not null)
        {
            var center = camera is null ? Vector2.Zero : new Vector2(camera.Transform.WorldPosition.X, camera.Transform.WorldPosition.Y);
            view = new View(center, view.Half);
            viewProjection = Matrix4x4.CreateTranslation(-center.X, -center.Y, 0) * Matrix4x4.CreateOrthographic(size * 2 * aspect, size * 2, -1000f, 1000f);
            sprites = FirstPersonDraws(game!, firstPerson.View, firstPerson.Viewer, camera, view, aspect);
            sprites.AddRange(DrawOrder(scene).Where(e => IsOnScreen(e, camera)).SelectMany(e => Draws(e, view)));
        }
        else
        {
            sprites = DrawOrder(scene).SelectMany(e => Draws(e, view)).ToList();
        }

        var strategy = game is not null && game.Rts.IsActive;
        if (strategy)
            sprites = WithStrategy(game!, sprites, camera, view.Center, view.Half, size);
        sprites = WithSandbox(scene, game, sprites, camera, view, size);

        // In the dark, the world is drawn first and darkened; what's pinned to the screen goes over it untouched.
        var ambient = component?.CurrentAmbient ?? Color.White;
        var dark = component?.IsDark == true && firstPerson.View is null;
        var litCount = sprites.Count;
        if (dark)
        {
            var screen = sprites.Where(d => IsOnScreen(d.Entity, camera)).ToList();
            sprites = sprites.Where(d => !IsOnScreen(d.Entity, camera)).Concat(screen).ToList();
            litCount = sprites.Count - screen.Count;
        }

        if (game is not null && Matrix4x4.Invert(viewProjection, out var clipToWorld))
        {
            // Quads over the whole view: twice the unit quad in clip space, taken back into the world.
            var whole = Matrix4x4.CreateScale(2, 2, 1) * clipToWorld;
            if (game.ScreenFlash.A > 0)
                sprites.Add(new SpriteDraw(whole, null, game.ScreenFlash, ScreenEntity));
            if (TransitionDraw(game, component?.Transition ?? SceneTransition.Fade, view, clipToWorld) is { } transition)
                sprites.Add(transition);
        }

        sprites.AddRange(DebugDraws(game?.Debug.Shapes ?? [], size));

        return new RenderFrame
        {
            Background = component?.Background ?? DefaultBackground,
            ViewProjection = viewProjection,
            Sprites = sprites,
            Camera = camera,
            Ambient = dark ? ambient : Color.White,
            Lights = dark ? LightsIn(scene, view) : [],
            LitCount = dark ? litCount : sprites.Count,
            Effects = camera?.GetComponent<ScreenEffects>() is { IsActive: true } effects ? effects : null,
        };
    }

    /// <summary>The lights in a scene that reach into the camera's view, with their flicker as it is now.</summary>
    public static IReadOnlyList<LightDraw> Lights(Scene scene, float aspect)
    {
        var camera = FindCamera(scene);
        var size = camera?.GetComponent<Camera>()?.Size ?? DefaultCameraSize;
        return LightsIn(scene, new View(ViewCenter(camera), new Vector2(size * aspect, size)));
    }

    private static List<LightDraw> LightsIn(Scene scene, View view)
    {
        var lights = new List<LightDraw>();
        foreach (var draw in LightDraws(scene))
        {
            var reach = view.Half + new Vector2(draw.Radius);
            if (MathF.Abs(draw.Center.X - view.Center.X) <= reach.X && MathF.Abs(draw.Center.Y - view.Center.Y) <= reach.Y)
                lights.Add(draw);
        }

        return lights;
    }

    /// <summary>
    /// Every light in the scene that gives off any light, as it is now (flicker included), with the shadows of those
    /// that cast them.
    /// </summary>
    public static IEnumerable<LightDraw> LightDraws(Scene scene)
    {
        List<ShadowEdge>? edges = null;
        foreach (var light in scene.Entities.Where(IsActive).SelectMany(e => e.GetComponents<Light>()))
        {
            var intensity = light.CurrentIntensity;
            if (!(light.Radius > 0) || !(intensity > 0))
                continue;
            var draw = new LightDraw(light.WorldCenter, light.Radius,
                new Color(light.Color.R * intensity, light.Color.G * intensity, light.Color.B * intensity), light.Softness);
            if (light.Shadows)
                draw = draw with { Casters = Shadows.CastersFor(edges ??= Shadows.Edges(scene), light) };
            yield return draw;
        }

        foreach (var glow in GlowDraws(scene))
            yield return glow;
    }

    // Pinned to the screen: a ScreenAnchor on the entity or any ancestor (HUDs, menus, dialogue boxes), or riding along
    // inside the camera (a score that's the camera's child).
    private static bool IsOnScreen(Entity entity, Entity? camera)
    {
        if (entity == ScreenEntity)
            return true;
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<ScreenAnchor>() is not null || (e != entity && e == camera))
                return true;
        }

        return false;
    }

    // The scene change's cover, drawn the transition's way: the whole view fading, a wipe across it, or an iris closing.
    private static SpriteDraw? TransitionDraw(Runtime.Game game, SceneTransition transition, View view, Matrix4x4 clipToWorld)
    {
        var amount = Math.Clamp(game.FadeAmount, 0, 1);
        if (amount <= 0)
            return null;
        var color = game.FadeColor;
        var whole = Matrix4x4.CreateScale(2, 2, 1) * clipToWorld;
        if (amount >= 1 || transition == SceneTransition.Fade)
            return new SpriteDraw(whole, null, color with { A = color.A * amount }, ScreenEntity);

        if (transition == SceneTransition.Wipe)
        {
            // Covering, it grows from the left edge; uncovering, its left edge sweeps on off to the right.
            var (from, to) = game.IsUncovering ? (1 - amount, 1f) : (0f, amount);
            var width = (to - from) * 2;
            var middle = -1 + (from + to);
            return new SpriteDraw(Matrix4x4.CreateScale(width, 2, 1) * Matrix4x4.CreateTranslation(middle, 0, 0) * clipToWorld, null, color, ScreenEntity);
        }

        // Iris: a hole around the player, shrinking to nothing. The quad reaches past every corner of the view from there.
        var center = IrisCenter(game.Scene) ?? view.Center;
        var corner = Vector2.Abs(center - view.Center) + view.Half;
        var full = corner.Length();
        var radius = full * (1 - amount);
        var half = full * 2 + 1;
        var quad = Matrix4x4.CreateScale(half * 2, half * 2, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0);
        return new SpriteDraw(quad, null, color, ScreenEntity) { HoleRadius = Math.Max(radius / half, 1e-4f) };
    }

    // What an iris closes in on: the entity called Player, or else the first with no-code movement.
    private static Vector2? IrisCenter(Scene scene)
    {
        var player = scene.Entities.FirstOrDefault(e => e.Name == "Player" && IsActive(e))
                     ?? scene.Entities.FirstOrDefault(e => IsActive(e) && (e.GetComponent<PlatformerMovement>() is not null || e.GetComponent<TopDownMovement>() is not null || e.GetComponent<GridMovement>() is not null));
        if (player is null)
            return null;
        var position = player.Transform.WorldPosition;
        return new Vector2(position.X, position.Y);
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
    /// <see cref="Camera.SortByY"/>, things lower down draw in front of things above them, before hierarchy order. A
    /// <see cref="Draggable"/> being held (and what's inside it) draws over everything else.
    /// </summary>
    public static IEnumerable<Entity> DrawOrder(Scene scene)
    {
        var sortByY = FindCamera(scene)?.GetComponent<Camera>()?.SortByY == true;
        return scene.Entities
            .Where(e => OrderOf(e) is not null && IsActive(e))
            .Select((entity, index) => (entity, index))
            .OrderBy(x => IsLifted(x.entity))
            .ThenBy(x => OrderOf(x.entity))
            .ThenBy(x => x.entity.Transform.WorldPosition.Z)
            .ThenByDescending(x => sortByY ? x.entity.Transform.WorldPosition.Y : 0)
            .ThenBy(x => x.index)
            .Select(x => x.entity);
    }

    // Held by the mouse, or inside something that is.
    private static bool IsLifted(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Draggable>() is { IsHeld: true })
                return true;
        }

        return false;
    }

    /// <summary>
    /// The draw order of an entity (its sprite's, tilemap's, bar's, slider's, text's, particles' or weather's, in that preference), or
    /// null if it draws nothing.
    /// </summary>
    public static int? OrderOf(Entity entity) =>
        entity.GetComponent<Sprite>()?.Order ?? entity.GetComponent<Tilemap>()?.Order ?? entity.GetComponent<Bar>()?.Order ??
        entity.GetComponent<Slider>()?.Order ?? entity.GetComponent<Text>()?.Order ?? entity.GetComponent<ParticleEmitter>()?.Order ??
        entity.GetComponent<Weather>()?.Order;

    // An entity's draws, and its weather over the view on top.
    private static IEnumerable<SpriteDraw> Draws(Entity entity, View view)
    {
        var draws = LayerDraws(entity, view);
        return entity.GetComponent<Weather>() is { Drops.Count: > 0 } weather ? draws.Concat(WeatherDraws(entity, weather, view)) : draws;
    }

    /// <summary>
    /// One quad per raindrop, snowflake, leaf or ember, wrapped into the view so it's always full however far the camera
    /// goes: rain as streaks along the way it falls, leaves turning, embers flickering.
    /// </summary>
    private static IEnumerable<SpriteDraw> WeatherDraws(Entity entity, Weather weather, View view)
    {
        var z = entity.Transform.WorldPosition.Z;
        var area = weather.Area;
        var corner = view.Center - area / 2;
        var slant = MathF.Atan2(weather.Wind, 18 * Math.Max(weather.Speed, 0.05f));
        foreach (var drop in weather.Drops)
        {
            var position = new Vector2(corner.X + Wrap(drop.Position.X - corner.X, area.X), corner.Y + Wrap(drop.Position.Y - corner.Y, area.Y));
            var color = new Color(drop.Color.R * weather.Tint.R, drop.Color.G * weather.Tint.G, drop.Color.B * weather.Tint.B, drop.Color.A * weather.Tint.A);
            var shape = weather.Kind switch
            {
                WeatherKind.Rain => Matrix4x4.CreateScale(0.035f, drop.Size, 1) * Matrix4x4.CreateRotationZ(slant),
                // A leaf flutters: it narrows and widens as it turns over.
                WeatherKind.Leaves => Matrix4x4.CreateScale(drop.Size * (0.35f + 0.65f * MathF.Abs(MathF.Cos(drop.Phase * 2))), drop.Size * 0.55f, 1) *
                                      Matrix4x4.CreateRotationZ(drop.Angle),
                _ => Matrix4x4.CreateScale(drop.Size, drop.Size, 1),
            };
            if (weather.Kind == WeatherKind.Embers)
                color = color with { A = color.A * (0.55f + 0.45f * MathF.Sin(drop.Phase)) };
            yield return new SpriteDraw(shape * Matrix4x4.CreateTranslation(position.X, position.Y, z), null, color, entity);
        }

        static float Wrap(float value, float length) => length > 0 ? value - MathF.Floor(value / length) * length : value;
    }

    // A parallax layer (the entity or an ancestor) shifts everything it draws, and may repeat its sprite across the view.
    private static IEnumerable<SpriteDraw> LayerDraws(Entity entity, View view)
    {
        var parallax = ParallaxOf(entity);
        if (parallax is null)
            return Shaken(entity, Draws(entity, (View?)view));

        var shift = parallax.OffsetFor(view.Center);
        var draws = Draws(entity).Select(d => d with { World = d.World * Matrix4x4.CreateTranslation(shift.X, shift.Y, 0) });
        if (parallax.Entity != entity || parallax is { RepeatX: false, RepeatY: false } || entity.GetComponent<Sprite>() is null or { IsBlinkedOut: true })
            return draws;

        // Only the sprite repeats (all nine pieces of a sliced one); the entity's text and particles are drawn once.
        var sprite = (entity.GetComponent<Tilemap>() is { } tilemap ? tilemap.Cells().Count() : 0) + TrailDraws(entity).Count();
        var pieces = SpritePieces(entity, entity.GetComponent<Sprite>()!).Count();
        if (pieces == 1)
            return draws.SelectMany((d, i) => i == sprite ? Repeated(d, parallax, view) : [d]);

        // The pieces repeat together, a whole sprite apart.
        var list = draws.ToList();
        var whole = list[sprite] with { World = QuadMatrix(entity, entity.GetComponent<Sprite>()!) * Matrix4x4.CreateTranslation(shift.X, shift.Y, 0) };
        var offsets = Repeated(whole, parallax, view).Select(d => new Vector2(d.World.M41 - whole.World.M41, d.World.M42 - whole.World.M42)).ToList();
        return list.SelectMany((d, i) => i >= sprite && i < sprite + pieces
            ? offsets.Select(o => d with { World = d.World * Matrix4x4.CreateTranslation(o.X, o.Y, 0) })
            : [d]);
    }

    // The cells of a map inside a view (and a cell around it), row by row.
    private static IEnumerable<(int X, int Y, int Tile)> CellsIn(Tilemap tilemap, Matrix4x4 world, View view)
    {
        if (!Matrix4x4.Invert(world, out var toLocal) || !(tilemap.TileSize > 0))
        {
            foreach (var cell in tilemap.Cells())
                yield return cell;
            yield break;
        }

        var (min, max) = (new Vector2(float.PositiveInfinity), new Vector2(float.NegativeInfinity));
        foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
        {
            var local = Vector2.Transform(view.Center + corner * view.Half, toLocal);
            (min, max) = (Vector2.Min(min, local), Vector2.Max(max, local));
        }

        var size = tilemap.TileSize;
        var (fromX, toX) = (Math.Max(0, (int)MathF.Floor(min.X / size) - 1), Math.Min(tilemap.Width - 1, (int)MathF.Floor(max.X / size) + 1));
        var (fromY, toY) = (Math.Max(0, (int)MathF.Floor(-max.Y / size) - 1), Math.Min(tilemap.Height - 1, (int)MathF.Floor(-min.Y / size) + 1));
        for (var y = fromY; y <= toY; y++)
        {
            for (var x = fromX; x <= toX; x++)
            {
                if (tilemap.GetTile(x, y) is var tile and >= 0)
                    yield return (x, y, tile);
            }
        }
    }

    // A crumbling platform about to go is drawn shaken off its place (with whatever is inside it); its collider stays put.
    // Something bopping to the beat is drawn bigger about its middle, its children with it.
    private static IEnumerable<SpriteDraw> Shaken(Entity entity, IEnumerable<SpriteDraw> draws)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<BeatPulse>() is { Scale: var scale } && scale != 1)
            {
                var center = e.Transform.WorldPosition;
                var about = Matrix4x4.CreateScale(scale, scale, 1, new Vector3(center.X, center.Y, 0));
                draws = draws.Select(d => d with { World = d.World * about });
            }
        }

        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Crumble>() is { ShakeOffset: var shake } && shake != Vector2.Zero)
                return draws.Select(d => d with { World = d.World * Matrix4x4.CreateTranslation(shake.X, shake.Y, 0) });
        }

        return draws;
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

    // An entity with several draws its tilemap, then its trail and sprite, its bar or slider, its text, and its particles on top.
    // A button shades its own sprite and text to show it's under the mouse or held.
    // With a view, a tilemap draws only the tiles in it: a big world doesn't draw all of itself every frame.
    private static IEnumerable<SpriteDraw> Draws(Entity entity, View? cull = null)
    {
        var button = entity.GetComponent<Button>();

        // A match-3 board draws its own tiles, moving.
        if (entity.GetComponent<Tilemap>() is { } tilemap)
        {
            var draws = entity.GetComponent<MatchBoard>() is { } board ? BoardDraws(entity, tilemap, board) : TileDraws(entity, tilemap, cull);
            foreach (var draw in draws)
                yield return draw;
        }

        foreach (var draw in TrailDraws(entity))
            yield return draw;

        // Blinked out, it draws nothing this frame.
        if (entity.GetComponent<Sprite>() is { IsBlinkedOut: false } sprite)
        {
            var tint = button?.Shade(sprite.Tint) ?? sprite.Tint;
            // A crate resting on a goal turns the goal's color, as faded as it was.
            if (entity.GetComponent<Pushable>() is { GoalTint: { } onGoal })
                tint = onGoal with { A = onGoal.A * tint.A };
            foreach (var (world, source) in SpritePieces(entity, sprite))
                yield return new SpriteDraw(world, sprite.Image, tint, entity) { Source = source, Flash = sprite.FlashOverlay };
        }

        if (entity.GetComponent<Bar>() is { } bar)
        {
            foreach (var draw in BarDraws(entity, bar.Size, bar.Direction, bar.Fraction, bar.TrailFraction, bar.Background, bar.Fill, bar.Trail))
                yield return draw;
        }

        if (entity.GetComponent<Slider>() is { } slider)
        {
            var fraction = slider.Fraction;
            foreach (var draw in BarDraws(entity, slider.Size, BarDirection.Right, fraction, fraction, slider.Background, slider.Shade(slider.Fill), Color.Transparent))
                yield return draw;

            // The handle: a little taller than the bar, at the end of the filled part.
            var size = Vector2.Abs(slider.Size);
            if (slider.Knob.A > 0 && size.Y > 0)
            {
                var knob = new Vector2(size.Y * 0.5f, size.Y * 1.5f);
                yield return Rect(entity, new Vector2(-size.X / 2 + fraction * size.X, 0), knob, slider.Shade(slider.Knob));
            }
        }

        var value = entity.GetComponent<Text>() is { } shown && entity.GetComponent<Typewriter>() is { } typewriter
            ? typewriter.Visible(shown.Shown)
            : entity.GetComponent<Text>()?.Shown;
        if (entity.GetComponent<Text>() is { } text && !string.IsNullOrEmpty(value))
        {
            var world = Matrix4x4.CreateScale(text.Size, text.Size, 1) * entity.Transform.WorldMatrix;
            yield return new SpriteDraw(world, null, button?.Shade(text.Color) ?? text.Color, entity, value)
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

    /// <summary>The copies a <see cref="Trail"/> has left, fading; drawn just behind the sprite that left them.</summary>
    public static IEnumerable<SpriteDraw> TrailDraws(Entity entity)
    {
        if (entity.GetComponent<Trail>() is not { Copies.Count: > 0 } trail)
            yield break;
        foreach (var copy in trail.Copies)
        {
            var opacity = trail.OpacityOf(copy);
            if (opacity > 0)
                yield return new SpriteDraw(copy.World, copy.Image, copy.Tint with { A = copy.Tint.A * opacity }, entity) { Source = copy.Source, Flash = trail.Color };
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
    public static IEnumerable<SpriteDraw> TileDraws(Entity entity, Tilemap tilemap) => TileDraws(entity, tilemap, null);

    private static IEnumerable<SpriteDraw> TileDraws(Entity entity, Tilemap tilemap, View? cull)
    {
        var world = entity.Transform.WorldMatrix;
        // A hair bigger than a cell, so neighbors overlap: edges computed tile by tile can round apart and let a hairline of
        // the background show between them as the camera moves. The image is stretched, not extended, so nothing bleeds in.
        const float seamless = 1.004f;
        var size = Matrix4x4.CreateScale(tilemap.TileSize * seamless, tilemap.TileSize * seamless, 1);
        foreach (var (x, y, tile) in cull is { } view ? CellsIn(tilemap, world, view) : tilemap.Cells())
        {
            var center = tilemap.CellCenter(x, y);
            var matrix = size * Matrix4x4.CreateTranslation(center.X, center.Y, 0) * world;
            var shown = tilemap.ShownTile(x, y, tile);
            var tileset = tilemap.TilesetOf(shown);
            yield return new SpriteDraw(matrix, tileset.Image, tilemap.Tint, entity) { Source = tileset.Image is null ? new Vector4(0, 0, 1, 1) : tileset.Rect(shown) };
        }
    }

    /// <summary>
    /// Quads for a bar of <paramref name="size"/> centered on the entity: the background, then the trail up to
    /// <paramref name="trail"/> and the fill up to <paramref name="fraction"/> (both 0 to 1), filling towards
    /// <paramref name="direction"/>. Fully transparent parts are left out.
    /// </summary>
    public static IEnumerable<SpriteDraw> BarDraws(Entity entity, Vector2 size, BarDirection direction, float fraction, float trail,
        Color background, Color fill, Color trailColor)
    {
        size = Vector2.Abs(size);
        if (size.X <= 0 || size.Y <= 0)
            yield break;

        if (background.A > 0)
            yield return Rect(entity, Vector2.Zero, size, background);
        if (trailColor.A > 0 && trail > fraction)
            yield return Part(fraction, trail, trailColor);
        if (fill.A > 0 && fraction > 0)
            yield return Part(0, fraction, fill);

        // The stretch of the bar from `from` to `to` (0 to 1) along the way it fills.
        SpriteDraw Part(float from, float to, Color color)
        {
            var (start, end) = (Math.Clamp(from, 0, 1), Math.Clamp(to, 0, 1));
            var horizontal = direction is BarDirection.Right or BarDirection.Left;
            var length = horizontal ? size.X : size.Y;
            var sign = direction is BarDirection.Right or BarDirection.Up ? 1 : -1;
            var middle = sign * (-length / 2 + (start + end) / 2 * length);
            var span = (end - start) * length;
            return horizontal
                ? Rect(entity, new Vector2(middle, 0), new Vector2(span, size.Y), color)
                : Rect(entity, new Vector2(0, middle), new Vector2(size.X, span), color);
        }
    }

    // A solid rectangle in the entity's space.
    private static SpriteDraw Rect(Entity entity, Vector2 center, Vector2 size, Color color) =>
        new(Matrix4x4.CreateScale(size.X, size.Y, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0) * entity.Transform.WorldMatrix,
            null, color, entity);

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

    private static bool IsSliced(Sprite sprite) => sprite.Slice > 0 && sprite.SliceSize > 0;

    /// <summary>
    /// The quads a sprite is drawn with and the part of its image on each: one, or nine for a <see cref="Sprite.Slice"/>d
    /// sprite (corners, edges, middle; left to right, top to bottom).
    /// </summary>
    public static IEnumerable<(Matrix4x4 World, Vector4 Source)> SpritePieces(Entity entity, Sprite sprite)
    {
        var frame = sprite.FrameRect;
        if (!IsSliced(sprite))
        {
            yield return (QuadMatrix(entity, sprite), frame);
            yield break;
        }

        // Edges of the three columns and rows, in the sprite's own space (-size/2..size/2, Y up) and in the frame (V down).
        var size = Vector2.Abs(sprite.Size);
        var border = Vector2.Min(new Vector2(sprite.SliceSize), size / 2);
        var slice = Math.Min(sprite.Slice, 0.5f);
        float[] xs = [-size.X / 2, -size.X / 2 + border.X, size.X / 2 - border.X, size.X / 2];
        float[] ys = [size.Y / 2, size.Y / 2 - border.Y, -size.Y / 2 + border.Y, -size.Y / 2];
        float[] us = [frame.X, frame.X + frame.Z * slice, frame.X + frame.Z * (1 - slice), frame.X + frame.Z];
        float[] vs = [frame.Y, frame.Y + frame.W * slice, frame.Y + frame.W * (1 - slice), frame.Y + frame.W];
        var outside = Matrix4x4.CreateScale(MathF.Sign(sprite.Size.X) * (sprite.FlipX ? -1 : 1), MathF.Sign(sprite.Size.Y) * (sprite.FlipY ? -1 : 1), 1) *
                      entity.Transform.WorldMatrix;
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var width = xs[column + 1] - xs[column];
                var height = ys[row] - ys[row + 1];
                if (width <= 0 || height <= 0)
                    continue;
                var center = new Vector2(xs[column] + xs[column + 1], ys[row] + ys[row + 1]) / 2;
                var piece = Matrix4x4.CreateScale(width, height, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0);
                yield return (piece * outside, new Vector4(us[column], vs[row], us[column + 1] - us[column], vs[row + 1] - vs[row]));
            }
        }
    }

    private static Matrix4x4 QuadMatrix(Entity entity, Sprite sprite) =>
        Matrix4x4.CreateScale(sprite.Size.X * (sprite.FlipX ? -1 : 1), sprite.Size.Y * (sprite.FlipY ? -1 : 1), 1) *
        entity.Transform.WorldMatrix;
}
