using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Assets;
using Griff.Projects;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Renaming and moving prefabs in Assets: every copy stays linked, wherever it is.</summary>
public sealed class PrefabMoveTests : IDisposable
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

    // A project with a Coin prefab, a Chest prefab holding a copy of the coin, a copy placed (and saved) in the open
    // scene, and another in a second scene, Level 2.
    private (IWorkspace Workspace, Project Project, Entity Copy) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;

        var coin = new Entity("Coin");
        coin.AddComponent<Pickup>().Amount = 1;
        Directory.CreateDirectory(Path.GetDirectoryName(project.DefaultPrefabPath("Coin"))!);
        File.WriteAllText(project.DefaultPrefabPath("Coin"), workspace.Serializer.SavePrefabToString(coin));

        var chest = new Scene();
        var root = new Entity("Chest");
        chest.Add(root);
        chest.Add(PrefabFiles.Instantiate(workspace, "Prefabs/Coin.prefab")!, root);
        File.WriteAllText(project.DefaultPrefabPath("Chest"), workspace.Serializer.SavePrefabToString(root));

        var level2 = new Scene();
        level2.Add(PrefabFiles.Instantiate(workspace, "Prefabs/Coin.prefab")!);
        File.WriteAllText(project.CreateScene("Level 2"), workspace.Serializer.SaveToString(level2));

        var copy = PrefabFiles.Instantiate(workspace, "Prefabs/Coin.prefab")!;
        copy.Transform.Position = new Vector3(3, 1, 0);
        workspace.Scene!.Execute(new AddEntityEdit(workspace.Scene.Scene, copy));
        Wait(workspace.SaveSceneAsync());
        return (workspace, project, copy);
    }

    private static T Answering<T>(string answers, Func<T> action)
    {
        using (AgentDialogs.Answering(JsonDocument.Parse(answers).RootElement))
            return action();
    }

    [Fact]
    public void MovingAPrefabIntoAFolder_KeepsEveryCopyLinked_InScenesAndOtherPrefabs() => EditorHost.Run(services =>
    {
        var (workspace, project, copy) = Open(services);
        var scene = workspace.Scene!;
        var assets = services.GetRequiredService<AssetsPanel>();

        var folder = Answering("""{"Folder name": "Money"}""", () => assets.NewFolder("Prefabs"));
        Assert.Equal("Prefabs/Money", folder);
        var message = assets.MovePrefab("Prefabs/Coin.prefab", "Prefabs/Money");

        Assert.True(File.Exists(Path.Combine(project.AssetsPath, "Prefabs", "Money", "Coin.prefab")));
        Assert.False(File.Exists(project.DefaultPrefabPath("Coin")));
        Assert.Contains("Relinked 3 copies", message);

        // The open scene, through its undo history; as its file is relinked too, it has nothing unsaved.
        Assert.Equal("Prefabs/Money/Coin.prefab", copy.Prefab);
        Assert.False(scene.IsDirty);
        Assert.Equal("Relink a copy of Coin", scene.History.UndoDescription);
        var saved = workspace.Serializer.LoadFromString(File.ReadAllText(scene.FilePath));
        Assert.Equal("Prefabs/Money/Coin.prefab", saved.Roots.Single(e => e.Name == "Coin").Prefab);

        // Another scene, and the coin inside the chest prefab.
        var level2 = workspace.Serializer.LoadFromString(File.ReadAllText(project.FindScene("Level 2")!));
        Assert.Equal("Prefabs/Money/Coin.prefab", level2.Roots.Single().Prefab);
        var chest = workspace.Serializer.LoadPrefabFromString(File.ReadAllText(project.DefaultPrefabPath("Chest")));
        Assert.Equal("Prefabs/Money/Coin.prefab", chest.Children.Single().Prefab);
    });

    [Fact]
    public void RenamingAPrefab_RenamesItsOpenTab_AndItSavesToItsNewFile() => EditorHost.Run(services =>
    {
        var (workspace, project, copy) = Open(services);
        Wait(workspace.OpenPrefabAsync("Coin"));
        var tab = workspace.Scene!;
        var assets = services.GetRequiredService<AssetsPanel>();

        Answering("""{"Prefab name": "Gold Coin"}""", () => assets.RenamePrefab("Prefabs/Coin.prefab"));

        Assert.Equal("Gold Coin", tab.Name);
        Assert.Equal("Prefabs/Gold Coin.prefab", copy.Prefab);

        // Saving the tab writes the renamed file and updates the copies linked to it.
        var amount = workspace.Components.Get(typeof(Pickup)).Properties.Single(p => p.Name == nameof(Pickup.Amount));
        tab.Execute(new SetPropertyEdit(tab.Scene.Roots[0].GetComponent<Pickup>()!, amount, 7));
        Wait(workspace.SaveSceneAsync());
        Assert.False(File.Exists(project.DefaultPrefabPath("Coin")));
        Assert.Contains("\"amount\": 7", File.ReadAllText(project.DefaultPrefabPath("Gold Coin")), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7, copy.GetComponent<Pickup>()!.Amount);

        // A name that's taken is refused.
        File.WriteAllText(project.DefaultPrefabPath("Gem"), "{}");
        Assert.ThrowsAny<AgentException>(() => Answering("""{"Prefab name": "Gem"}""", () => assets.RenamePrefab("Prefabs/Gold Coin.prefab")));
    });

    [Fact]
    public void AnAgentsMove_RelinksCopiesToo() => EditorHost.Run(services =>
    {
        var (workspace, _, copy) = Open(services);
        var tools = new AssetTools(new EditorAgentProject(workspace)).GetTools().ToDictionary(t => t.Name);

        var result = tools["asset_move"].Invoke(ToolArgs.Parse("""{"from": "Prefabs/Coin.prefab", "to": "Pickups/Coin.prefab"}""")).GetAwaiter().GetResult();

        Assert.False(result.IsError, result.Text);
        Assert.Contains("Relinked 3 copies", result.Text);
        Assert.Equal("Pickups/Coin.prefab", copy.Prefab);
    });
}
