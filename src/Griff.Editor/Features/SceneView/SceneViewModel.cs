using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Transform = Griff.Scenes.Transform;

namespace Griff.Editor.Features.SceneView;

/// <summary>What the Scene view's square handles on the primary selection do.</summary>
public enum SceneHandles
{
    /// <summary>Scale it: its transform's scale, and everything else selected with it.</summary>
    Scale,

    /// <summary>
    /// Resize its sprite (the sprite's size, around the entity's position) and the areas of its UI parts (a button's
    /// clickable area, a bar's or slider's size: sizes marked <see cref="ResizableAttribute"/>).
    /// </summary>
    Resize,

    /// <summary>Resize its box and circle colliders: a box's size and offset (its opposite side stays), a circle's radius.</summary>
    Collider,

    /// <summary>
    /// Drag its distances instead: round handles on the circles of its light radii, ranges and sight (see
    /// <see cref="SceneViewModel.Distances"/>).
    /// </summary>
    Distance,
}

/// <summary>
/// A distance on the primary selection that the Scene view draws as a circle: a number marked
/// <see cref="DistanceAttribute"/>, such as a light's radius or a unit's sight. <see cref="Center"/> is the circle's
/// middle in the world and <see cref="Value"/> its radius, in world units.
/// </summary>
public sealed record SceneDistance(Component Component, PropertyDescriptor Property, Vector2 Center, float Value)
{
    /// <summary>"Light.Radius", as <see cref="SceneViewModel.DragDistance"/> takes it.</summary>
    public string Key => $"{Component.GetType().Name}.{Property.Name}";

    /// <summary>What the Scene view and agents see: "Light.Radius = 4 around 1, 2".</summary>
    public string Label => FormattableString.Invariant($"{Key} = {Value:0.##} around {Center.X:0.##}, {Center.Y:0.##}");
}

/// <summary>
/// What the Scene view does with the selection: picking everything inside a box, moving, turning or scaling everything
/// selected together, resizing the primary selection's sprite and colliders, dragging its distances, the points of its
/// path (a mover's or grid patrol's points, a patrol's turns), its polygon collider's points and its hinge's anchor and
/// limits, painting the selected tilemap (cell by cell, a rectangle at a time or a paint-bucket fill), and the links it
/// draws to and from the entities the selection's fields name. The view's drags call these, and agents can too
/// (positions are world units, Y up).
/// </summary>
public sealed partial class SceneViewModel(IProjectSession projectSession, ISceneDocuments sceneDocuments, ISelection selection) : ObservableObject
{
    /// <summary>The shape of a camera's frame, as the game's window is.</summary>
    public const double CameraAspect = 16.0 / 9.0;

    /// <summary>The shape of a camera's view in the Scene view: the screen's, or a viewport's part of it.</summary>
    public static double AspectOf(Entity camera) => camera.GetComponent<Viewport>() is { } viewport ? viewport.AspectOn((float)CameraAspect) : CameraAspect;

    private const float SnapStep = 0.5f;

    private SceneHandles _handles;

    /// <summary>
    /// What the handles on the primary selection do: scale it (the default), resize its sprite, resize its box and
    /// circle colliders, or drag its distances (light radii, ranges, sight). View > Scale Handles, Resize Handles,
    /// Collider Handles and Distance Handles switch it.
    /// </summary>
    public SceneHandles Handles
    {
        get => _handles;
        set => SetProperty(ref _handles, value);
    }

    /// <summary>
    /// Selects every entity inside the box between two corners in the world: things that draw (sprites, tilemaps,
    /// cameras) when all of them is inside, other entities when their position is. <paramref name="add"/> keeps what
    /// was already selected. Returns the entities the box caught.
    /// </summary>
    public IReadOnlyList<Entity> SelectInBox(Vector2 from, Vector2 to, bool add = false)
    {
        var caught = EntitiesInBox(from, to);
        selection.SelectEntities(add ? selection.Entities.Concat(caught.Where(e => !selection.IsSelected(e))) : caught);
        return caught;
    }

    /// <summary>The entities a box between two world corners catches, in the Hierarchy's order (see <see cref="SelectInBox"/>).</summary>
    public IReadOnlyList<Entity> EntitiesInBox(Vector2 from, Vector2 to)
    {
        if (sceneDocuments.Scene is not { } document)
            return [];
        var (min, max) = (Vector2.Min(from, to), Vector2.Max(from, to));
        bool Inside(Vector2 p) => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;

        return document.Scene.Entities
            .Where(e => IsShown(e) && !e.IsLockedInHierarchy)
            .Where(e => Outline(e) is { } corners ? corners.All(Inside) : Inside(Flat(e.Transform.WorldPosition)))
            .ToList();
    }

    /// <summary>
    /// A click at a point in the world, as the Scene view takes one: selects the entity there (see
    /// <see cref="EntityAt"/>), or nothing on empty space. <paramref name="add"/> adds it to the selection or takes it
    /// out, as Ctrl-click does. Returns the entity clicked.
    /// </summary>
    public Entity? SelectAt(Vector2 world, bool add = false)
    {
        var hit = EntityAt(world, pivotRadius: 0.25f);
        if (hit is not null && add)
            selection.Toggle(hit);
        else if (hit is not null || !add)
            selection.Select(hit);
        return hit;
    }

