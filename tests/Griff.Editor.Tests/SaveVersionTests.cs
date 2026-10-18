using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Play;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Game &gt; Save Version sets the version of the project's saved games, kept in project.griff, and is a tool for agents.</summary>
public sealed class SaveVersionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SaveVersion_AsksForANumber_AndKeepsItInTheProject() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;
        var command = services.GetRequiredService<SaveVersionCommand>();
        Assert.Equal(0, project.SaveVersion);

        using (AgentDialogs.Answering(JsonDocument.Parse("""{"Version of the game's saves": "3"}""").RootElement))
            EditorHost.Pump(command.ExecuteAsync());
        Assert.Equal(3, project.SaveVersion);
        Assert.Contains("saveVersion", File.ReadAllText(project.ManifestPath), StringComparison.OrdinalIgnoreCase);

        // Agents see it as a tool, described by the command.
        var tool = services.GetRequiredService<AgentServer>().Tools["Game_SaveVersion"];
        Assert.Contains("version of the game's saved games", tool.Description);
    });
}
