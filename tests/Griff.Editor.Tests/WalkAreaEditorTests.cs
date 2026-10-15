using System.Numerics;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.SceneView;
using Griff.Modules.Adventures;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// An adventure's walk areas and hotspots in the Scene view: their points dragged, added and taken off as a polygon
/// collider's are (they're outlines too), each one undo step, a click on the floor selecting it, and the Entity menu's
/// Create Walk Area, Create Hotspot and Create Walker.
/// </summary>
public sealed class WalkAreaEditorTests
{
    // Floor: a walk area "-4 -1; 4 -1; 4 1; -4 1" at 0, -2. Door: a hotspot at 6, 0 with a square outline.
    private static (SceneDocument Document, ISelection Selection, SceneViewModel Model) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var floor = new Entity("Floor");
        floor.Transform.Position = new Vector3(0, -2, 0);
        floor.AddComponent<WalkArea>().Points = "-4 -1; 4 -1; 4 1; -4 1";
        scene.Add(floor);

        var door = new Entity("Door");
        door.Transform.Position = new Vector3(6, 0, 0);
        door.AddComponent<Hotspot>().Points = "-1 -1; 1 -1; 1 1; -1 1";
        scene.Add(door);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<SceneViewModel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    [Fact]
    public void AWalkAreasPoints_AreDraggedAddedAndTakenOff_EachOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var floor = Named(document, "Floor").GetComponent<WalkArea>()!;
        selection.Select(floor.Entity);

        var points = model.PathPoints();
        Assert.Equal(["WalkArea.1", "WalkArea.2", "WalkArea.3", "WalkArea.4"], points.Select(p => p.Key));
        Assert.Equal("WalkArea.3 at 4, -1", points[2].Label);

        model.DragPathPoint("WalkArea.3", new Vector2(5, 0));
        Assert.Equal("-4 -1; 4 -1; 5 2; -4 1", floor.Points);
        Assert.Equal("Change Points", document.History.UndoDescription);
        document.History.Undo();
        Assert.Equal("-4 -1; 4 -1; 4 1; -4 1", floor.Points);

        // A corner added on the top side (after point 3), then taken off again.
        model.InsertPathPoint(4, new Vector2(0, 0), component: "WalkArea");
        Assert.Equal("-4 -1; 4 -1; 4 1; 0 2; -4 1", floor.Points);
        model.RemovePathPoint("WalkArea.4");
        Assert.Equal("-4 -1; 4 -1; 4 1; -4 1", floor.Points);
        document.History.Undo();
        Assert.Equal("-4 -1; 4 -1; 4 1; 0 2; -4 1", floor.Points);
        model.RemovePathPoint("WalkArea.4");
        model.RemovePathPoint("WalkArea.1");
        Assert.Contains("at least three", Assert.Throws<InvalidOperationException>(() => model.RemovePathPoint("WalkArea.1")).Message);
    });

    [Fact]
    public void AHotspotsOutline_HasHandlesToo_AndAClickInsideEitherSelectsIt() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        selection.Select(Named(document, "Door"));
        Assert.Equal(["Hotspot.1", "Hotspot.2", "Hotspot.3", "Hotspot.4"], model.PathPoints().Select(p => p.Key));
        model.DragPathPoint("Hotspot.1", new Vector2(4, -2));
        Assert.Equal("-2 -2; 1 -1; 1 1; -1 1", Named(document, "Door").GetComponent<Hotspot>()!.Points);

        Assert.Equal("Floor", model.EntityAt(new Vector2(-3, -2.5f), pivotRadius: 0.25f)?.Name);
        Assert.Equal("Door", model.EntityAt(new Vector2(6.5f, 0.5f), pivotRadius: 0.25f)?.Name);
        Assert.Null(model.EntityAt(new Vector2(-3, 4), pivotRadius: 0.25f));
    });

    [Fact]
    public void CreateWalker_MakesAPlayerOnAFloorOfItsOwn_InOneUndoStep() => EditorHost.Run(services =>
    {
        var workspace = services.GetRequiredService<IWorkspace>();
        var document = new SceneDocument(new Scene(), "Test.scene");
        workspace.EnterPlayMode(document);
        var commands = services.GetRequiredService<CommandRegistry>();

        EditorHost.Pump(commands.Get("Entity.CreateWalker")!.ExecuteAsync());
        var player = Named(document, "Player");
        Assert.NotNull(player.GetComponent<Walker>());
        var floor = Named(document, "Walk Area").GetComponent<WalkArea>()!;
        Assert.True(floor.Contains(player.GetComponent<Walker>()!.Feet));

        // A second walker shares it.
        services.GetRequiredService<ISelection>().Select(floor.Entity);
        EditorHost.Pump(commands.Get("Entity.CreateWalker")!.ExecuteAsync());
        Assert.Single(document.Scene.Entities, e => e.GetComponent<WalkArea>() is not null);
        Assert.Null(document.Scene.Entities.Last(e => e.GetComponent<Walker>() is not null).Parent);

        document.History.Undo();
        document.History.Undo();
        Assert.Empty(document.Scene.Entities);

        EditorHost.Pump(commands.Get("Entity.CreateHotspot")!.ExecuteAsync());
        var hotspot = Named(document, "Hotspot").GetComponent<Hotspot>()!;
        Assert.Equal(4, hotspot.GivenPoints.Count);
        Assert.Equal("Nothing special about it.", hotspot.LookLine);
    });
}
