using System.Numerics;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Hierarchy;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Locking entities so the Scene view's clicks and box selections pass through them.</summary>
public class SceneLockTests
{
    // Backdrop is a 20-unit square sprite at the origin, drawn behind everything, holding a bare Stars at -5,5. Coin is a
    // one-unit sprite at 3,0 in front of it.
    private static (SceneDocument Document, ISelection Selection, SceneViewModel Model) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var backdrop = new Entity("Backdrop");
        backdrop.AddComponent<Sprite>().Size = new Vector2(20, 20);
        scene.Add(backdrop);
        var stars = new Entity("Stars");
        stars.Transform.Position = new Vector3(-5, 5, 0);
        scene.Add(stars, backdrop);
        var coin = new Entity("Coin");
        coin.Transform.Position = new Vector3(3, 0, 0);
        coin.AddComponent<Sprite>().Size = Vector2.One;
        scene.Add(coin);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<Workspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<SceneViewModel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static string[] Names(ISelection selection) => selection.Entities.Select(e => e.Name).ToArray();

    private static EntityNode Node(HierarchyPanel panel, string name) => All(panel.Roots).Single(n => n.Name == name);

    private static IEnumerable<EntityNode> All(IEnumerable<EntityNode> nodes) => nodes.SelectMany(n => All(n.Children).Prepend(n));

    [Fact]
    public void Clicks_PassThroughALockedEntity_ToWhatsBehind() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var backdrop = Named(document, "Backdrop");

        Assert.Same(backdrop, model.EntityAt(new Vector2(-2, -2), pivotRadius: 0.2f));
        Assert.Same(Named(document, "Stars"), model.EntityAt(new Vector2(-5, 5), pivotRadius: 0.2f));

        backdrop.IsLocked = true;
        // The backdrop is clicked through to empty space, and its child is locked along with it.
        Assert.Null(model.EntityAt(new Vector2(-2, -2), pivotRadius: 0.2f));
        Assert.Null(model.EntityAt(new Vector2(-5, 5), pivotRadius: 0.2f));
        Assert.Same(Named(document, "Coin"), model.SelectAt(new Vector2(3.2f, 0.2f)));

        // A click on the locked backdrop is a click on empty space: it clears the selection.
        Assert.Null(model.SelectAt(new Vector2(-2, -2)));
        Assert.Empty(selection.Entities);
    });

    [Fact]
    public void Box_LeavesOutLockedEntities_AndWhatsInsideThem() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);

        model.SelectInBox(new Vector2(-11, -11), new Vector2(11, 11));
        Assert.Equal(["Backdrop", "Stars", "Coin"], Names(selection));

        Named(document, "Backdrop").IsLocked = true;
        model.SelectInBox(new Vector2(-11, -11), new Vector2(11, 11));
        Assert.Equal(["Coin"], Names(selection));
    });

    [Fact]
    public void LockCommand_LocksTheSelection_UnlocksWhenAllAreLocked_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var command = services.GetRequiredService<CommandRegistry>().Get("Entity.Lock");
        var (backdrop, coin) = (Named(document, "Backdrop"), Named(document, "Coin"));
        coin.IsLocked = true;

        // Some locked: lock them all.
        selection.SelectEntities([backdrop, coin]);
        Assert.False(command.IsChecked);
        command.ExecuteAsync().GetAwaiter().GetResult();
        Assert.True(backdrop.IsLocked && coin.IsLocked);
        Assert.True(command.IsChecked);
        Assert.Equal("Lock Backdrop", document.History.UndoDescription);

        // All locked: unlock them all.
        command.ExecuteAsync().GetAwaiter().GetResult();
        Assert.False(backdrop.IsLocked || coin.IsLocked);
        Assert.Equal("Unlock 2 entities", document.History.UndoDescription);

        document.History.Undo();
        document.History.Undo();
        Assert.False(backdrop.IsLocked);
        Assert.True(coin.IsLocked);
    });

    [Fact]
    public void HierarchyToggle_LocksItsRow_OrTheWholeSelection() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var panel = services.GetRequiredService<HierarchyPanel>();
        var (backdrop, coin) = (Named(document, "Backdrop"), Named(document, "Coin"));

        // An unselected row locks just itself; its children show they're covered.
        selection.Select(coin);
        Node(panel, "Backdrop").IsLocked = true;
        Assert.True(backdrop.IsLocked);
        Assert.False(coin.IsLocked);
        Assert.True(Node(panel, "Backdrop").IsLocked);
        Assert.True(Node(panel, "Stars").IsLockedByParent);
        Assert.False(Node(panel, "Backdrop").IsLockedByParent);

        // A selected row takes the selection with it.
        selection.SelectEntities([backdrop, coin]);
        Node(panel, "Coin").IsLocked = true;
        Assert.True(coin.IsLocked);
        Node(panel, "Backdrop").IsLocked = false;
        Assert.False(backdrop.IsLocked || coin.IsLocked);
        Assert.False(Node(panel, "Stars").IsLockedByParent);
    });

    [Fact]
    public void Agents_GetALockTool_AndClickInTheSceneView() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var sources = services.GetServices<Griff.Agent.IAgentToolSource>().ToList();
        var model = sources.OfType<SurfaceTools>().Single().All().Single(r => r.Name == nameof(SceneViewModel)).Target;

        var tool = sources.SelectMany(s => s.GetTools()).Single(t => t.Name == CommandTools.ToolName("Entity.Lock"));
        Assert.Contains("clicks and box selections pass through", tool.Description);

        Named(document, "Backdrop").IsLocked = true;
        using var point = System.Text.Json.JsonDocument.Parse("[[3, 0]]");
        Griff.Agent.ObjectSurface.InvokeAsync(model, "SelectAt", point.RootElement).GetAwaiter().GetResult();
        Assert.Equal(["Coin"], Names(selection));
    });
}
