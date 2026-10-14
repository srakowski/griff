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
        Build(game.Scene, aspect, game, recycle?.Sprites as List<SpriteDraw>);

    private static RenderFrame Build(Scene scene, float aspect, Runtime.Game? game, List<SpriteDraw>? reuse = null)
    {
        // While something profiles the game, each part of the build is timed (see FrameCostKind.Build).
        if (game?.Profiler is { } profiler)
            return BuildTimed(scene, aspect, game, reuse, profiler.BuildTimer);
        return Build(scene, aspect, game, reuse, null);
    }

    private static RenderFrame Build(Scene scene, float aspect, Runtime.Game? game, List<SpriteDraw>? reuse, BuildTimer? timer)
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

    private static RenderFrame BuildTimed(Scene scene, float aspect, Runtime.Game game, List<SpriteDraw>? reuse, BuildTimer timer)
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

    private static RenderFrame BuildNow(Scene scene, float aspect, Runtime.Game? game, List<SpriteDraw>? reuse, BuildTimer? timer)
    {
        var camera = FindCamera(scene);
        var component = camera?.GetComponent<Camera>();
        // A zoom and a fade of the background are drawn between steps, like the camera's moves.
        var drawn = component?.Drawn(StepBlend.For(scene, camera));
        var size = drawn?.Size ?? DefaultCameraSize;
        var view = new Box(ViewCenter(camera), new Vector2(size * aspect, size));
        var sprites = reuse ?? [];
        var screenCount = DrawScene(scene, view, size, game is not null, sprites, null, overWorld: true, timer, reuse: true);

        // The game's systems draw what the game knows of (a strategy game's fog and HUD), or see the world their own way.
        timer?.Mark(SystemsPart);
        var frame = new FrameDrawing(game, scene, camera, size, aspect, view, ViewProjection(camera, size, aspect), sprites, screenCount);
        if (game is not null)
        {
            // A system that sees the world its own way (first person) draws it first; every system then draws over it.
            var systems = game.Systems.ToArray();
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

        timer?.Mark(LightsPart);

        sprites = frame.Sprites;
        view = frame.View;
        var viewProjection = frame.ViewProjection;

        // What the game draws over the screen (its fades and flashes, and debug shapes) changes once a step too, so it's
        // drawn between the steps as well.
        var blend = frame.Blend;

        // In the dark, the world is drawn first and darkened; what's pinned to the screen goes over it untouched.
        var ambient = component?.AmbientDrawn(blend) ?? Color.White;
        var dark = component?.IsDark == true && frame.Lit;
        var litCount = sprites.Count;
        if (dark)
            litCount = PutScreenLast(sprites, camera);

        IReadOnlyList<LightDraw> lights = dark ? LightsIn(scene, view) : [];
        timer?.Mark(ScreenPart);

        if (game is not null && Matrix4x4.Invert(viewProjection, out var clipToWorld))
        {
            // Quads over the whole view: twice the unit quad in clip space, taken back into the world.
            var whole = Matrix4x4.CreateScale(2, 2, 1) * clipToWorld;
            if (game.ScreenFlashDrawn(blend) is { A: > 0 } flash)
                sprites.Add(new SpriteDraw(whole, null, flash, ScreenEntity));
            if (TransitionDraw(game, game.FadeDrawn(blend), component?.Transition ?? SceneTransition.Fade, view, clipToWorld) is { } transition)
                sprites.Add(transition);
        }

        if (game is { Debug.HasShapes: true })
            sprites.AddRange(DebugDraws(game.Debug.ShapesAt(blend), size));

        return new RenderFrame
        {
            Background = drawn?.Background ?? DefaultBackground,
            ViewProjection = viewProjection,
            Sprites = sprites,
            Camera = camera,
            Ambient = dark ? ambient : Color.White,
            Lights = lights,
            LitCount = dark ? litCount : sprites.Count,
            Effects = camera?.GetComponent<ScreenEffects>() is { IsActive: true } effects ? effects : null,
            Look = camera?.GetComponent<ScreenEffects>() is { IsActive: true } shown ? shown.Drawn(StepBlend.For(scene, camera)) : default,
        };
    }

    // Moves the draws pinned to the screen after the others, each keeping their order, in place (by way of the scratch's
    // spare, so a big dark scene's frame makes no garbage). Returns how many draws aren't pinned.
    private static int PutScreenLast(List<SpriteDraw> sprites, Entity? camera)
    {
        var scratch = t_scratch ??= new Scratch();
        var drawn = CollectionsMarshal.AsSpan(sprites);
        var (world, screen) = (0, 0);
        foreach (ref readonly var draw in drawn)
        {
            if (IsOnScreen(draw.Entity, camera))
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
            if (IsOnScreen(drawn[i].Entity, camera))
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
        BuildTimer? timer = null, bool reuse = false)
    {
        var scratch = t_scratch ?? new Scratch();
        t_scratch = null;
        try
        {
            return DrawScene(scene, view, cameraSize, playing, sprites, include, overWorld ? scratch.Overs : null, scratch, timer, reuse);
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
    }

    // One pass over the entities, in hierarchy order: each is drawn as it's found, its draws kept together where they
    // land, and then those runs of draws are put back to front by what their entities sort by. An entity's drawable
    // components are called in that order too (hierarchy order, then DrawStep order within the entity), whatever order
    // their draws end up in.
    // With a timer (a profiler's), each entity's own work counts to the kind of thing it draws first, and each drawable's
    // drawing to its own kind.
    private static int DrawScene(Scene scene, Box view, float cameraSize, bool playing, List<SpriteDraw> sprites, Func<Entity, bool>? include,
        List<IDrawable>? overs, Scratch scratch, BuildTimer? timer, bool reuse)
    {
        var sortByY = FindCamera(scene)?.GetComponent<Camera>()?.SortByY == true;
        if (include is null && scene.Entities.Count >= ChunkedFrom)
        {
            DrawChunked(scene, view, cameraSize, playing, sprites, reuse ? 0 : sprites.Count, overs, scratch, timer, sortByY);
        }
        else
        {
            scratch.Chunked?.LetGo();
            if (reuse)
            {
                timer?.Mark(ClearingPart);
                sprites.Clear();
            }

            DrawEach(scene, view, cameraSize, playing, sprites, include, overs, scratch, timer, sortByY);
        }

        return DrawOverWorld(scene, view, cameraSize, playing, sprites, overs, timer);
    }

    // The scene's entities one by one, on this thread.
    private static void DrawEach(Scene scene, Box view, float cameraSize, bool playing, List<SpriteDraw> sprites, Func<Entity, bool>? include,
        List<IDrawable>? overs, Scratch scratch, BuildTimer? timer, bool sortByY)
    {
        var drawables = scratch.Drawables;
        var entries = scratch.Entries;
        var first = sprites.Count;
        sprites.EnsureCapacity(first + scene.Entities.Count + 16);
        var drawing = new Drawing(sprites, ScreenEntity, view, cameraSize / 180, playing);
        foreach (var entity in scene.Entities)
        {
            if ((entity.Roles & ComponentRoles.Drawable) == 0 || !entity.IsActive || (include is not null && !include(entity)))
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

        timer?.Mark(SortingPart);
        PutBackToFront(sprites, first, CollectionsMarshal.AsSpan(entries), sortByY, scratch);
    }

    // What goes over the world (ropes, wires), after it and under what's pinned to the screen. Returns how many draws at
    // the end are pinned to the screen when it had to split them for that, or -1.
    private static int DrawOverWorld(Scene scene, Box view, float cameraSize, bool playing, List<SpriteDraw> sprites, List<IDrawable>? overs, BuildTimer? timer)
    {
        if (overs is not { Count: > 0 })
            return -1;

        // Over the world: lower steps first, in hierarchy order within a step.
        var over = new List<SpriteDraw>();
        var overDrawing = new Drawing(over, ScreenEntity, view, cameraSize / 180, playing);
        foreach (var drawable in overs.Select((d, i) => (d, i)).OrderBy(x => x.d.DrawStep).ThenBy(x => x.i).Select(x => x.d))
        {
            overDrawing.Entity = ((Component)drawable).Entity!;
            Draw(drawable, overDrawing, timer);
        }

        timer?.Mark(SortingPart);
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
            // A light that grows, dims or flickers does it between steps too.
            var (center, radius, color, intensity, softness) = light.Drawn(StepBlend.For(scene, light.Entity));
            if (!(radius > 0) || !(intensity > 0))
                continue;
            var draw = new LightDraw(center, radius, new Color(color.R * intensity, color.G * intensity, color.B * intensity), softness);
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
