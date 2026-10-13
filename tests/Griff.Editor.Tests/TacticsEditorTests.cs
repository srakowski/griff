using System.IO;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Tactics units made from the Entity menu: two even sides, with the turns and controls they need.</summary>
public sealed class TacticsEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void Wait(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    [Fact]
    public void CreateTacticsUnit_AlternatesSides_AndSetsUpTurnsAndControlsOnce_AsOneUndoStep() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var document = workspace.Scene!;
        var commands = services.GetRequiredService<CommandRegistry>();
        var selection = services.GetRequiredService<ISelection>();
        var before = document.Scene.Entities.Count;
        Entity Create()
        {
            selection.Select(null);
            Wait(commands.Get("Entity.CreateTacticsUnit").ExecuteAsync());
            return selection.Entity!;
        }

        var first = Create();
        Assert.Equal("Blue Unit", first.Name);
        Assert.Equal(("Blue", 1), (first.GetComponent<TurnTaker>()!.Side, first.GetComponent<TurnTaker>()!.Actions));
        Assert.NotNull(first.GetComponent<GridMovement>());
        Assert.Equal(0, first.GetComponent<Health>()!.InvincibleTime);
        Assert.NotNull(first.GetComponent<TacticsUnit>());
        var order = Assert.Single(document.Scene.Entities.Select(e => e.GetComponent<TurnOrder>()).OfType<TurnOrder>());
        Assert.Equal(("Blue, Red", "Red", 0f), (order.Sides, order.ComputerSides, order.ComputerTurnTime));
        Assert.Contains(order.Entity!.Children, c => c.GetComponent<Griff.Scenes.Button>()?.Action == ButtonAction.EndTurn);
        Assert.Single(document.Scene.Entities, e => e.GetComponent<TacticsControls>() is not null);

        // Made one after another, each selecting the last, they stand side by side rather than inside each other.
        Wait(commands.Get("Entity.CreateTacticsUnit").ExecuteAsync());
        var second = selection.Entity!;
        Wait(commands.Get("Entity.CreateTacticsUnit").ExecuteAsync());
        var third = selection.Entity!;
        Assert.Null(second.Parent);
        Assert.Null(third.Parent);
        Assert.Equal(("Red Unit", "Red"), (second.Name, second.GetComponent<TurnTaker>()!.Side));
        Assert.Equal("Blue", third.GetComponent<TurnTaker>()!.Side);
        Assert.NotEqual(first.Transform.Position, third.Transform.Position);
        Assert.Single(document.Scene.Entities, e => e.GetComponent<TurnOrder>() is not null);
        Assert.Single(document.Scene.Entities, e => e.GetComponent<TacticsControls>() is not null);

        document.History.Undo();
        document.History.Undo();
        document.History.Undo();
        Assert.Equal(before, document.Scene.Entities.Count);
    });
}