    /// <summary>
    /// The topmost entity a click at <paramref name="world"/> picks, or null for empty space. Sprites, bars and sliders
    /// are hit by their area, tilemaps where they have a tile, and anything by its position within
    /// <paramref name="pivotRadius"/> world units; failing those, a polygon collider by its shape. Locked and hidden entities are passed over, so the click reaches what's
    /// behind.
    /// </summary>
    public Entity? EntityAt(Vector2 world, float pivotRadius)
    {
        if (sceneDocuments.Scene is not { } document)
            return null;

        foreach (var entity in DrawOrder(document.Scene).Reverse())
        {
            if (entity.IsLockedInHierarchy)
                continue;
            if (Vector2.Distance(Flat(entity.Transform.WorldPosition), world) <= pivotRadius)
                return entity;

            if (!Matrix4x4.Invert(entity.Transform.WorldMatrix, out var inverse))
                continue;
            var local = Vector3.Transform(new Vector3(world, 0), inverse);
            if (entity.GetComponent<Sprite>() is { } sprite && Math.Abs(local.X) <= sprite.Size.X / 2 && Math.Abs(local.Y) <= sprite.Size.Y / 2)
                return entity;
            if ((entity.GetComponent<Bar>()?.Size ?? entity.GetComponent<Slider>()?.Size) is { } bar &&
                Math.Abs(local.X) <= Math.Abs(bar.X) / 2 && Math.Abs(local.Y) <= Math.Abs(bar.Y) / 2)
                return entity;

            // Tilemaps are hit where there's a tile, so the empty parts don't block clicks on what's behind.
            if (entity.GetComponent<Tilemap>() is { } tilemap && tilemap.CellAt(new Vector2(local.X, local.Y)) is var (x, y) && tilemap.GetTile(x, y) >= 0)
                return entity;
        }

        // Inside a polygon collider (or another outline: a walk area, a hotspot) with nothing drawn there (a hill, a ramp, a
        // rock's outline drawn in the level, a room's floor), the polygon: after everything drawn, so a player standing in
        // front of it is still picked.
        foreach (var entity in DrawOrder(document.Scene).Reverse())
        {
            if (!entity.IsLockedInHierarchy && entity.Components.Any(c => c is IPointOutline { GivenPoints.Count: >= 3 } outline && outline.Contains(world)))
                return entity;
        }

        return null;
    }

    /// <summary>Everything drawn, back to front: drawables in render order, then cameras and bare entities on top.</summary>
    public static IEnumerable<Entity> DrawOrder(Scene scene) =>
        Views.DrawOrder(scene).Where(e => !e.IsHiddenInHierarchy)
            .Concat(scene.Entities.Where(e => Views.OrderOf(e) is null && IsShown(e)));

    /// <summary>
    /// <see cref="DrawOrder"/> with each row of a tilemap that sorts by Y in its own place, as the game draws them (see
    /// <see cref="Views.DrawParts"/>): the entity, and the row or null for the whole of it.
    /// </summary>
    public static IEnumerable<(Entity Entity, int? Depth)> DrawParts(Scene scene) =>
        Views.DrawParts(scene).Where(p => !p.Entity.IsHiddenInHierarchy)
            .Concat(scene.Entities.Where(e => Views.OrderOf(e) is null && IsShown(e)).Select(e => (e, (int?)null)));

    /// <summary>
    /// Whether the Scene view shows an entity: it's active (it and its parents enabled) and not hidden in the editor
    /// (see <see cref="Entity.IsHiddenInHierarchy"/>). The game shows hidden ones as usual.
    /// </summary>
    public static bool IsShown(Entity entity) => entity.IsActive && !entity.IsHiddenInHierarchy;

    /// <summary>Moves everything selected by <paramref name="offset"/> world units, as one undo step.</summary>
    public void MoveSelection(Vector2 offset)
    {
        if (BeginMove() is not { } move)
            return;
        sceneDocuments.Scene!.History.Seal();
        move.To(offset, snap: false);
        move.End();
    }

    /// <summary>
    /// Starts moving the selection, for a drag: <see cref="SelectionMove.To"/> places everything relative to where it
    /// started, and the whole drag is one undo step. Null when nothing is selected.
    /// </summary>
    public SelectionMove? BeginMove()
    {
        if (sceneDocuments.Scene is not { } document)
            return null;
        // Children go along with a selected parent, so only the outermost ones move.
        var moving = selection.TopLevel().Where(e => e.Scene == document.Scene).ToList();
        if (moving.Count == 0)
            return null;
        // Snapping lines up the one picked last (or the selected parent it's inside); the rest keep their distance.
        var anchor = moving.FirstOrDefault(e => selection.Entity is { } primary && primary.IsSelfOrDescendantOf(e)) ?? moving[^1];
        var position = projectSession.Components.Get(typeof(Transform)).Properties.Single(p => p.Name == nameof(Transform.Position));
        return new SelectionMove(document, moving, anchor, position);
    }

    /// <summary>
    /// Turns everything selected <paramref name="degrees"/> counterclockwise around the primary selection's position,
    /// as one undo step: each entity turns by that much and the others swing around with it. One entity just spins in
    /// place.
    /// </summary>
    public void RotateSelection(float degrees)
    {
        if (BeginRotate() is not { } rotate)
            return;
        sceneDocuments.Scene!.History.Seal();
        rotate.To(degrees, snap: false, round: false);
        rotate.End();
    }

    /// <summary>
    /// Starts turning the selection around the primary's position, for a drag of the rotate handle:
    /// <see cref="SelectionRotate.To"/> turns everything relative to where it started, and the whole drag is one undo
    /// step. Null when nothing is selected.
    /// </summary>
    public SelectionRotate? BeginRotate()
    {
        if (sceneDocuments.Scene is not { } document || selection.Entity is not { } primary || primary.Scene != document.Scene)
            return null;
        var turning = selection.TopLevel().Where(e => e.Scene == document.Scene).ToList();
        if (turning.Count == 0)
            return null;
        // Snapping lines up the one picked last (or the selected parent it's inside), as moving does.
        var anchor = turning.FirstOrDefault(e => primary.IsSelfOrDescendantOf(e)) ?? turning[^1];
        return new SelectionRotate(document, turning, anchor, Flat(primary.Transform.WorldPosition));
    }

