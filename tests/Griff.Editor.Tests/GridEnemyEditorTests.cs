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
        EditorHost.Pump(task);
    }

    private Workspace Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        return workspace;
    }

    [Fact]
    public void CreateGridEnemy_MakesAChaserWithAnAttack_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var workspace = Open(services);
        var document = workspace.Scene!;
        var before = document.Scene.Entities.Count;

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
        Assert.Equal(before, document.Scene.Entities.Count);
    });

    [Fact]
    public void CreateTurnOrder_InAGridScene_GivesItsGridMoversATurnEach_AndLaterOnesJoinIn() => EditorHost.Run(services =>
    {
        var workspace = Open(services);
        var document = workspace.Scene!;
        var commands = services.GetRequiredService<CommandRegistry>();
        var selection = services.GetRequiredService<ISelection>();
        Entity Create(string id)
        {
            selection.Select(null);
            Wait(commands.Get(id).ExecuteAsync());
            return selection.Entity!;
        }

        var player = Create("Entity.CreateGridPlayer");
        var enemy = Create("Entity.CreateGridEnemy");
        Assert.Null(player.GetComponent<TurnTaker>());

        var turns = Create("Entity.CreateTurnOrder");
        var order = turns.GetComponent<TurnOrder>()!;
        Assert.Equal(0, order.Between);
        Assert.Equal("Turn {1}", order.Format);
        Assert.Equal("Wait", Assert.Single(turns.Children).GetComponent<Text>()!.Value);
        Assert.Equal(("Player", 1), (player.GetComponent<TurnTaker>()!.Team, player.GetComponent<TurnTaker>()!.Actions));
        Assert.Equal(("Enemy", 1), (enemy.GetComponent<TurnTaker>()!.Team, enemy.GetComponent<TurnTaker>()!.Actions));

        // A grid enemy made now takes turns too.
        var another = Create("Entity.CreateGridEnemy");
        Assert.Equal(("Enemy", 1), (another.GetComponent<TurnTaker>()!.Team, another.GetComponent<TurnTaker>()!.Actions));

        // Undoing the turn order takes the turn takers it gave out with it.
        document.History.Undo();
        document.History.Undo();
        Assert.DoesNotContain(turns, document.Scene.Entities);
        Assert.Null(player.GetComponent<TurnTaker>());
        Assert.Null(enemy.GetComponent<TurnTaker>());
    });

    [Fact]
    public void CreateTurnOrder_WithNoGridMovers_IsTheRealTimeTurnOrder() => EditorHost.Run(services =>
    {
        Open(services);
        Wait(services.GetRequiredService<CommandRegistry>().Get("Entity.CreateTurnOrder").ExecuteAsync());

        var turns = services.GetRequiredService<ISelection>().Entity!;
        Assert.Equal(0.5f, turns.GetComponent<TurnOrder>()!.Between);
        Assert.Equal("End Turn", Assert.Single(turns.Children).GetComponent<Text>()!.Value);
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
