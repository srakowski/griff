using System.Numerics;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Entities;
using Griff.Modules.PhysicsGames;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Entity &gt; Add Breakable: what's selected breaks on hard knocks, tumbling as it should, in one undo step.</summary>
public sealed class BreakableCommandTests
{
    [Fact]
    public void Add_breakable_gives_each_selected_a_breakable_a_body_that_rotates_and_a_collider_its_sprites_size() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var scene = new Scene();
        var plank = new Entity("Plank");
        plank.AddComponent<Sprite>().Size = new Vector2(3, 0.4f);
        scene.Add(plank);
        var crate = new Entity("Crate");
        crate.AddComponent<CircleCollider>();
        crate.AddComponent<Body>();
        scene.Add(crate);
        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<Workspace>().EnterPlayMode(document);
        var selection = services.GetRequiredService<ISelection>();
        var command = services.GetServices<EditorCommand>().OfType<AddBreakableCommand>().Single();

        selection.SelectEntities([plank, crate]);
        Wait(command.ExecuteAsync());
        Assert.Equal(new Vector2(3, 0.4f), plank.GetComponent<BoxCollider>()!.Size);
        Assert.True(plank.GetComponent<Body>()!.Rotates);
        Assert.NotNull(plank.GetComponent<Breakable>());
        Assert.Null(crate.GetComponent<BoxCollider>());
        Assert.True(crate.GetComponent<Body>()!.Rotates);
        Assert.NotNull(crate.GetComponent<Breakable>());

        document.History.Undo();
        Assert.Equal(["Sprite"], plank.Components.Where(c => c is not Transform).Select(c => c.GetType().Name));
        Assert.Null(crate.GetComponent<Breakable>());
        Assert.False(crate.GetComponent<Body>()!.Rotates);
    });

    private static void Wait(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }
}
