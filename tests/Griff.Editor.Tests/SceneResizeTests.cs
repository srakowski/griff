using System.Numerics;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Resizing sprites and colliders with the Scene view's Resize and Collider handles.</summary>
public class SceneResizeTests
{
    // Plank: a 1x1 sprite at 2,0 with a 1x1 box collider. Ball: a circle collider (radius 0.5) at 0,4, inside Holder at 1,1.
    private static (SceneDocument Document, ISelection Selection, SceneViewModel Model) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var plank = new Entity("Plank");
        plank.Transform.Position = new Vector3(2, 0, 0);
        plank.AddComponent<Sprite>();
        plank.AddComponent<BoxCollider>();
        scene.Add(plank);

        var holder = new Entity("Holder");
        holder.Transform.Position = new Vector3(1, 1, 0);
        scene.Add(holder);
        var ball = new Entity("Ball");
        ball.Transform.Position = new Vector3(0, 4, 0);
        ball.AddComponent<CircleCollider>();
        scene.Add(ball, holder);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<SceneViewModel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static void Near(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}.");

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}.");

    [Fact]
    public void Sprite_GrowsAroundItsPosition_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var plank = Named(document, "Plank");
        var sprite = plank.GetComponent<Sprite>()!;
        selection.Select(plank);

        var resize = model.BeginResize(sprite)!;
        // Drag the right edge (at 0.5) out to 1.252: the left edge goes out as far, and the plank and its collider stay.
        resize.Drag(new Vector2(1, 0), new Vector2(0.8f, 0.3f), proportional: false, snap: false);
        resize.Drag(new Vector2(1, 0), new Vector2(1.252f, 0.3f), proportional: false, snap: false);
        resize.End();
        Assert.Equal(new Vector2(2.5f, 1), sprite.Size);
        Assert.Equal(new Vector3(2, 0, 0), plank.Transform.Position);
        Assert.Equal(Vector2.One, plank.GetComponent<BoxCollider>()!.Size);
        Assert.Equal("Resize Sprite", document.History.UndoDescription);

        document.History.Undo();
        Assert.Equal(Vector2.One, sprite.Size);
        Assert.Equal(new Vector3(2, 0, 0), plank.Transform.Position);
        Assert.False(document.History.CanUndo);
    });

    [Fact]
    public void Sprite_FollowsItsEntitysTurnAndScale_AndSnaps() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var plank = Named(document, "Plank");
        var sprite = plank.GetComponent<Sprite>()!;
        // Turned a quarter and doubled: its own right is the world's up, two world units to each of its own.
        plank.Transform.Rotation = new Vector3(0, 0, 90);
        plank.Transform.Scale = new Vector3(2, 2, 1);
        selection.Select(plank);

        var resize = model.BeginResize(sprite)!;
        Near(new Vector2(2, 2), resize.Frame.ToWorld(new Vector2(1, 0)));
        Near(new Vector2(1, 0), resize.Frame.ToLocal(new Vector2(2, 2)));
        // The right edge out to 0.9 of its own units (world y 1.8), snapped to half units: 2 across.
        resize.Drag(new Vector2(1, 0), resize.Frame.ToLocal(new Vector2(2, 1.8f)), proportional: false, snap: true);
        resize.End();
        Assert.Equal(new Vector2(2, 1), sprite.Size);
        Assert.Equal(new Vector3(2, 0, 0), plank.Transform.Position);
    });

    [Fact]
    public void Sprite_CornerWithShift_KeepsProportions_AndNeverReachesZero() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var plank = Named(document, "Plank");
        var sprite = plank.GetComponent<Sprite>()!;
        sprite.Size = new Vector2(2, 1);
        selection.Select(plank);

        var resize = model.BeginResize(sprite)!;
        // The top-right corner pulled out (and a little off the diagonal) doubles it both ways.
        resize.Drag(new Vector2(1, 1), new Vector2(1.95f, 1.1f), proportional: true, snap: false);
        Assert.Equal(new Vector2(4, 2), sprite.Size);

        // Dragged past the middle it stops at a hundredth, rather than flipping.
        resize.Drag(new Vector2(1, 1), new Vector2(-5, -5), proportional: false, snap: false);
        Assert.Equal(new Vector2(0.01f, 0.01f), sprite.Size);
        resize.End();

        document.History.Undo();
        Assert.Equal(new Vector2(2, 1), sprite.Size);
        Assert.Equal(new Vector3(2, 0, 0), plank.Transform.Position);
    });

    [Fact]
    public void BoxCollider_ChangesItsSizeAndOffset_NotTheEntity() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var plank = Named(document, "Plank");
        var box = plank.GetComponent<BoxCollider>()!;
        // Colliders ignore rotation, and the scale stretches them: two world units to each of its own across.
        plank.Transform.Rotation = new Vector3(0, 0, 30);
        plank.Transform.Scale = new Vector3(2, 1, 1);
        selection.Select(plank);

        var resize = model.BeginResize(box)!;
        Near(new Vector2(2, 0), resize.Frame.AxisX);
        // The bottom edge down to -1.5: the top stays at 0.5, so it's twice as tall and its middle moves down.
        resize.Drag(new Vector2(0, -1), new Vector2(0, -1.5f), proportional: false, snap: false);
        resize.End();
        Assert.Equal(new Vector2(1, 2), box.Size);
        Assert.Equal(new Vector2(0, -0.5f), box.Offset);
        Assert.Equal(new Vector3(2, 0, 0), plank.Transform.Position);
        Assert.Equal(Vector2.One, plank.GetComponent<Sprite>()!.Size);
        Assert.Equal(-1.5f, Griff.Runtime.Physics.BoundsOf(box).Min.Y, 4);
        Assert.Equal("Resize BoxCollider", document.History.UndoDescription);
    });

    [Fact]
    public void A_box_collider_whose_body_rotates_is_resized_along_its_turned_sides() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var plank = Named(document, "Plank");
        var box = plank.GetComponent<BoxCollider>()!;
        plank.AddComponent<Body>().Rotates = true;
        plank.Transform.Rotation = new Vector3(0, 0, 90);
        selection.Select(plank);

        var resize = model.BeginResize(box)!;
        // Turned a quarter, its own X runs up the screen: its right edge (now its top) dragged out to 1.5 doubles its width,
        // its bottom staying where it was.
        Near(new Vector2(0, 1), resize.Frame.AxisX);
        resize.Drag(new Vector2(1, 0), new Vector2(1.5f, 0), proportional: false, snap: false);
        resize.End();
        Assert.Equal(new Vector2(2, 1), box.Size);
        Assert.Equal(new Vector2(0.5f, 0), box.Offset);
        Near(new Vector2(1.5f, 1.5f), box.WorldCorners.C);
        Near(new Vector2(2.5f, -0.5f), box.WorldCorners.A);
    });

    [Fact]
    public void CircleCollider_GrowsAroundItsMiddle_SnappingToQuarters() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var ball = Named(document, "Ball");
        var circle = ball.GetComponent<CircleCollider>()!;
        circle.Offset = new Vector2(1, 0);
        selection.Select(ball);

        var resize = model.BeginResize(circle)!;
        Assert.Equal(4, resize.Frame.Spots.Count);
        // Any handle: the radius is how far from the middle it's dragged.
        resize.Drag(new Vector2(0, 1), new Vector2(1, 0.8f), proportional: false, snap: true);
        Assert.Equal(0.75f, circle.Radius);
        resize.Drag(new Vector2(0, 1), new Vector2(1.333f, 1.6f), proportional: false, snap: false);
        Assert.Equal(1.63f, circle.Radius, 4);
        Assert.Equal(new Vector2(1, 0), circle.Offset);
        resize.End();

        document.History.Undo();
        Assert.Equal(0.5f, circle.Radius);
    });

    [Fact]
    public void ShapesFollowTheHandles_OnlyOnThePrimary() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var (plank, ball) = (Named(document, "Plank"), Named(document, "Ball"));
        selection.SelectEntities([ball, plank]);

        Assert.Equal(SceneHandles.Scale, model.Handles);
        Assert.Empty(model.ResizableShapes());
        model.Handles = SceneHandles.Resize;
        Assert.Equal(new Component[] { plank.GetComponent<Sprite>()! }, model.ResizableShapes());
        model.Handles = SceneHandles.Collider;
        Assert.Equal(new Component[] { plank.GetComponent<BoxCollider>()! }, model.ResizableShapes());

        // Only the primary selection's shapes resize.
        Assert.Null(model.BeginResize(ball.GetComponent<CircleCollider>()!));
        selection.Select(ball);
        model.Handles = SceneHandles.Resize;
        Assert.Empty(model.ResizableShapes());
    });

    [Fact]
    public void Commands_PickTheHandles_AndTickTheOneInUse() => EditorHost.Run(services =>
    {
        Open(services);
        var model = services.GetRequiredService<SceneViewModel>();
        var commands = services.GetRequiredService<CommandRegistry>();
        var (scale, resize, collider) = (commands.Get("View.ScaleHandles"), commands.Get("View.ResizeHandles"), commands.Get("View.ColliderHandles"));
        Assert.True(scale.IsChecked);

        collider.ExecuteAsync().GetAwaiter().GetResult();
        Assert.Equal(SceneHandles.Collider, model.Handles);
        Assert.True(collider.IsChecked);
        Assert.False(scale.IsChecked);
        resize.ExecuteAsync().GetAwaiter().GetResult();
        Assert.Equal(SceneHandles.Resize, model.Handles);
        scale.ExecuteAsync().GetAwaiter().GetResult();
        Assert.Equal(SceneHandles.Scale, model.Handles);
    });

    [Fact]
    public void Agents_ReachResizing() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var (plank, ball) = (Named(document, "Plank"), Named(document, "Ball"));
        selection.Select(plank);
        var surface = services.GetServices<Griff.Agent.IAgentToolSource>().OfType<SurfaceTools>().Single();
        var model = surface.All().Single(r => r.Name == nameof(SceneViewModel)).Target;

        // The sprite three wide, keeping its left edge; the box as tall as two, keeping its top.
        using (var args = System.Text.Json.JsonDocument.Parse("""["Sprite", [3, 1], [-1, 0]]"""))
            Griff.Agent.ObjectSurface.InvokeAsync(model, "ResizeShape", args.RootElement).GetAwaiter().GetResult();
        Assert.Equal(new Vector2(3, 1), plank.GetComponent<Sprite>()!.Size);
        Near(new Vector3(3, 0, 0), plank.Transform.Position);
        using (var args = System.Text.Json.JsonDocument.Parse("""["boxcollider", [1, 2], [0, 1]]"""))
            Griff.Agent.ObjectSurface.InvokeAsync(model, "ResizeShape", args.RootElement).GetAwaiter().GetResult();
        Near(new Vector2(0, -0.5f), plank.GetComponent<BoxCollider>()!.Offset);

        // A circle takes its diameter.
        selection.Select(ball);
        using (var args = System.Text.Json.JsonDocument.Parse("""["CircleCollider", [3, 3], [0, 0]]"""))
            Griff.Agent.ObjectSurface.InvokeAsync(model, "ResizeShape", args.RootElement).GetAwaiter().GetResult();
        Assert.Equal(1.5f, ball.GetComponent<CircleCollider>()!.Radius);

        // The handles' mode is a value agents set.
        using (var value = System.Text.Json.JsonDocument.Parse("\"Collider\""))
            Griff.Agent.ObjectSurface.Set(model, "Handles", value.RootElement);
        Assert.Equal(SceneHandles.Collider, services.GetRequiredService<SceneViewModel>().Handles);
    });
}
