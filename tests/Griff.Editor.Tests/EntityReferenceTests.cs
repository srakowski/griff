using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Hierarchy;
using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Renaming an entity renames it in the fields that name it, and fields naming nothing are flagged.</summary>
public class EntityReferenceTests
{
    // A lever that turns on the bridge, a camera following the player, and a fighter with its hitbox.
    private static (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var lever = new Entity("Lever");
        lever.AddComponent(new BoxCollider { IsTrigger = true });
        lever.AddComponent(new Switch { Targets = "Bridge" });
        scene.Add(lever);
        scene.Add(new Entity("Bridge"));
        scene.Add(new Entity("Player"));
        var camera = new Entity("Camera");
        camera.AddComponent(new Follow { Target = "Player" });
        scene.Add(camera);
        var fighter = new Entity("Fighter");
        fighter.AddComponent(new Attack { Target = "Player" });
        scene.Add(fighter);
        scene.Add(new Entity("Hitbox"), fighter);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<InspectorPanel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static PropertyField Field(InspectorPanel inspector, string component, string label) =>
        inspector.Components.Single(c => c.Title == component).Properties.Single(p => p.Label == label);

    private static EntityNode Node(IEnumerable<EntityNode> nodes, string name) =>
        nodes.SelectMany(n => (IEnumerable<EntityNode>)[n, .. n.Children]).Single(n => n.Name == name);

    [Fact]
    public void RenamingInTheHierarchy_RenamesTheFieldsThatNameIt_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, _, _) = Open(services);
        var hierarchy = services.GetRequiredService<HierarchyPanel>();
        var player = Node(hierarchy.Roots, "Player");

        player.EditName = "Hero";
        player.IsEditing = true;
        hierarchy.CommitRename(player);

        Assert.Equal("Hero", Named(document, "Camera").GetComponent<Follow>()!.Target);
        Assert.Equal("Hero", Named(document, "Fighter").GetComponent<Attack>()!.Target);
        document.History.Undo();
        Assert.Equal("Player", Named(document, "Camera").GetComponent<Follow>()!.Target);
        Assert.Equal("Player", Named(document, "Fighter").GetComponent<Attack>()!.Target);
        Assert.NotNull(Named(document, "Player"));
    });

    [Fact]
    public void RenamingInTheInspector_RenamesTheFieldsThatNameIt() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Bridge"));

        inspector.Header!.Name = "Drawbridge";

        Assert.Equal("Drawbridge", Named(document, "Lever").GetComponent<Switch>()!.Targets);
        Assert.Equal("Rename Bridge", document.History.UndoDescription);
    });

    [Fact]
    public void FieldsNamingNothing_AreFlagged_UntilTheyMatch() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Lever"));
        var targets = Field(inspector, "Switch", "Targets");
        Assert.False(targets.IsUnmatched);

        targets.TextValue = "Bridge, Gate, Lift";
        Assert.True(targets.IsUnmatched);
        Assert.Equal(["Gate", "Lift"], targets.UnmatchedNames);
        Assert.Equal("Nothing in the scene is called \"Gate\" or \"Lift\"", targets.UnmatchedTip);

        // Deleting what it names flags it; undoing that clears the flag again.
        targets.TextValue = "Bridge";
        document.Execute(new Griff.Editing.RemoveEntityEdit(document.Scene, Named(document, "Bridge")));
        Assert.True(Field(inspector, "Switch", "Targets").IsUnmatched);
        document.History.Undo();
        Assert.False(Field(inspector, "Switch", "Targets").IsUnmatched);
    });

    [Fact]
    public void AHitboxIsLookedForInside() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Fighter"));
        var hitbox = Field(inspector, "Attack", "Hitbox");
        Assert.False(hitbox.IsUnmatched);

        hitbox.TextValue = "Bridge"; // in the scene, but not inside the fighter
        Assert.Equal("Nothing inside Fighter is called \"Bridge\"", hitbox.UnmatchedTip);
    });

    [Fact]
    public void Agents_SeeTheFlag() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        Named(document, "Camera").GetComponent<Follow>()!.Target = "Playr";
        selection.Select(Named(document, "Camera"));

        // What ui_inspect shows of the field.
        var field = Griff.Agent.ObjectSurface.Describe(inspector, "Components[Follow].Properties[Target]").ToString();
        Assert.Contains("\"IsUnmatched\"", field);
        Assert.Contains("Nothing in the scene is called", field);
        Assert.Contains("Playr", field);
    });
}
