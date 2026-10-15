using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Entities;
using Griff.Editor.Features.Hierarchy;
using Griff.Editor.Features.Inspector;
using Griff.Projects;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Placed copies of prefabs: they remember their prefab, and saving it updates every copy in every scene.</summary>
public sealed class PrefabLinkTests : IDisposable
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

    // An empty project with a Coin prefab (a sprite and a Pickup worth 1), open in the editor, and a second scene, Level 2,
    // holding a copy of the coin.
    private (IWorkspace Workspace, Project Project) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;

        var coin = new Entity("Coin");
        coin.AddComponent<Sprite>().Image = "Sprites/coin.png";
        coin.AddComponent<Pickup>().Amount = 1;
        Directory.CreateDirectory(Path.GetDirectoryName(project.DefaultPrefabPath("Coin"))!);
        File.WriteAllText(project.DefaultPrefabPath("Coin"), workspace.Serializer.SavePrefabToString(coin));

        var level2 = new Scene();
        var copy = PrefabFiles.Instantiate(workspace, "Prefabs/Coin.prefab")!;
        copy.Transform.Position = new Vector3(7, 0, 0);
        level2.Add(copy);
        level2.Add(new Entity("Player"));
        File.WriteAllText(project.CreateScene("Level 2"), workspace.Serializer.SaveToString(level2));
        return (workspace, project);
    }

    private static Entity Place(IWorkspace workspace, string name, Vector3 position)
    {
        var copy = PrefabFiles.Instantiate(workspace, "Prefabs/Coin.prefab")!;
        copy.Name = name;
        copy.Transform.Position = position;
        workspace.Scene!.Execute(new Griff.Editing.AddEntityEdit(workspace.Scene.Scene, copy));
        return copy;
    }

    private static Scene Load(IWorkspace workspace, Project project, string scene) =>
        workspace.Serializer.LoadFromString(File.ReadAllText(project.FindScene(scene)!));

    private static Task Run(IServiceProvider services, string id, string answers) =>
        RunCommand(services, id, answers);

    private static async Task RunCommand(IServiceProvider services, string id, string answers)
    {
        using (AgentDialogs.Answering(JsonDocument.Parse(answers).RootElement))
            await services.GetRequiredService<CommandRegistry>().Get(id).ExecuteAsync();
    }

    [Fact]
    public void PlacedCopies_RememberTheirPrefab() => EditorHost.Run(services =>
    {
        var (workspace, _) = Open(services);
        var copy = Place(workspace, "Coin", Vector3.Zero);
        Assert.Equal("Prefabs/Coin.prefab", copy.Prefab);

        services.GetRequiredService<ISelection>().Select(copy);
        var inspector = services.GetRequiredService<InspectorPanel>();
        Assert.Equal("Copy of the Coin prefab", inspector.Header!.PrefabNote);
        var row = services.GetRequiredService<HierarchyPanel>().Roots.Single(n => n.Entity == copy);
        Assert.True(row.IsPrefabCopy);
        Assert.Contains("Coin prefab", row.PrefabTip);

        // An agent's copies are linked too.
        var host = new EditorAgentScene(workspace, services.GetRequiredService<ISelection>());
        Assert.Equal("Prefabs/Coin.prefab", host.InstantiatePrefab("Coin")!.Prefab);
    });

    [Fact]
    public void SavingACopy_UpdatesEveryOtherCopy_HereAsOneUndoStep_AndInOtherScenes() => EditorHost.Run(services =>
    {
        var (workspace, project) = Open(services);
        var document = workspace.Scene!;
        var edited = Place(workspace, "Big Coin", new Vector3(1, 0, 0));
        var other = Place(workspace, "Coin", new Vector3(-3, 2, 0));
        var otherId = other.Id;

        edited.GetComponent<Pickup>()!.Amount = 10;
        services.GetRequiredService<ISelection>().Select(edited);
        Wait(Run(services, "Entity.SaveAsPrefab", """{"Save Prefab": true}"""));

        // The prefab is saved over (not as "Big Coin"), and the other copy here takes it, staying where it was.
        Assert.False(File.Exists(project.DefaultPrefabPath("Big Coin")));
        Assert.Contains("\"amount\": 10", File.ReadAllText(project.DefaultPrefabPath("Coin")), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, other.GetComponent<Pickup>()!.Amount);
        Assert.Equal(("Coin", otherId, new Vector3(-3, 2, 0)), (other.Name, other.Id, other.Transform.Position));
        Assert.Equal("Update Coin from its prefab", document.History.UndoDescription);

        // Level 2's copy is updated on disk.
        var level2 = Load(workspace, project, "Level 2");
        var copy = level2.Roots.Single(e => e.Prefab is not null);
        Assert.Equal(10, copy.GetComponent<Pickup>()!.Amount);
        Assert.Equal(new Vector3(7, 0, 0), copy.Transform.Position);

        document.History.Undo();
        Assert.Equal(1, other.GetComponent<Pickup>()!.Amount);
    });

    [Fact]
    public void SavingAnOrdinaryEntity_OverAPrefab_LinksIt_AndUpdatesTheCopies() => EditorHost.Run(services =>
    {
        var (workspace, project) = Open(services);
        var copy = Place(workspace, "Placed", Vector3.Zero);

        var coin = new Entity("Coin");
        coin.AddComponent<Pickup>().Amount = 3;
        workspace.Scene!.Execute(new Griff.Editing.AddEntityEdit(workspace.Scene.Scene, coin));
        services.GetRequiredService<ISelection>().Select(coin);
        Wait(Run(services, "Entity.SaveAsPrefab", """{"Replace Prefab": true}"""));

        Assert.Equal("Prefabs/Coin.prefab", coin.Prefab);
        Assert.Equal(3, copy.GetComponent<Pickup>()!.Amount);
        Assert.Null(copy.GetComponent<Sprite>());
        Assert.Equal("Save Coin as Prefab", workspace.Scene.History.UndoDescription);

        workspace.Scene.History.Undo();
        Assert.Null(coin.Prefab);
        Assert.NotNull(copy.GetComponent<Sprite>());

        // Saying no leaves everything as it was.
        coin.GetComponent<Pickup>()!.Amount = 8;
        var saved = File.ReadAllText(project.DefaultPrefabPath("Coin"));
        Wait(Run(services, "Entity.SaveAsPrefab", """{"Replace Prefab": false}"""));
        Assert.Null(coin.Prefab);
        Assert.Equal(saved, File.ReadAllText(project.DefaultPrefabPath("Coin")));
        Assert.Equal(1, copy.GetComponent<Pickup>()!.Amount);
    });

    [Fact]
    public void Unlink_MakesCopiesOrdinary_SoSavingThePrefabLeavesThemAlone() => EditorHost.Run(services =>
    {
        var (workspace, _) = Open(services);
        var kept = Place(workspace, "Kept", Vector3.Zero);
        var source = Place(workspace, "Source", Vector3.One);
        var selection = services.GetRequiredService<ISelection>();
        var commands = services.GetRequiredService<CommandRegistry>();

        selection.Select(kept);
        Assert.True(commands.Get("Entity.UnlinkPrefab").CanExecute());
        Wait(Run(services, "Entity.UnlinkPrefab", "{}"));
        Assert.Null(kept.Prefab);
        Assert.Equal("Unlink Kept", workspace.Scene!.History.UndoDescription);
        Assert.False(commands.Get("Entity.UnlinkPrefab").CanExecute());

        source.GetComponent<Pickup>()!.Amount = 4;
        selection.Select(source);
        Wait(Run(services, "Entity.SaveAsPrefab", """{"Save Prefab": true}"""));
        Assert.Equal(1, kept.GetComponent<Pickup>()!.Amount);
    });

    [Fact]
    public void APrefabFileAnAgentWrites_UpdatesItsCopies() => EditorHost.Run(services =>
    {
        var (workspace, project) = Open(services);
        var copy = Place(workspace, "Coin", Vector3.Zero);

        var coin = new Entity("Coin");
        coin.AddComponent<Pickup>().Amount = 25;
        File.WriteAllText(project.DefaultPrefabPath("Coin"), workspace.Serializer.SavePrefabToString(coin));
        var feedback = Wait(new EditorAgentProject(workspace).AssetChangedAsync("Prefabs/Coin.prefab"));

        Assert.Equal("Updated 2 copies in Main and Level 2.", feedback!.Message);
        Assert.Equal(25, copy.GetComponent<Pickup>()!.Amount);
        Assert.Equal(25, Load(workspace, project, "Level 2").Roots.Single(e => e.Prefab is not null).GetComponent<Pickup>()!.Amount);
    });
}
