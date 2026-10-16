using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Play;
using Griff.Editing;
using Griff.Projects;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// File &gt; Close Project: the editor goes back to having no project, asking first about unsaved changes, and a project
/// (the same or another) opens again afterwards.
/// </summary>
public sealed class CloseProjectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static Workspace Open(IServiceProvider services, string root, string name)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(root, name));
        return workspace;
    }

    private static Task Close(IServiceProvider services, string answers = "{}")
    {
        return Run();

        async Task Run()
        {
            using (AgentDialogs.Answering(JsonDocument.Parse(answers).RootElement))
                await services.GetRequiredService<CommandRegistry>().Get("Project.Close").ExecuteAsync();
        }
    }

    private static void Dirty(Workspace workspace) =>
        workspace.Scene!.Execute(new AddEntityEdit(workspace.Scene.Scene, new Entity("Unsaved")));

    [Fact]
    public void CloseProject_ReturnsTheEditorToNoProject_AndAProjectOpensAgain() => EditorHost.Run(services =>
    {
        var workspace = Open(services, _root, "First");
        var path = workspace.Project!.RootPath;
        Assert.NotNull(workspace.Assets);
        Assert.NotNull(workspace.PrefabLibrary);

        EditorHost.Pump(Close(services));

        Assert.Null(workspace.Project);
        Assert.Null(workspace.Scene);
        Assert.Null(workspace.Assets);
        Assert.Null(workspace.PrefabLibrary);
        Assert.Null(workspace.Names);
        Assert.Empty(workspace.Prefabs);
        Assert.False(workspace.IsPlaying);
        Assert.False(services.GetRequiredService<CommandRegistry>().Get("Project.Close").CanExecute(null));

        // The same project and another one open as from a fresh start.
        EditorHost.Pump(workspace.OpenProjectAsync(path));
        Assert.Equal(path, workspace.Project!.RootPath);
        Assert.NotNull(workspace.Scene);
        Assert.NotNull(workspace.PrefabLibrary);
        EditorHost.Pump(Close(services));
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Second"));
        Assert.Equal("Second", workspace.Project!.Name);
        Assert.NotNull(workspace.Scene);
    });

    [Fact]
    public void CloseProject_AsksAboutUnsavedChanges() => EditorHost.Run(services =>
    {
        var workspace = Open(services, _root, "Game");
        var project = workspace.Project!;
        var scenePath = project.StartScenePath!;

        // Cancel keeps the project, and its changes, open.
        Dirty(workspace);
        EditorHost.Pump(Close(services, """{"Save changes": "Cancel"}"""));
        Assert.Same(project, workspace.Project);
        Assert.True(workspace.Scene!.IsDirty);

        // Not asked an answer by an agent: the call comes back asking, and nothing was closed.
        Assert.Throws<AgentInputNeededException>(() => EditorHost.Pump(Close(services, "{}")));
        Assert.Same(project, workspace.Project);

        // Don't save closes it without writing the change.
        EditorHost.Pump(Close(services, """{"Save changes": "Discard"}"""));
        Assert.Null(workspace.Project);
        Assert.DoesNotContain("Unsaved", File.ReadAllText(scenePath));

        // Save writes it first.
        EditorHost.Pump(workspace.OpenProjectAsync(project.RootPath));
        Dirty(workspace);
        EditorHost.Pump(Close(services, """{"Save changes": "Save"}"""));
        Assert.Null(workspace.Project);
        Assert.Contains("Unsaved", File.ReadAllText(scenePath));
    });

    [Fact]
    public void CloseProject_StopsAGameThatIsPlaying() => EditorHost.Run(services =>
    {
        var workspace = Open(services, _root, "Game");
        var play = services.GetRequiredService<PlayMode>();
        play.Play();
        Assert.True(play.IsPlaying);

        EditorHost.Pump(Close(services));

        Assert.False(play.IsPlaying);
        Assert.False(workspace.IsPlaying);
        Assert.Null(workspace.Project);
    });
}
