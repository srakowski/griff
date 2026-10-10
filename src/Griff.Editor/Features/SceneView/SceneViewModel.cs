using System.Numerics;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Rendering;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Transform = Griff.Scenes.Transform;

namespace Griff.Editor.Features.SceneView;

/// <summary>
/// What the Scene view does with the selection: picking everything inside a box, and moving everything selected
/// together. The view's drags call these, and agents can too (positions are world units, Y up).
/// </summary>
public sealed class SceneViewModel(IWorkspace workspace, ISelection selection)
{
    /// <summary>The shape of a camera's frame, as the game's window is.</summary>
    public const double CameraAspect = 16.0 / 9.0;

    private const float SnapStep = 0.5f;

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
        if (workspace.Scene is not { } document)
            return [];
        var (min, max) = (Vector2.Min(from, to), Vector2.Max(from, to));
        bool Inside(Vector2 p) => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;

        return document.Scene.Entities
            .Where(e => SceneRenderer.IsActive(e) && !e.IsLockedInHierarchy)
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
    /// <paramref name="pivotRadius"/> world units. Locked entities are passed over, so the click reaches what's behind.
    /// </summary>
    public Entity? EntityAt(Vector2 world, float pivotRadius)
    {
        if (workspace.Scene is not { } document)
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

        return null;
    }

    /// <summary>Everything drawn, back to front: drawables in render order, then cameras and bare entities on top.</summary>
    public static IEnumerable<Entity> DrawOrder(Scene scene) =>
        SceneRenderer.DrawOrder(scene).Concat(scene.Entities.Where(e => SceneRenderer.OrderOf(e) is null && SceneRenderer.IsActive(e)));

    /// <summary>Moves everything selected by <paramref name="offset"/> world units, as one undo step.</summary>
    public void MoveSelection(Vector2 offset)
    {
        if (BeginMove() is not { } move)
            return;
        workspace.Scene!.History.Seal();
        move.To(offset, snap: false);
        move.End();
    }

    /// <summary>
    /// Starts moving the selection, for a drag: <see cref="SelectionMove.To"/> places everything relative to where it
    /// started, and the whole drag is one undo step. Null when nothing is selected.
    /// </summary>
    public SelectionMove? BeginMove()
    {
        if (workspace.Scene is not { } document)
            return null;
        // Children go along with a selected parent, so only the outermost ones move.
        var moving = selection.TopLevel().Where(e => e.Scene == document.Scene).ToList();
        if (moving.Count == 0)
            return null;
        // Snapping lines up the one picked last (or the selected parent it's inside); the rest keep their distance.
        var anchor = moving.FirstOrDefault(e => selection.Entity is { } primary && primary.IsSelfOrDescendantOf(e)) ?? moving[^1];
        var position = workspace.Components.Get(typeof(Transform)).Properties.Single(p => p.Name == nameof(Transform.Position));
        return new SelectionMove(document, moving, anchor, position);
    }

    /// <summary>World-space corners of what the entity draws (its sprite, tilemap or camera frame), or null if it draws none of those.</summary>
    public static Vector2[]? Outline(Entity entity)
    {
        Vector2 min, max;
        if (entity.GetComponent<Sprite>() is { } sprite)
            (min, max) = (-sprite.Size / 2, sprite.Size / 2);
        else if (entity.GetComponent<Tilemap>() is { Height: > 0 } tilemap)
            (min, max) = (new Vector2(0, -tilemap.Height * tilemap.TileSize), new Vector2(tilemap.Width * tilemap.TileSize, 0));
        else if (entity.GetComponent<Camera>() is { } camera)
        {
            max = new Vector2((float)(camera.Size * CameraAspect), camera.Size);
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
        /// nested entities follow the cursor). <paramref name="snap"/> puts the anchor on the half-unit grid.
        /// </summary>
        public void To(Vector2 offset, bool snap)
        {
            var targets = _moving.Select(m => m.Start + Vector3.TransformNormal(new Vector3(offset, 0), m.ParentInverse)).ToList();
            if (snap)
            {
                // Snap the anchor in its parent's space, then shift everything by the same amount in the world.
                var anchor = _moving[_anchor];
                var local = targets[_anchor];
                var snapped = new Vector3(Snap(local.X), Snap(local.Y), local.Z);
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
}
