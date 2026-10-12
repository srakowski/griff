using System.Numerics;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Editing several selected entities in the Inspector: the components they share, mixed values, one undo step.</summary>
public class InspectorMultiEditTests
{
    // Two coins (a red one and a gold one, at different places) and a wall with a Body instead of a Sprite.
    private static (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var red = new Entity("Red");
        red.Transform.Position = new Vector3(1, 2, 0);
        red.AddComponent(new Sprite { Tint = Color.Red, Order = 3 });
        red.AddComponent(new Body());
        scene.Add(red);
        var gold = new Entity("Gold");
        gold.Transform.Position = new Vector3(5, 6, 0);
        gold.AddComponent(new Sprite { Tint = Color.Yellow, Order = 3 });
        scene.Add(gold);
        scene.Add(new Entity("Wall"));

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<InspectorPanel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static PropertyField Field(InspectorPanel inspector, string component, string label) =>
        inspector.Components.Single(c => c.Title == component).Properties.Single(p => p.Label == label);

    [Fact]
    public void EditsTheComponentsAllSelectedEntitiesHave_AndGreysTheRest() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.SelectEntities([Named(document, "Red"), Named(document, "Gold")]);

        Assert.Equal(["Transform", "Sprite", "Body"], inspector.Components.Select(c => c.Title));
        Assert.Equal([false, false, true], inspector.Components.Select(c => c.IsPartial));
        var body = inspector.Components[2];
        Assert.Equal("on 1 of 2", body.PartialNote);
        Assert.Empty(body.Properties);
        Assert.Equal([Named(document, "Gold")], body.Lacking);
        Assert.Null(inspector.HiddenNote);
        Assert.True(inspector.Header!.IsSeveral);
        Assert.Equal("2 entities", inspector.Header.Name);
        Assert.Equal("Gold, Red", inspector.Header.Names);

        // One entity: everything it has, nothing hidden, and its name to edit.
        selection.Select(Named(document, "Red"));
        Assert.Equal(["Transform", "Sprite", "Body"], inspector.Components.Select(c => c.Title));
        Assert.Null(inspector.HiddenNote);
        inspector.Header!.Name = "Ruby";
        Assert.Equal("Ruby", Named(document, "Ruby").Name);
    });

    [Fact]
    public void DifferingValuesShowAsMixed_AndSettingOneSetsAll_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var (red, gold) = (Named(document, "Red"), Named(document, "Gold"));
        selection.SelectEntities([red, gold]);

        var tint = Field(inspector, "Sprite", "Tint");
        var order = Field(inspector, "Sprite", "Order");
        Assert.True(tint.IsMixed);
        Assert.Equal(PropertyField.Mixed, tint.ColorHex);
        Assert.False(order.IsMixed);
        Assert.Equal(3, order.NumberValue);

        // Leaving the dash in place changes nothing.
        tint.ColorHex = PropertyField.Mixed;
        Assert.False(document.History.CanUndo);

        tint.ColorHex = "#00ff00";
        Assert.Equal(new Color(0, 1, 0), red.GetComponent<Sprite>()!.Tint);
        Assert.Equal(new Color(0, 1, 0), gold.GetComponent<Sprite>()!.Tint);
        Assert.False(tint.IsMixed);
        Assert.Equal("Change Tint on 2 entities", document.History.UndoDescription);

        document.History.Undo();
        Assert.Equal(Color.Red, red.GetComponent<Sprite>()!.Tint);
        Assert.Equal(Color.Yellow, gold.GetComponent<Sprite>()!.Tint);
        Assert.True(tint.IsMixed);
    });

    [Fact]
    public void BoolsAndEnabled_AreInBetweenWhenMixed() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var (red, gold) = (Named(document, "Red"), Named(document, "Gold"));
        gold.GetComponent<Sprite>()!.FlipX = true;
        gold.IsEnabled = false;
        selection.SelectEntities([red, gold]);

        var flip = Field(inspector, "Sprite", "Flip X");
        Assert.Null(flip.BoolValue);
        flip.BoolValue = true;
        Assert.True(red.GetComponent<Sprite>()!.FlipX);

        Assert.Null(inspector.Header!.IsEnabled);
        inspector.Header.IsEnabled = true;
        Assert.True(red.IsEnabled && gold.IsEnabled);
        document.History.Undo();
        Assert.False(gold.IsEnabled);
        Assert.True(red.IsEnabled);
    });

    [Fact]
    public void SettingOneAxis_KeepsEachEntitysOtherAxes_AndADragIsOneStep() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var (red, gold) = (Named(document, "Red"), Named(document, "Gold"));
        selection.SelectEntities([red, gold]);

        var position = Field(inspector, "Transform", "Position");
        var (x, y) = (position.Axes[0], position.Axes[1]);
        Assert.True(x.IsMixed);
        Assert.False(position.Axes[2].IsMixed);

        // Scrubbing X: several values while the drag goes on, then the drag ends.
        x.Value = 8;
        x.Value = 10;
        inspector.Seal();
        Assert.Equal(new Vector3(10, 2, 0), red.Transform.Position);
        Assert.Equal(new Vector3(10, 6, 0), gold.Transform.Position);
        Assert.False(x.IsMixed);
        Assert.True(y.IsMixed);

        document.History.Undo();
        Assert.Equal(new Vector3(1, 2, 0), red.Transform.Position);
        Assert.Equal(new Vector3(5, 6, 0), gold.Transform.Position);
        Assert.False(document.History.CanUndo);
    });

    [Fact]
    public void AddingAndRemovingComponents_ActsOnEverySelectedEntity() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var (red, gold, wall) = (Named(document, "Red"), Named(document, "Gold"), Named(document, "Wall"));
        selection.SelectEntities([red, gold, wall]);
        Assert.Equal(["Transform"], inspector.Components.Where(c => !c.IsPartial).Select(c => c.Title));

        var registry = services.GetRequiredService<IWorkspace>().Components;
        inspector.AddComponent(registry.Get(typeof(Health)));
        Assert.All([red, gold, wall], e => Assert.NotNull(e.GetComponent<Health>()));
        Assert.Contains("Health", inspector.Components.Select(c => c.Title));

        inspector.Components.Single(c => c.Title == "Health").RemoveCommand.Execute(null);
        Assert.All([red, gold, wall], e => Assert.Null(e.GetComponent<Health>()));

        document.History.Undo();
        Assert.All([red, gold, wall], e => Assert.NotNull(e.GetComponent<Health>()));
        document.History.Undo();
        Assert.All([red, gold, wall], e => Assert.Null(e.GetComponent<Health>()));
    });
}
