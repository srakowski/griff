using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// How a scene is seen, which a running game asks as it plays and the renderer reads as it builds a frame (see
/// Griff.Rendering's SceneRenderer): the main camera and how much of the world it shows, the view's projection, what's
/// pinned to the screen, the order things are drawn in (what's on top), and the lights reaching into the view. The
/// runtime asks it rather than the renderer, so a game runs without knowing how it's drawn; it keeps nothing between
/// calls.
/// </summary>
public static class Views
{
    public const float DefaultCameraSize = 5f;

    public static readonly Color DefaultBackground = Color.FromBytes(0x1E, 0x22, 0x2B);

    /// <summary>
    /// Stands in for the entity of draws that belong to the screen rather than the scene, like the scene-change fade.
    /// </summary>
    public static readonly Entity ScreenEntity = new("Screen");

    // One entity that draws: its sprite when that's all it draws (or else its drawable components, gathered in step order
    // apart), where its draws are in the frame's list once it's drawn (First, Count), and what it sorts by.
    internal readonly record struct Drawn(Entity Entity, Sprite? Sole, int First, int Count, bool Lifted, int Order, float Z, float Y);

    // Whether the entity draws in the world, and what it sorts by: its sprite when that's all it draws, or else its
    // drawable components gathered into `drawables` in step order (and those that go over the world into `overs`). Its
    // sprite's order is the entity's, or else that of its first drawable (by step) with one; with none it draws nothing
    // in the world.
    internal static bool Describe(Entity entity, bool sortByY, List<IDrawable> drawables, List<IDrawable>? overs, out Drawn entry)
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
    internal static float FeetOf(Sprite sprite, in Matrix4x4 world)
    {
        var size = sprite.Size;
        return world.M42 - (MathF.Abs(world.M12 * size.X) + MathF.Abs(world.M22 * size.Y)) / 2;
    }

    // Lifted ones last, then by Order, then Z, then (sorting by Y) higher up first, keeping hierarchy order among equals
    // (the order they come in): empty when that's the order they're in. A big scene is often in order already, all on one
    // layer, or nearly (a few things over or under its crowd): that's seen, and merged, in a look at each.
    internal static ReadOnlySpan<int> SortBackToFront(ReadOnlySpan<Drawn> entries, bool sortByY, StableSort keys)
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
    internal const int MaxStretches = 16;

    private readonly struct BackToFront : IComparer<Drawn>
    {
        public int Compare(Drawn a, Drawn b) => Views.Compare(a, b);
    }

    internal static int Compare(in Drawn a, in Drawn b)
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
    /// A draw order for a control made inside <paramref name="inside"/>: at least <paramref name="order"/>, and above
    /// anything drawn by it or the entities it's inside (a button made on a panel draws over the panel's backdrop).
    /// </summary>
    public static int OrderAbove(Entity? inside, int order)
    {
        for (var e = inside; e is not null; e = e.Parent)
        {
            if (OrderOf(e) is { } under)
                order = Math.Max(order, under + 10);
        }

        return order;
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

    // Adds the lights reaching into `view` (or all of them) to `lights`, as LightDraws gives them. With `lists`, the sides
    // that block light are listed in its list and each light's shadows go in a list from it, so a dark scene's frame makes
    // no garbage; the renderer gives the shadows' lists back to it once it's done with the frame.
    internal static void AddLights(Scene scene, Box? view, List<LightDraw> lights, LightLists? lists)
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
                    edges = lists?.Edges ?? [];
                    edges.Clear();
                    Shadows.AddEdges(scene, edges);
                }

                var casters = CastersList(lists);
                Shadows.AddCasters(CollectionsMarshal.AsSpan(edges), light, casters);
                draw = draw with { Casters = casters };
            }

            lights.Add(draw);
        }

        // The scene's sides are kept no longer than they're needed: they hold on to the entities they belong to.
        if (edges is not null && lists is not null)
            edges.Clear();

        var spots = lists?.Spots ?? [];
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
    private static List<ShadowEdge> CastersList(LightLists? lists)
    {
        if (lists?.Casters is not { Count: > 0 } pool)
            return [];
        var casters = pool[^1];
        pool.RemoveAt(pool.Count - 1);
        return casters;
    }

    /// <summary>The middle of what <paramref name="camera"/> sees (shaking, between steps), or the origin without one.</summary>
    internal static Vector2 ViewCenter(Entity? camera)
    {
        if (camera is null)
            return Vector2.Zero;
        var position = camera.Transform.WorldPosition;
        return new Vector2(position.X, position.Y) + ShakeDrawn(camera);
    }

    // The camera's shake in the frame being drawn: between steps, like its moves.
    private static Vector2 ShakeDrawn(Entity camera) =>
        camera.GetComponent<Camera>()?.Drawn(StepBlend.For(camera.Scene, camera)).Shake ?? Vector2.Zero;

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

    /// <summary>
    /// The lists the lights of a frame are worked out with (see <see cref="AddLights"/>), kept from frame to frame by
    /// whoever builds frames, so a dark scene's frame makes no garbage.
    /// </summary>
    internal sealed class LightLists
    {
        // Every side of what blocks light in the scene, while the lights are worked out.
        public List<ShadowEdge> Edges { get; } = [];

        // Lists for lights' shadows (see LightDraw.Casters), from frames done with, to use again.
        public List<List<ShadowEdge>> Casters { get; } = [];

        // The light an ILightSource gives off, while it's asked.
        public List<LightSpot> Spots { get; } = [];
    }
}
