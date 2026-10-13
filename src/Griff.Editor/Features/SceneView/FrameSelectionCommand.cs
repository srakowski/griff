using Griff.Editor.Core.Commands;

namespace Griff.Editor.Features.SceneView;

/// <summary>Centers the Scene view on the selection (the middle of everything selected), or on the origin with nothing selected.</summary>
public sealed class FrameSelectionCommand(SceneViewport viewport) : EditorCommand
{
    public override string Id => "View.FrameSelection";
    public override string Text => "_Frame Selection";

    // F is handled by the Scene view itself, so typing an F elsewhere doesn't move it.
    public override string? GestureText => "F";
    public override MenuPlacement? Placement { get; } = new(Menus.View, Group: 0, Order: 0);

    protected override Task ExecuteCoreAsync()
    {
        viewport.FrameSelection();
        return Task.CompletedTask;
    }
}
