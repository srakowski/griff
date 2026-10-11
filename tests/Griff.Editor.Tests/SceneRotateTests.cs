using System.Numerics;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Turning the selection with the Scene view's rotate handle.</summary>
public class SceneRotateTests
{
    // Sprites one unit square: A at 0,0 and B at 3,0; Parent at 10,0 holding Child one unit to its right; Mirror, flipped
    // left to right at -10,0, holding Reflection one unit to its right.
    private static (SceneDocument Document, ISelection Selection, SceneViewModel Model) Open(IServiceProvider services)
    {
        var scene = new Scene();
        Entity Add(string name, float x, float y, Entity? parent = null)
        {
            var entity = new Entity(name);
            entity.Transform.Position = new Vector3(x, y, 0);
            entity.AddComponent<Sprite>().Size = Vector2.One;
            scene.Add(entity, parent);
            return entity;
        }

        Add("A", 0, 0);
        Add("B", 3, 0);
        var parent = Add("Parent", 10, 0);
        Add("Child", 1, 0, parent);
        var mirror = Add("Mirror", -10, 0);
        mirror.Transform.Scale = new Vector3(-1, 1, 1);
        Add("Reflection", 1, 0, mirror);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<SceneViewModel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}.");

    [Fact]
    public void One_SpinsInPlace_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var b = Named(document, "B");
        selection.Select(b);

        model.RotateSelection(30);
        Assert.Equal(new Vector3(0, 0, 30), b.Transform.Rotation);
        Assert.Equal(new Vector3(3, 0, 0), b.Transform.Position);
        Assert.Equal("Rotate B", document.History.UndoDescription);

        // Each call is its own step.
        model.RotateSelection(-45);
        Assert.Equal(new Vector3(0, 0, -15), b.Transform.Rotation);
        document.History.Undo();
        Assert.Equal(new Vector3(0, 0, 30), b.Transform.Rotation);
        document.History.Undo();
        Assert.Equal(Vector3.Zero, b.Transform.Rotation);
    });

    [Fact]
    public void Several_TurnAroundThePrimary_ChildrenWithTheirParent() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var (a, b, parent, child) = (Named(document, "A"), Named(document, "B"), Named(document, "Parent"), Named(document, "Child"));
        // A is picked last, so everything turns around it. Child is inside Parent, so it rides along.
        selection.SelectEntities([b, parent, child, a]);

        model.RotateSelection(90);
        Assert.Equal(Vector3.Zero, a.Transform.Position);
        Near(new Vector3(0, 3, 0), b.Transform.Position);
        Near(new Vector3(0, 10, 0), parent.Transform.Position);
        Assert.Equal(new Vector3(0, 0, 90), parent.Transform.Rotation);
        Assert.Equal(new Vector3(1, 0, 0), child.Transform.Position);
        Assert.Equal(Vector3.Zero, child.Transform.Rotation);
        Near(new Vector3(0, 11, 0), child.Transform.WorldPosition);
        Assert.Equal("Rotate 3 entities", document.History.UndoDescription);

        document.History.Undo();
        Assert.Equal(new Vector3(3, 0, 0), b.Transform.Position);
        Assert.Equal(Vector3.Zero, parent.Transform.Rotation);
        Assert.False(document.History.CanUndo);
    });

    [Fact]
    public void Drag_IsOneUndoStep_RoundsToWholeDegrees_AndSnapsTheAnchor() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var (a, b) = (Named(document, "A"), Named(document, "B"));
        a.Transform.Rotation = new Vector3(0, 0, 7);
        selection.SelectEntities([b, a]);

        var rotate = model.BeginRotate()!;
        Assert.Equal(Vector2.Zero, rotate.Pivot);
        rotate.To(10.4f, snap: false);
        Assert.Equal(new Vector3(0, 0, 17), a.Transform.Rotation);
        Assert.Equal(new Vector3(0, 0, 10), b.Transform.Rotation);

        // Snapping puts A on 15 degrees; B turns by the same amount.
        rotate.To(10.4f, snap: true);
        Assert.Equal(new Vector3(0, 0, 15), a.Transform.Rotation);
        Assert.Equal(new Vector3(0, 0, 8), b.Transform.Rotation);

        // Going round more than once keeps counting.
        rotate.To(400, snap: false);
        Assert.Equal(new Vector3(0, 0, 407), a.Transform.Rotation);
        rotate.End();

        document.History.Undo();
        Assert.Equal(new Vector3(0, 0, 7), a.Transform.Rotation);
        Assert.Equal(new Vector3(3, 0, 0), b.Transform.Position);
        Assert.False(document.History.CanUndo);
    });

    [Fact]
    public void UnderAFlippedParent_TurnsTheWayTheHandleGoes() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var reflection = Named(document, "Reflection");
        selection.Select(reflection);

        // Counterclockwise on screen is clockwise inside the mirrored parent.
        model.RotateSelection(30);
        Assert.Equal(new Vector3(0, 0, -30), reflection.Transform.Rotation);
        var up = Vector3.TransformNormal(Vector3.UnitY, reflection.Transform.WorldMatrix);
        Near(new Vector3(-MathF.Sin(MathF.PI / 6), MathF.Cos(MathF.PI / 6), 0), up);
    });

    [Fact]
    public void NothingSelected_TurnsNothing() => EditorHost.Run(services =>
    {
        var (document, _, model) = Open(services);
        Assert.Null(model.BeginRotate());
        model.RotateSelection(45);
        Assert.False(document.History.CanUndo);
    });

    [Fact]
    public void Agents_ReachRotating() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        selection.SelectEntities([Named(document, "B"), Named(document, "A")]);
        var surface = services.GetServices<Griff.Agent.IAgentToolSource>().OfType<SurfaceTools>().Single();
        var model = surface.All().Single(r => r.Name == nameof(SceneViewModel)).Target;

        using (var degrees = System.Text.Json.JsonDocument.Parse("[180]"))
            Griff.Agent.ObjectSurface.InvokeAsync(model, "RotateSelection", degrees.RootElement).GetAwaiter().GetResult();
        Near(new Vector3(-3, 0, 0), Named(document, "B").Transform.Position);
        Assert.Equal(new Vector3(0, 0, 180), Named(document, "A").Transform.Rotation);
    });
}
