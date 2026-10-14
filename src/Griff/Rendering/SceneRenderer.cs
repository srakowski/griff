using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Rendering;

/// <summary>
/// Builds <see cref="RenderFrame"/>s from scenes. Shared by the editor and the game so they always agree. It knows how to
/// see a scene (cameras, parallax, light and dark, what's pinned to the screen) but nothing of what's in it: every
/// component that draws is an <see cref="IDrawable"/>, changed by its entity's <see cref="IDrawEffect"/>s, and a running
/// game's systems add what the game itself draws (see <see cref="Runtime.GameSystem"/>'s Draw).
/// </summary>
public static class SceneRenderer
{
    public const float DefaultCameraSize = 5f;

    public static readonly Color DefaultBackground = Color.FromBytes(0x1E, 0x22, 0x2B);

    // Stands in for the entity of draws that belong to the screen rather than the scene, like the scene-change fade.
    internal static readonly Entity ScreenEntity = new("Screen");

    /// <param name="aspect">Viewport width divided by height.</param>
    public static RenderFrame Build(Scene scene, float aspect) => Build(scene, aspect, null);

    /// <summary>
    /// Builds a running game's frame: its scene, with what its systems draw, any <see cref="Runtime.Game.FlashScreen"/>
    /// and then the screen fade of a scene change on top (drawn the way the camera's <see cref="Camera.Transition"/>
    /// says), and its <see cref="Runtime.Game.Debug"/> shapes over that.
    /// </summary>
    public static RenderFrame Build(Runtime.Game game, float aspect) => Build(game.Scene, aspect, game);

    /// <summary>
    /// Builds a running game's frame as <see cref="Build(Runtime.Game, float)"/> does, reusing what
    /// <paramref name="recycle"/> (a frame of this game the caller is done with) holds rather than making it afresh, so a
    /// big scene's frames don't keep the garbage collector busy. <paramref name="recycle"/> can't be used after.
    /// </summary>
    public static RenderFrame Build(Runtime.Game game, float aspect, RenderFrame? recycle) =>
        Build(game.Scene, aspect, game, recycle?.Sprites as List<SpriteDraw>);

    private static RenderFrame Build(Scene scene, float aspect, Runtime.Game? game, List<SpriteDraw>? reuse = null)
    {
        // A running game is drawn between its last two steps: everything that moved in the last one (the camera too) is
        // placed part way there, so it moves smoothly whatever the screen's frame rate.
        if (game is null || !BlendSteps(scene, game))
            return BuildNow(scene, aspect, game, reuse);
        try
        {
            return BuildNow(scene, aspect, game, reuse);
        }
        finally
        {
            scene.DrawStamp = 0;
        }
    }

    // Each frame drawn between steps has its own stamp, so a transform knows the matrix it worked out is this frame's.
    private static long s_drawStamps;

    // Turns on drawing between the game's last two steps, unless it would draw everything as it is anyway: no step yet,
    // the scene came in on the last one, or the game is drawn right at its last step.
    private static bool BlendSteps(Scene scene, Runtime.Game game)
    {
        var blend = game.Time.Blend;
        if (blend >= 1 || scene.Step <= 0 || scene.Step == scene.FirstStep)
            return false;

        // Moving further than this in one step (a quarter of the camera's view, top to bottom) is jumping there.
        var size = FindCamera(scene)?.GetComponent<Camera>()?.Size ?? DefaultCameraSize;
        scene.GlideLimit = Math.Max(size, 1) * 0.5f;
        scene.DrawBlend = Math.Max(0, blend);
        scene.DrawStamp = Interlocked.Increment(ref s_drawStamps);
        return true;
    }

