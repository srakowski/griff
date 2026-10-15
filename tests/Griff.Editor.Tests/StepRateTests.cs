using System.IO;
using System.Windows.Threading;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Play;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Game &gt; Step Rate sets the project's step rate: a submenu ticking the one in use, undone like an edit, and a tool for agents.</summary>
public sealed class StepRateTests : IDisposable
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
    public void StepRate_PicksARate_TicksIt_AndUndoes_WithoutUnsavingTheScene() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;
        var command = services.GetRequiredService<StepRateCommand>();

        Assert.Equal(["30 steps a second", "60 steps a second", "120 steps a second", "240 steps a second"], command.Choices);
        Assert.Equal("60 steps a second", command.Chosen);

        Wait(command.ChooseAsync("120 steps a second"));
        Assert.Equal(120, project.StepRate);
        Assert.Equal("120 steps a second", command.Chosen);
        var scene = workspace.Scene!;
        Assert.Equal("Step Rate 120", scene.History.UndoDescription);
        Assert.False(scene.IsDirty);

        // Any rate from 10 to 240, named by its number; it joins the list while it's in use.
        Wait(command.ChooseAsync("90"));
        Assert.Equal(90, project.StepRate);
        Assert.Contains("90 steps a second", command.Choices!);
        Assert.Throws<ArgumentException>(() => Wait(command.ChooseAsync("500")));

        scene.History.Undo();
        Assert.Equal(120, project.StepRate);
        scene.History.Undo();
        Assert.Equal(60, project.StepRate);
        Assert.False(scene.IsDirty);

        // Agents see it as a tool that says which rate is in use.
        var tool = services.GetRequiredService<AgentServer>().Tools["Game_StepRate"];
        Assert.Contains("Now \"60 steps a second\"", tool.Description);
        Assert.Contains("choice", tool.InputSchema.ToJsonString());
    });
}
