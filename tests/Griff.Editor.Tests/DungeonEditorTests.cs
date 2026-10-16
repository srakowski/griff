using System.IO;
using System.Numerics;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Modules.Dungeons;
using Griff.Runtime;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Dungeons made from the Entity menu, and generated in the editor to see them.</summary>
public sealed class DungeonEditorTests : IDisposable
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

    [Fact]
    public void CreateDungeon_MakesAGeneratedDungeonCenteredOnTheScene_AndUndoTakesItAway() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var commands = services.GetRequiredService<CommandRegistry>();
        var selection = services.GetRequiredService<ISelection>();
        selection.Select(null);
        Wait(commands.Get("Entity.CreateDungeon").ExecuteAsync());
        var entity = selection.Entity!;

        var dungeon = entity.GetComponent<DungeonGenerator>()!;
        var map = entity.GetComponent<Tilemap>()!;
        Assert.Equal((0, "Player", true), (dungeon.Seed, dungeon.Players, dungeon.FillWalls));
        Assert.Equal((48, 32), (map.Width, map.Height));
        Assert.Contains(map.Cells(), c => c.Tile == 0);
        Assert.Equal(new Vector3(-24, 16, 0), entity.Transform.Position);

        workspace.Scene!.History.Undo();
        Assert.DoesNotContain(entity, workspace.Scene.Scene.Entities);
    });

    [Fact]
    public void GenerateDungeon_FillsTheMapAndPlacesThePlayerAndExit_AsOneUndoStep() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var commands = services.GetRequiredService<CommandRegistry>();
        var selection = services.GetRequiredService<ISelection>();
        var scene = workspace.Scene!.Scene;
        var player = new Entity("Player");
        var stairs = new Entity("Stairs");
        scene.Add(player);
        scene.Add(stairs);

        var entity = new Entity("Dungeon");
        entity.Transform.Position = new Vector3(-10, 5, 0);
        var map = entity.AddComponent<Tilemap>();
        var dungeon = entity.AddComponent<DungeonGenerator>();
        (dungeon.Seed, dungeon.Exit) = (7, "Stairs");
        scene.Add(entity);

        selection.Select(player);
        Assert.False(commands.Get("Entity.GenerateDungeon").CanExecute());
        selection.Select(entity);
        Wait(commands.Get("Entity.GenerateDungeon").ExecuteAsync());

        var layout = DungeonGen.Generate(dungeon, 7);
        Assert.Equal(layout.Tiles, map.Tiles);
        var start = layout.Players[0];
        Assert.Equal(new Vector3(-10 + start.X + 0.5f, 5 - start.Y - 0.5f, 0), player.Transform.Position);
        Assert.Equal(new Vector3(-10 + layout.Exit.X + 0.5f, 5 - layout.Exit.Y - 0.5f, 0), stairs.Transform.Position);

        workspace.Scene.History.Undo();
        Assert.Empty(map.Tiles);
        Assert.Equal(Vector3.Zero, player.Transform.Position);
        Assert.Equal(Vector3.Zero, stairs.Transform.Position);
    });
}
