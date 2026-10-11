using System.Numerics;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Scaling the selection with the Scene view's scale handles.</summary>
public class SceneScaleTests
{
    // Sprites one unit square: A at 0,0 and B at 3,0; Parent at 10,0 holding Child one unit to its right.
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

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<SceneViewModel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}.");

    [Fact]
    public void One_GrowsInPlace_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var b = Named(document, "B");
        selection.Select(b);

        model.ScaleSelection(new Vector2(2, 1));
        Assert.Equal(new Vector3(2, 1, 1), b.Transform.Scale);
        Assert.Equal(new Vector3(3, 0, 0), b.Transform.Position);
        Assert.Equal("Scale B", document.History.UndoDescription);

        // Each call is its own step.
        model.ScaleSelection(new Vector2(1, 0.5f));
        Assert.Equal(new Vector3(2, 0.5f, 1), b.Transform.Scale);
        document.History.Undo();
        Assert.Equal(new Vector3(2, 1, 1), b.Transform.Scale);
        document.History.Undo();
        Assert.Equal(Vector3.One, b.Transform.Scale);
    });

    [Fact]
    public void Several_ScaleAroundThePrimary_ChildrenWithTheirParent() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var (a, b, parent, child) = (Named(document, "A"), Named(document, "B"), Named(document, "Parent"), Named(document, "Child"));
        // A is picked last, so everything scales around it. Child is inside Parent, so it rides along.
        selection.SelectEntities([b, parent, child, a]);

        model.ScaleSelection(new Vector2(2, 3));
        Assert.Equal(Vector3.Zero, a.Transform.Position);
        Assert.Equal(new Vector3(2, 3, 1), a.Transform.Scale);
        Near(new Vector3(6, 0, 0), b.Transform.Position);
        Near(new Vector3(20, 0, 0), parent.Transform.Position);
        Assert.Equal(new Vector3(2, 3, 1), parent.Transform.Scale);
        Assert.Equal(new Vector3(1, 0, 0), child.Transform.Position);
        Assert.Equal(Vector3.One, child.Transform.Scale);
        Near(new Vector3(22, 0, 0), child.Transform.WorldPosition);
        Assert.Equal("Scale 3 entities", document.History.UndoDescription);

        document.History.Undo();
        Assert.Equal(new Vector3(3, 0, 0), b.Transform.Position);
        Assert.Equal(Vector3.One, parent.Transform.Scale);
        Assert.False(document.History.CanUndo);
    });

    [Fact]
    public void Drag_IsOneUndoStep_RoundsTheAnchor_SnapsIt_AndNeverReachesZero() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var (a, b) = (Named(document, "A"), Named(document, "B"));
        a.Transform.Scale = new Vector3(1.5f, 1.234f, 1);
        selection.SelectEntities([b, a]);

        var scale = model.BeginScale()!;
        Assert.Equal(Vector2.Zero, scale.Pivot);
        // A lands on a hundredth; Y, which the drag leaves alone, keeps its odd value. B scales by the same amount.
        scale.To(new Vector2(1.2345f, 1), snap: false);
        Assert.Equal(new Vector3(1.85f, 1.234f, 1), a.Transform.Scale);
        Near(new Vector3(1.85f / 1.5f, 1, 1), b.Transform.Scale);
        Near(new Vector3(3 * 1.85f / 1.5f, 0, 0), b.Transform.Position);

        // Snapping puts A on a tenth.
        scale.To(new Vector2(1.2345f, 1), snap: true);
        Assert.Equal(1.9f, a.Transform.Scale.X);

        // Shrinking right down stops short of zero; going past the pivot flips.
        scale.To(new Vector2(0.001f, 1), snap: false);
        Assert.Equal(0.01f, a.Transform.Scale.X);
        scale.To(new Vector2(-1, 1), snap: false);
        Assert.Equal(-1.5f, a.Transform.Scale.X);
        scale.End();

        document.History.Undo();
        Assert.Equal(new Vector3(1.5f, 1.234f, 1), a.Transform.Scale);
        Assert.Equal(new Vector3(3, 0, 0), b.Transform.Position);
        Assert.False(document.History.CanUndo);
    });

    [Fact]
    public void ScalesAlongThePrimarysOwnAxes() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var (a, b) = (Named(document, "A"), Named(document, "B"));
        // A is turned a quarter: its own right is the world's up, so B, to the world's right, is below A in A's terms.
        a.Transform.Rotation = new Vector3(0, 0, 90);
        selection.SelectEntities([b, a]);

        var scale = model.BeginScale()!;
        Near(new Vector3(0, 1, 0), new Vector3(scale.AxisX, 0));
        Near(new Vector3(0, -3, 0), new Vector3(scale.Along(new Vector2(3, 0)), 0));
        scale.End();

        // Widening A doesn't move B, which is beside it along A's other axis; stretching A's height does.
        model.ScaleSelection(new Vector2(2, 1));
        Near(new Vector3(3, 0, 0), b.Transform.Position);
        model.ScaleSelection(new Vector2(1, 2));
        Near(new Vector3(6, 0, 0), b.Transform.Position);
        Assert.Equal(new Vector3(2, 2, 1), a.Transform.Scale);
    });

    [Fact]
    public void NothingSelected_ScalesNothing() => EditorHost.Run(services =>
    {
        var (document, _, model) = Open(services);
        Assert.Null(model.BeginScale());
        model.ScaleSelection(new Vector2(2, 2));
        Assert.False(document.History.CanUndo);
    });

    [Fact]
    public void Agents_ReachScaling() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        selection.SelectEntities([Named(document, "B"), Named(document, "A")]);
        var surface = services.GetServices<Griff.Agent.IAgentToolSource>().OfType<SurfaceTools>().Single();
        var model = surface.All().Single(r => r.Name == nameof(SceneViewModel)).Target;

        using (var factor = System.Text.Json.JsonDocument.Parse("[[2, 0.5]]"))
            Griff.Agent.ObjectSurface.InvokeAsync(model, "ScaleSelection", factor.RootElement).GetAwaiter().GetResult();
        Near(new Vector3(6, 0, 0), Named(document, "B").Transform.Position);
        Assert.Equal(new Vector3(2, 0.5f, 1), Named(document, "A").Transform.Scale);
    });
}