    private static RenderFrame BuildNow(Scene scene, float aspect, Runtime.Game? game, List<SpriteDraw>? reuse)
    {
        var camera = FindCamera(scene);
        var component = camera?.GetComponent<Camera>();
        var size = component?.Size ?? DefaultCameraSize;
        var view = new Box(ViewCenter(camera), new Vector2(size * aspect, size));
        var sprites = reuse ?? [];
        sprites.Clear();
        var screenCount = DrawScene(scene, view, size, game is not null, sprites, null, overWorld: true);

        // The game's systems draw what the game knows of (a strategy game's fog and HUD), or see the world their own way.
        var frame = new FrameDrawing(game, scene, camera, size, aspect, view, ViewProjection(camera, size, aspect), sprites, screenCount);
        if (game is not null)
        {
            // A system that sees the world its own way (first person) draws it first; every system then draws over it.
            var systems = game.Systems.ToArray();
            foreach (var system in systems)
                system.RunDrawView(frame);
            foreach (var system in systems)
                system.RunDraw(frame);
        }

        sprites = frame.Sprites;
        view = frame.View;
        var viewProjection = frame.ViewProjection;

        // In the dark, the world is drawn first and darkened; what's pinned to the screen goes over it untouched.
        var ambient = component?.CurrentAmbient ?? Color.White;
        var dark = component?.IsDark == true && frame.Lit;
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

    // ----- Entities -----

    // One entity that draws: where its drawable components are (in step order) in a list of them, and what it sorts by.
    private readonly record struct Drawn(Entity Entity, int First, int Count, bool Lifted, int Order, float Z, float Y, int Index);

    /// <summary>
    /// Draws the scene's entities (those <paramref name="include"/> takes, or all) back to front into
    /// <paramref name="sprites"/>, culled to <paramref name="view"/>, and then, with <paramref name="overWorld"/>, what
    /// goes over the world (ropes, wires) between them and what's pinned to the screen. Returns how many draws at the
    /// end are pinned to the screen when it had to split them for that, or -1.
    /// </summary>
    internal static int DrawScene(Scene scene, Box view, float cameraSize, bool playing, List<SpriteDraw> sprites, Func<Entity, bool>? include, bool overWorld)
    {
        var scratch = t_scratch ?? new Scratch();
        t_scratch = null;
        try
        {
            return DrawScene(scene, view, cameraSize, playing, sprites, include, overWorld ? scratch.Overs : null, scratch);
        }
        finally
        {
            scratch.Drawables.Clear();
            scratch.Overs.Clear();
            scratch.Entries.Clear();
            t_scratch = scratch;
        }
    }

    // Lists reused from frame to frame (a set a thread), so a big scene's frame doesn't make large ones afresh: big arrays
    // go straight to the large object heap, which only full garbage collections clear.
    [ThreadStatic]
    private static Scratch? t_scratch;

    private sealed class Scratch
    {
        public List<IDrawable> Drawables { get; } = [];

        public List<IDrawable> Overs { get; } = [];

        public List<Drawn> Entries { get; } = [];
    }

    private static int DrawScene(Scene scene, Box view, float cameraSize, bool playing, List<SpriteDraw> sprites, Func<Entity, bool>? include,
        List<IDrawable>? overs, Scratch scratch)
    {
        var drawables = scratch.Drawables;
        var entities = Sorted(scene, drawables, overs, include, scratch.Entries);
        sprites.EnsureCapacity(sprites.Count + entities.Count + 16);
        var drawing = new Drawing(sprites, ScreenEntity, view, cameraSize / 180, playing);
        foreach (var entity in entities)
            DrawEntity(entity, drawables, drawing, sprites);

        if (overs is not { Count: > 0 })
            return -1;

        // Over the world: lower steps first, in hierarchy order within a step.
        var over = new List<SpriteDraw>();
        var overDrawing = new Drawing(over, ScreenEntity, view, cameraSize / 180, playing);
        foreach (var drawable in overs.Select((d, i) => (d, i)).OrderBy(x => x.d.DrawStep).ThenBy(x => x.i).Select(x => x.d))
        {
            overDrawing.Entity = ((Component)drawable).Entity!;
            drawable.Draw(overDrawing);
        }

        if (over.Count == 0)
            return -1;
        var camera = FindCamera(scene);
        var screen = sprites.Where(d => IsOnScreen(d.Entity, camera)).ToList();
        var world = sprites.Where(d => !IsOnScreen(d.Entity, camera)).ToList();
        sprites.Clear();
        sprites.AddRange(world);
        sprites.AddRange(over);
        sprites.AddRange(screen);
        return screen.Count;
    }

    // The scene's drawing entities back to front, their drawable components gathered into `drawables` (and those that go
    // over the world into `overs`).
    private static List<Drawn> Sorted(Scene scene, List<IDrawable> drawables, List<IDrawable>? overs, Func<Entity, bool>? include, List<Drawn> entries)
    {
        var sortByY = FindCamera(scene)?.GetComponent<Camera>()?.SortByY == true;
        foreach (var entity in scene.Entities)
        {
            if (!entity.IsActive || (include is not null && !include(entity)))
                continue;
            var first = drawables.Count;
            int? spriteOrder = null, order = null;
            var orderStep = int.MaxValue;
            foreach (var component in entity.ComponentList)
            {
                if (component is not IDrawable drawable)
                    continue;
                var step = drawable.DrawStep;
                if (step >= DrawSteps.OverWorld)
                {
                    overs?.Add(drawable);
                    continue;
                }

                // In step order, keeping the order they're attached within a step.
                var at = drawables.Count;
                while (at > first && drawables[at - 1].DrawStep > step)
                    at--;
                drawables.Insert(at, drawable);

                if (drawable is Sprite sprite)
                    spriteOrder ??= sprite.Order;
                else if (step < orderStep && drawable.DrawOrder is { } own)
                    (order, orderStep) = (own, step);
            }

            if ((spriteOrder ?? order) is not { } entityOrder)
            {
                drawables.RemoveRange(first, drawables.Count - first);
                continue;
            }

            var position = entity.Transform.WorldPosition;
            entries.Add(new Drawn(entity, first, drawables.Count - first, IsLifted(entity), entityOrder, position.Z,
                sortByY ? position.Y : 0, entries.Count));
        }

        entries.Sort(static (a, b) =>
        {
            var c = a.Lifted.CompareTo(b.Lifted);
            if (c == 0)
                c = a.Order.CompareTo(b.Order);
            if (c == 0)
                c = a.Z.CompareTo(b.Z);
            if (c == 0)
                c = b.Y.CompareTo(a.Y);
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        });
        return entries;
    }

    /// <summary>
    /// Drawable entities back to front: Order, then Z (higher is closer), then hierarchy order. With the camera's
    /// <see cref="Camera.SortByY"/>, things lower down draw in front of things above them, before hierarchy order. An
    /// entity drawn on top (a <see cref="Draggable"/> being held, and what's inside it) draws over everything else.
    /// </summary>
    public static IEnumerable<Entity> DrawOrder(Scene scene) => Sorted(scene, [], null, null, []).Select(d => d.Entity);

    /// <summary>
    /// The draw order of an entity: its sprite's, or else that of its first drawable component (in
    /// <see cref="IDrawable.DrawStep"/> order) with one; null if it draws nothing in the world.
    /// </summary>
    public static int? OrderOf(Entity entity)
    {
        int? order = null;
        var orderStep = int.MaxValue;
        foreach (var component in entity.ComponentList)
        {
            if (component is Sprite sprite)
                return sprite.Order;
            if (component is IDrawable drawable && drawable.DrawStep is var step and < DrawSteps.OverWorld && step < orderStep && drawable.DrawOrder is { } own)
                (order, orderStep) = (own, step);
        }

        return order;
    }

    // Drawn over everything else: something it's in (or it) says so, like a card being dragged.
    private static bool IsLifted(Entity entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            foreach (var component in e.ComponentList)
            {
                if (component is IDrawEffect { DrawsOnTop: true })
                    return true;
            }
        }

        return false;
    }

