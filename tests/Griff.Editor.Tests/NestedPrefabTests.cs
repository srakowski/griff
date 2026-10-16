using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows.Threading;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Projects;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Copies of a prefab placed inside other prefabs (coins in a chest) follow it when it's saved.</summary>
public sealed class NestedPrefabTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // Runs a task to the end on the test's WPF thread, as the editor's dispatcher would.
    private static T Wait<T>(Task<T> task)
    {
        return EditorHost.Pump(task);
    }

    private static void Wait(Task task) => Wait(task.ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }, TaskScheduler.Default));

    // A project with a Coin prefab (worth 1), a Chest prefab holding a coin it made gold, a copy of the coin placed in the
    // open scene and a copy of the chest beside it.
    private (Workspace Workspace, Project Project, Entity Coin, Entity Chest) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;

        var coin = new Entity("Coin");
        coin.AddComponent<Sprite>().Tint = new Color(1, 1, 0);
        coin.AddComponent<Pickup>().Amount = 1;
        Directory.CreateDirectory(Path.GetDirectoryName(project.DefaultPrefabPath("Coin"))!);
        File.WriteAllText(project.DefaultPrefabPath("Coin"), workspace.Serializer.SavePrefabToString(coin));

        var chest = new Scene();
        var root = new Entity("Chest");
        chest.Add(root);
        var inside = services.GetRequiredService<PrefabFiles>().Instantiate("Prefabs/Coin.prefab")!;
        inside.GetComponent<Sprite>()!.Tint = new Color(1, 0.8f, 0);
        chest.Add(inside, root);
        File.WriteAllText(project.DefaultPrefabPath("Chest"), workspace.Serializer.SavePrefabToString(root));

        var scene = workspace.Scene!;
        var placedCoin = services.GetRequiredService<PrefabFiles>().Instantiate("Prefabs/Coin.prefab")!;
        scene.Execute(new AddEntityEdit(scene.Scene, placedCoin));
        var placedChest = services.GetRequiredService<PrefabFiles>().Instantiate("Prefabs/Chest.prefab")!;
        placedChest.Transform.Position = new Vector3(4, 0, 0);
        scene.Execute(new AddEntityEdit(scene.Scene, placedChest));
        return (workspace, project, placedCoin, placedChest);
    }

    private static async Task Run(IServiceProvider services, string id, string answers)
    {
        using (AgentDialogs.Answering(JsonDocument.Parse(answers).RootElement))
            await services.GetRequiredService<CommandRegistry>().Get(id).ExecuteAsync();
    }

    private static Entity LoadPrefab(Workspace workspace, Project project, string name) =>
        workspace.Serializer.LoadPrefabFromString(File.ReadAllText(project.DefaultPrefabPath(name)));

    [Fact]
    public void SavingAPrefab_UpdatesItsCopiesInsideOtherPrefabs_KeepingWhatTheyChanged() => EditorHost.Run(services =>
    {
        var (workspace, project, coin, chest) = Open(services);

        coin.GetComponent<Pickup>()!.Amount = 5;
        services.GetRequiredService<ISelection>().Select(coin);
        Wait(Run(services, "Entity.SaveAsPrefab", """{"Save Prefab": true}"""));

        // The chest prefab's coin is worth 5 now, and still gold, as the chest made it.
        var inside = LoadPrefab(workspace, project, "Chest").Children.Single();
        Assert.Equal(5, inside.GetComponent<Pickup>()!.Amount);
        Assert.Equal(new Color(1, 0.8f, 0), inside.GetComponent<Sprite>()!.Tint);
        Assert.Equal("Prefabs/Coin.prefab", inside.Prefab);

        // The placed chest's coin is updated too, so the chest copy is just like its prefab.
        Assert.Equal(5, chest.Children.Single().GetComponent<Pickup>()!.Amount);
        Assert.Empty(PrefabOverrides.Find(chest, LoadPrefab(workspace, project, "Chest"), workspace.Components));
    });

    [Fact]
    public void SavingAPrefabsTab_UpdatesCopiesInsideAnotherPrefabsOpenTab() => EditorHost.Run(services =>
    {
        var (workspace, project, _, _) = Open(services);
        Wait(workspace.OpenPrefabAsync("Chest"));
        var chestTab = workspace.Scene!;
        Wait(workspace.OpenPrefabAsync("Coin"));
        var coinTab = workspace.Scene!;
        var amount = workspace.Components.Get(typeof(Pickup)).Properties.Single(p => p.Name == nameof(Pickup.Amount));
        coinTab.Execute(new SetPropertyEdit(coinTab.Scene.Roots[0].GetComponent<Pickup>()!, amount, 9));

        Wait(workspace.SaveSceneAsync());

        // The chest's tab shows it as one undo step, and as its file was updated too, it has nothing unsaved.
        Assert.Equal(9, chestTab.Scene.Roots[0].Children.Single().GetComponent<Pickup>()!.Amount);
        Assert.False(chestTab.IsDirty);
        Assert.NotNull(chestTab.History.UndoDescription);
        Assert.Equal(9, LoadPrefab(workspace, project, "Chest").Children.Single().GetComponent<Pickup>()!.Amount);
    });

    [Fact]
    public void PrefabsHoldingEachOther_AreLeftAlone() => EditorHost.Run(services =>
    {
        var (workspace, project, _, _) = Open(services);

        // A coin holding a chest, which holds a coin: saving the coin can't go into the chest.
        Wait(workspace.OpenPrefabAsync("Coin"));
        var coinTab = workspace.Scene!;
        coinTab.Execute(new AddEntityEdit(coinTab.Scene, services.GetRequiredService<PrefabFiles>().Instantiate("Prefabs/Chest.prefab")!, coinTab.Scene.Roots[0]));
        var before = File.ReadAllText(project.DefaultPrefabPath("Chest"));

        using (AgentDialogs.Answering(JsonDocument.Parse("{}").RootElement))
        {
            Assert.True(Wait(workspace.SaveSceneAsync()));
            Assert.Contains(AgentDialogs.Reported, e => e.Contains("hold copies of each other"));
        }

        Assert.Equal(before, File.ReadAllText(project.DefaultPrefabPath("Chest")));
    });
}
