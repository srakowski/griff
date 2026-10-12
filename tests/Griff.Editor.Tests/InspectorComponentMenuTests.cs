using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// A component's menu in the Inspector (reset, copy and paste values, move up and down) and, with several entities
/// selected, the components only some of them have, with Add to All.
/// </summary>
public class InspectorComponentMenuTests
{
    // A hero with a sprite, a body and health, and two coins with just a sprite (one of them glows).
    private static (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var hero = new Entity("Hero");
        hero.AddComponent(new Sprite { Tint = Color.Red, Order = 2 });
        hero.AddComponent(new Body());
        hero.AddComponent(new Health { Max = 7 });
        scene.Add(hero);
        var coin = new Entity("Coin");
        coin.AddComponent(new Sprite());
        scene.Add(coin);
        var gem = new Entity("Gem");
        gem.AddComponent(new Sprite());
        gem.AddComponent(new Light { Radius = 6 });
        scene.Add(gem);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<InspectorPanel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static ComponentSection Section(InspectorPanel inspector, string title) => inspector.Components.Single(c => c.Title == title);

    [Fact]
    public void ResetToDefaults_PutsEveryValueBack_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var hero = Named(document, "Hero");
        selection.Select(hero);

        Section(inspector, "Health").ResetToDefaults();
        Assert.Equal(new Health().Max, hero.GetComponent<Health>()!.Max);
        Assert.Equal("Reset Health", document.History.UndoDescription);
        Assert.Equal(new Health().Max, Section(inspector, "Health").Properties.Single(p => p.Label == "Max").NumberValue);

        document.History.Undo();
        Assert.Equal(7, hero.GetComponent<Health>()!.Max);
    });

    [Fact]
    public void CopiedValues_PasteOntoTheSameComponent_OnEverySelectedEntity() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var (hero, coin, gem) = (Named(document, "Hero"), Named(document, "Coin"), Named(document, "Gem"));
        selection.Select(hero);
        Assert.False(Section(inspector, "Sprite").CanPaste);
        Assert.Equal("Nothing's been copied yet: Copy Values on a component first.",
            Assert.Throws<InvalidOperationException>(Section(inspector, "Sprite").PasteValues).Message);
        Section(inspector, "Sprite").CopyValues();
        Assert.Equal("Sprite", inspector.CopiedType);
        Assert.True(Section(inspector, "Sprite").CanPaste);
        Assert.False(Section(inspector, "Body").CanPaste);

        selection.SelectEntities([coin, gem]);
        Section(inspector, "Sprite").PasteValues();
        Assert.All([coin, gem], e => Assert.Equal((Color.Red, 2), (e.GetComponent<Sprite>()!.Tint, e.GetComponent<Sprite>()!.Order)));

        document.History.Undo();
        Assert.All([coin, gem], e => Assert.Equal(Color.White, e.GetComponent<Sprite>()!.Tint));
    });

    [Fact]
    public void MovingAComponent_ReordersTheSections_AndUndoes() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Hero"));
        Assert.False(Section(inspector, "Sprite").CanMoveUp);
        Assert.False(Section(inspector, "Health").CanMoveDown);
        Assert.False(Section(inspector, "Transform").CanMoveDown);
        // Agents calling them anyway are told why.
        Assert.Equal("Sprite is already first, under the Transform.", Assert.Throws<InvalidOperationException>(() => Section(inspector, "Sprite").MoveUp()).Message);
        Assert.Equal("The Transform stays first.", Assert.Throws<InvalidOperationException>(() => Section(inspector, "Transform").MoveDown()).Message);

        Section(inspector, "Health").MoveUp();
        Assert.Equal(["Transform", "Sprite", "Health", "Body"], inspector.Components.Select(c => c.Title));
        Section(inspector, "Sprite").MoveDown();
        Assert.Equal(["Transform", "Health", "Sprite", "Body"], inspector.Components.Select(c => c.Title));

        document.History.Undo();
        document.History.Undo();
        Assert.Equal(["Transform", "Sprite", "Body", "Health"], inspector.Components.Select(c => c.Title));
    });

    [Fact]
    public void ComponentsOnlySomeHave_ShowGreyed_AndAddToAllCopiesThem() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var (hero, coin, gem) = (Named(document, "Hero"), Named(document, "Coin"), Named(document, "Gem"));
        // The last one selected is the primary, whose components come first.
        selection.SelectEntities([hero, coin, gem]);

        Assert.Equal(["Transform", "Sprite", "Light", "Body", "Health"], inspector.Components.Select(c => c.Title));
        var light = Section(inspector, "Light");
        Assert.True(light.IsPartial);
        Assert.Equal("on 1 of 3", light.PartialNote);
        Assert.Equal("Gem has it; Coin and Hero don't. Add to All gives them a copy.", light.PartialTip);
        Assert.False(light.HasValues);
        Assert.Throws<InvalidOperationException>(light.ResetToDefaults);
        Assert.Throws<InvalidOperationException>(Section(inspector, "Sprite").AddToAll);
        Assert.False(Section(inspector, "Sprite").AddToAllCommand.CanExecute(null));
        Assert.False(Section(inspector, "Sprite").IsPartial);

        light.AddToAll();
        Assert.All([hero, coin, gem], e => Assert.Equal(6, e.GetComponent<Light>()!.Radius));
        Assert.False(Section(inspector, "Light").IsPartial);
        Assert.Equal(6, Section(inspector, "Light").Properties.Single(p => p.Label == "Radius").NumberValue);

        document.History.Undo();
        Assert.Null(hero.GetComponent<Light>());
        Assert.True(Section(inspector, "Light").IsPartial);

        // Removing one only some have removes it from those.
        Section(inspector, "Health").RemoveCommand.Execute(null);
        Assert.Null(hero.GetComponent<Health>());
        Assert.DoesNotContain("Health", inspector.Components.Select(c => c.Title));
    });
}