    // An entity's draws, component by component. On a parallax layer (the entity or an ancestor) they shift with it, and
    // its sprite may repeat across the view; otherwise its and its ancestors' effects move them (a shake, a beat).
    // Steps from DrawSteps.View on are drawn where they say.
    private static void DrawEntity(Drawn entry, List<IDrawable> drawables, Drawing drawing, List<SpriteDraw> sprites)
    {
        var entity = entry.Entity;
        var parallax = ParallaxOf(entity);
        drawing.Entity = entity;
        drawing.CanCull = parallax is null;
        var end = entry.First + entry.Count;
        var i = entry.First;
        if (parallax is null)
        {
            var start = sprites.Count;
            for (; i < end && drawables[i].DrawStep < DrawSteps.View; i++)
                drawables[i].Draw(drawing);
            MoveByEffects(entity, sprites, start);
            for (; i < end; i++)
                drawables[i].Draw(drawing);
            return;
        }

        var shift = parallax.OffsetFor(drawing.View.Center);
        var by = Matrix4x4.CreateTranslation(shift.X, shift.Y, 0);
        var repeats = parallax.Entity == entity && (parallax.RepeatX || parallax.RepeatY);
        for (; i < end; i++)
        {
            var drawable = drawables[i];
            var from = sprites.Count;
            drawable.Draw(drawing);
            if (drawable.DrawStep >= DrawSteps.View)
                continue;
            for (var d = from; d < sprites.Count; d++)
                sprites[d] = sprites[d] with { World = sprites[d].World * by };

            // Only the sprite repeats (all nine pieces of a sliced one, together); the entity's text and particles are drawn once.
            if (repeats && drawable is Sprite sprite && sprites.Count > from)
                Repeat(sprite, sprites, from, by, parallax, drawing.View);
        }
    }

