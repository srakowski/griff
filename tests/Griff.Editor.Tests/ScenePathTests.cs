using System.Numerics;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Mover paths and patrol routes, drawn and edited with the Scene view's path handles.</summary>
public class ScenePathTests
{
    // Lift: a mover at 2, 1 inside Room (at 10, 0, doubled in size) going "0 3; 4 3". Guard: a patrol at 0, 5 that has
    // walked 1 to the right, turning 2 to its left.
    private static (SceneDocument Document, ISelection Selection, SceneViewModel Model) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var room = new Entity("Room");
        room.Transform.Position = new Vector3(10, 0, 0);
        room.Transform.Scale = new Vector3(2, 2, 1);
        scene.Add(room);

        var lift = new Entity("Lift");
        lift.Transform.Position = new Vector3(2, 1, 0);
        lift.AddComponent<Mover>().Points = "0 3; 4 3";
        scene.Add(lift, room);

        var guard = new Entity("Guard");
        guard.Transform.Position = new Vector3(0, 5, 0);
        var patrol = guard.AddComponent<Patrol>();
        (patrol.Left, patrol.Walked) = (2, 1);
        scene.Add(guard);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<SceneViewModel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static void Near(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}.");

    [Fact]
    public void PathPoints_AreTheMoversPointsAndThePatrolsTurns_InTheWorld() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var (lift, guard) = (Named(document, "Lift"), Named(document, "Guard"));

        // The mover's start (where it's placed) has no handle; its points are in its parent's space.
        selection.Select(lift);
        var points = model.PathPoints();
        Assert.Equal(["Mover.1", "Mover.2"], points.Select(p => p.Key));
        Near(new Vector2(14, 8), points[0].World);
        Near(new Vector2(22, 8), points[1].World);
        Assert.Equal("Mover.2 at 22, 8", points[1].Label);
        Assert.Equal([new Vector2(14, 2), new Vector2(14, 8), new Vector2(22, 8)], SceneViewModel.PathOf(lift.GetComponent<Mover>()!));

        // A patrol's route is measured from where it was placed: it's walked 1 right of it. Its right is open.
        selection.Select(guard);
        points = model.PathPoints();
        Assert.Equal(["Patrol.Left", "Patrol.Right"], points.Select(p => p.Key));
        Near(new Vector2(-3, 5), points[0].World);
        Assert.False(points[0].Open);
        Assert.True(points[1].Open);
        Near(new Vector2(-1 + SceneViewModel.OpenRouteStub, 5), points[1].World);
        Assert.Equal("Patrol.Right not set (walks on until a wall or an edge)", points[1].Label);

