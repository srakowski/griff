using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Rendering;

/// <summary>
/// Builds <see cref="RenderFrame"/>s from scenes. Shared by the editor and the game so they always agree. It knows how to
/// see a scene (cameras, parallax, light and dark, what's pinned to the screen) but nothing of what's in it: every
/// component that draws is an <see cref="IDrawable"/>, changed by its entity's <see cref="IDrawEffect"/>s, and a running
/// game's systems add what the game itself draws (see <see cref="Runtime.GameSystem"/>'s Draw).
/// </summary>
public static partial class SceneRenderer
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
        Build(game.Scene, aspect, game, new Recycled(recycle));

    /// <summary>
    /// Builds a picture of a running game as it is now, for a saved game's screenshot (see
    /// <see cref="Runtime.Game.PictureWanted"/>): the frame <see cref="Build(Runtime.Game, float)"/> builds, less the panels
    /// open over the game (the save screen it's saved from), a fade or flash and debug shapes.
    /// </summary>
    public static RenderFrame BuildPicture(Runtime.Game game, float aspect)
    {
        var outer = t_picture;
        t_picture = game;
        try
        {
            return Build(game, aspect, null);
        }
        finally
        {
            t_picture = outer;
        }
    }

    // The game whose picture is being built, if one is.
    [ThreadStatic]
    private static Runtime.Game? t_picture;

    private static Func<Entity, bool>? s_notInPanel;

    // A frame the caller is done with, to fill again: its lists of draws and of lights (with their shadows' lists) too.
    private readonly record struct Recycled(RenderFrame? Frame)
    {
        public List<SpriteDraw>? Sprites => Frame?.Sprites as List<SpriteDraw>;

        public List<LightDraw>? Lights => Frame?.Lights as List<LightDraw>;
    }

    private static RenderFrame Build(Scene scene, float aspect, Runtime.Game? game, Recycled reuse = default)
    {
        // While something profiles the game, each part of the build is timed (see FrameCostKind.Build).
        if (game?.Profiler is { } profiler)
            return BuildTimed(scene, aspect, game, reuse, profiler.BuildTimer);
        return Build(scene, aspect, game, reuse, null);
    }

    private static RenderFrame Build(Scene scene, float aspect, Runtime.Game? game, Recycled reuse, BuildTimer? timer)
    {
        // A running game is drawn between its last two steps: everything that moved in the last one (the camera too) is
        // placed part way there, so it moves smoothly whatever the screen's frame rate.
        if (game is null || !BlendSteps(scene, game))
            return BuildNow(scene, aspect, game, reuse, timer);
        try
        {
            return BuildNow(scene, aspect, game, reuse, timer);
        }
        finally
        {
            scene.DrawStamp = 0;
        }
    }

    // The parts of a timed build that aren't a kind of thing drawn: from one mark to the next counts to the first.
    private const string CameraPart = "Camera", ClearingPart = "Clearing", SortingPart = "Sorting", SystemsPart = "Systems", LightsPart = "Lights",
        ScreenPart = "Screen";

    private static RenderFrame BuildTimed(Scene scene, float aspect, Runtime.Game game, Recycled reuse, BuildTimer timer)
    {
        timer.Begin(CameraPart);
        try
        {
            return Build(scene, aspect, game, reuse, timer);
        }
        finally
        {
            timer.End();
        }
    }

    // Each frame drawn between steps has its own stamp, so a transform knows the matrix it worked out is this frame's.
    private static long s_drawStamps;

    // The time of a scene that isn't running (the editor's), for what the renderer moves by itself.
    private static readonly System.Diagnostics.Stopwatch s_clock = System.Diagnostics.Stopwatch.StartNew();

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

    private static RenderFrame BuildNow(Scene scene, float aspect, Runtime.Game? game, Recycled reuse, BuildTimer? timer, Viewport? viewport = null)
    {
        var main = FindCamera(scene);
        var camera = viewport?.Entity ?? main;
        var component = camera?.GetComponent<Camera>();
        // A zoom and a fade of the background are drawn between steps, like the camera's moves.
        var drawn = component?.Drawn(StepBlend.For(scene, camera));
        var size = drawn?.Size ?? DefaultCameraSize;
        var view = new Box(ViewCenter(camera), new Vector2(size * aspect, size));
        var sprites = reuse.Sprites ?? [];
        // A minigame with the screen (in a running game) is all that's drawn.
        var only = game is not null ? MinigameRoot.Shown(scene) : null;
        // With viewports (in a running game), each view draws what's its own.
        var showsViews = game is not null && viewport is null && AnyViewport(scene, only);
        var filter = new ViewFilter(camera, main, viewport, game is not null && (showsViews || viewport is not null), only);
        // A picture of the game (for a saved game) leaves out the panels open over it: the save screen itself.
        Func<Entity, bool>? leaveOut = null;
        if (t_picture is { } pictured && pictured == game)
            leaveOut = s_notInPanel ??= e => t_picture is not { } shown || !shown.IsInOpenPanel(e);
        var screenCount = DrawScene(scene, view, size, game is not null, sprites, leaveOut, overWorld: true, timer, reuse: true, filter);

        // The game's systems draw what the game knows of (a strategy game's fog and HUD), or see the world their own way.
        timer?.Mark(SystemsPart);
        // The last frame's drawing is used again, unless a frame being built round this one has it.
        var frame = (t_scratch ??= new Scratch()).Frame;
        t_scratch.Frame = null;
        if (frame is null)
            frame = new FrameDrawing(game, scene, camera, size, aspect, view, ViewProjection(camera, size, aspect), sprites, screenCount, filter);
        else
            frame.Reset(game, scene, camera, size, aspect, view, ViewProjection(camera, size, aspect), sprites, screenCount, filter);
        if (game is not null)
            DrawSystems(game, frame, timer);

        timer?.Mark(LightsPart);

        sprites = frame.Sprites;
        view = frame.View;
        // A viewport's view goes onto its part of the screen.
        var viewProjection = viewport is null ? frame.ViewProjection : frame.ViewProjection * viewport.ToScreen();

        // What the game draws over the screen (its fades and flashes, and debug shapes) changes once a step too, so it's
        // drawn between the steps as well.
        var blend = frame.Blend;

        // In the dark, the world is drawn first and darkened; what's pinned to the screen goes over it untouched. Viewports
        // go between the two as well.
        var ambient = component?.AmbientDrawn(blend) ?? Color.White;
        var dark = component?.IsDark == true && frame.Lit;
        var litCount = sprites.Count;
        var worldCount = sprites.Count;
        if (dark || showsViews)
            worldCount = PutScreenLast(sprites, camera, t_scratch ??= new Scratch(), filter);
        if (dark)
            litCount = worldCount;

        // The last frame's lights are replaced, their shadows' lists kept for this frame's.
        var lights = reuse.Lights;
        if (lights is not null)
            LetGoOf(lights);
        if (dark)
            AddLights(scene, view, lights ??= [], t_scratch ??= new Scratch());
        timer?.Mark(ScreenPart);

        if (game is not null && leaveOut is null && viewport is null && Matrix4x4.Invert(viewProjection, out var clipToWorld))
        {
            // Quads over the whole view: twice the unit quad in clip space, taken back into the world.
            var whole = Matrix4x4.CreateScale(2, 2, 1) * clipToWorld;
            if (game.ScreenFlashDrawn(blend) is { A: > 0 } flash)
                sprites.Add(new SpriteDraw(whole, null, flash, ScreenEntity));
            if (TransitionDraw(game, game.FadeDrawn(blend), component?.Transition ?? SceneTransition.Fade, view, clipToWorld) is { } transition)
                sprites.Add(transition);
        }

        if (viewport is null && leaveOut is null && game is { Debug.HasShapes: true })
            sprites.AddRange(DebugDraws(game.Debug.ShapesAt(blend), size));

        frame.Release();
        (t_scratch ??= new Scratch()).Frame = frame;
        var built = reuse.Frame ?? new RenderFrame();
        built.Background = drawn?.Background ?? DefaultBackground;
        built.ViewProjection = viewProjection;
        built.Sprites = sprites;
        built.Camera = camera;
        built.Ambient = dark ? ambient : Color.White;
        built.Lights = lights ?? [];
        built.LitCount = dark ? litCount : sprites.Count;
        var effects = viewport is null && camera?.GetComponent<ScreenEffects>() is { IsActive: true } shown ? shown : null;
        built.Effects = effects;
        built.Look = effects?.Drawn(StepBlend.For(scene, camera)) ?? default;
        built.Time = game?.Time.Total ?? s_clock.Elapsed.TotalSeconds;
        built.Pictures = game?.Pictures;
        built.ViewsAt = worldCount;
        built.ClearViews();
        if (showsViews)
            BuildViews(scene, aspect, game!, timer, built, sprites, worldCount);
        return built;
    }

    // True if the scene has a viewport to draw: one in the minigame with the screen, if one has.
    private static bool AnyViewport(Scene scene, Entity? only)
    {
        foreach (var viewport in scene.ActiveNow<Viewport>())
        {
            if (!MinigameRoot.IsBehind(viewport.Entity!, only))
                return true;
        }

        return false;
    }

    // Each viewport showing, lowest Order first, built as a frame of its own (the one it had last time, filled again), and a
    // border round each drawn over them all, under what's pinned to the screen.
    private static void BuildViews(Scene scene, float aspect, Runtime.Game game, BuildTimer? timer, RenderFrame built, List<SpriteDraw> sprites,
        int worldCount)
    {
        var scratch = t_scratch ??= new Scratch();
        var order = scratch.Viewports;
        order.Clear();
        var only = MinigameRoot.Shown(scene);
        foreach (var viewport in scene.ActiveNow<Viewport>())
        {
            if (!viewport.IsShowing || MinigameRoot.IsBehind(viewport.Entity!, only))
                continue;
            var at = order.Count;
            while (at > 0 && order[at - 1].Order > viewport.Order)
                at--;
            order.Insert(at, viewport);
        }

        try
        {
            if (order.Count == 0)
                return;
            Matrix4x4.Invert(built.ViewProjection, out var clipToWorld);
            var borderAt = worldCount;
            foreach (var viewport in order)
            {
                var view = built.AddView();
                view.Viewport = viewport;
                view.Frame = BuildNow(scene, viewport.AspectOn(aspect), game, new Recycled(view.Frame), timer, viewport);
                (view.Left, view.Top, view.Width, view.Height) = viewport.Area;
                view.Border = viewport.Border;
                if (viewport.Border.A > 0)
                    borderAt = AddBorder(sprites, borderAt, view, aspect, clipToWorld);
            }
        }
        finally
        {
            order.Clear();
        }
    }

    // A frame round a view, about two pixels thick on a 720-pixel-high screen, just outside it: four quads in clip space,
    // taken back into the world, put in at `at`. Returns where the next go.
    private static int AddBorder(List<SpriteDraw> sprites, int at, RenderView view, float aspect, Matrix4x4 clipToWorld)
    {
        var thickY = 1 / 180f;
        var thickX = thickY / (aspect > 0 ? aspect : 1);
        var (left, right) = (view.Left * 2 - 1, (view.Left + view.Width) * 2 - 1);
        var (top, bottom) = (1 - view.Top * 2, 1 - (view.Top + view.Height) * 2);
        sprites.Insert(at++, Edge((left + right) / 2, top + thickY / 2, right - left + 2 * thickX, thickY));
        sprites.Insert(at++, Edge((left + right) / 2, bottom - thickY / 2, right - left + 2 * thickX, thickY));
        sprites.Insert(at++, Edge(left - thickX / 2, (top + bottom) / 2, thickX, top - bottom));
        sprites.Insert(at++, Edge(right + thickX / 2, (top + bottom) / 2, thickX, top - bottom));
        return at;

        SpriteDraw Edge(float x, float y, float width, float height) =>
            new(Matrix4x4.CreateScale(width, height, 1) * Matrix4x4.CreateTranslation(x, y, 0) * clipToWorld, null, view.Border, ScreenEntity);
    }

    // The game's systems draw, in the order they were added: those that see the world their own way (first person) first,
    // then every system over that. The list is copied first (a system may add or remove one as it draws), into one kept
    // for the next frame.
    private static void DrawSystems(Runtime.Game game, FrameDrawing frame, BuildTimer? timer)
    {
        var scratch = t_scratch ??= new Scratch();
        // A system drawing a frame of its own (a picture in picture) uses lists of its own.
        var systems = scratch.Systems is { Count: 0 } spare ? spare : [];
        systems.AddRange(game.Systems);
        try
        {
            foreach (var system in systems)
            {
                timer?.Mark(system);
                system.RunDrawView(frame);
            }

            foreach (var system in systems)
            {
                timer?.Mark(system);
                system.RunDraw(frame);
            }
        }
        finally
        {
            systems.Clear();
        }
    }

    // Moves the draws pinned to the screen after the others, each keeping their order, in place (by way of the scratch's
    // spare, so a big dark scene's frame makes no garbage). Returns how many draws aren't pinned.
    private static int PutScreenLast(List<SpriteDraw> sprites, Entity? camera, Scratch scratch, in ViewFilter filter = default)
    {
        var drawn = CollectionsMarshal.AsSpan(sprites);
        var (world, screen) = (0, 0);
        foreach (ref readonly var draw in drawn)
        {
            if (filter.IsOnScreen(draw.Entity, camera))
                screen++;
        }

        if (screen == 0)
            return drawn.Length;
        if (scratch.Spare.Length < screen)
            scratch.Spare = new SpriteDraw[Math.Max(screen, scratch.Spare.Length * 2)];
        var spare = scratch.Spare.AsSpan(0, screen);
        screen = 0;
        for (var i = 0; i < drawn.Length; i++)
        {
            if (filter.IsOnScreen(drawn[i].Entity, camera))
                spare[screen++] = drawn[i];
            else
                drawn[world++] = drawn[i];
        }

        spare.CopyTo(drawn[world..]);
        // The copies would otherwise keep the entities they belong to from being collected.
        spare.Clear();
        return world;
    }

    // ----- Entities -----

    // One entity that draws: its sprite when that's all it draws (or else its drawable components, gathered in step order
    // apart), where its draws are in the frame's list once it's drawn (First, Count), and what it sorts by.
    private readonly record struct Drawn(Entity Entity, Sprite? Sole, int First, int Count, bool Lifted, int Order, float Z, float Y);

    /// <summary>
    /// Draws the scene's entities (those <paramref name="include"/> takes, or all) back to front into
    /// <paramref name="sprites"/>, culled to <paramref name="view"/>, and then, with <paramref name="overWorld"/>, what
    /// goes over the world (ropes, wires) between them and what's pinned to the screen. Returns how many draws at the
    /// end are pinned to the screen when it had to split them for that, or -1. With <paramref name="reuse"/>,
    /// <paramref name="sprites"/> holds last frame's draws, to be replaced; otherwise the draws are added after what it holds.
    /// </summary>
    internal static int DrawScene(Scene scene, Box view, float cameraSize, bool playing, List<SpriteDraw> sprites, Func<Entity, bool>? include, bool overWorld,
        BuildTimer? timer = null, bool reuse = false, ViewFilter filter = default)
    {
        var scratch = t_scratch ?? new Scratch();
        t_scratch = null;
        try
        {
            return DrawScene(scene, view, cameraSize, playing, sprites, include, overWorld ? scratch.Overs : null, scratch, timer, reuse, filter);
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

        public StableSort Sort { get; } = new();

        // The runs of the frame's draws in the order they go in, and room for those that move by way of it.
        public List<Block> Blocks { get; } = [];

        public SpriteDraw[] Spare { get; set; } = [];

        // For a big scene's frame, built in chunks.
        public ChunkedScratch? Chunked { get; set; }

        // The systems drawing this frame, and what they draw with: one kept from frame to frame (taken while in use).
        public List<GameSystem> Systems { get; } = [];

        public FrameDrawing? Frame { get; set; }

        // The viewports of the frame being built, in the order they're drawn.
        public List<Viewport> Viewports { get; } = [];

        // What the entities draw with, one at a time, and what goes over the world (ropes, wires) with.
        public Drawing? Drawing;

        public Drawing? OverDrawing;

        // What goes over the world, while it's drawn.
        public List<SpriteDraw> Over { get; } = [];

        // Every side of what blocks light in the scene, while the lights are worked out.
        public List<ShadowEdge> Edges { get; } = [];

        // Lists for lights' shadows (see LightDraw.Casters), from frames done with, to use again.
        public List<List<ShadowEdge>> Casters { get; } = [];

        // The light an ILightSource gives off, while it's asked.
        public List<LightSpot> Spots { get; } = [];
    }

    // One pass over the entities, in hierarchy order: each is drawn as it's found, its draws kept together where they
    // land, and then those runs of draws are put back to front by what their entities sort by. An entity's drawable
    // components are called in that order too (hierarchy order, then DrawStep order within the entity), whatever order
    // their draws end up in.
    // With a timer (a profiler's), each entity's own work counts to the kind of thing it draws first, and each drawable's
    // drawing to its own kind.
    private static int DrawScene(Scene scene, Box view, float cameraSize, bool playing, List<SpriteDraw> sprites, Func<Entity, bool>? include,
        List<IDrawable>? overs, Scratch scratch, BuildTimer? timer, bool reuse, in ViewFilter filter)
    {
        var camera = filter.Camera ?? FindCamera(scene);
        var sortByY = camera?.GetComponent<Camera>()?.SortByY == true;
        if (include is null && scene.EntitiesNow.Length >= ChunkedFrom)
        {
            DrawChunked(scene, view, cameraSize, playing, sprites, reuse ? 0 : sprites.Count, overs, scratch, timer, sortByY, filter);
        }
        else
        {
            scratch.Chunked?.LetGo();
            if (reuse)
            {
                timer?.Mark(ClearingPart);
                sprites.Clear();
            }

            DrawEach(scene, view, cameraSize, playing, sprites, include, overs, scratch, timer, sortByY, filter);
        }

        return overs is { Count: > 0 } ? DrawOverWorld(view, cameraSize, playing, sprites, overs, scratch, timer, camera, filter) : -1;
    }

    // A drawing that adds to `draws`: the one kept in `kept`, set up afresh, or a new one kept there. Released once done with.
    private static Drawing DrawingFor(ref Drawing? kept, List<SpriteDraw> draws, Box view, float cameraSize, bool playing)
    {
        if (kept is null)
            return kept = new Drawing(draws, ScreenEntity, view, cameraSize / 180, playing);
        kept.Reset(draws, view, cameraSize / 180, playing);
        return kept;
    }

    // The scene's entities one by one, on this thread.
    private static void DrawEach(Scene scene, Box view, float cameraSize, bool playing, List<SpriteDraw> sprites, Func<Entity, bool>? include,
        List<IDrawable>? overs, Scratch scratch, BuildTimer? timer, bool sortByY, in ViewFilter filter)
    {
        var drawables = scratch.Drawables;
        var entries = scratch.Entries;
        var first = sprites.Count;
        sprites.EnsureCapacity(first + scene.EntitiesNow.Length + 16);
        var drawing = DrawingFor(ref scratch.Drawing, sprites, view, cameraSize, playing);
        // By index: going through a list of them as such would make garbage.
        var entities = scene.EntitiesNow;
        for (var e = 0; e < entities.Length; e++)
        {
            var entity = entities[e];
            if ((entity.Roles & ComponentRoles.Drawable) == 0 || !entity.IsActive || (include is not null && !include(entity)) || !filter.Shows(entity))
                continue;
            timer?.Mark(LeadOf(entity));
            if (!Describe(entity, sortByY, drawables, overs, out var entry))
                continue;
            var start = sprites.Count;
            DrawEntity(entity, entry.Sole, drawables, drawing, sprites, timer);
            // What draws nothing this frame (blinked out, out of view) has nothing to put in order.
            if (drawing.Sorts.Count > 0)
            {
                if (sortByY)
                    AddSorted(entries, entry, start, sprites.Count, drawing.Sorts);
                else if (sprites.Count > start)
                    entries.Add(entry with { First = start, Count = sprites.Count - start });
                drawing.Sorts.Clear();
            }
            else if (sprites.Count > start)
            {
                entries.Add(entry with { First = start, Count = sprites.Count - start });
            }
        }

        drawing.Release();
        timer?.Mark(SortingPart);
        PutBackToFront(sprites, first, CollectionsMarshal.AsSpan(entries), sortByY, scratch);
    }

    // What goes over the world (ropes, wires), after it and under what's pinned to the screen. Returns how many draws at
    // the end are pinned to the screen when it had to split them for that, or -1.
    private static int DrawOverWorld(Box view, float cameraSize, bool playing, List<SpriteDraw> sprites, List<IDrawable> overs,
        Scratch scratch, BuildTimer? timer, Entity? camera, in ViewFilter filter)
    {
        // Over the world: lower steps first, in hierarchy order within a step (the order they're listed in).
        for (var i = 1; i < overs.Count; i++)
        {
            var drawable = overs[i];
            var at = i;
            for (; at > 0 && overs[at - 1].DrawStep > drawable.DrawStep; at--)
                overs[at] = overs[at - 1];
            overs[at] = drawable;
        }

        var over = scratch.Over;
        var overDrawing = DrawingFor(ref scratch.OverDrawing, over, view, cameraSize, playing);
        try
        {
            foreach (var drawable in overs)
            {
                overDrawing.Entity = ((Component)drawable).Entity!;
                Draw(drawable, overDrawing, timer);
            }

            timer?.Mark(SortingPart);
            if (over.Count == 0)
                return -1;
            var count = sprites.Count;
            var world = PutScreenLast(sprites, camera, scratch, filter);
            sprites.InsertRange(world, over);
            return count - world;
        }
        finally
        {
            // The draws would otherwise keep the entities they belong to from being collected.
            over.Clear();
            overDrawing.Release();
        }
    }

    // An entity's draws from `start` to `end` as an entry for each stretch that sorts at its own height (see
    // Drawing.SortAt), and one at the entity's own for any before the first.
    private static void AddSorted(List<Drawn> entries, Drawn entry, int start, int end, List<(int At, float Y)> sorts)
    {
        var (from, y) = (start, entry.Y);
        foreach (var (at, sortY) in sorts)
        {
            if (at > from)
                entries.Add(entry with { First = from, Count = at - from, Y = y });
            (from, y) = (Math.Max(from, at), sortY);
        }

        if (end > from)
            entries.Add(entry with { First = from, Count = end - from, Y = y });
    }

    // Puts the draws from `first` on (each entry's run of them, the runs in hierarchy order) back to front, each run kept
    // as it is. Nothing moves when they're in that order already, as a big scene on one layer is; otherwise entries next to
    // each other in both orders move together, the biggest such block of them in place and the rest by way of the spare.
    private static void PutBackToFront(List<SpriteDraw> sprites, int first, ReadOnlySpan<Drawn> entries, bool sortByY, Scratch scratch)
    {
        var order = SortBackToFront(entries, sortByY, scratch.Sort);
        if (order.IsEmpty)
            return;

        var blocks = scratch.Blocks;
        blocks.Clear();
        var biggest = 0;
        foreach (var index in order)
        {
            ref readonly var entry = ref entries[index];
            if (blocks.Count > 0 && blocks[^1].From + blocks[^1].Count == entry.First)
                blocks[^1] = blocks[^1] with { Count = blocks[^1].Count + entry.Count };
            else
                blocks.Add(new Block(entry.First, entry.Count));
            if (blocks[^1].Count > blocks[biggest].Count)
                biggest = blocks.Count - 1;
        }

        var drawn = CollectionsMarshal.AsSpan(sprites);
        var big = blocks[biggest];
        var spareCount = drawn.Length - first - big.Count;
        if (scratch.Spare.Length < spareCount)
            scratch.Spare = new SpriteDraw[Math.Max(spareCount, scratch.Spare.Length * 2)];
        var spare = scratch.Spare.AsSpan(0, spareCount);
        var at = 0;
        var bigTo = first;
        for (var b = 0; b < blocks.Count; b++)
        {
            if (b < biggest)
                bigTo += blocks[b].Count;
            if (b == biggest)
                continue;
            drawn.Slice(blocks[b].From, blocks[b].Count).CopyTo(spare[at..]);
            at += blocks[b].Count;
        }

        // Copying within one array as if by way of another, so it may overlap where it was.
        drawn.Slice(big.From, big.Count).CopyTo(drawn[bigTo..]);
        (at, var to) = (0, first);
        for (var b = 0; b < blocks.Count; b++)
        {
            var count = blocks[b].Count;
            if (b != biggest)
            {
                spare.Slice(at, count).CopyTo(drawn[to..]);
                at += count;
            }

            to += count;
        }

        // The copies would otherwise keep the entities they belong to (and what they draw) from being collected.
        spare.Clear();
    }

    // Whether the entity draws in the world, and what it sorts by: its sprite when that's all it draws, or else its
    // drawable components gathered into `drawables` in step order (and those that go over the world into `overs`). Its
    // sprite's order is the entity's, or else that of its first drawable (by step) with one; with none it draws nothing
    // in the world.
    private static bool Describe(Entity entity, bool sortByY, List<IDrawable> drawables, List<IDrawable>? overs, out Drawn entry)
    {
        Vector3 position;
        Matrix4x4 world;
        if (entity.SoleSprite is { } sole)
        {
            world = entity.Transform.WorldMatrix;
            position = world.Translation;
            entry = new Drawn(entity, sole, 0, 0, IsLifted(entity), sole.Order, position.Z, sortByY ? FeetOf(sole, world) : 0);
            return true;
        }

        drawables.Clear();
        Sprite? firstSprite = null;
        int? spriteOrder = null, ownOrder = null;
        var orderStep = int.MaxValue;
        foreach (var component in entity.ComponentList)
        {
            if ((component.Roles & ComponentRoles.Drawable) == 0)
                continue;
            var drawable = Unsafe.As<IDrawable>(component);
            var step = drawable.DrawStep;
            if (step >= DrawSteps.OverWorld)
            {
                overs?.Add(drawable);
                continue;
            }

            // In step order, keeping the order they're attached within a step.
            var at = drawables.Count;
            while (at > 0 && drawables[at - 1].DrawStep > step)
                at--;
            drawables.Insert(at, drawable);

            if ((component.Roles & ComponentRoles.Sprite) != 0 && firstSprite is null)
                (firstSprite, spriteOrder) = (Unsafe.As<Sprite>(component), Unsafe.As<Sprite>(component).Order);
            else if (step < orderStep && drawable.DrawOrder is { } own)
                (ownOrder, orderStep) = (own, step);
        }

        if ((spriteOrder ?? ownOrder) is not { } entityOrder)
        {
            entry = default;
            return false;
        }

        world = entity.Transform.WorldMatrix;
        position = world.Translation;
        var y = !sortByY ? 0 : firstSprite is not null ? FeetOf(firstSprite, world) : position.Y;
        entry = new Drawn(entity, null, 0, 0, IsLifted(entity), entityOrder, position.Z, y);
        return true;
    }

    // What a sprite sorts by when the camera sorts by Y: the bottom of its picture on the screen, where a character's feet
    // are (or a tree's trunk meets the ground), so a tall sprite and a short one beside it sort by where they stand.
    private static float FeetOf(Sprite sprite, in Matrix4x4 world)
    {
        var size = sprite.Size;
        return world.M42 - (MathF.Abs(world.M12 * size.X) + MathF.Abs(world.M22 * size.Y)) / 2;
    }

    // Lifted ones last, then by Order, then Z, then (sorting by Y) higher up first, keeping hierarchy order among equals
    // (the order they come in): empty when that's the order they're in. A big scene is often in order already, all on one
    // layer, or nearly (a few things over or under its crowd): that's seen, and merged, in a look at each.
    private static ReadOnlySpan<int> SortBackToFront(ReadOnlySpan<Drawn> entries, bool sortByY, StableSort keys)
    {
        switch (keys.TryMerge(entries, new BackToFront(), MaxStretches))
        {
            case 1:
                return [];
            case > 1:
                return keys.Order;
        }

        keys.Begin(entries.Length);
        var key = keys.Keys;
        if (sortByY)
        {
            for (var i = 0; i < entries.Length; i++)
                key[i] = ~StableSort.KeyOf(entries[i].Y);
            keys.Apply();
        }

        for (var i = 0; i < entries.Length; i++)
            key[i] = StableSort.KeyOf(entries[i].Z);
        keys.Apply();
        for (var i = 0; i < entries.Length; i++)
            key[i] = StableSort.KeyOf(entries[i].Order);
        keys.Apply();
        for (var i = 0; i < entries.Length; i++)
            key[i] = entries[i].Lifted ? 1u : 0u;
        keys.Apply();
        return keys.Order;
    }

    // Past this many stretches in order, the entries are sorted by key rather than merged.
    private const int MaxStretches = 16;

    private readonly struct BackToFront : IComparer<Drawn>
    {
        public int Compare(Drawn a, Drawn b) => SceneRenderer.Compare(a, b);
    }

    // A run of the frame's draws: where it starts and how many.
    private readonly record struct Block(int From, int Count);

    private static int Compare(in Drawn a, in Drawn b)
    {
        var c = a.Lifted.CompareTo(b.Lifted);
        if (c == 0)
            c = a.Order.CompareTo(b.Order);
        if (c == 0)
            c = a.Z.CompareTo(b.Z);
        if (c == 0)
            c = b.Y.CompareTo(a.Y);
        return c;
    }

    /// <summary>
    /// Drawable entities back to front: Order, then Z (higher is closer), then hierarchy order. With the camera's
    /// <see cref="Camera.SortByY"/>, things lower down draw in front of things above them, before hierarchy order. An
    /// entity drawn on top (a <see cref="Draggable"/> being held, and what's inside it) draws over everything else.
    /// </summary>
    public static IEnumerable<Entity> DrawOrder(Scene scene)
    {
        var sortByY = FindCamera(scene)?.GetComponent<Camera>()?.SortByY == true;
        var drawables = new List<IDrawable>();
        var entries = new List<Drawn>();
        foreach (var entity in scene.Entities)
        {
            if ((entity.Roles & ComponentRoles.Drawable) != 0 && entity.IsActive && Describe(entity, sortByY, drawables, null, out var entry))
                entries.Add(entry);
        }

        var order = SortBackToFront(CollectionsMarshal.AsSpan(entries), sortByY, new StableSort());
        return order.IsEmpty ? entries.Select(d => d.Entity).ToList() : order.ToArray().Select(at => entries[at].Entity).ToList();
    }

    /// <summary>
    /// Like <see cref="DrawOrder"/>, but with each part of what sorts in parts (<see cref="ISortsInParts"/>, when the
    /// camera sorts by Y) in its own place among the rest, as the game draws them: the entity and the part (a tilemap's
    /// row of tiles), or null for the whole entity. An entity drawn in parts comes first at its own place, for what else
    /// it draws, then each of its parts.
    /// </summary>
    public static IReadOnlyList<(Entity Entity, int? Part)> DrawParts(Scene scene)
    {
        var sortByY = FindCamera(scene)?.GetComponent<Camera>()?.SortByY == true;
        var drawables = new List<IDrawable>();
        var entries = new List<Drawn>();
        var partOf = new List<int?>();
        foreach (var entity in scene.Entities)
        {
            if ((entity.Roles & ComponentRoles.Drawable) == 0 || !entity.IsActive || !Describe(entity, sortByY, drawables, null, out var entry))
                continue;
            entries.Add(entry);
            partOf.Add(null);
            if (!sortByY)
                continue;
            foreach (var component in entity.ComponentList)
            {
                if (component is not ISortsInParts parted)
                    continue;
                foreach (var (part, y) in parted.SortParts())
                {
                    entries.Add(entry with { Y = y });
                    partOf.Add(part);
                }
            }
        }

        var order = SortBackToFront(CollectionsMarshal.AsSpan(entries), sortByY, new StableSort());
        return order.IsEmpty
            ? entries.Select((d, i) => (d.Entity, partOf[i])).ToList()
            : order.ToArray().Select(at => (entries[at].Entity, partOf[at])).ToList();
    }

    /// <summary>
    /// The draw order of an entity: its sprite's, or else that of its first drawable component (in
    /// <see cref="IDrawable.DrawStep"/> order) with one; null if it draws nothing in the world.
    /// </summary>
    public static int? OrderOf(Entity entity)
    {
        int? order = null;
        var orderStep = int.MaxValue;
        if ((entity.Roles & ComponentRoles.Drawable) == 0)
            return null;
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
            if ((e.Roles & ComponentRoles.DrawEffect) == 0)
                continue;
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
    private static void DrawEntity(Entity entity, Sprite? sole, List<IDrawable> drawables, Drawing drawing, List<SpriteDraw> sprites, BuildTimer? timer)
    {
        if (sole is not null)
        {
            IDrawable drawable = sole;
            DrawEntity(entity, new ReadOnlySpan<IDrawable>(ref drawable), drawing, sprites, timer);
        }
        else
        {
            DrawEntity(entity, CollectionsMarshal.AsSpan(drawables), drawing, sprites, timer);
        }
    }

    private static void DrawEntity(Entity entity, ReadOnlySpan<IDrawable> drawables, Drawing drawing, List<SpriteDraw> sprites, BuildTimer? timer)
    {
        var parallax = ParallaxOf(entity);
        drawing.Entity = entity;
        drawing.CanCull = parallax is null;
        var end = drawables.Length;
        var i = 0;
        if (parallax is null)
        {
            var start = sprites.Count;
            for (; i < end && drawables[i].DrawStep < DrawSteps.View; i++)
                Draw(drawables[i], drawing, timer);
            MoveByEffects(entity, sprites, start);
            for (; i < end; i++)
                Draw(drawables[i], drawing, timer);
            return;
        }

        var shift = parallax.OffsetFor(drawing.View.Center);
        var by = Matrix4x4.CreateTranslation(shift.X, shift.Y, 0);
        var repeats = parallax.Entity == entity && (parallax.RepeatX || parallax.RepeatY);
        for (; i < end; i++)
        {
            var drawable = drawables[i];
            var from = sprites.Count;
            Draw(drawable, drawing, timer);
            if (drawable.DrawStep >= DrawSteps.View)
                continue;
            for (var d = from; d < sprites.Count; d++)
                sprites[d] = sprites[d] with { World = sprites[d].World * by };

            // Only the sprite repeats (all nine pieces of a sliced one, together); the entity's text and particles are drawn once.
            if (repeats && drawable is Sprite sprite && sprites.Count > from)
                Repeat(sprite, sprites, from, by, parallax, drawing.View);
        }
    }

    // A drawable component draws, timed as its kind while a profiler times the build.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Draw(IDrawable drawable, Drawing drawing, BuildTimer? timer)
    {
        timer?.Mark(drawable.GetType());
        drawable.Draw(drawing);
    }

    // The kind of thing an entity draws first, for a profiler: its sprite when that's all it draws, or else its drawable
    // component of the lowest step.
    private static Type LeadOf(Entity entity)
    {
        if (entity.SoleSprite is { } sole)
            return sole.GetType();
        Component? lead = null;
        var step = int.MaxValue;
        foreach (var component in entity.ComponentList)
        {
            if ((component.Roles & ComponentRoles.Drawable) != 0 && Unsafe.As<IDrawable>(component).DrawStep is var drawStep && drawStep < step)
                (lead, step) = (component, drawStep);
        }

        return lead?.GetType() ?? typeof(Entity);
    }

    // Moves the draws from `start` on by the effects of the entity and what it's inside.
    private static void MoveByEffects(Entity entity, List<SpriteDraw> sprites, int start)
    {
        if (sprites.Count == start)
            return;
        for (var e = entity; e is not null; e = e.Parent)
        {
            if ((e.Roles & ComponentRoles.DrawEffect) == 0)
                continue;
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
            if ((e.Roles & ComponentRoles.Parallax) != 0 && e.GetComponent<Parallax>() is { } parallax)
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
            if ((e.Roles & ComponentRoles.ScreenAnchor) != 0 || (e != entity && e == camera))
                return true;
        }

        return false;
    }

    // ----- Lights -----

    /// <summary>The lights in a scene that reach into the camera's view, with their flicker as it is now.</summary>
    public static IReadOnlyList<LightDraw> Lights(Scene scene, float aspect)
    {
        var camera = FindCamera(scene);
        var size = camera?.GetComponent<Camera>()?.Drawn(StepBlend.For(scene, camera)).Size ?? DefaultCameraSize;
        var lights = new List<LightDraw>();
        AddLights(scene, new Box(ViewCenter(camera), new Vector2(size * aspect, size)), lights, null);
        return lights;
    }

    /// <summary>
    /// Every light in the scene that gives off any light, as it is now (flicker included), with the shadows of those
    /// that cast them, and then the light other components give off (see <see cref="ILightSource"/>).
    /// </summary>
    public static IEnumerable<LightDraw> LightDraws(Scene scene)
    {
        var lights = new List<LightDraw>();
        AddLights(scene, null, lights, null);
        return lights;
    }

    // Adds the lights reaching into `view` (or all of them) to `lights`, as LightDraws gives them. With `scratch`, the
    // sides that block light are listed in its list and each light's shadows go in a list from it, so a dark scene's frame
    // makes no garbage; the shadows' lists come back to it with LetGoOf.
    private static void AddLights(Scene scene, Box? view, List<LightDraw> lights, Scratch? scratch)
    {
        List<ShadowEdge>? edges = null;
        foreach (var light in scene.ActiveNow<Light>())
        {
            // A light that grows, dims or flickers does it between steps too.
            var (center, radius, color, intensity, softness) = light.Drawn(StepBlend.For(scene, light.Entity));
            if (!(radius > 0) || !(intensity > 0) || !Reaches(view, center, radius))
                continue;
            var draw = new LightDraw(center, radius, new Color(color.R * intensity, color.G * intensity, color.B * intensity), softness);
            if (light.Shadows)
            {
                if (edges is null)
                {
                    edges = scratch?.Edges ?? [];
                    edges.Clear();
                    Shadows.AddEdges(scene, edges);
                }

                var casters = CastersList(scratch);
                Shadows.AddCasters(CollectionsMarshal.AsSpan(edges), light, casters);
                draw = draw with { Casters = casters };
            }

            lights.Add(draw);
        }

        // The scene's sides are kept no longer than they're needed: they hold on to the entities they belong to.
        if (edges is not null && scratch is not null)
            edges.Clear();

        var spots = scratch?.Spots ?? [];
        foreach (var source in scene.ActiveNow<ILightSource>())
        {
            spots.Clear();
            source.AddLights(spots);
            foreach (var spot in spots)
            {
                if (Reaches(view, spot.Center, spot.Radius))
                    lights.Add(new LightDraw(spot.Center, spot.Radius, spot.Color, spot.Softness));
            }
        }

        spots.Clear();

        // A light's reach overlaps the view (or there's no view).
        static bool Reaches(Box? view, Vector2 center, float radius)
        {
            if (view is not { } box)
                return true;
            var reach = box.HalfSize + new Vector2(radius);
            return MathF.Abs(center.X - box.Center.X) <= reach.X && MathF.Abs(center.Y - box.Center.Y) <= reach.Y;
        }
    }

    // A list for a light's shadows: one kept from a frame done with, or a new one.
    private static List<ShadowEdge> CastersList(Scratch? scratch)
    {
        if (scratch?.Casters is not { Count: > 0 } pool)
            return [];
        var casters = pool[^1];
        pool.RemoveAt(pool.Count - 1);
        return casters;
    }

    // Empties a frame's lights that it's done with, keeping their shadows' lists (emptied) to use again.
    private static void LetGoOf(List<LightDraw> lights)
    {
        var pool = (t_scratch ??= new Scratch()).Casters;
        foreach (var light in lights)
        {
            if (light.Casters is List<ShadowEdge> casters)
            {
                casters.Clear();
                pool.Add(casters);
            }
        }

        lights.Clear();
    }

    // ----- The screen -----

    // The scene change's cover, drawn the transition's way: the whole view fading, a wipe across it, or an iris closing.
    private static SpriteDraw? TransitionDraw(Runtime.Game game, float fade, SceneTransition transition, Box view, Matrix4x4 clipToWorld)
    {
        var amount = Math.Clamp(fade, 0, 1);
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
        // In the minigame that has the screen, if one has.
        var shown = MinigameRoot.Shown(scene);
        var player = scene.EntitiesNow.FirstOrDefault(e => e.Name == "Player" && e.IsActive && !MinigameRoot.IsBehind(e, shown))
                     ?? scene.EntitiesNow.FirstOrDefault(e => e.IsActive && PlayerMovementAttribute.IsOn(e) && !MinigameRoot.IsBehind(e, shown));
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
        return new Vector2(position.X, position.Y) + ShakeDrawn(camera);
    }

    // The camera's shake in the frame being drawn: between steps, like its moves.
    private static Vector2 ShakeDrawn(Entity camera) =>
        camera.GetComponent<Camera>()?.Drawn(StepBlend.For(camera.Scene, camera)).Shake ?? Vector2.Zero;

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

    /// <summary>
    /// The scene's main camera, which fills the screen: the first active camera without a <see cref="Viewport"/> (a
    /// viewport's camera draws into its own part of the screen), or with every camera in one, the first. While a minigame
    /// has the screen (see <see cref="MinigameRoot.Shown"/>), the first such camera in it.
    /// </summary>
    public static Entity? FindCamera(Scene scene)
    {
        var cameras = scene.ActiveNow<Camera>();
        if (cameras.Count == 0)
            return null;
        if (MinigameRoot.Shown(scene) is { } shown && FirstCamera(cameras, shown) is { } inside)
            return inside;
        return FirstCamera(cameras, null);

        static Entity? FirstCamera(ComponentList<Camera> cameras, Entity? within)
        {
            Entity? first = null;
            foreach (var camera in cameras)
            {
                var entity = camera.Entity!;
                if (within is not null && !entity.IsSelfOrDescendantOf(within))
                    continue;
                if ((entity.Roles & ComponentRoles.Viewport) == 0)
                    return entity;
                first ??= entity;
            }

            return first;
        }
    }

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
            var shake = ShakeDrawn(camera);
            var world = Matrix4x4.CreateFromQuaternion(transform.RotationQuaternionDrawn) *
                        Matrix4x4.CreateTranslation(transform.WorldPosition + new Vector3(shake, 0));
            Matrix4x4.Invert(world, out view);
        }

        var height = size * 2;
        var projection = Matrix4x4.CreateOrthographic(height * aspect, height, -1000f, 1000f);
        return view * projection;
    }
}
