using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Fields that name entities: picked from the scene's entities or dropped from the Hierarchy.</summary>
public class InspectorEntityPickerTests
{
    // A lever and a plate (switches), a gate with a bar inside it, a camera, and a fighter with its hitbox.
    private static (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var lever = new Entity("Lever");
        lever.AddComponent(new BoxCollider { IsTrigger = true });
        lever.AddComponent(new Switch { Targets = "Lift" });
        scene.Add(lever);
        var plate = new Entity("Plate");
        plate.AddComponent(new BoxCollider { IsTrigger = true });
        plate.AddComponent(new Switch());
        scene.Add(plate);
        var gate = new Entity("Gate");
        scene.Add(gate);
        scene.Add(new Entity("Bar"), gate);
        var camera = new Entity("Camera");
        camera.AddComponent(new Follow());
        scene.Add(camera);
        var fighter = new Entity("Fighter");
        fighter.AddComponent(new Attack());
        scene.Add(fighter);
        scene.Add(new Entity("Hitbox"), fighter);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<InspectorPanel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static PropertyField Field(InspectorPanel inspector, string component, string label) =>
        inspector.Components.Single(c => c.Title == component).Properties.Single(p => p.Label == label);

    [Fact]
    public void FieldsThatNameEntities_ArePickers() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Lever"));

        Assert.Equal(FieldKind.Entity, Field(inspector, "Switch", "Targets").Kind);
        Assert.True(Field(inspector, "Switch", "Targets").NamesMany);
        Assert.Equal(FieldKind.Entity, Field(inspector, "Switch", "Prompt").Kind);
        Assert.False(Field(inspector, "Switch", "Prompt").NamesMany);
        // Other names (an item, a sound) stay as they were.
        Assert.Equal(FieldKind.Text, Field(inspector, "Switch", "Key").Kind);
        Assert.Equal(FieldKind.Sound, Field(inspector, "Switch", "Sound").Kind);
    });

    [Fact]
    public void Choices_AreTheScenesEntities_InHierarchyOrder() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Camera"));

        var choices = Field(inspector, "Follow", "Target").EntityChoices();
        Assert.Equal(["Lever", "Plate", "Gate", "Bar", "Camera", "Fighter", "Hitbox"], choices.Select(c => c.Entity.Name));
        Assert.Equal(1, choices.Single(c => c.Entity.Name == "Bar").Depth);
        Assert.Equal("Gate/Bar", choices.Single(c => c.Entity.Name == "Bar").Label);
        Assert.All(choices, c => Assert.False(c.IsNamed));
    });

    [Fact]
    public void AnAttacksHitbox_ListsWhatsInsideIt() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Fighter"));

        var hitbox = Field(inspector, "Attack", "Hitbox");
        Assert.Equal(["Hitbox"], hitbox.EntityChoices().Select(c => c.Entity.Name));
        Assert.True(hitbox.EntityChoices()[0].IsNamed);
        Assert.Equal("Fighter/Hitbox (named)", hitbox.EntityChoices()[0].Label);
    });

    [Fact]
    public void Picking_NamesIt_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var camera = Named(document, "Camera");
        selection.Select(camera);

        Field(inspector, "Follow", "Target").PickEntity("Gate");
        Assert.Equal("Gate", camera.GetComponent<Follow>()!.Target);
        Assert.True(Field(inspector, "Follow", "Target").EntityChoices().Single(c => c.Entity.Name == "Gate").IsNamed);

        document.History.Undo();
        Assert.Equal("Player", camera.GetComponent<Follow>()!.Target);
    });

    [Fact]
    public void Picking_OnAFieldThatNamesSeveral_AddsItOrTakesItOut() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var lever = Named(document, "Lever").GetComponent<Switch>()!;
        selection.Select(lever.Entity!);
        var targets = Field(inspector, "Switch", "Targets");

        targets.PickEntity("Gate");
        Assert.Equal("Lift, Gate", lever.Targets);
        targets.PickEntity("Lift");
        Assert.Equal("Gate", lever.Targets);
        targets.PickEntity("Gate");
        Assert.Null(lever.Targets);

        document.History.Undo();
        Assert.Equal("Gate", lever.Targets);
    });

    [Fact]
    public void Picking_WithSeveralSelected_KeepsEachOnesOtherNames() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var (lever, plate) = (Named(document, "Lever").GetComponent<Switch>()!, Named(document, "Plate").GetComponent<Switch>()!);
        selection.SelectEntities([lever.Entity!, plate.Entity!]);
        var targets = Field(inspector, "Switch", "Targets");
        Assert.True(targets.IsMixed);

        targets.PickEntity("Gate");
        Assert.Equal("Lift, Gate", lever.Targets);
        Assert.Equal("Gate", plate.Targets);
        Assert.True(targets.Names("Gate"));
        Assert.Equal("Change Targets on 2 entities", document.History.UndoDescription);

        // Named by all of them: picking it again takes it out of all of them.
        targets.PickEntity("Gate");
        Assert.Equal("Lift", lever.Targets);
        Assert.Null(plate.Targets);
    });

    [Fact]
    public void DroppingFromTheHierarchy_NamesIt_OrAddsIt() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var lever = Named(document, "Lever").GetComponent<Switch>()!;
        selection.Select(lever.Entity!);

        Field(inspector, "Switch", "Prompt").DropEntity(Named(document, "Bar"));
        Assert.Equal("Bar", lever.Prompt);

        var targets = Field(inspector, "Switch", "Targets");
        targets.DropEntity(Named(document, "Gate"));
        targets.DropEntity(Named(document, "Gate"));
        Assert.Equal("Lift, Gate", lever.Targets);
    });

    [Fact]
    public void Agents_PickThroughUiInvoke() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var lever = Named(document, "Lever").GetComponent<Switch>()!;
        selection.Select(lever.Entity!);

        // What ui_invoke runs (on the UI thread): an item picked by its title, then a field by its label.
        using var gate = System.Text.Json.JsonDocument.Parse("""["Gate"]""");
        Griff.Agent.ObjectSurface.InvokeAsync(inspector, "Components[Switch].Properties[Targets].PickEntity", gate.RootElement).GetAwaiter().GetResult();
        using var bar = System.Text.Json.JsonDocument.Parse("""["Bar"]""");
        Griff.Agent.ObjectSurface.InvokeAsync(inspector, "Components[Switch].Properties[Prompt].PickEntity", bar.RootElement).GetAwaiter().GetResult();

        Assert.Equal("Lift, Gate", lever.Targets);
        Assert.Equal("Bar", lever.Prompt);
    });
}
