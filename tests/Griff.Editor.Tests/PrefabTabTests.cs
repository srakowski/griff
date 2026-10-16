using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Hierarchy;
using Griff.Editor.Features.Inspector;
using Griff.Projects;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Prefabs opened on their own, in tabs of their own: the panels edit them, and saving one updates its copies.</summary>
public sealed class PrefabTabTests : IDisposable
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

    // An empty project with a Coin prefab (a sprite and a Pickup worth 1) and a copy of it placed in the open scene, and a
    // second scene, Level 2, holding another copy.
    private (Workspace Workspace, Project Project, Entity Copy) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;

        var coin = new Entity("Coin");
        coin.AddComponent<Sprite>().Image = "Sprites/coin.png";
        coin.AddComponent<Pickup>().Amount = 1;
        Directory.CreateDirectory(Path.GetDirectoryName(project.DefaultPrefabPath("Coin"))!);
        File.WriteAllText(project.DefaultPrefabPath("Coin"), workspace.Serializer.SavePrefabToString(coin));

        var level2 = new Scene();
        level2.Add(services.GetRequiredService<PrefabFiles>().Instantiate("Prefabs/Coin.prefab")!);
        File.WriteAllText(project.CreateScene("Level 2"), workspace.Serializer.SaveToString(level2));

        var copy = services.GetRequiredService<PrefabFiles>().Instantiate("Prefabs/Coin.prefab")!;
        copy.Transform.Position = new Vector3(4, 2, 0);
        workspace.Scene!.Execute(new AddEntityEdit(workspace.Scene.Scene, copy));
        return (workspace, project, copy);
    }

    private static Task Run(IServiceProvider services, string id, string answers = "{}") => RunCommand(services, id, answers);

    private static async Task RunCommand(IServiceProvider services, string id, string answers)
    {
        using (AgentDialogs.Answering(JsonDocument.Parse(answers).RootElement))
            await services.GetRequiredService<CommandRegistry>().Get(id).ExecuteAsync();
    }

    // Sets a Pickup's Amount through the document's undo history, as the Inspector does.
    private static void SetAmount(Workspace workspace, SceneDocument document, Entity entity, int amount)
    {
        var property = workspace.Components.Get(typeof(Pickup)).Properties.Single(p => p.Name == nameof(Pickup.Amount));
        document.Execute(new SetPropertyEdit(entity.GetComponent<Pickup>()!, property, amount));
    }

    [Fact]
    public void EditPrefab_OpensTheCopysPrefabInATabOfItsOwn_AndThePanelsEditIt() => EditorHost.Run(services =>
    {
        var (workspace, _, copy) = Open(services);
        var scene = workspace.Scene!;
        var selection = services.GetRequiredService<ISelection>();
        selection.Select(copy);

        Wait(Run(services, "Entity.EditPrefab"));

        var prefab = Assert.Single(workspace.Prefabs);
        Assert.Same(prefab, workspace.Scene);
        Assert.True(prefab.IsPrefab);
        Assert.Equal("Coin", prefab.Name);
        Assert.Same(scene, workspace.EditScene);

        // The prefab on its own: its entity, not linked to anything, selected so the Inspector shows it.
        var root = Assert.Single(prefab.Scene.Roots);
        Assert.Equal("Coin", root.Name);
        Assert.Null(root.Prefab);
        Assert.Equal(root, selection.Entity);
        Assert.Equal(root, Assert.Single(services.GetRequiredService<HierarchyPanel>().Roots).Entity);
        Assert.NotNull(services.GetRequiredService<InspectorPanel>().Header);

        // Back to the scene: its selection is as it was, and the prefab stays open in its tab.
        workspace.ShowScene();
        Assert.Same(scene, workspace.Scene);
        Assert.Equal(copy, selection.Entity);
        Assert.Single(workspace.Prefabs);

        // Opening it again goes to its tab, with what was selected there.
        Wait(workspace.OpenPrefabAsync("Coin"));
        Assert.Same(prefab, workspace.Scene);
        Assert.Equal(root, selection.Entity);
    });

    [Fact]
    public void SavingThePrefabsTab_UpdatesEveryCopy_KeepingWhatEachChanged() => EditorHost.Run(services =>
    {
        var (workspace, project, copy) = Open(services);
        var scene = workspace.Scene!;
        var changed = services.GetRequiredService<PrefabFiles>().Instantiate("Prefabs/Coin.prefab")!;
        changed.Name = "Red Coin";
        changed.GetComponent<Sprite>()!.Tint = new Color(1, 0, 0);
        scene.Execute(new AddEntityEdit(scene.Scene, changed));
        Wait(workspace.SaveSceneAsync());

        Assert.True(Wait(workspace.OpenPrefabAsync("Prefabs/Coin.prefab")));
        var prefab = workspace.Scene!;
        var root = prefab.Scene.Roots[0];
        SetAmount(workspace, prefab, root, 5);
        root.GetComponent<Sprite>()!.Tint = new Color(0, 0, 1);
        Assert.True(prefab.IsDirty);
        Assert.False(scene.IsDirty);

        Wait(Run(services, "Scene.Save"));

        // The file, the copies in the open scene (one undo step there) and the copy in Level 2 all take it; the red coin
        // stays red.
        Assert.False(prefab.IsDirty);
        Assert.Contains("\"amount\": 5", File.ReadAllText(project.DefaultPrefabPath("Coin")), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, copy.GetComponent<Pickup>()!.Amount);
        Assert.Equal(new Vector3(4, 2, 0), copy.Transform.Position);
        Assert.Equal(new Color(0, 0, 1), copy.GetComponent<Sprite>()!.Tint);
        Assert.Equal(5, changed.GetComponent<Pickup>()!.Amount);
        Assert.Equal(new Color(1, 0, 0), changed.GetComponent<Sprite>()!.Tint);
        Assert.True(scene.IsDirty);
        var level2 = workspace.Serializer.LoadFromString(File.ReadAllText(project.FindScene("Level 2")!));
        Assert.Equal(5, level2.Roots.Single().GetComponent<Pickup>()!.Amount);

        scene.History.Undo();
        Assert.Equal(1, copy.GetComponent<Pickup>()!.Amount);
    });

    [Fact]
    public void InAPrefabsTab_NewThingsGoInsideThePrefab_AndItSavesOnlyAsOneEntity() => EditorHost.Run(services =>
    {
        var (workspace, project, _) = Open(services);
        Wait(workspace.OpenPrefabAsync("Coin"));
        var prefab = workspace.Scene!;
        var root = prefab.Scene.Roots[0];
        var selection = services.GetRequiredService<ISelection>();

        // Made with nothing selected, it goes inside the prefab.
        selection.Select(null);
        Wait(Run(services, "Entity.CreateEmpty"));
        Assert.Equal(root, selection.Entity!.Parent);
        Assert.Single(prefab.Scene.Roots);

        // A second entity at the top can't be saved: a prefab is one entity.
        prefab.Execute(new AddEntityEdit(prefab.Scene, new Entity("Stray")));
        var before = File.ReadAllText(project.DefaultPrefabPath("Coin"));
        using (AgentDialogs.Answering(JsonDocument.Parse("{}").RootElement))
        {
            Assert.False(Wait(workspace.SaveSceneAsync()));
            Assert.Contains(AgentDialogs.Reported, e => e.Contains("Stray"));
        }

        Assert.Equal(before, File.ReadAllText(project.DefaultPrefabPath("Coin")));
        Assert.True(prefab.IsDirty);

        // Save as Prefab on the prefab's own entity saves the tab.
        Assert.Contains("Coin prefab", EditorHost.AgentUndo(services));
        selection.Select(root);
        Wait(Run(services, "Entity.SaveAsPrefab"));
        Assert.False(prefab.IsDirty);
        Assert.Single(workspace.Serializer.LoadPrefabFromString(File.ReadAllText(project.DefaultPrefabPath("Coin"))).Children);
    });

    [Fact]
    public void ClosingAPrefabWithChanges_AsksToSaveThem_AndShowsTheSceneAgain() => EditorHost.Run(services =>
    {
        var (workspace, project, copy) = Open(services);
        var scene = workspace.Scene!;
        Wait(workspace.OpenPrefabAsync("Coin"));
        var prefab = workspace.Scene!;
        SetAmount(workspace, prefab, prefab.Scene.Roots[0], 3);

        // Cancelling keeps it open.
        Wait(Run(services, "Prefab.Close", """{"Save changes": "Cancel"}"""));
        Assert.Same(prefab, workspace.Scene);

        Wait(Run(services, "Prefab.Close", """{"Save changes": "Save"}"""));
        Assert.Empty(workspace.Prefabs);
        Assert.Same(scene, workspace.Scene);
        Assert.Equal(3, copy.GetComponent<Pickup>()!.Amount);
        Assert.Contains("\"amount\": 3", File.ReadAllText(project.DefaultPrefabPath("Coin")), StringComparison.OrdinalIgnoreCase);
    });

    [Fact]
    public void SavingThePrefabFromACopy_UpdatesItsOpenTab() => EditorHost.Run(services =>
    {
        var (workspace, _, copy) = Open(services);
        Wait(workspace.OpenPrefabAsync("Coin"));
        var prefab = workspace.Scene!;
        workspace.ShowScene();

        copy.GetComponent<Pickup>()!.Amount = 8;
        services.GetRequiredService<ISelection>().Select(copy);
        Wait(Run(services, "Entity.SaveAsPrefab", """{"Save Prefab": true}"""));

        // The tab shows the new version, and as it had no changes of its own, it has nothing unsaved.
        Assert.Equal(8, prefab.Scene.Roots[0].GetComponent<Pickup>()!.Amount);
        Assert.False(prefab.IsDirty);
    });

    [Fact]
    public void PlayingShowsTheScene() => EditorHost.Run(services =>
    {
        var (workspace, _, _) = Open(services);
        var scene = workspace.Scene!;
        Wait(workspace.OpenPrefabAsync("Coin"));

        // Play mode copies EditScene (the game itself needs the main window, which tests don't have).
        Assert.Same(scene, workspace.EditScene);
        workspace.EnterPlayMode(new SceneDocument(new Scene(), scene.FilePath, isRuntime: true));
        Assert.True(workspace.Scene!.IsRuntime);
        Assert.Same(scene, workspace.EditScene);
        workspace.Show(workspace.Prefabs[0]);
        Assert.True(workspace.Scene!.IsRuntime);

        workspace.ExitPlayMode();
        Assert.Same(scene, workspace.Scene);
    });

    [Fact]
    public void InAPrefabsTab_NamesOfThingsInTheScene_ArentFlagged_AndAgentsSeeThePrefab() => EditorHost.Run(services =>
    {
        var (workspace, project, _) = Open(services);
        var enemy = new Entity("Enemy");
        enemy.AddComponent<Chase>().Target = "Player";
        File.WriteAllText(project.DefaultPrefabPath("Enemy"), workspace.Serializer.SavePrefabToString(enemy));

        Wait(workspace.OpenPrefabAsync("Enemy"));
        var root = workspace.Scene!.Scene.Roots[0];
        services.GetRequiredService<ISelection>().Select(root);
        var inspector = services.GetRequiredService<InspectorPanel>();
        Assert.True(inspector.InPrefab);
        var target = inspector.Components.Single(c => c.Title == "Chase").Properties.Single(p => p.Label == "Target");
        Assert.False(target.IsUnmatched);

        IAgentScene host = new EditorAgentScene(workspace, workspace, workspace, services.GetRequiredService<ISelection>());
        Assert.True(host.IsPrefab);
        Assert.Equal("Prefabs/Enemy.prefab", host.ScenePath);
    });
}
