using System.Windows.Controls;
using Griff.Agent;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Shell;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// A component's menu in the Inspector (reset, copy and paste values, move up and down, remove) and, with several entities
/// selected, the components only some of them have, with Add to All: each a command given the component, which the menu
/// is built from and agents call by name.
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

    private static EditorCommand Command(IServiceProvider services, string id) => services.GetRequiredService<CommandRegistry>().Get(id);

    // Runs a command of the menu on the component, as clicking it does.
    private static void Run(IServiceProvider services, string id, ComponentSection section) => EditorHost.Command(services, id, section);

    private static ContextMenu MenuOf(IServiceProvider services, ComponentSection section) =>
        CommandMenuItems.ContextMenu(services.GetRequiredService<CommandRegistry>().All, ComponentMenuCommand.Menu, section);

    // What the menu shows for a component: its items (greyed in brackets), separators as "-".
    private static List<string> Menu(IServiceProvider services, ComponentSection section) =>
        [.. MenuOf(services, section).Items.Cast<Control>().Select(c => c is MenuItem item ? (item.IsEnabled ? (string)item.Header : $"({item.Header})") : "-")];

    [Fact]
    public void TheMenu_IsMadeOfItsCommands_GreyedWithWhy() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Hero"));

        Assert.Equal(["Reset to Defaults", "-", "Copy Values", "(Paste Values)", "-", "(Move Up)", "Move Down", "-", "Remove Component"], Menu(services, Section(inspector, "Sprite")));
        Assert.Equal(["Reset to Defaults", "-", "Copy Values", "(Paste Values)", "-", "(Move Up)", "(Move Down)"], Menu(services, Section(inspector, "Transform")));
        var paste = MenuOf(services, Section(inspector, "Sprite")).Items.OfType<MenuItem>().Single(i => i.Tag is PasteComponentValuesCommand);
        Assert.Equal("Nothing's been copied yet: Copy Values on a component first.", paste.ToolTip);
        Assert.Same(Section(inspector, "Sprite"), paste.CommandParameter);

        Run(services, "Component.CopyValues", Section(inspector, "Sprite"));
        Assert.Contains("Paste Sprite Values", Menu(services, Section(inspector, "Sprite")));
        Assert.Contains("(Paste Sprite Values)", Menu(services, Section(inspector, "Body")));
    });

    [Fact]
    public void ResetToDefaults_PutsEveryValueBack_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var hero = Named(document, "Hero");
        selection.Select(hero);

        Run(services, "Component.Reset", Section(inspector, "Health"));
        Assert.Equal(new Health().Max, hero.GetComponent<Health>()!.Max);
        Assert.Equal("Reset Health", document.History.UndoDescription);
        Assert.Equal(new Health().Max, Section(inspector, "Health").Properties.Single(p => p.Label == "Max").NumberValue);

        Assert.True(EditorHost.Undo(services));
        Assert.Equal(7, hero.GetComponent<Health>()!.Max);
    });

    [Fact]
    public void CopiedValues_PasteOntoTheSameComponent_OnEverySelectedEntity() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var (hero, coin, gem) = (Named(document, "Hero"), Named(document, "Coin"), Named(document, "Gem"));
        selection.Select(hero);
        Assert.False(Section(inspector, "Sprite").CanPaste);
        Assert.Equal("Nothing's been copied yet: Copy Values on a component first.", Command(services, "Component.PasteValues").WhyNot(Section(inspector, "Sprite")));
        Run(services, "Component.CopyValues", Section(inspector, "Sprite"));
        Assert.Equal("Sprite", inspector.CopiedType);
        Assert.True(Section(inspector, "Sprite").CanPaste);
        Assert.False(Section(inspector, "Body").CanPaste);
        Assert.Equal("The copied values are a Sprite's, not a Body's.", Section(inspector, "Body").WhyNotPaste);

        selection.SelectEntities([coin, gem]);
        Run(services, "Component.PasteValues", Section(inspector, "Sprite"));
        Assert.All([coin, gem], e => Assert.Equal((Color.Red, 2), (e.GetComponent<Sprite>()!.Tint, e.GetComponent<Sprite>()!.Order)));

        Assert.True(EditorHost.Undo(services));
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
        Assert.False(Command(services, "Component.MoveUp").CanExecute(Section(inspector, "Sprite")));
        Assert.Equal("Sprite is already first, under the Transform.", Command(services, "Component.MoveUp").WhyNot(Section(inspector, "Sprite")));
        Assert.Equal("The Transform stays first.", Command(services, "Component.MoveDown").WhyNot(Section(inspector, "Transform")));

        Run(services, "Component.MoveUp", Section(inspector, "Health"));
        Assert.Equal(["Transform", "Sprite", "Health", "Body"], inspector.Components.Select(c => c.Title));
        Run(services, "Component.MoveDown", Section(inspector, "Sprite"));
        Assert.Equal(["Transform", "Health", "Sprite", "Body"], inspector.Components.Select(c => c.Title));

        Assert.True(EditorHost.Undo(services));
        Assert.True(EditorHost.Undo(services));
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
        // Its menu is Add to All (and Remove); the values' commands are for one they all have.
        Assert.Equal(["Add to All", "-", "Remove Component"], Menu(services, light));
        Assert.False(Command(services, "Component.Reset").CanExecute(light));
        Assert.False(Command(services, "Component.AddToAll").CanExecute(Section(inspector, "Sprite")));
        Assert.Equal("Every selected entity has Sprite already.", Command(services, "Component.AddToAll").WhyNot(Section(inspector, "Sprite")));
        Assert.False(Section(inspector, "Sprite").IsPartial);

        Run(services, "Component.AddToAll", light);
        Assert.All([hero, coin, gem], e => Assert.Equal(6, e.GetComponent<Light>()!.Radius));
        Assert.False(Section(inspector, "Light").IsPartial);
        Assert.Equal(6, Section(inspector, "Light").Properties.Single(p => p.Label == "Radius").NumberValue);

        Assert.True(EditorHost.Undo(services));
        Assert.Null(hero.GetComponent<Light>());
        Assert.True(Section(inspector, "Light").IsPartial);

        // Removing one only some have removes it from those.
        Run(services, "Component.Remove", Section(inspector, "Health"));
        Assert.Null(hero.GetComponent<Health>());
        Assert.DoesNotContain("Health", inspector.Components.Select(c => c.Title));
    });

    [Fact]
    public void Agents_CallTheMenusCommands_NamingTheComponent() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var hero = Named(document, "Hero");
        var tools = services.GetServices<IAgentToolSource>().SelectMany(s => s.GetTools()).ToDictionary(t => t.Name);
        ToolResult Call(string id, string args) => EditorHost.Pump(tools[CommandTools.ToolName(id)].Invoke(ToolArgs.Parse(args)));

        var reset = tools[CommandTools.ToolName("Component.Reset")];
        Assert.Contains("On a component's menu in the Inspector", reset.Description, StringComparison.Ordinal);
        Assert.Contains("\"component\"", reset.InputSchema.ToJsonString(), StringComparison.Ordinal);

        // Naming the entity selects it; the component goes by the Inspector's name or its type.
        Assert.False(Call("Component.Reset", """{"entity": "Hero", "component": "Health"}""").IsError);
        Assert.Equal(new Health().Max, hero.GetComponent<Health>()!.Max);
        Assert.Same(hero, selection.Entities.Single());
        Call("Component.MoveUp", """{"component": "health"}""");
        Assert.Equal(["Transform", "Sprite", "Health", "Body"], inspector.Components.Select(c => c.Title));

        // Told why when it can't, or what there is when there's no such component.
        var first = Assert.Throws<AgentException>(() => Call("Component.MoveUp", """{"component": "Sprite"}"""));
        Assert.Contains("(Sprite is already first, under the Transform).", first.Message, StringComparison.Ordinal);
        var none = Assert.Throws<AgentException>(() => Call("Component.Reset", """{"component": "Light"}"""));
        Assert.Contains("The selection has no Light; it has Transform, Sprite, Health, Body.", none.Message, StringComparison.Ordinal);

        // Undone like any change to the scene.
        Assert.False(Call("Edit.Undo", "{}").IsError);
        Assert.Equal(["Transform", "Sprite", "Body", "Health"], inspector.Components.Select(c => c.Title));
    });
}
