using System.IO;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Match-3 boards made from the Entity menu.</summary>
public sealed class MatchBoardEditorTests : IDisposable
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
    public void CreateMatchBoard_MakesADealtBoardCenteredOnTheScene_AndUndoTakesItAway() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var commands = services.GetRequiredService<CommandRegistry>();
        var selection = services.GetRequiredService<ISelection>();
        selection.Select(null);
        Wait(commands.Get("Entity.CreateMatchBoard").ExecuteAsync());
        var entity = selection.Entity!;

        var board = entity.GetComponent<MatchBoard>()!;
        var map = entity.GetComponent<Tilemap>()!;
        Assert.False(map.Solid);
        Assert.True(board.Deal);
        Assert.Equal((8, 8), (map.Width, map.Height));
        Assert.All(map.Cells(), c => Assert.True(board.IsGem(c.Tile)));
        Assert.Equal(64, map.Cells().Count());
        Assert.NotNull(board.FindMove());
        Assert.Equal(new System.Numerics.Vector3(-4, 4, 0), entity.Transform.Position);

        workspace.Scene!.History.Undo();
        Assert.DoesNotContain(entity, workspace.Scene.Scene.Entities);
    });
}
