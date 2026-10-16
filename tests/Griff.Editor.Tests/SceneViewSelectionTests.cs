using System.Numerics;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>The Scene view's box selection and moving several selected entities together.</summary>
public class SceneViewSelectionTests
{
    // Sprites one unit square: A at 0,0, B at 3,0, Big (4 wide) at 0,4; a bare Marker at 1,-3; Parent at 10,0 holding
    // Child one unit to its right; Hidden (disabled) at 3,-3.
    private static (SceneDocument Document, ISelection Selection, SceneViewModel Model) Open(IServiceProvider services)
    {
        var scene = new Scene();
        Entity Add(string name, float x, float y, Entity? parent = null, float width = 1, bool sprite = true)
        {
            var entity = new Entity(name);
            entity.Transform.Position = new Vector3(x, y, 0);
            if (sprite)
                entity.AddComponent<Sprite>().Size = new Vector2(width, 1);
            scene.Add(entity, parent);
            return entity;
        }

        Add("A", 0, 0);
        Add("B", 3, 0);
        Add("Big", 0, 4, width: 4);
        Add("Marker", 1, -3, sprite: false);
        var parent = Add("Parent", 10, 0);
        Add("Child", 1, 0, parent);
        Add("Hidden", 3, -3).IsEnabled = false;

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<Workspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<SceneViewModel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}.");

    private static string[] Names(ISelection selection) => selection.Entities.Select(e => e.Name).ToArray();

    [Fact]
    public void Box_CatchesWhatsWhollyInside_AndBareEntitiesByTheirPosition() => EditorHost.Run(services =>
    {
        var (_, selection, model) = Open(services);

        // Corners either way round. Big pokes out of the box, and Hidden isn't shown.
        model.SelectInBox(new Vector2(4, -4), new Vector2(-1, 4));
        Assert.Equal(["A", "B", "Marker"], Names(selection));
        Assert.Equal("Marker", selection.Entity!.Name);

        model.SelectInBox(new Vector2(-3, 3), new Vector2(3, 5));
        Assert.Equal(["Big"], Names(selection));
    });

    [Fact]
    public void Box_WithAdd_KeepsTheSelection() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        selection.Select(Named(document, "Parent"));

        model.SelectInBox(new Vector2(-1, -1), new Vector2(4, 1), add: true);
        Assert.Equal(["Parent", "A", "B"], Names(selection));

        // An empty box changes nothing when adding, and clears otherwise.
        model.SelectInBox(new Vector2(20, 20), new Vector2(21, 21), add: true);
        Assert.Equal(3, selection.Entities.Count);
        model.SelectInBox(new Vector2(20, 20), new Vector2(21, 21));
        Assert.Empty(selection.Entities);
    });

    [Fact]
    public void Move_MovesEverySelected_ChildrenWithTheirParent_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var (a, parent, child) = (Named(document, "A"), Named(document, "Parent"), Named(document, "Child"));
        // Child is inside Parent, so it rides along rather than moving twice.
        selection.SelectEntities([a, child, parent]);

        model.MoveSelection(new Vector2(0.5f, -1));
        Assert.Equal(new Vector3(0.5f, -1, 0), a.Transform.Position);
        Assert.Equal(new Vector3(10.5f, -1, 0), parent.Transform.Position);
        Assert.Equal(new Vector3(1, 0, 0), child.Transform.Position);
        Assert.Equal("Move 2 entities", document.History.UndoDescription);

        // Each nudge is its own step.
        model.MoveSelection(new Vector2(1, 0));
        document.History.Undo();
        Assert.Equal(new Vector3(0.5f, -1, 0), a.Transform.Position);
        document.History.Undo();
        Assert.Equal(Vector3.Zero, a.Transform.Position);
        Assert.Equal(new Vector3(10, 0, 0), parent.Transform.Position);
    });

    [Fact]
    public void Drag_IsOneUndoStep_AndSnappingLinesUpThePrimary() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var (a, b, child) = (Named(document, "A"), Named(document, "B"), Named(document, "Child"));
        // Child (at 11, 0 in the world) is picked last, so it's the one that snaps.
        selection.SelectEntities([a, b, child]);

        var move = model.BeginMove()!;
        move.To(new Vector2(0.3f, 0.1f), snap: false);
        move.To(new Vector2(1.2f, 0.4f), snap: true);
        move.End();

        // Child lands on the half-unit grid; the others keep their distance from it.
        Assert.Equal(new Vector3(2, 0.5f, 0), child.Transform.Position);
        Near(new Vector3(1, 0.5f, 0), a.Transform.Position);
        Near(new Vector3(4, 0.5f, 0), b.Transform.Position);

        document.History.Undo();
        Assert.Equal(Vector3.Zero, a.Transform.Position);
        Assert.Equal(new Vector3(1, 0, 0), child.Transform.Position);
        Assert.False(document.History.CanUndo);
    });

    // A tilemap lined up with another (a board on a level) lands on that map's cell corners however it's dragged.
    [Fact]
    public void Dragging_ABoardLinedUpWithALevel_KeepsItOnTheLevelsCells() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var level = new Entity("Level");
        level.Transform.Position = new Vector3(0.25f, 0, 0);
        level.AddComponent(new Tilemap { Tiles = ["0000"], TileSize = 2 });
        document.Scene.Add(level);
        var board = new Entity("Board");
        board.Transform.Position = new Vector3(4.25f, 2, 0);
        var map = board.AddComponent<Tilemap>();
        document.Scene.Add(board);
        map.LinesUpWith = "Level";
        selection.SelectEntities([board]);

        var move = model.BeginMove()!;
        move.To(new Vector2(1.3f, -0.7f), snap: false);
        Assert.Equal(new Vector3(6.25f, 2, 0), board.Transform.Position);
        move.To(new Vector2(-3.2f, 1.2f), snap: true);
        move.End();

        Assert.Equal(new Vector3(0.25f, 4, 0), board.Transform.Position);
        document.History.Undo();
        Assert.Equal(new Vector3(4.25f, 2, 0), board.Transform.Position);

        // Typed into the Inspector (or set by an agent), it lands on the level's cells too.
        var position = services.GetRequiredService<Workspace>().Components.Get(typeof(Transform)).Properties.Single(p => p.Name == "Position");
        document.Execute(new Griff.Editing.SetPropertyEdit(board.Transform, position, new Vector3(1.1f, 0.7f, 0)));
        Assert.Equal(new Vector3(0.25f, 0, 0), board.Transform.Position);
    });

    [Fact]
    public void Agents_ReachBoxSelectionAndMoving() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var surface = services.GetServices<Griff.Agent.IAgentToolSource>().OfType<SurfaceTools>().Single();
        var model = surface.All().Single(r => r.Name == nameof(SceneViewModel)).Target;

        using (var box = System.Text.Json.JsonDocument.Parse("[[-1, -1], [4, 1]]"))
            Griff.Agent.ObjectSurface.InvokeAsync(model, "SelectInBox", box.RootElement).GetAwaiter().GetResult();
        Assert.Equal(["A", "B"], Names(selection));

        using (var offset = System.Text.Json.JsonDocument.Parse("[[0, 2]]"))
            Griff.Agent.ObjectSurface.InvokeAsync(model, "MoveSelection", offset.RootElement).GetAwaiter().GetResult();
        Assert.Equal(new Vector3(3, 2, 0), Named(document, "B").Transform.Position);
    });
}
