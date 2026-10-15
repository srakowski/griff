using Griff.Art;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Art;
using Griff.Editor.Themes;
using Griff.Scenes;

namespace Griff.Editor.Features.Entities;

/// <summary>
/// Fits the selected entity's collider to its sprite: traces round what's solid in the frame its sprite shows (the biggest
/// blob of it, holes filled in) and makes that a Polygon Collider's points, up to 12 of them, hugging it without crossing.
/// An entity without a polygon collider gets one in place of its box or circle collider, keeping whether it's a trigger
/// and its layers. Drag the points in the Scene view to tidy it. One undo step.
/// </summary>
public sealed class FitPolygonColliderCommand(IWorkspace workspace, ISelection selection, EditorArt art) : EditorCommand
{
    /// <summary>The most points a fitted outline has.</summary>
    public const int MostPoints = 12;

    public override string Id => "Entity.FitPolygonCollider";
    public override string Text => "Fit _Polygon Collider to Sprite";
    public override string? Icon => Icons.Image;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 10);

    protected override bool CanExecuteCore() =>
        workspace.Scene is not null && selection.Entity?.GetComponent<Sprite>() is { Image.Length: > 0 };

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is not { } document || selection.Entity is not { } entity || entity.GetComponent<Sprite>() is not { Image.Length: > 0 } sprite)
            throw new InvalidOperationException("Select an entity with a sprite (with a picture) to fit a polygon collider to it.");
        if (art.Pixels(sprite.Image) is not var (pixels, width, height))
            throw new InvalidOperationException($"Couldn't read {sprite.Image}: is it in the project's Assets?");

        var rect = sprite.FrameRect;
        var frame = ((int)MathF.Round(rect.X * width), (int)MathF.Round(rect.Y * height), (int)MathF.Round(rect.Z * width), (int)MathF.Round(rect.W * height));
        var outline = SpriteOutline.Trace(pixels, width, height, frame, sprite.Size, sprite.FlipX, sprite.FlipY, MostPoints)
            ?? throw new InvalidOperationException($"Nothing in the frame {entity.Name}'s sprite shows is solid, so there's no outline to fit.");
        var points = PolygonCollider.FormatPoints(outline);

        using (document.History.BeginGroup("Fit Polygon Collider to Sprite"))
        {
            if (entity.GetComponent<PolygonCollider>() is { } polygon)
            {
                var property = workspace.Components.Get(typeof(PolygonCollider)).Properties.Single(p => p.Name == nameof(PolygonCollider.Points));
                document.Execute(new SetPropertyEdit(polygon, property, points));
            }
            else
            {
                // In place of the box or circle it had, as the same kind of thing (a trigger or not, on the same layers).
                var replaced = entity.Components.OfType<Collider>().Where(c => c is BoxCollider or CircleCollider).ToList();
                var first = replaced.FirstOrDefault();
                var fitted = new PolygonCollider
                {
                    Points = points,
                    IsTrigger = first?.IsTrigger ?? false,
                    Layer = first?.Layer,
                    IgnoreLayers = first?.IgnoreLayers,
                };
                var index = first is null ? (int?)null : entity.Components.ToList().IndexOf(first);
                foreach (var collider in replaced)
                    document.Execute(new RemoveComponentEdit(collider));
                document.Execute(new AddComponentEdit(entity, fitted, index));
            }
        }

        document.History.Seal();
        return Task.CompletedTask;
    }
}
