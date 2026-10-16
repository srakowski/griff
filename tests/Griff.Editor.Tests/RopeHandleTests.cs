using System.Numerics;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Entities;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>A rope placed with Entity &gt; Add Rope, its ends and length dragged in the Scene view, with undo.</summary>
public sealed class RopeHandleTests
{
    // Lamp: a lamp at 0, 2 tied by a rope from its top to Hook at 0, 6, 4 long. Crate (a body) and Ceiling (a wall), with no
    // rope yet.
    private static (SceneDocument Document, ISelection Selection, SceneViewModel Model) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var hook = new Entity("Hook");
        hook.Transform.Position = new Vector3(0, 6, 0);
        scene.Add(hook);
        var lamp = new Entity("Lamp");
        lamp.Transform.Position = new Vector3(0, 2, 0);
        lamp.AddComponent<BoxCollider>().Size = new Vector2(1, 1);
        lamp.AddComponent<Body>();
        var rope = lamp.AddComponent<Rope>();
        (rope.To, rope.Anchor, rope.Length) = ("Hook", new Vector2(0, 0.5f), 4);
        scene.Add(lamp);

        var crate = new Entity("Crate");
        crate.Transform.Position = new Vector3(10, 0, 0);
        crate.AddComponent<BoxCollider>().Size = new Vector2(1, 1);
        crate.AddComponent<Body>();
        scene.Add(crate);
        var ceiling = new Entity("Ceiling");
        ceiling.Transform.Position = new Vector3(20, 8, 0);
        ceiling.AddComponent<BoxCollider>().Size = new Vector2(10, 1);
        scene.Add(ceiling);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<Workspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<SceneViewModel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static void Near(Vector2 expected, Vector2 actual, float within = 1e-3f) =>
        Assert.True(Vector2.Distance(expected, actual) < within, $"Expected {expected}, got {actual}.");

    [Fact]
    public void A_rope_has_a_handle_on_each_end_and_one_at_its_middle_for_its_length() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var rope = Named(document, "Lamp").GetComponent<Rope>()!;
        selection.Select(rope.Entity);

        var points = model.PathPoints();
        Assert.Equal(["Rope.Start", "Rope.Length", "Rope.End"], points.Select(p => p.Key));
        Near(new Vector2(0, 2.5f), points[0].World);
        Near(new Vector2(0, 6), points[2].World);
        Assert.Equal("Rope.Start at 0, 2.5", points[0].Label);

        // 4 long, half a unit longer than its ends are apart, its middle hangs a quarter below the lower; the knob for 5 makes it 5.
        Near(new Vector2(0, 4), points[1].World, 0.05f);
        model.DragPathPoint("Rope.Length", SceneViewModel.RopeLengthPoint(rope, 5));
        Assert.Equal(5, rope.Length, 2);
    });

    [Fact]
    public void Dragging_the_ends_moves_them_in_the_own_units_of_what_they_are_on_and_the_length_knob_sets_its_length_with_undo() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var rope = Named(document, "Lamp").GetComponent<Rope>()!;
        selection.Select(rope.Entity);

        var drag = model.BeginPathPoint(model.PathPoints()[0])!;
        drag.To(new Vector2(0.333f, 2.111f), snap: false);
        Assert.Equal(new Vector2(0.33f, 0.11f), rope.Anchor);
        drag.To(new Vector2(0.4f, 2.6f), snap: true);
        Assert.Equal(new Vector2(0.5f, 0.5f), rope.Anchor);
        drag.End();
        document.History.Undo();
        Assert.Equal(new Vector2(0, 0.5f), rope.Anchor);

        // Its other end, on the hook, in the hook's own units.
        model.DragPathPoint("Rope.End", new Vector2(1, 7));
        Assert.Equal(new Vector2(1, 1), rope.OtherAnchor);
        document.History.Undo();
        Assert.Equal(Vector2.Zero, rope.OtherAnchor);

        // Its length: from its start through the knob to its end, never shorter than they're apart.
        model.DragPathPoint("Rope.Length", new Vector2(3, 4.25f));
        Assert.Equal(2 * MathF.Sqrt(9 + 1.75f * 1.75f), rope.Length, 2);
        model.DragPathPoint("Rope.Length", new Vector2(0, 4));
        Assert.Equal(3.5f, rope.Length, 2);
        document.History.Undo();
        Assert.Equal(2 * MathF.Sqrt(9 + 1.75f * 1.75f), rope.Length, 2);

        // Double-clicking the length knob makes it as long as its ends are apart; its ends can't be taken off.
        model.RemovePathPoint("Rope.Length");
        Assert.Equal(0, rope.Length);
        Assert.Throws<InvalidOperationException>(() => model.RemovePathPoint("Rope.Start"));

        // Tied to the world, its end is a point in the world; loose, the length knob is at its tip.
        (rope.To, rope.OtherAnchor) = (null, new Vector2(3, 6));
        model.DragPathPoint("Rope.End", new Vector2(4.2f, 5.3f));
        Assert.Equal(new Vector2(4.2f, 5.3f), rope.OtherAnchor);
        (rope.LooseEnd, rope.OtherAnchor, rope.Length) = (true, new Vector2(0, 0.5f), 4);
        Near(new Vector2(0, -1.5f), model.PathPoints()[1].World);
        model.DragPathPoint("Rope.Length", new Vector2(0, -3));
        Assert.Equal(5.5f, rope.Length, 2);
    });

    [Fact]
    public void Add_rope_ties_two_together_hangs_a_body_or_hangs_a_loose_rope_as_one_undo_step() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var (document, selection, _) = Open(services);
        var command = services.GetServices<EditorCommand>().OfType<AddRopeCommand>().Single();
        var (crate, ceiling) = (Named(document, "Crate"), Named(document, "Ceiling"));

        // Two: from the one picked last to the one picked first, a little longer than they're apart.
        selection.SelectEntities([ceiling, crate]);
        Wait(command.ExecuteAsync());
        var tied = crate.GetComponent<Rope>()!;
        Assert.Equal("Ceiling", tied.To);
        Assert.Equal(Vector2.Zero, tied.Anchor);
        Assert.InRange(tied.Length, Vector2.Distance(new Vector2(10, 0), new Vector2(20, 8)) * 1.1f, Vector2.Distance(new Vector2(10, 0), new Vector2(20, 8)) * 1.2f);
        document.History.Undo();
        Assert.Null(crate.GetComponent<Rope>());

        // A body: hung from the world 3 above its top.
        selection.Select(crate);
        Wait(command.ExecuteAsync());
        var hung = crate.GetComponent<Rope>()!;
        Assert.Equal((new Vector2(0, 0.5f), new Vector2(10, 3.5f), 3f, false), (hung.Anchor, hung.OtherAnchor, hung.Length, hung.LooseEnd));

        // A wall: a loose rope 3 down from its bottom.
        selection.Select(ceiling);
        Wait(command.ExecuteAsync());
        var loose = ceiling.GetComponent<Rope>()!;
        Assert.Equal((new Vector2(0, -0.5f), new Vector2(20, 4.5f), 3f, true), (loose.Anchor, loose.OtherAnchor, loose.Length, loose.LooseEnd));
    });

    private static void Wait(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }
}
