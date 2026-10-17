using System.IO;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor.Core.Undo;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Over MCP, Edit_Undo and Edit_Redo name the open scene or prefab to act on, and Scene_Open takes a scene by name.</summary>
public sealed class AgentDocumentArgumentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-doc-arg-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static ToolResult Call(IServiceProvider services, string tool, string json) =>
        EditorHost.Pump(services.GetRequiredService<AgentServer>().Tools[tool].Invoke(ToolArgs.Parse(json)));

    [Fact]
    public void UndoAndRedo_TakeTheOpenDocumentToActOn_WhicheverIsActive() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;
        var sceneName = workspace.Scene!.Name;

        var bullet = new Entity("Bullet");
        Directory.CreateDirectory(Path.GetDirectoryName(project.DefaultPrefabPath("Bullet"))!);
        File.WriteAllText(project.DefaultPrefabPath("Bullet"), workspace.Serializer.SavePrefabToString(bullet));
        var sceneDocument = workspace.Scene!;
        sceneDocument.Execute(new AddEntityEdit(sceneDocument.Scene, new Entity("Crate")));
        EditorHost.Pump(workspace.OpenPrefabAsync("Bullet"));
        var prefab = workspace.Scene!;
        Assert.NotSame(sceneDocument, prefab);
        prefab.Execute(new AddEntityEdit(prefab.Scene, new Entity("Spark")));

        // The prefab is the one showing, so a plain undo goes there; naming the scene undoes the Crate and leaves the prefab.
        var documents = services.GetRequiredService<ActiveDocument>();
        var result = Call(services, "Edit_Undo", $$"""{"document": "{{sceneName}}"}""");
        Assert.False(result.IsError, result.Text);
        Assert.DoesNotContain(sceneDocument.Scene.Entities, e => e.Name == "Crate");
        Assert.Contains(prefab.Scene.Entities, e => e.Name == "Spark");
        Assert.Same(documents.Scene, documents.Editor);

        Assert.False(Call(services, "Edit_Redo", $$"""{"document": "{{sceneName}}"}""").IsError);
        Assert.Contains(sceneDocument.Scene.Entities, e => e.Name == "Crate");

        // An unknown name says which are open.
        var unknown = Assert.Throws<AgentException>(() => Call(services, "Edit_Undo", """{"document": "Nowhere"}"""));
        Assert.Contains(sceneName, unknown.Message);

        // Without it, the active document (the prefab) is undone, as before.
        Assert.False(Call(services, "Edit_Undo", "{}").IsError);
        Assert.DoesNotContain(prefab.Scene.Entities, e => e.Name == "Spark");
    });

    [Fact]
    public void SceneOpen_TakesASceneByName() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        workspace.Project!.CreateScene("Game Over");

        var result = Call(services, "Scene_Open", """{"scene": "game over"}""");

        Assert.False(result.IsError, result.Text);
        Assert.Equal("Game Over", workspace.Scene!.Name);
        Assert.Contains("no scene", Assert.Throws<AgentException>(() => Call(services, "Scene_Open", """{"scene": "Nowhere"}""")).Message);
    });
}
