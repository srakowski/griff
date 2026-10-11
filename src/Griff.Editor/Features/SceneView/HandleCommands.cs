using Griff.Editor.Core.Commands;

namespace Griff.Editor.Features.SceneView;

/// <summary>Switches what the Scene view's square handles on the selection do; the one in use is ticked.</summary>
public abstract class HandlesCommand(SceneViewModel model, SceneHandles handles) : EditorCommand
{
    public override bool? IsChecked => model.Handles == handles;

    protected override Task ExecuteCoreAsync()
    {
        model.Handles = handles;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Makes the Scene view's square handles scale the selection: corners scale both ways, edges one, around the entity
/// picked last. This is how the Scene view starts.
/// </summary>
public sealed class ScaleHandlesCommand(SceneViewModel model) : HandlesCommand(model, SceneHandles.Scale)
{
    public override string Id => "View.ScaleHandles";
    public override string Text => "_Scale Handles";

    // S, R and C are handled by the Scene view itself, as F is.
    public override string? GestureText => "S";
    public override MenuPlacement? Placement { get; } = new(Menus.View, Group: 1, Order: 0);
}

/// <summary>
/// Makes the Scene view's square handles resize the selected entity's sprite (its Size) around its position: drag an
/// edge to make a platform longer without stretching what's on it, as scaling would.
/// </summary>
public sealed class ResizeHandlesCommand(SceneViewModel model) : HandlesCommand(model, SceneHandles.Resize)
{
    public override string Id => "View.ResizeHandles";
    public override string Text => "_Resize Handles";
    public override string? GestureText => "R";
    public override MenuPlacement? Placement { get; } = new(Menus.View, Group: 1, Order: 1);
}

/// <summary>
/// Makes the Scene view's handles resize the selected entity's box and circle colliders: drag a box's edges and
/// corners (the opposite side stays put), or a circle's edge to change its radius.
/// </summary>
public sealed class ColliderHandlesCommand(SceneViewModel model) : HandlesCommand(model, SceneHandles.Collider)
{
    public override string Id => "View.ColliderHandles";
    public override string Text => "_Collider Handles";
    public override string? GestureText => "C";
    public override MenuPlacement? Placement { get; } = new(Menus.View, Group: 1, Order: 2);
}

/// <summary>
/// Puts round handles on the circles of the selected entity's distances, such as a light's radius, how far an enemy
/// notices the player, or a unit's sight: drag one to change how far it reaches.
/// </summary>
public sealed class DistanceHandlesCommand(SceneViewModel model) : HandlesCommand(model, SceneHandles.Distance)
{
    public override string Id => "View.DistanceHandles";
    public override string Text => "_Distance Handles";
    public override string? GestureText => "D";
    public override MenuPlacement? Placement { get; } = new(Menus.View, Group: 1, Order: 3);
}