        selection.Select(null);
        Assert.Empty(model.PathPoints());
    });

    [Fact]
    public void DraggingAMoversPoint_RewritesItsPoints_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var lift = Named(document, "Lift");
        var mover = lift.GetComponent<Mover>()!;
        selection.Select(lift);

        // In the world, through the room's scale: 2 world units are 1 of the lift's.
        var drag = model.BeginPathPoint(model.PathPoints()[1])!;
        drag.To(new Vector2(25.341f, 6), snap: false);
        Assert.Equal("0 3; 5.67 2", mover.Points);
        drag.To(new Vector2(25.333f, 6.6f), snap: true);
        Assert.Equal("0 3; 5.5 2.5", mover.Points);
        drag.To(new Vector2(14, 2), snap: false);
        drag.End();
        Assert.Equal("0 3; 0 0", mover.Points);
        Assert.Equal("Change Points", document.History.UndoDescription);

        document.History.Undo();
        Assert.Equal("0 3; 4 3", mover.Points);
        Assert.False(document.History.CanUndo);

        // Agents name the point.
        model.DragPathPoint("mover.1", new Vector2(14, 4));
        Assert.Equal("0 1; 4 3", mover.Points);
        var error = Assert.Throws<ArgumentException>(() => model.DragPathPoint("Mover.3", Vector2.Zero));
        Assert.Contains("it has Mover.1, Mover.2", error.Message);
    });

    [Fact]
    public void AddingAndRemovingPoints_AreEachOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var lift = Named(document, "Lift");
        var mover = lift.GetComponent<Mover>()!;
        selection.Select(lift);

        // A plus between two points adds one there, and dragging it moves it, all in one go.
        var drag = model.BeginInsertPathPoint(mover, 2)!;
        drag.To(new Vector2(18, 8), snap: false);
        drag.To(new Vector2(18, 10), snap: false);
        drag.End();
        Assert.Equal("0 3; 2 4; 4 3", mover.Points);
        document.History.Undo();
        Assert.Equal("0 3; 4 3", mover.Points);

        // One past the last adds it at the end.
        model.InsertPathPoint(3, new Vector2(22, 2));
        Assert.Equal("0 3; 4 3; 4 0", mover.Points);
        Assert.Throws<ArgumentOutOfRangeException>(() => model.InsertPathPoint(5, Vector2.Zero));

        model.RemovePathPoint("Mover.2");
        Assert.Equal("0 3; 4 0", mover.Points);
        document.History.Undo();
        Assert.Equal("0 3; 4 3; 4 0", mover.Points);

        // A mover keeps one point to go to.
        model.RemovePathPoint("Mover.1");
        model.RemovePathPoint("Mover.1");
        Assert.Equal("4 0", mover.Points);
        Assert.Throws<InvalidOperationException>(() => model.RemovePathPoint("Mover.1"));
    });

    [Fact]
    public void DraggingAPatrolsTurn_SetsItsSide_AndBackPastHomeClearsIt() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var guard = Named(document, "Guard");
        var patrol = guard.GetComponent<Patrol>()!;
        selection.Select(guard);

        // Only along its side: up and down don't count. Home is at -1.
        var drag = model.BeginPathPoint(model.PathPoints().Single(p => p.Key == "Patrol.Right"))!;
        drag.To(new Vector2(2.333f, 9), snap: false);
        Assert.Equal(3.33f, patrol.Right);
        drag.To(new Vector2(2.333f, 9), snap: true);
        Assert.Equal(3.5f, patrol.Right);
        drag.End();
        Assert.False(model.PathPoints().Single(p => p.Key == "Patrol.Right").Open);

        // Back past home it walks on until a wall again.
        model.DragPathPoint("Patrol.Left", new Vector2(4, 5));
        Assert.Equal(0, patrol.Left);
        document.History.Undo();
        Assert.Equal(2, patrol.Left);

        // Taking a turn off clears it.
        model.RemovePathPoint("Patrol.Right");
        Assert.Equal(0, patrol.Right);
        Assert.True(model.PathPoints().Single(p => p.Key == "Patrol.Right").Open);
    });

    [Fact]
    public void Agents_ReachPathPoints() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var lift = Named(document, "Lift");
        selection.Select(lift);
        var surface = services.GetServices<Griff.Agent.IAgentToolSource>().OfType<SurfaceTools>().Single();
        var model = surface.All().Single(r => r.Name == nameof(SceneViewModel)).Target;

        void Invoke(string method, string json)
        {
            using var args = System.Text.Json.JsonDocument.Parse(json);
            Griff.Agent.ObjectSurface.InvokeAsync(model, method, args.RootElement).GetAwaiter().GetResult();
        }

        Invoke("DragPathPoint", """["Mover.2", [26, 8]]""");
        Invoke("InsertPathPoint", """[3, [26, 2], true]""");
        Invoke("RemovePathPoint", """["Mover.1"]""");
        Assert.Equal("6 3; 6 0", lift.GetComponent<Mover>()!.Points);

        using var none = System.Text.Json.JsonDocument.Parse("[]");
        var listed = Griff.Agent.ObjectSurface.InvokeAsync(model, "PathPoints", none.RootElement).GetAwaiter().GetResult();
        Assert.Contains("Mover.2 at 26, 2", listed!.ToString());
    });
}
