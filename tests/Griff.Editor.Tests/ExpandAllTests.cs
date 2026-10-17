using System.IO;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Assets;
using Griff.Editor.Features.Hierarchy;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.Scripts;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Expand All and Collapse All on every panel that shows a tree or folds: the Hierarchy, the Inspector, the Assets panel
/// and the script editor, each a command (so agents have them) that the panel's buttons run.
/// </summary>
public sealed class ExpandAllTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static readonly string[] Ids =
    [
        "Hierarchy.ExpandAll", "Hierarchy.CollapseAll", "Inspector.ExpandAll", "Inspector.CollapseAll",
        "Assets.ExpandAll", "Assets.CollapseAll", "Script.ExpandAll", "Script.CollapseAll",
    ];

    private static void Run(IServiceProvider services, string id) => EditorHost.Pump(services.GetRequiredService<CommandRegistry>().Get(id).ExecuteAsync());

    // As an agent runs it: the command's tool.
    private static void RunTool(IServiceProvider services, string id)
    {
        var tool = services.GetServices<Griff.Agent.IAgentToolSource>().SelectMany(s => s.GetTools()).Single(t => t.Name == CommandTools.ToolName(id));
        var result = EditorHost.Pump(tool.Invoke(Griff.Agent.ToolArgs.Empty));
        Assert.False(result.IsError, result.Text);
    }

    private static IEnumerable<EntityNode> All(IEnumerable<EntityNode> nodes) => nodes.SelectMany(n => All(n.Children).Prepend(n));

    [Fact]
    public void EveryPanel_HasBoth_AsToolsForAgents() => EditorHost.Run(services =>
    {
        var commands = services.GetRequiredService<CommandRegistry>();
        var tools = services.GetRequiredService<AgentServer>().Tools;
        foreach (var id in Ids)
        {
            var command = commands.Get(id);
            Assert.IsAssignableFrom<FoldAllCommand>(command);
            Assert.Equal(id.EndsWith("ExpandAll", StringComparison.Ordinal) ? "Expand All" : "Collapse All", command.DisplayText);
            Assert.True(tools.ContainsKey(CommandTools.ToolName(id)), $"No tool for {id}.");
            // Shortcuts would be the window's, not a panel's: the trees keep their own (numpad * opens a row's whole branch).
            Assert.Null(command.Gesture);
        }
    });

    // Level > Room > Chest, and Player > Sword.
    private static (SceneDocument Document, HierarchyPanel Panel) OpenScene(IServiceProvider services)
    {
        var scene = new Scene();
        var level = new Entity("Level");
        scene.Add(level);
        var room = new Entity("Room");
        scene.Add(room, level);
        scene.Add(new Entity("Chest"), room);
        var player = new Entity("Player");
        scene.Add(player);
        scene.Add(new Entity("Sword"), player);
        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<Workspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<HierarchyPanel>());
    }

    [Fact]
    public void Hierarchy_ExpandAll_OpensEveryEntityWithEntitiesInside_AndCollapseAllShutsThem() => EditorHost.Run(services =>
    {
        var (_, panel) = OpenScene(services);
        Assert.All(All(panel.Roots), n => Assert.False(n.IsExpanded));

        Run(services, "Hierarchy.ExpandAll");
        Assert.Equal(["Level", "Room", "Player"], All(panel.Roots).Where(n => n.IsExpanded).Select(n => n.Name));

        RunTool(services, "Hierarchy.CollapseAll");
        Assert.All(All(panel.Roots), n => Assert.False(n.IsExpanded));
    });

    [Fact]
    public void Hierarchy_WhileFiltering_WhatEitherDoes_IsWhatComesBack() => EditorHost.Run(services =>
    {
        var (_, panel) = OpenScene(services);
        panel.Filter = "chest";
        // The filter opens what the match is in.
        Assert.True(All(panel.Roots).Single(n => n.Name == "Room").IsExpanded);

        panel.ExpandAll();
        panel.Filter = "";
        Assert.Equal(["Level", "Room", "Player"], All(panel.Roots).Where(n => n.IsExpanded).Select(n => n.Name));

        panel.Filter = "sword";
        panel.CollapseAll();
        panel.Filter = "";
        Assert.All(All(panel.Roots), n => Assert.False(n.IsExpanded));
    });

    [Fact]
    public void Inspector_CollapseAll_FoldsEverySectionAndSubheading_AndExpandAllOpensThem() => EditorHost.Run(services =>
    {
        var scene = new Scene();
        var player = new Entity("Player");
        player.AddComponent(new BoxCollider());
        player.AddComponent(new Body());
        player.AddComponent(new PlatformerMovement());
        scene.Add(player);
        var other = new Entity("Other");
        other.AddComponent(new Body());
        scene.Add(other);
        services.GetRequiredService<Workspace>().EnterPlayMode(new SceneDocument(scene, "Test.scene"));
        var inspector = services.GetRequiredService<InspectorPanel>();
        var settings = services.GetRequiredService<ISettingsStore>();
        Assert.False(services.GetRequiredService<CommandRegistry>().Get("Inspector.CollapseAll").CanExecute());
        services.GetRequiredService<ISelection>().Select(player);

        Run(services, "Inspector.CollapseAll");
        Assert.All(inspector.Components, s => Assert.False(s.IsExpanded));
        var movement = inspector.Components.Single(c => c.IsNamed("Platformer Movement"));
        Assert.All(movement.Groups, g => Assert.False(g.IsExpanded));
        Assert.Contains("Griff.PlatformerMovement/Dash", settings.Settings.CollapsedComponents);
        // As folding one does, it holds for the component on every entity.
        services.GetRequiredService<ISelection>().Select(other);
        Assert.False(inspector.Components.Single(c => c.IsNamed("Body")).IsExpanded);

        services.GetRequiredService<ISelection>().Select(player);
        RunTool(services, "Inspector.ExpandAll");
        Assert.All(inspector.Components, s => Assert.True(s.IsExpanded));
        Assert.All(inspector.Components.Single(c => c.IsNamed("Platformer Movement")).Groups, g => Assert.True(g.IsExpanded));
        Assert.Empty(settings.Settings.CollapsedComponents);
    });

    // A project with Sprites/Enemies/Bosses, Sounds and a sound in it.
    private AssetsPanel OpenProject(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;
        Directory.CreateDirectory(Path.Combine(project.AssetsPath, "Sprites", "Enemies", "Bosses"));
        Directory.CreateDirectory(Path.Combine(project.AssetsPath, "Sounds"));
        File.WriteAllBytes(Path.Combine(project.AssetsPath, "Sounds", "jump.wav"), [1, 2, 3]);
        var assets = services.GetRequiredService<AssetsPanel>();
        assets.Rebuild();
        return assets;
    }

    private static IEnumerable<AssetNode> Folders(IEnumerable<AssetNode> nodes) =>
        nodes.Where(n => n.Kind == AssetKind.Folder).SelectMany(n => Folders(n.Children).Prepend(n));

    private static string[] OpenFolders(AssetsPanel assets) => [.. Folders(assets.FolderTree).Where(n => n.IsExpanded).Select(n => n.AssetPath)];

    [Fact]
    public void Assets_ExpandAll_OpensEveryFolder_InBothViews_AndCollapseAllLeavesAssetsOpen() => EditorHost.Run(services =>
    {
        var assets = OpenProject(services);
        Assert.Equal([""], OpenFolders(assets));

        Run(services, "Assets.ExpandAll");
        Assert.All(Folders(assets.FolderTree), n => Assert.True(n.IsExpanded, n.AssetPath));
        // The Tree view shows the same folders.
        Assert.All(Folders(assets.Roots), n => Assert.True(n.IsExpanded, n.AssetPath));
        // They stay open as the files change.
        assets.Rebuild();
        Assert.All(Folders(assets.FolderTree), n => Assert.True(n.IsExpanded, n.AssetPath));

        RunTool(services, "Assets.CollapseAll");
        Assert.Equal([""], OpenFolders(assets));
        Assert.All(Folders(assets.Roots), n => Assert.False(n.IsExpanded, n.AssetPath));
    });

    [Fact]
    public void Assets_CollapseAll_KeepsThePickedFolder_AndItStaysShutAsFilesChange() => EditorHost.Run(services =>
    {
        var assets = OpenProject(services);
        assets.SelectedFolder = "Sprites/Enemies";
        Assert.Equal(["", "Sprites"], OpenFolders(assets));

        assets.CollapseAll();
        Assert.Equal("Sprites/Enemies", assets.SelectedFolder);
        assets.Rebuild();
        Assert.Equal([""], OpenFolders(assets));
        Assert.Equal("Sprites/Enemies", assets.SelectedFolder);

        // Picking a folder still opens the ones it's in.
        assets.SelectedFolder = "Sprites/Enemies/Bosses";
        Assert.Equal(["", "Sprites", "Sprites/Enemies"], OpenFolders(assets));
    });

    [Fact]
    public void Script_ExpandAllAndCollapseAll_AskTheShowingScriptsEditor() => EditorHost.Run(services =>
    {
        var editors = services.GetRequiredService<ScriptEditors>();
        var commands = services.GetRequiredService<CommandRegistry>();
        Assert.False(commands.Get("Script.CollapseAll").CanExecute());

        var script = new ScriptDocument(Path.Combine(_root, "Ship.cs"), "public class Ship\n{\n    void Update()\n    {\n    }\n}\n", editors.Language);
        var asked = new List<bool>();
        script.FoldAllRequested += (_, open) => asked.Add(open);
        editors.Active = script;

        Run(services, "Script.CollapseAll");
        RunTool(services, "Script.ExpandAll");
        script.CollapseAllCommand.Execute(null);
        Assert.Equal([false, true, false], asked);
        script.Dispose();
    });
}
