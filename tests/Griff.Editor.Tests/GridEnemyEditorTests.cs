using System.IO;
using System.Numerics;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Grid enemies in the editor: made from the Entity menu, and a grid patrol's route drawn in the Scene view.</summary>
public sealed class GridEnemyEditorTests : IDisposable
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

    private IWorkspace Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        return workspace;
    }

    [Fact]
    public void CreateGridEnemy_MakesAChaserWithAnAttack_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var workspace = Open(services);
        var document = workspace.Scene!;
        var before = document.Scene.Entities.Count();

        Wait(services.GetRequiredService<CommandRegistry>().Get("Entity.CreateGridEnemy").ExecuteAsync());

        var enemy = services.GetRequiredService<ISelection>().Entity!;
        Assert.Equal("Enemy", enemy.Name);
        Assert.Equal("Enemy", enemy.GetComponent<BoxCollider>()!.Layer);
        Assert.Equal(0, enemy.GetComponent<Body>()!.GravityScale);
        Assert.NotNull(enemy.GetComponent<GridMovement>());
        Assert.Equal("Player", enemy.GetComponent<GridChase>()!.Target);
        Assert.Equal("", enemy.GetComponent<Attack>()!.Action);
        var hitbox = Assert.Single(enemy.Children);
        Assert.Equal("Hitbox", hitbox.Name);
        Assert.Equal("Player", hitbox.GetComponent<DamageOnTouch>()!.Layers);

        document.History.Undo();
        Assert.Equal(before, document.Scene.Entities.Count());
    });

    [Fact]
    public void AGridPatrolsRoute_IsDrawnThroughTheMiddlesOfItsCells() => EditorHost.Run(_ =>
    {
        var scene = new Scene();
        var level = new Entity("Level");
        level.Transform.Position = new Vector3(-2, 1, 0);
        level.AddComponent<Tilemap>().Tiles = ["......", "......"];
        scene.Add(level);
        var guard = new Entity("Guard");
        guard.Transform.Position = new Vector3(-0.4f, 0.3f, 0);
        guard.AddComponent<GridMovement>();
        var patrol = guard.AddComponent<GridPatrol>();
        patrol.Points = "3 0; 3 -1";
        scene.Add(guard);

        // Placed in cell (1, 0) of a map whose top-left corner is at (-2, 1): its middle is (-0.5, 0.5).
        Assert.Equal([new Vector2(-0.5f, 0.5f), new Vector2(2.5f, 0.5f), new Vector2(2.5f, -0.5f)], SceneViewModel.RouteOf(patrol));
    });
}
