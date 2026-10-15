using System.Numerics;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Scenes;

namespace Griff.Editor.Features.Entities;

/// <summary>
/// Adds a rope to the selected entity: with two entities selected, tied from the middle of the one picked last to the middle
/// of the one picked first (a little longer than they're apart, so it sags); one that moves (has a Body), hung from a hook in
/// the world 3 above its top; one that doesn't (a ceiling, a branch), hanging loose 3 down from its bottom, to climb and
/// swing on. Drag its ends' knobs and the one at its middle (its length) in the Scene view. One undo step.
/// </summary>
public sealed class AddRopeCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    // How far a rope hangs, or a body hangs below its hook, as added.
    private const float Drop = 3;

    public override string Id => "Entity.AddRope";
    public override string Text => "Add _Rope";
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 12);

    protected override bool CanExecuteCore() => workspace.Scene is not null && selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is not { } document || selection.Entity is not { } entity)
            throw new InvalidOperationException("Select the entity to tie a rope to (and, to tie it to another, pick that one first).");
        var other = selection.Entities.Count > 1 ? selection.Entities[^2] : null;
        var rope = new Rope();
        if (other is not null)
        {
            var (from, to) = (Middle(entity), Middle(other));
            (rope.To, rope.Anchor, rope.OtherAnchor) = (other.Name, Own(entity, from), Own(other, to));
            rope.Length = Round(Math.Max(0.5f, Vector2.Distance(from, to) * 1.15f));
        }
        else if (entity.GetComponent<Body>() is not null)
        {
            var top = Edge(entity, up: true);
            (rope.Anchor, rope.OtherAnchor, rope.Length) = (Own(entity, top), top + new Vector2(0, Drop), Drop);
        }
        else
        {
            var bottom = Edge(entity, up: false);
            (rope.Anchor, rope.OtherAnchor, rope.LooseEnd, rope.Length) = (Own(entity, bottom), bottom - new Vector2(0, Drop), true, Drop);
        }

        using (document.History.BeginGroup("Add Rope"))
            document.Execute(new AddComponentEdit(entity, rope));
        document.History.Seal();
        return Task.CompletedTask;
    }

    // The middle of what an entity is made of (its collider), or where it is.
    private static Vector2 Middle(Entity entity) =>
        entity.GetComponent<Collider>() is { } collider ? collider.WorldBounds.Center : Flat(entity.Transform.WorldPosition);

    // The middle of the top (or bottom) of what an entity is made of, or where it is.
    private static Vector2 Edge(Entity entity, bool up)
    {
        if (entity.GetComponent<Collider>() is not { } collider)
            return Flat(entity.Transform.WorldPosition);
        var bounds = collider.WorldBounds;
        return bounds.Center + new Vector2(0, up ? bounds.HalfSize.Y : -bounds.HalfSize.Y);
    }

    // A point in the world in an entity's own units, on hundredths.
    private static Vector2 Own(Entity entity, Vector2 world)
    {
        var own = Matrix4x4.Invert(entity.Transform.WorldMatrix, out var inverse) ? Vector2.Transform(world, inverse) : world;
        return new Vector2(Round(own.X), Round(own.Y));
    }

    private static float Round(float value) => MathF.Round(value * 100) / 100 + 0f;

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
}