    /// <summary>
    /// Scales everything selected by <paramref name="factor"/> (2, 1 doubles the width) along the primary selection's
    /// own axes, around its position, as one undo step: each entity's scale is multiplied by it and the others move
    /// toward or away from the primary with it. One entity just grows or shrinks in place.
    /// </summary>
    public void ScaleSelection(Vector2 factor)
    {
        if (BeginScale() is not { } scale)
            return;
        sceneDocuments.Scene!.History.Seal();
        scale.To(factor, snap: false, round: false);
        scale.End();
    }

    /// <summary>
    /// Starts scaling the selection around the primary's position, for a drag of a scale handle:
    /// <see cref="SelectionScale.To"/> scales everything relative to where it started, and the whole drag is one undo
    /// step. Null when nothing is selected.
    /// </summary>
    public SelectionScale? BeginScale()
    {
        if (sceneDocuments.Scene is not { } document || selection.Entity is not { } primary || primary.Scene != document.Scene)
            return null;
        var scaling = selection.TopLevel().Where(e => e.Scene == document.Scene).ToList();
        if (scaling.Count == 0)
            return null;
        // Rounding and snapping line up the one picked last (or the selected parent it's inside), as moving does.
        var anchor = scaling.FirstOrDefault(e => primary.IsSelfOrDescendantOf(e)) ?? scaling[^1];
        var world = primary.Transform.WorldMatrix;
        return new SelectionScale(document, scaling, anchor, Flat(primary.Transform.WorldPosition),
            Flat(Vector3.TransformNormal(Vector3.UnitX, world)), Flat(Vector3.TransformNormal(Vector3.UnitY, world)));
    }

    /// <summary>
    /// The primary selection's shapes the square handles resize in the current <see cref="Handles"/> mode: its sprite and
    /// the components with an area (Resize: a button's clickable area, a bar's size), or its box and circle colliders
    /// (Collider). Empty when scaling, or with nothing selected.
    /// </summary>
    public IReadOnlyList<Component> ResizableShapes()
    {
        if (sceneDocuments.Scene is not { } document || selection.Entity is not { } primary || primary.Scene != document.Scene)
            return [];
        return Handles switch
        {
            SceneHandles.Resize => [.. primary.Components.OfType<Sprite>().Take(1), .. primary.Components.Where(c => ShapeFrame.AreaOf(c) is not null)],
            SceneHandles.Collider => primary.Components.Where(c => c is BoxCollider or CircleCollider).ToList(),
            _ => [],
        };
    }

    /// <summary>
    /// Resizes a shape on the primary selection as one undo step: <paramref name="component"/> is "Sprite",
    /// "BoxCollider", "CircleCollider" or a component with an area ("Button", "Slider": see <see cref="ResizableAttribute"/>),
    /// its first one. <paramref name="size"/> is the new size in its own units, as
    /// the Inspector shows it (a circle's diameter, as its X); <paramref name="keep"/> is the point of it that stays
    /// where it is, from -1 to 1 along its own axes: (-1, 0) keeps the left edge, (0, 0) the middle. A box or circle
    /// keeps it by moving its offset; a sprite by moving its entity, and everything on it (its colliders and children)
    /// with it, so (0, 0) is usually what a sprite wants, as its handles do.
    /// </summary>
    public void ResizeShape(string component, Vector2 size, Vector2 keep)
    {
        var shape = selection.Entity?.Components.FirstOrDefault(c => (c is Sprite or BoxCollider or CircleCollider || ShapeFrame.AreaOf(c) is not null) &&
            string.Equals(c.GetType().Name, component.Trim(), StringComparison.OrdinalIgnoreCase));
        if (shape is null || BeginResize(shape) is not { } resize)
            return;
        sceneDocuments.Scene!.History.Seal();
        resize.To(size, keep);
        resize.End();
    }

    /// <summary>
    /// Starts resizing a sprite, box collider or circle collider on the primary selection, for a drag of one of its
    /// handles: <see cref="ShapeResize.Drag"/> resizes it from where it started, and the whole drag is one undo step.
    /// Null when it isn't the primary selection's, or its entity is squashed flat.
    /// </summary>
    public ShapeResize? BeginResize(Component shape)
    {
        if (sceneDocuments.Scene is not { } document || selection.Entity is not { } primary || primary.Scene != document.Scene ||
            shape.Entity != primary || ShapeFrame.Of(shape) is not { } frame)
            return null;

        PropertyDescriptor Property(Component c, string name) => projectSession.Components.Get(c.GetType()).Properties.Single(p => p.Name == name);
        if (ShapeFrame.AreaOf(shape) is var (sized, area))
            return new ShapeResize(document, shape, frame, Property(shape, sized.Name), area.Center is { } center ? Property(shape, center) : Property(primary.Transform, nameof(Transform.Position)));
        return shape switch
        {
            Sprite => new ShapeResize(document, shape, frame, Property(shape, nameof(Sprite.Size)), Property(primary.Transform, nameof(Transform.Position))),
            BoxCollider => new ShapeResize(document, shape, frame, Property(shape, nameof(BoxCollider.Size)), Property(shape, nameof(BoxCollider.Offset))),
            _ => new ShapeResize(document, shape, frame, Property(shape, nameof(CircleCollider.Radius)), Property(shape, nameof(CircleCollider.Offset))),
        };
    }

