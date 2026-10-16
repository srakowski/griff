using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Entities;
using Griff.Editor.Features.Inspector;
using Griff.Projects;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// What placed copies change from their prefab: marked in the Inspector, kept when the prefab is saved, and reverted or
/// applied to the prefab field by field.
/// </summary>
public sealed class PrefabOverrideTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static T Wait<T>(Task<T> task)
    {
        return EditorHost.Pump(task);
    }

    private static void Wait(Task task) => Wait(task.ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }, TaskScheduler.Default));

    // An empty project with a Coin prefab (a sprite and a Pickup worth 1), open in the editor, and a second scene, Level 2,
    // holding a copy of the coin.
    private (Workspace Workspace, Project Project) Open(IServiceProvider services)
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
        return (workspace, project);
    }

    private static Entity Place(Workspace workspace, string name, Vector3 position)
    {
        var copy = new PrefabFiles(workspace, workspace, workspace).Instantiate("Prefabs/Coin.prefab")!;
        copy.Name = name;
        copy.Transform.Position = position;
        workspace.Scene!.Execute(new Griff.Editing.AddEntityEdit(workspace.Scene.Scene, copy));
        return copy;
    }

    private static PropertyField Field(InspectorPanel inspector, string component, string label) =>
        inspector.Components.Single(c => c.Title == component).Properties.Single(p => p.Label == label);

    private static Entity LevelTwoCoin(Workspace workspace, Project project) =>
        workspace.Serializer.LoadFromString(File.ReadAllText(project.FindScene("Level 2")!)).Roots.Single(e => e.Prefab is not null);

    private static Task Run(IServiceProvider services, string id, string answers) => RunCommand(services, id, answers);

    private static async Task RunCommand(IServiceProvider services, string id, string answers)
    {
        using (AgentDialogs.Answering(JsonDocument.Parse(answers).RootElement))
            await services.GetRequiredService<CommandRegistry>().Get(id).ExecuteAsync();
    }

    [Fact]
    public void TheInspector_MarksFieldsACopyChanged_AndRevertsThem() => EditorHost.Run(services =>
    {
        var (workspace, _) = Open(services);
        var copy = Place(workspace, "Gold", new Vector3(4, 2, 0));
        services.GetRequiredService<ISelection>().Select(copy);
        var inspector = services.GetRequiredService<InspectorPanel>();

        // Placed somewhere of its own, it changes nothing yet.
        Assert.Equal("Copy of the Coin prefab", inspector.Header!.PrefabNote);
        Assert.DoesNotContain(inspector.Components.SelectMany(c => c.Properties), p => p.IsOverridden);

        Field(inspector, "Pickup", "Amount").NumberValue = 5;
        inspector.Seal();
        var amount = Field(inspector, "Pickup", "Amount");
        Assert.True(amount.IsOverridden);
        Assert.Contains("which has 1", amount.OverrideTip);
        Assert.False(Field(inspector, "Sprite", "Image").IsOverridden);
        Assert.Equal("Copy of the Coin prefab, with 1 change", inspector.Header!.PrefabNote);

        EditorHost.Command(services, "Component.RevertField", amount);
        Assert.Equal(1, copy.GetComponent<Pickup>()!.Amount);
        Assert.False(Field(inspector, "Pickup", "Amount").IsOverridden);

        workspace.Scene!.History.Undo();
        Assert.Equal(5, copy.GetComponent<Pickup>()!.Amount);
        Assert.True(Field(inspector, "Pickup", "Amount").IsOverridden);
    });

    [Fact]
    public void ApplyToPrefab_SavesTheField_AndCopiesThatDidntChangeItFollow() => EditorHost.Run(services =>
    {
        var (workspace, project) = Open(services);
        var big = Place(workspace, "Big", Vector3.Zero);
        var own = Place(workspace, "Own", Vector3.One);
        var plain = Place(workspace, "Plain", -Vector3.One);
        own.GetComponent<Pickup>()!.Amount = 3;
        big.GetComponent<Pickup>()!.Amount = 10;
        var selection = services.GetRequiredService<ISelection>();
        selection.Select(big);
        var inspector = services.GetRequiredService<InspectorPanel>();

        // As an agent applies it, naming the field.
        var apply = services.GetServices<Griff.Agent.IAgentToolSource>().SelectMany(s => s.GetTools()).Single(t => t.Name == Features.Agent.CommandTools.ToolName("Component.ApplyToPrefab"));
        Assert.False(EditorHost.Pump(apply.Invoke(Griff.Agent.ToolArgs.Parse("""{"field": "Pickup.Amount"}"""))).IsError);

        var prefab = workspace.Serializer.LoadPrefabFromString(File.ReadAllText(project.DefaultPrefabPath("Coin")));
        Assert.Equal(10, prefab.GetComponent<Pickup>()!.Amount);
        Assert.Equal(10, plain.GetComponent<Pickup>()!.Amount);
        Assert.Equal(3, own.GetComponent<Pickup>()!.Amount);
        Assert.Equal(10, LevelTwoCoin(workspace, project).GetComponent<Pickup>()!.Amount);
        // The copy it came from now matches its prefab.
        Assert.False(Field(inspector, "Pickup", "Amount").IsOverridden);
        selection.Select(own);
        Assert.True(Field(inspector, "Pickup", "Amount").IsOverridden);
    });

    [Fact]
    public void SavingThePrefab_KeepsWhatEachCopyChanged() => EditorHost.Run(services =>
    {
        var (workspace, project) = Open(services);
        var source = Place(workspace, "Source", Vector3.Zero);
        var own = Place(workspace, "Own", Vector3.One);
        own.GetComponent<Pickup>()!.Amount = 3;
        own.AddComponent<Light>();

        source.GetComponent<Sprite>()!.Image = "Sprites/ruby.png";
        source.GetComponent<Pickup>()!.Amount = 2;
        services.GetRequiredService<ISelection>().Select(source);
        Wait(Run(services, "Entity.SaveAsPrefab", """{"Save Prefab": true}"""));

        Assert.Equal("Sprites/ruby.png", own.GetComponent<Sprite>()!.Image);
        Assert.Equal(3, own.GetComponent<Pickup>()!.Amount);
        Assert.NotNull(own.GetComponent<Light>());
        Assert.Equal(2, LevelTwoCoin(workspace, project).GetComponent<Pickup>()!.Amount);

        // The added component is marked as added in the Inspector.
        services.GetRequiredService<ISelection>().Select(own);
        var inspector = services.GetRequiredService<InspectorPanel>();
        Assert.True(inspector.Components.Single(c => c.Title == "Light").IsAddedToCopy);
        Assert.False(inspector.Components.Single(c => c.Title == "Pickup").IsAddedToCopy);
        Assert.Equal("Copy of the Coin prefab, with 2 changes", inspector.Header!.PrefabNote);
    });

    [Fact]
    public void RevertToPrefab_PutsWholeCopiesBack_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (workspace, _) = Open(services);
        var copy = Place(workspace, "Gold", new Vector3(5, 0, 0));
        var plain = Place(workspace, "Plain", Vector3.Zero);
        copy.GetComponent<Pickup>()!.Amount = 7;
        copy.AddComponent<Light>();
        copy.RemoveComponent(copy.GetComponent<Sprite>()!);
        var selection = services.GetRequiredService<ISelection>();
        var commands = services.GetRequiredService<CommandRegistry>();

        selection.Select(copy);
        Assert.True(commands.Get("Entity.RevertToPrefab").CanExecute());
        Wait(Run(services, "Entity.RevertToPrefab", "{}"));
        Assert.Equal(1, copy.GetComponent<Pickup>()!.Amount);
        Assert.Null(copy.GetComponent<Light>());
        Assert.NotNull(copy.GetComponent<Sprite>());
        Assert.Equal(("Gold", new Vector3(5, 0, 0)), (copy.Name, copy.Transform.Position));
        Assert.Equal("Revert Gold to Prefab", workspace.Scene!.History.UndoDescription);

        workspace.Scene.History.Undo();
        Assert.Equal(7, copy.GetComponent<Pickup>()!.Amount);

        // A copy just like its prefab has nothing to revert.
        selection.Select(plain);
        var before = workspace.Scene.History.UndoDescription;
        Wait(Run(services, "Entity.RevertToPrefab", "{}"));
        Assert.Equal(before, workspace.Scene.History.UndoDescription);
    });

    [Fact]
    public void APrefabAnAgentWritesOver_KeepsWhatCopiesChanged() => EditorHost.Run(services =>
    {
        var (workspace, project) = Open(services);
        var own = Place(workspace, "Own", Vector3.Zero);
        own.GetComponent<Pickup>()!.Amount = 4;
        var path = project.DefaultPrefabPath("Coin");
        var before = File.ReadAllText(path);

        var coin = new Entity("Coin");
        coin.AddComponent<Sprite>().Image = "Sprites/gem.png";
        coin.AddComponent<Pickup>().Amount = 6;
        File.WriteAllText(path, workspace.Serializer.SavePrefabToString(coin));
        Wait(new EditorAgentProject(workspace, workspace, workspace, services.GetRequiredService<PrefabFiles>()).AssetChangedAsync("Prefabs/Coin.prefab", before));

        Assert.Equal("Sprites/gem.png", own.GetComponent<Sprite>()!.Image);
        Assert.Equal(4, own.GetComponent<Pickup>()!.Amount);
    });

    [Fact]
    public void ThingsInsideACopy_AreMarkedAgainstThePrefabsOwn() => EditorHost.Run(services =>
    {
        var (workspace, project) = Open(services);
        var coin = new Entity("Coin");
        coin.AddComponent<Pickup>();
        var scene = new Scene();
        scene.Add(coin);
        var shine = new Entity("Shine");
        shine.AddComponent<Light>().Radius = 2;
        scene.Add(shine, coin);
        File.WriteAllText(project.DefaultPrefabPath("Coin"), workspace.Serializer.SavePrefabToString(coin));

        var copy = Place(workspace, "Coin", Vector3.Zero);
        var inner = copy.Children.Single();
        inner.GetComponent<Light>()!.Radius = 5;
        services.GetRequiredService<ISelection>().Select(inner);
        var inspector = services.GetRequiredService<InspectorPanel>();

        Assert.Equal("Inside a copy of the Coin prefab", inspector.Header!.PrefabNote);
        Assert.True(Field(inspector, "Light", "Radius").IsOverridden);
        // Inside a copy, where a thing sits is part of the prefab like anything else.
        Assert.False(Field(inspector, "Transform", "Position").IsOverridden);
        EditorHost.Command(services, "Component.RevertField", Field(inspector, "Light", "Radius"));
        Assert.Equal(2, inner.GetComponent<Light>()!.Radius);
    });
}
