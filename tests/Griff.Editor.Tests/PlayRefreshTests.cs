using System.Numerics;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Hierarchy;
using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The panels keep up with a big scene while it plays without slowing it: they catch up with the game ten times a second
/// rather than every frame, and the Hierarchy keeps the rows of entities still there rather than making them all again.
/// </summary>
public class PlayRefreshTests
{
    private static SceneDocument Play(IServiceProvider services)
    {
        var scene = new Scene();
        var player = new Entity("Player");
        scene.Add(player);
        scene.Add(new Entity("Sword"), player);
        scene.Add(new Entity("Coin"));
        var document = new SceneDocument(scene, "Test.scene", isRuntime: true);
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return document;
    }

    [Fact]
    public void The_Hierarchy_catches_up_with_what_the_game_spawns_on_a_refresh_keeping_its_rows() => EditorHost.Run(services =>
    {
        var document = Play(services);
        var hierarchy = services.GetRequiredService<HierarchyPanel>();
        var player = hierarchy.Roots[0];
        var coin = hierarchy.Roots[1];
        var resets = 0;
        hierarchy.Roots.CollectionChanged += (_, e) => resets += e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset ? 1 : 0;

        // The game pours in thousands: frames go by without the tree, and a refresh shows them all at once.
        for (var i = 0; i < 5000; i++)
            document.Scene.Add(new Entity($"Bunny {i}"));
        document.NotifyRuntimeTick();
        Assert.Equal(2, hierarchy.Roots.Count);
        document.NotifyRuntimeRefresh();
        Assert.Equal(5002, hierarchy.Roots.Count);
        Assert.Equal(1, resets);
        Assert.Same(player, hierarchy.Roots[0]);
        Assert.Same(coin, hierarchy.Roots[1]);
        Assert.Equal("Sword", Assert.Single(player.Children).Name);

        // A few more are added on the end; one destroyed takes its row out, and the rest stay as they were.
        var bunny = hierarchy.Roots[2];
        document.Scene.Add(new Entity("Late"));
        document.NotifyRuntimeRefresh();
        Assert.Equal(1, resets);
        Assert.Equal("Late", hierarchy.Roots[^1].Name);
        document.Scene.Remove(coin.Entity);
        document.NotifyRuntimeRefresh();
        Assert.Equal(5002, hierarchy.Roots.Count);
        Assert.DoesNotContain(coin, hierarchy.Roots);
        Assert.Same(bunny, hierarchy.Roots[1]);
    });

    [Fact]
    public void A_row_kept_through_a_change_reads_its_entity_again() => EditorHost.Run(services =>
    {
        var document = Play(services);
        var hierarchy = services.GetRequiredService<HierarchyPanel>();
        var player = hierarchy.Roots[0];
        var changed = new List<string?>();
        player.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        player.Entity.Name = "Hero";
        document.Scene.Add(new Entity("Arrow"));
        document.NotifyRuntimeRefresh();

        Assert.Same(player, hierarchy.Roots[0]);
        Assert.Contains(string.Empty, changed);
        Assert.Equal("Hero", player.Name);
    });

    [Fact]
    public void The_Inspector_shows_live_values_on_a_refresh_not_every_frame() => EditorHost.Run(services =>
    {
        var document = Play(services);
        var player = document.Scene.Roots[0];
        services.GetRequiredService<ISelection>().Select(player);
        var inspector = services.GetRequiredService<InspectorPanel>();
        var position = inspector.Components.Single(c => c.IsNamed("Transform")).Properties.First(p => p.Label == "Position");
        var updates = 0;
        position.PropertyChanged += (_, _) => updates++;

        player.Transform.Position = new Vector3(4, 2, 0);
        document.NotifyRuntimeTick();
        Assert.Equal(0, updates);

        document.NotifyRuntimeRefresh();
        Assert.True(updates > 0);
    });
}
