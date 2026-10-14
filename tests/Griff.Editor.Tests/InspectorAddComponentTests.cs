using System.Text.Json;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Inspector;
using Griff.Modules.Contraptions;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Adding and removing components in the Inspector with what they need in mind: the searchable Add Component list,
/// sections flagged for a missing requirement, and a warning before removing what another component needs.
/// </summary>
public class InspectorAddComponentTests
{
    private static (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services, params Entity[] entities)
    {
        var scene = new Scene();
        foreach (var entity in entities)
            scene.Add(entity);
        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<InspectorPanel>());
    }

    private static ComponentSection Section(InspectorPanel inspector, string title) => inspector.Components.Single(c => c.Title == title);

    [Fact]
    public void TypingFiltersTheList_ByCategory_AndPicksTheBestMatch() => EditorHost.Run(services =>
    {
        var slime = new Entity("Slime");
        var (_, selection, inspector) = Open(services, slime);
        selection.Select(slime);

        inspector.IsAddOpen = true;
        var picker = inspector.Picker!;
        Assert.Equal("", picker.Search);
        Assert.Contains("Physics", picker.View.Groups.Cast<System.Windows.Data.CollectionViewGroup>().Select(g => (string)g.Name));

        // The component called what's typed comes first, even though others mention it in their descriptions.
        picker.Search = "body";
        Assert.Equal("Body", picker.Selected!.Name);
        Assert.Contains(picker.View.Cast<CatalogItem>(), i => i.Name != "Body");

        picker.Search = "patr";
        Assert.Equal("Patrol", picker.Selected!.Name);
        Assert.Equal("Adds Patrol to Slime, with BoxCollider and Body that it needs.", picker.PlanText);

        // Up and Down move through what's shown, stopping at the ends.
        picker.Search = "collider";
        var shown = picker.View.Cast<CatalogItem>().ToList();
        picker.MoveSelection(-5);
        Assert.Equal(shown[0], picker.Selected);
        picker.MoveSelection(1);
        Assert.Equal(shown[1], picker.Selected);

        picker.Search = "zzzz";
        Assert.False(picker.HasResults);
        Assert.Null(picker.Selected);
        inspector.AddPicked();
        Assert.True(inspector.IsAddOpen);

        // Closing and opening again starts afresh.
        inspector.IsAddOpen = false;
        Assert.Null(inspector.Picker);
        inspector.IsAddOpen = true;
        Assert.Equal("", inspector.Picker!.Search);
    });

    [Fact]
    public void AddingFromTheList_BringsWhatItNeeds_AsOneUndoStep_AndCloses() => EditorHost.Run(services =>
    {
        var slime = new Entity("Slime");
        var (document, selection, inspector) = Open(services, slime);
        selection.Select(slime);

        inspector.IsAddOpen = true;
        inspector.Picker!.Search = "Patrol";
        inspector.AddPicked();

        Assert.False(inspector.IsAddOpen);
        Assert.Equal(["Transform", "BoxCollider", "Body", "Patrol"], slime.Components.Select(c => c.GetType().Name));
        Assert.Equal("Add Patrol", document.History.UndoDescription);
        Assert.All(inspector.Components, c => Assert.False(c.HasNeeds));

        document.History.Undo();
        Assert.Equal(["Transform"], slime.Components.Select(c => c.GetType().Name));
    });

    [Fact]
    public void AddByName_AddsToEverySelectedEntity_WithWhatEachNeeds() => EditorHost.Run(services =>
    {
        var a = new Entity("A");
        var b = new Entity("B");
        b.AddComponent(new Body());
        var (document, selection, inspector) = Open(services, a, b);
        selection.SelectEntities([a, b]);

        inspector.Add("Platformer Movement");

        Assert.Equal(["Transform", "BoxCollider", "Body", "PlatformerMovement"], a.Components.Select(c => c.GetType().Name));
        Assert.Equal(["Transform", "Body", "BoxCollider", "PlatformerMovement"], b.Components.Select(c => c.GetType().Name));
        Assert.Equal("Add PlatformerMovement to 2 entities", document.History.UndoDescription);
        Assert.Throws<ArgumentException>(() => inspector.Add("Jetpack"));
    });

    [Fact]
    public void ASectionMissingWhatItNeeds_IsFlagged_WithAButtonThatAddsIt() => EditorHost.Run(services =>
    {
        var player = new Entity("Player");
        player.AddComponent(new PlatformerMovement());
        var (document, selection, inspector) = Open(services, player);
        selection.Select(player);

        var needs = Section(inspector, "Platformer Movement").Needs;
        Assert.Equal(["Body", "Collider"], needs.Select(n => n.Name));
        Assert.Equal("Needs a Body, which Player doesn't have.", needs[0].Message);
        Assert.Equal("Add Body", needs[0].AddLabel);
        // Any kind of collider will do; the button adds the first kind.
        Assert.Equal("Add Box Collider", needs[1].AddLabel);
        Assert.Contains("Box Collider or Circle Collider", needs[1].Tip);
        Assert.All(inspector.Components.Where(c => c.Title != "Platformer Movement"), c => Assert.False(c.HasNeeds));

        // Adding the collider leaves the Body to add.
        needs[1].AddCommand.Execute(null);
        Assert.IsType<BoxCollider>(player.Components[^1]);
        Assert.Equal("Add BoxCollider", document.History.UndoDescription);
        Assert.Equal(["Body"], Section(inspector, "Platformer Movement").Needs.Select(n => n.Name));

        Section(inspector, "Platformer Movement").Needs[0].AddCommand.Execute(null);
        Assert.False(Section(inspector, "Platformer Movement").HasNeeds);
        Assert.IsType<Body>(player.Components[^1]);

        // A Body brings its own collider when there's none.
        var crate = new Entity("Crate");
        crate.AddComponent(new Thruster());
        document.Scene.Add(crate);
        selection.Select(crate);
        Section(inspector, "Thruster").Needs.Single(n => n.Name == "Body").AddCommand.Execute(null);
        Assert.Equal(["Transform", "Thruster", "BoxCollider", "Body"], crate.Components.Select(c => c.GetType().Name));
    });

    [Fact]
    public void WithSeveralSelected_TheFlagCountsThoseMissingIt_AndTheButtonAddsToThemOnly() => EditorHost.Run(services =>
    {
        var a = new Entity("A");
        a.AddComponent(new Pickup());
        a.AddComponent(new CircleCollider());
        var b = new Entity("B");
        b.AddComponent(new Pickup());
        var (document, selection, inspector) = Open(services, a, b);
        selection.SelectEntities([a, b]);

        var need = Assert.Single(Section(inspector, "Pickup").Needs);
        Assert.Equal("Needs a Collider (usually a trigger), which 1 of the 2 don't have.", need.Message);

        need.AddCommand.Execute(null);
        Assert.Single(a.Components.OfType<Collider>());
        Assert.IsType<BoxCollider>(b.Components.OfType<Collider>().Single());
    });

    [Fact]
    public void RemovingWhatAnotherNeeds_AsksFirst_AndSaysWhichNeedsIt() => EditorHost.Run(services =>
    {
        var player = new Entity("Player");
        player.AddComponent(new Body());
        player.AddComponent(new BoxCollider());
        player.AddComponent(new PlatformerMovement());
        player.AddComponent(new Sprite());
        var (document, selection, inspector) = Open(services, player);
        selection.Select(player);

        var body = Section(inspector, "Body");
        Assert.Equal("Remove component. Platformer Movement needs it on Player, and won't work without it.", body.RemoveTip);
        Assert.Equal("Remove component", Section(inspector, "Sprite").RemoveTip);

        // Saying no keeps it.
        using (AgentDialogs.Answering(JsonDocument.Parse("""{"Remove Body?": false}""").RootElement))
            body.RemoveCommand.Execute(null);
        Assert.NotNull(player.GetComponent<Body>());
        Assert.False(document.History.CanUndo);

        using (AgentDialogs.Answering(JsonDocument.Parse("""{"Remove Body?": true}""").RootElement))
            Section(inspector, "Body").RemoveCommand.Execute(null);
        Assert.Null(player.GetComponent<Body>());
        Assert.Equal(["Body"], Section(inspector, "Platformer Movement").Needs.Select(n => n.Name));

        // Nothing needs the sprite: it goes without asking.
        Section(inspector, "Sprite").RemoveCommand.Execute(null);
        Assert.Null(player.GetComponent<Sprite>());
    });

    [Fact]
    public void ASecondColliderCanGo_WithoutAsking() => EditorHost.Run(services =>
    {
        var player = new Entity("Player");
        player.AddComponent(new Body());
        player.AddComponent(new BoxCollider());
        player.AddComponent(new CircleCollider());
        player.AddComponent(new PlatformerMovement());
        var (_, selection, inspector) = Open(services, player);
        selection.Select(player);

        Assert.Equal("Remove component", Section(inspector, "Box Collider").RemoveTip);
        Section(inspector, "Box Collider").RemoveCommand.Execute(null);
        Assert.Null(player.GetComponent<BoxCollider>());
    });
}
