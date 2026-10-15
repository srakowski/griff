using System.IO;
using System.Numerics;
using System.Windows.Threading;
using Griff.Art;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Entities;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>A polygon collider's points, dragged, added and taken off in the Scene view, and fitted to a sprite.</summary>
public sealed class PolygonPointTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // Rock: a triangle "0 0; 2 0; 1 2" at 10, 0, twice the size. Wheel: a square turned 90 degrees on a body that rotates.
    private static (SceneDocument Document, ISelection Selection, SceneViewModel Model) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var rock = new Entity("Rock");
        rock.Transform.Position = new Vector3(10, 0, 0);
        rock.Transform.Scale = new Vector3(2, 2, 1);
        rock.AddComponent<PolygonCollider>().Points = "0 0; 2 0; 1 2";
        scene.Add(rock);

        var wheel = new Entity("Wheel");
        wheel.Transform.Rotation = new Vector3(0, 0, 90);
        wheel.AddComponent<PolygonCollider>().Points = "-1 -1; 1 -1; 1 1; -1 1";
        wheel.AddComponent<Body>().Rotates = true;
        scene.Add(wheel);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<SceneViewModel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static void Near(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}.");

    [Fact]
    public void ItsPointsHaveHandles_WhereTheyAreInTheWorld() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);

        selection.Select(Named(document, "Rock"));
        var points = model.PathPoints();
        Assert.Equal(["PolygonCollider.1", "PolygonCollider.2", "PolygonCollider.3"], points.Select(p => p.Key));
        Near(new Vector2(10, 0), points[0].World);
        Near(new Vector2(14, 0), points[1].World);
        Near(new Vector2(12, 4), points[2].World);
        Assert.Equal("PolygonCollider.3 at 12, 4", points[2].Label);

        // Turning with its body, its points turn too.
        selection.Select(Named(document, "Wheel"));
        Near(new Vector2(1, -1), model.PathPoints()[0].World);
    });

    [Fact]
    public void DraggingAPoint_RewritesItsPoints_InItsOwnUnits_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var rock = Named(document, "Rock").GetComponent<PolygonCollider>()!;
        selection.Select(rock.Entity);

        var drag = model.BeginPathPoint(model.PathPoints()[2])!;
        drag.To(new Vector2(12.5f, 5.123f), snap: false);
        Assert.Equal("0 0; 2 0; 1.25 2.56", rock.Points);
        drag.To(new Vector2(12.6f, 5.4f), snap: true);
        Assert.Equal("0 0; 2 0; 1.5 2.5", rock.Points);
        drag.End();
        Assert.Equal("Change Points", document.History.UndoDescription);
        document.History.Undo();
        Assert.Equal("0 0; 2 0; 1 2", rock.Points);
        Assert.False(document.History.CanUndo);

        // Agents name the point; a turned one lands in its own turned units.
        model.DragPathPoint("polygoncollider.2", new Vector2(16, 0));
        Assert.Equal("0 0; 3 0; 1 2", rock.Points);
        var wheel = Named(document, "Wheel").GetComponent<PolygonCollider>()!;
        selection.Select(wheel.Entity);
        model.DragPathPoint("PolygonCollider.1", new Vector2(2, -1));
        Assert.Equal("-1 -2; 1 -1; 1 1; -1 1", wheel.Points);
    });

    [Fact]
    public void AddingAndRemovingPoints_AreEachOneUndoStep_AndItKeepsThree() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var rock = Named(document, "Rock").GetComponent<PolygonCollider>()!;
        selection.Select(rock.Entity);

        // The plus on the side after point 1 adds point 2, and dragging it moves it, all in one go.
        var drag = model.BeginInsertPathPoint(rock, 2)!;
        drag.To(new Vector2(12, 0), snap: false);
        drag.To(new Vector2(12, -1), snap: false);
        drag.End();
        Assert.Equal("0 0; 1 -0.5; 2 0; 1 2", rock.Points);
        Assert.Null(rock.Problem);
        document.History.Undo();
        Assert.Equal("0 0; 2 0; 1 2", rock.Points);

        // One past the last goes between the last and the first.
        model.InsertPathPoint(4, new Vector2(10, 4), component: "PolygonCollider");
        Assert.Equal("0 0; 2 0; 1 2; 0 2", rock.Points);
        Assert.Throws<ArgumentOutOfRangeException>(() => model.InsertPathPoint(6, Vector2.Zero));

        model.RemovePathPoint("PolygonCollider.3");
        Assert.Equal("0 0; 2 0; 0 2", rock.Points);
        document.History.Undo();
        Assert.Equal("0 0; 2 0; 1 2; 0 2", rock.Points);
        model.RemovePathPoint("PolygonCollider.4");
        var error = Assert.Throws<InvalidOperationException>(() => model.RemovePathPoint("PolygonCollider.1"));
        Assert.Contains("at least three", error.Message);
    });

    [Fact]
    public void FittingToASprite_TracesItsPicture_InPlaceOfItsBox_AsOneUndoStep() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));

        // A 16-pixel picture of a right triangle, solid below the line from its bottom left to its top right.
        var sheet = new PixelSheet(SheetLayout.Sprite(16));
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                sheet.Set(0, x, y, y >= 15 - x ? PixelSheet.Pack(200, 100, 50, 255) : 0);
        services.GetRequiredService<EditorArt>().Save(sheet, "Sprites/ramp.png");

        var ramp = new Entity("Ramp");
        ramp.AddComponent(new Sprite { Image = "Sprites/ramp.png", Size = new Vector2(2, 2) });
        ramp.AddComponent(new BoxCollider { Size = new Vector2(2, 2), Layer = "Ground" });
        workspace.Scene!.Execute(new AddEntityEdit(workspace.Scene.Scene, ramp));
        services.GetRequiredService<ISelection>().Select(ramp);
        var command = services.GetServices<EditorCommand>().OfType<FitPolygonColliderCommand>().Single();
        Assert.True(command.CanExecute());

        Wait(command.ExecuteAsync());

        var polygon = ramp.GetComponent<PolygonCollider>()!;
        Assert.Null(ramp.GetComponent<BoxCollider>());
        Assert.Equal(2, ramp.Components.ToList().IndexOf(polygon));
        Assert.Equal("Ground", polygon.Layer);
        Assert.Null(polygon.Problem);
        Assert.InRange(polygon.GivenPoints.Count, 3, FitPolygonColliderCommand.MostPoints);
        var area = 0f;
        for (var i = 0; i < polygon.Outline.Count; i++)
            area += (polygon.Outline[i].X * polygon.Outline[(i + 1) % polygon.Outline.Count].Y - polygon.Outline[i].Y * polygon.Outline[(i + 1) % polygon.Outline.Count].X) / 2;
        Assert.InRange(area, 1.9f, 2.4f);

        workspace.Scene.History.Undo();
        Assert.Null(ramp.GetComponent<PolygonCollider>());
        Assert.Equal("Ground", ramp.GetComponent<BoxCollider>()!.Layer);

        // Without a picture, there's nothing to fit to.
        ramp.GetComponent<Sprite>()!.Image = null;
        Assert.False(command.CanExecute());
    });

    private static void Wait(Task task)
    {
        EditorHost.Pump(task);
    }
}
