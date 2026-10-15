using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Prefabs;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Small fixes: Exit's save question reaches an agent, and the Scene tab comes back to the front.</summary>
public sealed class SmallFixesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void Wait(Task task)
    {
        EditorHost.Pump(task);
    }

    [Fact]
    public void Exit_AsksItsSaveQuestionThroughTheDialogs_SoAnAgentCanAnswerIt() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        workspace.Scene!.Execute(new AddEntityEdit(workspace.Scene.Scene, new Entity("Extra")));
        Assert.True(workspace.Scene.IsDirty);
        var exit = services.GetRequiredService<CommandRegistry>().Get("App.Exit");

        using (AgentDialogs.Answering(null))
        {
            var asked = Assert.Throws<AgentInputNeededException>(() => Wait(exit.ExecuteAsync()));
            Assert.Contains("Save changes", asked.Message);
        }

        // Cancel keeps the editor open, with its changes.
        using (AgentDialogs.Answering(JsonDocument.Parse("""{"Save changes": "Cancel"}""").RootElement))
            Wait(exit.ExecuteAsync());
        Assert.True(workspace.Scene.IsDirty);
    });

    [Fact]
    public void ShowScene_BringsTheSceneTabBack_FromAScriptOrTheGameTab() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        var tabs = new TabControl();
        TabItem scene = new() { Header = "Scene" }, game = new() { Header = "Game" }, script = new() { Header = "Script", Tag = "script" };
        tabs.Items.Add(scene);
        tabs.Items.Add(game);
        tabs.Items.Add(script);
        services.GetRequiredService<PrefabTabs>().Attach(tabs, scene, new Border());

        Wait(workspace.CreateProjectAsync(_root, "Game"));
        tabs.SelectedItem = script;
        workspace.ShowScene();
        Assert.Same(scene, tabs.SelectedItem);

        // Opening a project brings it forward too.
        tabs.SelectedItem = game;
        Wait(workspace.OpenProjectAsync(workspace.Project!.RootPath));
        Assert.Same(scene, tabs.SelectedItem);
    });
}