    /// <summary>
    /// The primary selection's distances the Scene view draws as circles, whatever <see cref="Handles"/> is out: every
    /// number marked <see cref="DistanceAttribute"/> on its components (scripts' too) that's above zero, and whose
    /// <see cref="DistanceAttribute.When"/> is set. In Distance mode each gets a handle.
    /// </summary>
    public IReadOnlyList<SceneDistance> Distances()
    {
        if (sceneDocuments.Scene is not { } document || selection.Entity is not { } primary || primary.Scene != document.Scene)
            return [];
        var distances = new List<SceneDistance>();
        foreach (var component in primary.Components)
        {
            var properties = projectSession.Components.Get(component.GetType()).Properties;
            foreach (var property in properties.Where(p => p.Distance is not null))
            {
                if (property.GetValue(component) is not float value || !(value > 0) || !float.IsFinite(value))
                    continue;
                var attribute = property.Distance!;
                if (attribute.When is { } when && !IsSet(properties.FirstOrDefault(p => p.Name == when)?.GetValue(component)))
                    continue;
                var center = attribute.Center is { } name && properties.FirstOrDefault(p => p.Name == name)?.GetValue(component) is Vector2 offset
                    ? Vector2.Transform(offset, primary.Transform.WorldMatrix)
                    : Flat(primary.Transform.WorldPosition);
                distances.Add(new SceneDistance(component, property, center, value));
            }
        }

        return distances;

        static bool IsSet(object? value) => value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            bool on => on,
            float number => number != 0,
            int number => number != 0,
            _ => true,
        };
    }

    /// <summary>
    /// Drags the handle of one of the primary selection's distances (see <see cref="Distances"/>), named like
    /// "Light.Radius" or "Unit.Sight", to a point in the world, as one undo step: the distance becomes how far that
    /// point is from the circle's middle, landing on hundredths (<paramref name="snap"/>: half units). Throws when the
    /// selection has no such distance showing.
    /// </summary>
    public void DragDistance(string distance, Vector2 to, bool snap = false)
    {
        var distances = Distances();
        var found = distances.FirstOrDefault(d => string.Equals(d.Key, distance.Trim(), StringComparison.OrdinalIgnoreCase));
        if (found is null)
        {
            throw new ArgumentException(distances.Count == 0
                ? $"The selection has no distance \"{distance}\" showing: select an entity with one above zero."
                : $"The selection has no distance \"{distance}\" showing; it has {string.Join(", ", distances.Select(d => d.Key))}.");
        }

        if (BeginDistance(found) is not { } drag)
            return;
        sceneDocuments.Scene!.History.Seal();
        drag.To(Vector2.Distance(found.Center, to), snap);
        drag.End();
    }

    /// <summary>
    /// Starts dragging one of the primary selection's distances, for a drag of its handle: <see cref="DistanceDrag.To"/>
    /// sets it, and the whole drag is one undo step. Null when it isn't the primary selection's.
    /// </summary>
    public DistanceDrag? BeginDistance(SceneDistance distance)
    {
        if (sceneDocuments.Scene is not { } document || selection.Entity is not { } primary || distance.Component.Entity != primary)
            return null;
        return new DistanceDrag(document, distance);
    }

    /// <summary>
    /// The selection's links the Scene view draws as arrows: each entity a selected entity's fields name (a switch's
    /// Targets, a camera's Follow target, a wire's From), and each field elsewhere in the scene naming a selected
    /// entity, from the field's entity to the one it names. Each field and entity it names is listed once.
    /// </summary>
    public IReadOnlyList<EntityReferences.Link> Links()
    {
        if (sceneDocuments.Scene is not { } document)
            return [];
        var selected = selection.Entities.Where(e => e.Scene == document.Scene).ToList();
        return selected.SelectMany(e => EntityReferences.From(e, projectSession.Components))
            .Concat(selected.SelectMany(e => EntityReferences.To(e, projectSession.Components)))
            .Distinct()
            .ToList();
    }

    /// <summary>World-space corners of what the entity draws (its sprite, tilemap or camera frame), or null if it draws none of those.</summary>
    public static Vector2[]? Outline(Entity entity)
    {
        Vector2 min, max;
        if (entity.GetComponent<Sprite>() is { } sprite)
            (min, max) = (-sprite.Size / 2, sprite.Size / 2);
        else if (entity.GetComponent<Tilemap>() is { Height: > 0 } tilemap)
            (min, max) = tilemap.Bounds();
        else if (entity.GetComponent<Camera>() is { } camera)
        {
            max = new Vector2((float)(camera.Size * AspectOf(entity)), camera.Size);
            min = -max;
        }
        else
            return null;

        var world = entity.Transform.WorldMatrix;
        return
        [
            Flat(Vector3.Transform(new Vector3(min.X, min.Y, 0), world)),
            Flat(Vector3.Transform(new Vector3(max.X, min.Y, 0), world)),
            Flat(Vector3.Transform(new Vector3(max.X, max.Y, 0), world)),
            Flat(Vector3.Transform(new Vector3(min.X, max.Y, 0), world)),
        ];
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);

    /// <summary>One drag of the selection, from <see cref="BeginMove"/>.</summary>
    public sealed class SelectionMove
    {
        private readonly SceneDocument _document;
        private readonly List<(Transform Transform, Vector3 Start, Matrix4x4 ParentInverse)> _moving;
        private readonly int _anchor;
        private readonly PropertyDescriptor _position;

        internal SelectionMove(SceneDocument document, IReadOnlyList<Entity> entities, Entity anchor, PropertyDescriptor position)
        {
            _document = document;
            _position = position;
            _anchor = entities.ToList().IndexOf(anchor);
            _moving = entities.Select(e => (e.Transform, e.Transform.Position,
                e.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse) ? inverse : Matrix4x4.Identity)).ToList();
            document.History.Seal();
        }

        /// <summary>
        /// Puts everything <paramref name="offset"/> world units from where it started (each in its parent's space, so
        /// nested entities follow the cursor). <paramref name="snap"/> puts the anchor on the half-unit grid; a tilemap that
        /// lines up with another (<see cref="Tilemap.LinesUpWith"/>) always lands on that map's cell corners.
        /// </summary>
        public void To(Vector2 offset, bool snap)
        {
            var targets = _moving.Select(m => m.Start + Vector3.TransformNormal(new Vector3(offset, 0), m.ParentInverse)).ToList();
            var lined = _moving[_anchor].Transform.Entity?.GetComponent<Tilemap>() is { GridMap: not null } map;
            if (snap || lined)
            {
                // Snap the anchor in its parent's space, then shift everything by the same amount in the world.
                var anchor = _moving[_anchor];
                var local = targets[_anchor];
                var snapped = lined ? anchor.Transform.Entity!.GetComponent<Tilemap>()!.LinedUp(local) : new Vector3(Snap(local.X), Snap(local.Y), local.Z);
                Matrix4x4.Invert(anchor.ParentInverse, out var parentWorld);
                var shift = Vector3.TransformNormal(snapped - local, parentWorld);
                targets = _moving.Select((m, i) => targets[i] + Vector3.TransformNormal(shift, m.ParentInverse)).ToList();
                targets[_anchor] = snapped;
            }

            if (_moving.Select((m, i) => m.Transform.Position == targets[i]).All(same => same))
                return;
            if (_moving.Count == 1)
                _document.Execute(new SetPropertyEdit(_moving[0].Transform, _position, targets[0]));
            else
                _document.Execute(new SetPositionsEdit(_moving.Select(m => m.Transform).ToList(), targets));
        }

        /// <summary>Ends the drag, so the next move is its own undo step.</summary>
        public void End() => _document.History.Seal();

        private static float Snap(float value) => MathF.Round(value / SnapStep) * SnapStep;
    }

    /// <summary>One drag of the rotate handle, from <see cref="BeginRotate"/>.</summary>
    public sealed class SelectionRotate
    {
        /// <summary>The angle Ctrl snaps to, in degrees.</summary>
        public const float SnapAngle = 15;

        private readonly SceneDocument _document;
        private readonly List<(Transform Transform, Vector3 Position, Vector3 Rotation, Vector3 World, Matrix4x4 ParentInverse, float Sign)> _turning;
        private readonly int _anchor;

        internal SelectionRotate(SceneDocument document, IReadOnlyList<Entity> entities, Entity anchor, Vector2 pivot)
        {
            _document = document;
            Pivot = pivot;
            _anchor = entities.ToList().IndexOf(anchor);
            _turning = entities.Select(e =>
            {
                var parent = e.Parent?.Transform.WorldMatrix ?? Matrix4x4.Identity;
                var inverse = Matrix4x4.Invert(parent, out var i) ? i : Matrix4x4.Identity;
                // Under a flipped parent, turning counterclockwise in the world is turning clockwise in the parent.
                var sign = parent.M11 * parent.M22 - parent.M12 * parent.M21 < 0 ? -1f : 1f;
                return (e.Transform, e.Transform.Position, e.Transform.Rotation, e.Transform.WorldPosition, inverse, sign);
            }).ToList();
            document.History.Seal();
        }

        /// <summary>The world point everything turns around.</summary>
        public Vector2 Pivot { get; }

        /// <summary>
        /// Turns everything <paramref name="degrees"/> counterclockwise from where it started. <paramref name="snap"/>
        /// puts the anchor's angle on a multiple of <see cref="SnapAngle"/>; otherwise <paramref name="round"/> puts it
        /// on a whole degree, so a drag leaves tidy numbers in the Inspector.
        /// </summary>
        public void To(float degrees, bool snap, bool round = true)
        {
            if (snap || round)
            {
                var anchor = _turning[_anchor];
                var angle = anchor.Rotation.Z + degrees * anchor.Sign;
                var step = snap ? SnapAngle : 1;
                degrees = (MathF.Round(angle / step) * step - anchor.Rotation.Z) * anchor.Sign;
            }

            var turn = Matrix3x2.CreateRotation(degrees * MathF.PI / 180, Pivot);
            var positions = _turning.Select(t =>
            {
                var world = Vector2.Transform(Flat(t.World), turn);
                var local = Vector3.Transform(new Vector3(world, t.World.Z), t.ParentInverse);
                // Whoever sits on the pivot stays exactly where it was, without float drift.
                return Vector2.Distance(Flat(t.World), Pivot) < 1e-6f ? t.Position : local;
            }).ToList();
            var rotations = _turning.Select(t => t.Rotation + new Vector3(0, 0, degrees * t.Sign)).ToList();

            if (_turning.Select((t, i) => t.Transform.Position == positions[i] && t.Transform.Rotation == rotations[i]).All(same => same))
                return;
            _document.Execute(new RotateEntitiesEdit(_turning.Select(t => t.Transform).ToList(), positions, rotations));
        }

        /// <summary>Ends the drag, so the next turn is its own undo step.</summary>
        public void End() => _document.History.Seal();
    }

    /// <summary>One drag of a scale handle, from <see cref="BeginScale"/>.</summary>
    public sealed class SelectionScale
    {
        /// <summary>The step Ctrl snaps the anchor's scale to.</summary>
        public const float SnapStep = 0.1f;

        private readonly SceneDocument _document;
        private readonly List<(Transform Transform, Vector3 Position, Vector3 Scale, Vector3 World, Matrix4x4 ParentInverse)> _scaling;
        private readonly int _anchor;

        internal SelectionScale(SceneDocument document, IReadOnlyList<Entity> entities, Entity anchor, Vector2 pivot, Vector2 axisX, Vector2 axisY)
        {
            _document = document;
            Pivot = pivot;
            // A squashed-flat or skewed-flat primary has no axes to scale along; fall back to the world's.
            var (x, y) = (Direction(axisX), Direction(axisY));
            (AxisX, AxisY) = MathF.Abs(x.X * y.Y - x.Y * y.X) < 1e-4f ? (Vector2.UnitX, Vector2.UnitY) : (x, y);
            _anchor = entities.ToList().IndexOf(anchor);
            _scaling = entities.Select(e => (e.Transform, e.Transform.Position, e.Transform.Scale, e.Transform.WorldPosition,
                e.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse) ? inverse : Matrix4x4.Identity)).ToList();
            document.History.Seal();
        }

        /// <summary>The world point everything scales around: the primary selection's position.</summary>
        public Vector2 Pivot { get; }

        /// <summary>The primary selection's own right in the world: the first factor scales along it.</summary>
        public Vector2 AxisX { get; }

        /// <summary>The primary selection's own up in the world: the second factor scales along it.</summary>
        public Vector2 AxisY { get; }

        /// <summary>A world point as distances from <see cref="Pivot"/> along <see cref="AxisX"/> and <see cref="AxisY"/>.</summary>
        public Vector2 Along(Vector2 world)
        {
            var offset = world - Pivot;
            var det = AxisX.X * AxisY.Y - AxisX.Y * AxisY.X;
            return new Vector2(offset.X * AxisY.Y - offset.Y * AxisY.X, AxisX.X * offset.Y - AxisX.Y * offset.X) / det;
        }

        /// <summary>
        /// Scales everything by <paramref name="factor"/> from where it started. <paramref name="snap"/> puts the
        /// anchor's scale on a multiple of <see cref="SnapStep"/>; otherwise <paramref name="round"/> puts it on a
        /// hundredth, so a drag leaves tidy numbers in the Inspector. An axis the factor leaves alone (1) isn't
        /// rounded, and no scale reaches zero.
        /// </summary>
        public void To(Vector2 factor, bool snap, bool round = true)
        {
            var start = _scaling[_anchor].Scale;
            Vector2? exact = null;
            if (snap || round)
            {
                var perUnit = snap ? 1 / SnapStep : 100;
                var fitted = new Vector2(Fit(start.X, factor.X, perUnit), Fit(start.Y, factor.Y, perUnit));
                factor = new Vector2(start.X == 0 ? factor.X : fitted.X / start.X, start.Y == 0 ? factor.Y : fitted.Y / start.Y);
                exact = fitted;
            }

            var positions = _scaling.Select(s =>
            {
                // Whoever sits on the pivot stays exactly where it was, without float drift.
                if (Vector2.Distance(Flat(s.World), Pivot) < 1e-6f)
                    return s.Position;
                var along = Along(Flat(s.World)) * factor;
                var world = Pivot + along.X * AxisX + along.Y * AxisY;
                return Vector3.Transform(new Vector3(world, s.World.Z), s.ParentInverse);
            }).ToList();
            var scales = _scaling.Select(s => s.Scale * new Vector3(factor, 1)).ToList();
            if (exact is { } anchorScale)
                scales[_anchor] = new Vector3(anchorScale, start.Z);

            if (_scaling.Select((s, i) => s.Transform.Position == positions[i] && s.Transform.Scale == scales[i]).All(same => same))
                return;
            _document.Execute(new ScaleEntitiesEdit(_scaling.Select(s => s.Transform).ToList(), positions, scales));
        }

        /// <summary>Ends the drag, so the next scale is its own undo step.</summary>
        public void End() => _document.History.Seal();

        // The scale an axis lands on: rounded to 1/perUnit, never zero (it keeps the sign it would have had), and left
        // as it is when the factor doesn't touch it.
        private static float Fit(float start, float factor, float perUnit)
        {
            var scaled = start * factor;
            if (factor == 1 || start == 0)
                return scaled;
            var fitted = MathF.Round(scaled * perUnit) / perUnit;
            return fitted != 0 ? fitted : MathF.CopySign(1 / perUnit, scaled == 0 ? start : scaled);
        }

        private static Vector2 Direction(Vector2 v) => v.LengthSquared() < 1e-12f ? Vector2.Zero : Vector2.Normalize(v);
    }

    /// <summary>
    /// Where a sprite, box collider or circle collider is, in its own units (as the Inspector shows its size): a point
    /// there is <see cref="Origin"/> plus so much of <see cref="AxisX"/> and <see cref="AxisY"/> in the world. A sprite
    /// turns and stretches with its entity; colliders stay upright, so theirs lie along the world's axes, unless they turn
    /// with a body that rotates (<see cref="Collider.Turns"/>).
    /// </summary>
    public readonly record struct ShapeFrame(Vector2 Origin, Vector2 AxisX, Vector2 AxisY, Vector2 Center, Vector2 Size, bool IsCircle)
    {
        private static readonly Vector2[] RectangleSpots =
            [new(-1, -1), new(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0)];

        private static readonly Vector2[] CircleSpots = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0)];

        /// <summary>
        /// The frame of a sprite, box collider or circle collider (a circle's size is its diameter, both ways), or null
        /// for anything else, or when its entity is squashed flat.
        /// </summary>
        public static ShapeFrame? Of(Component shape)
        {
            var area = AreaOf(shape);
            if (shape.Entity is not { } entity || shape is not (Sprite or BoxCollider or CircleCollider) && area is null)
                return null;
            var world = entity.Transform.WorldMatrix;
            var (axisX, axisY) = (Flat(Vector3.TransformNormal(Vector3.UnitX, world)), Flat(Vector3.TransformNormal(Vector3.UnitY, world)));
            // Colliders are boxes and circles along the world's axes, stretched by the scale (a circle by the larger).
            var scale = new Vector2(axisX.Length(), axisY.Length());
            var round = MathF.Max(scale.X, scale.Y);
            var origin = Flat(entity.Transform.WorldPosition);
            // A collider that turns with its body turns its axes (a circle's still both as long as its larger scale).
            var turns = shape is Collider { Turns: true } && scale.X > 0 && scale.Y > 0;
            var (turnedX, turnedY) = turns ? (axisX / scale.X, axisY / scale.Y) : (Vector2.UnitX, Vector2.UnitY);
            ShapeFrame frame = shape switch
            {
                _ when area is var (sized, marked) => marked.Upright
                    ? new(origin, new Vector2(scale.X, 0), new Vector2(0, scale.Y), MiddleOf(shape, marked), (Vector2)sized.GetValue(shape)!, false)
                    : new(origin, axisX, axisY, MiddleOf(shape, marked), (Vector2)sized.GetValue(shape)!, false),
                Sprite sprite => new(origin, axisX, axisY, Vector2.Zero, sprite.Size, false),
                BoxCollider box when turns => new(origin, axisX, axisY, box.Offset, box.Size, false),
                CircleCollider circle when turns => new(origin, turnedX * round, turnedY * round, circle.Offset, new Vector2(circle.Radius * 2), true),
                BoxCollider box => new(origin, new Vector2(scale.X, 0), new Vector2(0, scale.Y), box.Offset, box.Size, false),
                CircleCollider circle => new(origin, new Vector2(round, 0), new Vector2(0, round), circle.Offset, new Vector2(circle.Radius * 2), true),
                _ => default,
            };
            return MathF.Abs(frame.AxisX.X * frame.AxisY.Y - frame.AxisX.Y * frame.AxisY.X) < 1e-8f ? null : frame;
        }

        /// <summary>
        /// The size a component's handles resize when it isn't a sprite or collider: its first <see cref="Vector2"/> marked
        /// <see cref="ResizableAttribute"/> (a button's clickable area), or null for none.
        /// </summary>
        public static (System.Reflection.PropertyInfo Property, ResizableAttribute Area)? AreaOf(Component component) =>
            component is Sprite or Collider ? null : ComponentModel.Of(component).Resizable;

        /// <summary>
        /// True for a shape that grows around its middle as an edge is dragged (a sprite, an area centered on its entity),
        /// rather than keeping its opposite side where it is (a box, an area with its own center).
        /// </summary>
        public static bool GrowsAroundMiddle(Component shape) => shape is Sprite || AreaOf(shape) is { Area.Center: null };

        // Where an area's middle is from its entity: the point its Center property names, or the entity itself.
        private static Vector2 MiddleOf(Component shape, ResizableAttribute area) =>
            area.Center is { } name && ComponentModel.Of(shape).SavedProperty(name)?.GetValue(shape) is Vector2 center ? center : Vector2.Zero;

        /// <summary>
        /// Where its handles are, from -1 to 1 along its own axes: corners and edge middles for a rectangle, the four
        /// edges for a circle.
        /// </summary>
        public IReadOnlyList<Vector2> Spots => IsCircle ? CircleSpots : RectangleSpots;

        /// <summary>A world point in the shape's own units.</summary>
        public Vector2 ToLocal(Vector2 world)
        {
            var offset = world - Origin;
            var det = AxisX.X * AxisY.Y - AxisX.Y * AxisY.X;
            return new Vector2(offset.X * AxisY.Y - offset.Y * AxisY.X, AxisX.X * offset.Y - AxisX.Y * offset.X) / det;
        }

        /// <summary>A point in the shape's own units, in the world.</summary>
        public Vector2 ToWorld(Vector2 local) => Origin + local.X * AxisX + local.Y * AxisY;

        /// <summary>Where the handle at <paramref name="spot"/> is, in the shape's own units.</summary>
        public Vector2 HandleAt(Vector2 spot) => Center + spot * Size / 2;
    }

    /// <summary>One resize of a sprite, box collider or circle collider, from <see cref="BeginResize"/>.</summary>
    public sealed class ShapeResize
    {
        /// <summary>The step Ctrl snaps sizes to: half a unit (a circle's radius to a quarter, so it's half a unit across).</summary>
        public const float SnapStep = 0.5f;

        // Sizes never get smaller than this, and a drag leaves them on multiples of it.
        private const float RoundStep = 0.01f;

        private readonly SceneDocument _document;
        private readonly PropertyDescriptor _size;
        private readonly PropertyDescriptor _center;
        private readonly Vector3 _position;
        private readonly Matrix4x4 _parentInverse;

        internal ShapeResize(SceneDocument document, Component shape, ShapeFrame frame, PropertyDescriptor sizeProperty, PropertyDescriptor centerProperty)
        {
            _document = document;
            Shape = shape;
            Frame = frame;
            (_size, _center) = (sizeProperty, centerProperty);
            var entity = shape.Entity!;
            _position = entity.Transform.Position;
            _parentInverse = entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse) ? inverse : Matrix4x4.Identity;
            document.History.Seal();
        }

        /// <summary>The sprite or collider being resized.</summary>
        public Component Shape { get; }

        /// <summary>Where it was when the resize started; sizes and points are in its units.</summary>
        public ShapeFrame Frame { get; }

        private Vector2 Center => Frame.Center;

        private Vector2 Size => Frame.Size;

        /// <summary>
        /// Drags the handle at <paramref name="spot"/> to <paramref name="to"/> (in the shape's own units): an edge
        /// resizes one way, a corner both. A sprite grows around its entity's position (the opposite edge moves out as
        /// much), as typing a new size does, so nothing else on the entity moves; a box keeps its opposite side where it
        /// is, moving its offset; a circle grows or shrinks around its middle, whichever handle is dragged.
        /// <paramref name="proportional"/> keeps the shape's proportions; <paramref name="snap"/> puts the size on a
        /// multiple of <see cref="SnapStep"/>, and otherwise it lands on a hundredth.
        /// </summary>
        public void Drag(Vector2 spot, Vector2 to, bool proportional, bool snap)
        {
            var step = snap ? SnapStep : RoundStep;
            if (Shape is CircleCollider)
            {
                var radius = Fit((to - Center).Length(), snap ? SnapStep / 2 : RoundStep);
                Apply(new Vector2(radius * 2), Vector2.Zero);
                return;
            }

            // A sprite grows around its entity's position, so nothing else on the entity moves; a box keeps its opposite
            // side, moving its offset. An area does as a sprite does, unless it has a center of its own.
            var aroundMiddle = ShapeFrame.GrowsAroundMiddle(Shape);
            var pinned = aroundMiddle ? Center : Center - spot * Size / 2;
            var line = aroundMiddle ? spot * Size / 2 : spot * Size;
            Vector2 size;
            if (proportional)
            {
                // How far along the line from the pinned point to the handle the cursor is, fitted on the axis it leads
                // with (X, unless it's the top or bottom edge); the other axis follows.
                if (line.LengthSquared() < 1e-12f || MathF.Abs(spot.X != 0 ? Size.X : Size.Y) < 1e-6f)
                    return;
                var factor = Vector2.Dot(to - pinned, line) / line.LengthSquared();
                if (spot.X != 0)
                {
                    var width = Fit(Size.X * factor, step);
                    size = new Vector2(width, Fit(Size.Y * width / Size.X, RoundStep));
                }
                else
                {
                    var height = Fit(Size.Y * factor, step);
                    size = new Vector2(Fit(Size.X * height / Size.Y, RoundStep), height);
                }
            }
            else
            {
                var reach = (to - pinned) * spot * (aroundMiddle ? 2 : 1);
                size = new Vector2(spot.X == 0 ? Size.X : Fit(reach.X, step), spot.Y == 0 ? Size.Y : Fit(reach.Y, step));
            }

            Apply(size, aroundMiddle ? Vector2.Zero : -spot);
        }

        /// <summary>
        /// Resizes the shape to <paramref name="size"/> (in its own units; a circle's diameter as X), keeping the point
        /// <paramref name="keep"/> of it (from -1 to 1 along its own axes) where it is. Sizes never reach zero.
        /// </summary>
        public void To(Vector2 size, Vector2 keep)
        {
            size = Vector2.Max(size, new Vector2(RoundStep));
            Apply(Shape is CircleCollider ? new Vector2(size.X) : size, Vector2.Clamp(keep, -Vector2.One, Vector2.One));
        }

        /// <summary>Ends the drag, so the next resize is its own undo step.</summary>
        public void End() => _document.History.Seal();

        private void Apply(Vector2 size, Vector2 keep)
        {
            var center = Center + keep * (Size - size) / 2;
            List<(Component, PropertyDescriptor, object?)> values = Shape switch
            {
                // A sprite (or an area with no center) is always around its entity's position, so keeping a side moves the entity.
                _ when ShapeFrame.GrowsAroundMiddle(Shape) => [(Shape, _size, size), (Shape.Entity!.Transform, _center,
                    _position + Vector3.TransformNormal(new Vector3(Frame.ToWorld(center) - Frame.ToWorld(Center), 0), _parentInverse))],
                CircleCollider => [(Shape, _size, size.X / 2), (Shape, _center, center)],
                _ => [(Shape, _size, size), (Shape, _center, center)],
            };
            if (values.All(v => Equals(v.Item2.GetValue(v.Item1), v.Item3)))
                return;
            _document.Execute(new SetPropertyGroupEdit($"Resize {Shape.GetType().Name}", values));
        }

        // A size on a multiple of the step, and at least one step.
        private static float Fit(float value, float step)
        {
            var perUnit = MathF.Round(1 / step);
            return MathF.Max(step, MathF.Round(value * perUnit) / perUnit);
        }
    }

    /// <summary>One drag of a distance's handle, from <see cref="BeginDistance"/>.</summary>
    public sealed class DistanceDrag
    {
        /// <summary>The step Ctrl snaps distances to: half a unit.</summary>
        public const float SnapStep = 0.5f;

        // Distances never get smaller than this (zero often means "anywhere"), and a drag leaves them on multiples of it.
        private const float RoundStep = 0.01f;

        private readonly SceneDocument _document;

        internal DistanceDrag(SceneDocument document, SceneDistance distance)
        {
            _document = document;
            Distance = distance;
            document.History.Seal();
        }

        /// <summary>The distance being dragged, as it was when the drag started.</summary>
        public SceneDistance Distance { get; }

        /// <summary>
        /// Sets the distance to <paramref name="value"/> world units, on a hundredth, or with <paramref name="snap"/> a
        /// multiple of <see cref="SnapStep"/>; never less than a hundredth.
        /// </summary>
        public void To(float value, bool snap)
        {
            var step = snap ? SnapStep : RoundStep;
            var perUnit = MathF.Round(1 / step);
            var fitted = MathF.Max(step, MathF.Round(value * perUnit) / perUnit);
            if (Equals(Distance.Property.GetValue(Distance.Component), fitted))
                return;
            _document.Execute(new SetPropertyEdit(Distance.Component, Distance.Property, fitted));
        }

        /// <summary>Ends the drag, so the next change is its own undo step.</summary>
        public void End() => _document.History.Seal();
    }
}
