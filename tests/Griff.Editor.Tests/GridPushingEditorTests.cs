using System.IO;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Modules.Puzzles;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Sokoban crates and goals made from the Entity menu.</summary>
public sealed class GridPushingEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // Runs a task to the end on the test's WPF thread, as the editor's dispatcher would.
    private static void Wait(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    [Fact]
    public void CreateCrate_InAGridScene_IsAGridCrate_AndTurnOrdersLeaveCratesOut() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var commands = services.GetRequiredService<CommandRegistry>();
        var selection = services.GetRequiredService<ISelection>();
        Entity Create(string id)
        {
            selection.Select(null);
            Wait(commands.Get(id).ExecuteAsync());
            return selection.Entity!;
        }

        // With no grid movers, a crate is the physics crate it always was.
        var physics = Create("Entity.CreateCrate");
        Assert.Null(physics.GetComponent<GridMovement>());
        Assert.NotEqual(0, physics.GetComponent<Body>()!.GravityScale);

        var player = Create("Entity.CreateGridPlayer");
        var crate = Create("Entity.CreateCrate");
        Assert.NotNull(crate.GetComponent<GridMovement>());
        Assert.NotNull(crate.GetComponent<Pushable>());
        Assert.Equal(0, crate.GetComponent<Body>()!.GravityScale);
        Assert.Equal("Crate", crate.GetComponent<BoxCollider>()!.Layer);

        var goal = Create("Entity.CreateGoal");
        Assert.Equal("Solved", goal.GetComponent<Goal>()!.AllCoveredMessage);
        Assert.True(goal.GetComponent<Sprite>()!.Order < 0);
        Assert.Null(goal.GetComponent<Collider>());

        // The first goal brings undo and restart along; the next doesn't add another.
        Assert.NotNull(goal.GetComponent<MoveHistory>());
        Assert.Null(Create("Entity.CreateGoal").GetComponent<MoveHistory>());
        workspace.Scene!.History.Undo();

        Create("Entity.CreateTurnOrder");
        Assert.NotNull(player.GetComponent<TurnTaker>());
        Assert.Null(crate.GetComponent<TurnTaker>());

        workspace.Scene!.History.Undo();
        workspace.Scene.History.Undo();
        Assert.DoesNotContain(goal, workspace.Scene.Scene.Entities);
    });
}
