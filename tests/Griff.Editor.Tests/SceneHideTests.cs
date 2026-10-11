using System.Numerics;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Hierarchy;
using Griff.Editor.Features.SceneView;
using Griff.Rendering;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Hiding entities in the Scene view while editing, so they stop covering the level.</summary>
public class SceneHideTests
{
    // Menu is a 20-unit square sprite at the origin, drawn over everything, holding a bare Title at -5,5. Coin is a
    // one-unit sprite at 3,0 under it.
    private static (SceneDocument Document, ISelection Selection, SceneViewModel Model) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var coin = new Entity("Coin");
        coin.Transform.Position = new Vector3(3, 0, 0);
        coin.AddComponent<Sprite>().Size = Vector2.One;
        scene.Add(coin);
        var menu = new Entity("Menu");
        var panel = menu.AddComponent<Sprite>();
        (panel.Size, panel.Order) = (new Vector2(20, 20), 10);
        scene.Add(menu);
        var title = new Entity("Title");
        title.Transform.Position = new Vector3(-5, 5, 0);
        scene.Add(title, menu);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<SceneViewModel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static string[] Names(IEnumerable<Entity> entities) => entities.Select(e => e.Name).ToArray();

    private static EntityNode Node(HierarchyPanel panel, string name) => All(panel.Roots).Single(n => n.Name == name);

    private static IEnumerable<EntityNode> All(IEnumerable<EntityNode> nodes) => nodes.SelectMany(n => All(n.Children).Prepend(n));

    [Fact]
    public void AHiddenEntity_AndWhatsInsideIt_AreNotDrawn_ButTheGameStillDrawsThem() => EditorHost.Run(services =>
    {
        var (document, _, _) = Open(services);
        var menu = Named(document, "Menu");
        Assert.Equal(["Coin", "Menu", "Title"], Names(SceneViewModel.DrawOrder(document.Scene)));

        menu.IsHidden = true;
        Assert.Equal(["Coin"], Names(SceneViewModel.DrawOrder(document.Scene)));
        Assert.False(SceneViewModel.IsShown(Named(document, "Title")));
        Assert.Contains(menu, SceneRenderer.DrawOrder(document.Scene));
    });

    [Fact]
    public void Clicks_AndBoxes_PassOverAHiddenEntity_ToWhatsUnderIt() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        Assert.Equal("Menu", model.EntityAt(new Vector2(3, 0), pivotRadius: 0.2f)?.Name);

        Named(document, "Menu").IsHidden = true;
        Assert.Equal("Coin", model.SelectAt(new Vector2(3.2f, 0.2f))?.Name);
        Assert.Null(model.EntityAt(new Vector2(-5, 5), pivotRadius: 0.2f));

        model.SelectInBox(new Vector2(-11, -11), new Vector2(11, 11));
        Assert.Equal(["Coin"], Names(selection.Entities));
    });

    [Fact]
    public void HideCommand_HidesTheSelection_ShowsWhenAllAreHidden_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var command = services.GetRequiredService<CommandRegistry>().Get("Entity.Hide");
        var (menu, coin) = (Named(document, "Menu"), Named(document, "Coin"));
        coin.IsHidden = true;

        // Some hidden: hide them all.
        selection.SelectEntities([menu, coin]);
        Assert.False(command.IsChecked);
        command.ExecuteAsync().GetAwaiter().GetResult();
        Assert.True(menu.IsHidden && coin.IsHidden);
        Assert.True(command.IsChecked);
        Assert.Equal("Hide Menu", document.History.UndoDescription);

        // All hidden: show them all.
        command.ExecuteAsync().GetAwaiter().GetResult();
        Assert.False(menu.IsHidden || coin.IsHidden);
        Assert.Equal("Show 2 entities", document.History.UndoDescription);

        document.History.Undo();
        document.History.Undo();
        Assert.False(menu.IsHidden);
        Assert.True(coin.IsHidden);
    });

    [Fact]
    public void HierarchyEye_HidesItsRow_OrTheWholeSelection() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var panel = services.GetRequiredService<HierarchyPanel>();
        var (menu, coin) = (Named(document, "Menu"), Named(document, "Coin"));

        // An unselected row hides just itself; its children show they're covered.
        selection.Select(coin);
        Node(panel, "Menu").IsHidden = true;
        Assert.True(menu.IsHidden);
        Assert.False(coin.IsHidden);
        Assert.True(Node(panel, "Menu").IsHidden);
        Assert.True(Node(panel, "Title").IsHiddenByParent);
        Assert.False(Node(panel, "Menu").IsHiddenByParent);

        // A selected row takes the selection with it.
        selection.SelectEntities([menu, coin]);
        Node(panel, "Coin").IsHidden = true;
        Assert.True(coin.IsHidden);
        Node(panel, "Menu").IsHidden = false;
        Assert.False(menu.IsHidden || coin.IsHidden);
        Assert.False(Node(panel, "Title").IsHiddenByParent);
    });

    [Fact]
    public void AHiddenTilemap_IsntPainted_AndPickingAHiddenLayerShowsIt() => EditorHost.Run(services =>
    {
        var scene = new Scene();
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        (map.Image, map.Columns, map.Rows) = ("Tiles/ground.png", 4, 2);
        scene.Add(level);
        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        var selection = services.GetRequiredService<ISelection>();
        var model = services.GetRequiredService<SceneViewModel>();
        selection.Select(level);

        model.AddLayer();
        var layer = level.Children.Single();
        model.ShowLayer(0);
        layer.IsHidden = true;
        Assert.Same(map, model.PaintTarget);

        // Hidden, it can't be painted unseen; picking its tab shows it again, as an undo step of its own.
        model.ShowLayer(1);
        Assert.False(layer.IsHidden);
        Assert.Same(layer, model.PaintTarget?.Entity);
        Assert.Equal("Show Level Layer 2", document.History.UndoDescription);

        level.IsHidden = true;
        Assert.Null(model.PaintTarget);
    });

    [Fact]
    public void Agents_GetAHideTool() => EditorHost.Run(services =>
    {
        Open(services);
        var tool = services.GetServices<Griff.Agent.IAgentToolSource>().SelectMany(s => s.GetTools())
            .Single(t => t.Name == CommandTools.ToolName("Entity.Hide"));
        Assert.Contains("aren't drawn, clicked or box selected", tool.Description);
    });
}