    // Moves the draws from `start` on by the effects of the entity and what it's inside.
    private static void MoveByEffects(Entity entity, List<SpriteDraw> sprites, int start)
    {
        if (sprites.Count == start)
            return;
        for (var e = entity; e is not null; e = e.Parent)
        {
            foreach (var component in e.ComponentList)
            {
                if (component is not IDrawEffect effect || effect.DrawTransform is not { } transform)
                    continue;
                for (var d = start; d < sprites.Count; d++)
                    sprites[d] = sprites[d] with { World = sprites[d].World * transform };
            }
        }
    }

    // A parallax sprite's draws (from `from` to the end) repeated one sprite apart across the view, along the repeated axes.
    private static void Repeat(Sprite sprite, List<SpriteDraw> sprites, int from, Matrix4x4 shift, Parallax parallax, Box view)
    {
        var pieces = sprites.GetRange(from, sprites.Count - from);
        sprites.RemoveRange(from, pieces.Count);
        if (pieces.Count == 1)
        {
            sprites.AddRange(Repeated(pieces[0], parallax, view));
            return;
        }

        // The pieces repeat together, a whole sprite apart.
        var whole = pieces[0] with { World = sprite.QuadMatrix * shift };
        var offsets = Repeated(whole, parallax, view).Select(d => new Vector2(d.World.M41 - whole.World.M41, d.World.M42 - whole.World.M42)).ToList();
        foreach (var piece in pieces)
        {
            foreach (var offset in offsets)
                sprites.Add(piece with { World = piece.World * Matrix4x4.CreateTranslation(offset.X, offset.Y, 0) });
        }
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
    private static IEnumerable<SpriteDraw> Repeated(SpriteDraw draw, Parallax parallax, Box view)
    {
        var center = new Vector2(draw.World.M41, draw.World.M42);
        var step = new Vector2(Math.Abs(draw.World.M11) + Math.Abs(draw.World.M21), Math.Abs(draw.World.M12) + Math.Abs(draw.World.M22));
        var (fromX, toX) = parallax.RepeatX ? Span(center.X, step.X, view.Center.X, view.HalfSize.X) : (0, 0);
        var (fromY, toY) = parallax.RepeatY ? Span(center.Y, step.Y, view.Center.Y, view.HalfSize.Y) : (0, 0);
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

    /// <summary>What <paramref name="drawable"/> draws this frame, on its own, for a view of <paramref name="view"/> (the editor's Scene view).</summary>
    public static IReadOnlyList<SpriteDraw> DrawsOf(IDrawable drawable, Box view, float lineWidth)
    {
        var draws = new List<SpriteDraw>();
        drawable.Draw(new Drawing(draws, ((Component)drawable).Entity!, view, lineWidth, false));
        return draws;
    }

    // Pinned to the screen: a ScreenAnchor on the entity or any ancestor (HUDs, menus, dialogue boxes), or riding along
    // inside the camera (a score that's the camera's child).
    internal static bool IsOnScreen(Entity entity, Entity? camera)
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

    // ----- Lights -----

    /// <summary>The lights in a scene that reach into the camera's view, with their flicker as it is now.</summary>
    public static IReadOnlyList<LightDraw> Lights(Scene scene, float aspect)
    {
        var camera = FindCamera(scene);
        var size = camera?.GetComponent<Camera>()?.Size ?? DefaultCameraSize;
        return LightsIn(scene, new Box(ViewCenter(camera), new Vector2(size * aspect, size)));
    }

    private static List<LightDraw> LightsIn(Scene scene, Box view)
    {
        var lights = new List<LightDraw>();
        foreach (var draw in LightDraws(scene))
        {
            var reach = view.HalfSize + new Vector2(draw.Radius);
            if (MathF.Abs(draw.Center.X - view.Center.X) <= reach.X && MathF.Abs(draw.Center.Y - view.Center.Y) <= reach.Y)
                lights.Add(draw);
        }

        return lights;
    }

    /// <summary>
    /// Every light in the scene that gives off any light, as it is now (flicker included), with the shadows of those
    /// that cast them, and then the light other components give off (see <see cref="ILightSource"/>).
    /// </summary>
    public static IEnumerable<LightDraw> LightDraws(Scene scene)
    {
        List<ShadowEdge>? edges = null;
        foreach (var light in scene.Active<Light>())
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

        var spots = new List<LightSpot>();
        foreach (var source in scene.Active<ILightSource>())
        {
            spots.Clear();
            source.AddLights(spots);
            foreach (var spot in spots)
                yield return new LightDraw(spot.Center, spot.Radius, spot.Color, spot.Softness);
        }
    }

    // ----- The screen -----

    // The scene change's cover, drawn the transition's way: the whole view fading, a wipe across it, or an iris closing.
    private static SpriteDraw? TransitionDraw(Runtime.Game game, SceneTransition transition, Box view, Matrix4x4 clipToWorld)
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
        var corner = Vector2.Abs(center - view.Center) + view.HalfSize;
        var full = corner.Length();
        var radius = full * (1 - amount);
        var half = full * 2 + 1;
        var quad = Matrix4x4.CreateScale(half * 2, half * 2, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0);
        return new SpriteDraw(quad, null, color, ScreenEntity) { HoleRadius = Math.Max(radius / half, 1e-4f) };
    }

    // What an iris closes in on: the entity called Player, or else the first a player moves (see PlayerMovementAttribute).
    private static Vector2? IrisCenter(Scene scene)
    {
        var player = scene.Entities.FirstOrDefault(e => e.Name == "Player" && e.IsActive)
                     ?? scene.Entities.FirstOrDefault(e => e.IsActive && PlayerMovementAttribute.IsOn(e));
        if (player is null)
            return null;
        var position = player.Transform.WorldPosition;
        return new Vector2(position.X, position.Y);
    }

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
                    yield return Quads.Line(ScreenEntity, line.From, line.To, thickness, line.Color);
                    break;
                case Runtime.DebugCircle circle:
                    foreach (var draw in Quads.Circle(ScreenEntity, circle.Center, circle.Radius, thickness, circle.Color, segments: 32))
                        yield return draw;
                    break;
                case Runtime.DebugText text:
                    yield return new SpriteDraw(Matrix4x4.CreateScale(text.Size, text.Size, 1) *
                                                Matrix4x4.CreateTranslation(text.Position.X, text.Position.Y, 0), null, text.Color, ScreenEntity, text.Text);
                    break;
            }
        }
    }

    /// <summary>The first enabled entity with a Camera, in hierarchy order.</summary>
    public static Entity? FindCamera(Scene scene) => scene.Active<Camera>().FirstOrDefault()?.Entity;

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
}
