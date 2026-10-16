using System.Numerics;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Entities;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>A hinge placed with Entity &gt; Add Hinge, its anchor and limits dragged in the Scene view, with undo.</summary>
public sealed class HingeHandleTests
{
    // Door: a plank 4 long at 10, 0 with a hinge at its left end, limits -30 to 60. Cart and Wheel: a wheel and the cart it
    // goes on, with nothing hinged yet.
    private static (SceneDocument Document, ISelection Selection, SceneViewModel Model) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var door = new Entity("Door");
        door.Transform.Position = new Vector3(10, 0, 0);
        door.AddComponent<BoxCollider>().Size = new Vector2(4, 0.4f);
        door.AddComponent<Body>().Rotates = true;
        var hinge = door.AddComponent<HingeJoint>();
        (hinge.Anchor, hinge.Limit, hinge.LowerAngle, hinge.UpperAngle) = (new Vector2(-2, 0), true, -30, 60);
        scene.Add(door);

        var cart = new Entity("Cart");
        cart.Transform.Position = new Vector3(0, 5, 0);
        cart.AddComponent<BoxCollider>().Size = new Vector2(3, 0.5f);
        cart.AddComponent<Body>().Rotates = true;
        scene.Add(cart);
        var wheel = new Entity("Wheel");
        wheel.Transform.Position = new Vector3(1, 4.6f, 0);
        wheel.AddComponent<Sprite>();
        scene.Add(wheel);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<Workspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<SceneViewModel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static void Near(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < 1e-3f, $"Expected {expected}, got {actual}.");

    [Fact]
    public void A_hinge_has_a_handle_on_its_anchor_and_on_each_end_of_its_limits() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var hinge = Named(document, "Door").GetComponent<HingeJoint>()!;
        selection.Select(hinge.Entity);

        var points = model.PathPoints();
        Assert.Equal(["HingeJoint.Anchor", "HingeJoint.Lower", "HingeJoint.Upper"], points.Select(p => p.Key));
        Near(new Vector2(8, 0), points[0].World);
        Assert.Equal("HingeJoint.Anchor at 8, 0", points[0].Label);

        // The arc goes round the anchor through the door's middle, 2 out: -30 degrees is below it, 60 above.
        var arc = SceneViewModel.ArcOf(hinge);
        Near(new Vector2(1, 0), arc.Way);
        Assert.Equal(2, arc.Radius, 3);
        Near(new Vector2(8 + 2 * MathF.Cos(-MathF.PI / 6), 2 * MathF.Sin(-MathF.PI / 6)), points[1].World);
        Near(new Vector2(8 + 2 * MathF.Cos(MathF.PI / 3), 2 * MathF.Sin(MathF.PI / 3)), points[2].World);

        // Without limits, just the anchor.
        hinge.Limit = false;
        Assert.Equal(["HingeJoint.Anchor"], model.PathPoints().Select(p => p.Key));
    });

    [Fact]
    public void Dragging_the_anchor_moves_it_in_the_entitys_own_units_as_one_undo_step() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var hinge = Named(document, "Door").GetComponent<HingeJoint>()!;
        selection.Select(hinge.Entity);

        var drag = model.BeginPathPoint(model.PathPoints()[0])!;
        drag.To(new Vector2(9.123f, 0.456f), snap: false);
        Assert.Equal(new Vector2(-0.88f, 0.46f), hinge.Anchor);
        drag.To(new Vector2(11.3f, 0.2f), snap: true);
        Assert.Equal(new Vector2(1.5f, 0), hinge.Anchor);
        drag.End();
        document.History.Undo();
        Assert.Equal(new Vector2(-2, 0), hinge.Anchor);

        // A hinge the game has joined (kept from playing) is joined again where it's moved to: one undo step for both.
        (hinge.Joined, hinge.OtherAnchor) = (true, new Vector2(8, 0));
        model.DragPathPoint("hingejoint.anchor", new Vector2(12, 0));
        Assert.Equal(new Vector2(2, 0), hinge.Anchor);
        Assert.False(hinge.Joined);
        document.History.Undo();
        Assert.Equal(new Vector2(-2, 0), hinge.Anchor);
        Assert.True(hinge.Joined);

        // In a turned, scaled entity's own units.
        hinge.Entity!.Transform.Rotation = new Vector3(0, 0, 90);
        hinge.Entity.Transform.Scale = new Vector3(2, 2, 1);
        model.DragPathPoint("HingeJoint.Anchor", new Vector2(10, 4));
        Assert.Equal(new Vector2(2, 0), hinge.Anchor);
    });

    [Fact]
    public void Dragging_an_end_of_the_arc_sets_that_limit_in_degrees_and_double_clicking_clears_the_limits() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var hinge = Named(document, "Door").GetComponent<HingeJoint>()!;
        selection.Select(hinge.Entity);

        model.DragPathPoint("HingeJoint.Lower", new Vector2(8, -3));
        Assert.Equal(-90, hinge.LowerAngle);
        model.DragPathPoint("HingeJoint.Upper", new Vector2(8 + MathF.Cos(1), MathF.Sin(1)), snap: true);
        Assert.Equal(60, hinge.UpperAngle);
        model.DragPathPoint("HingeJoint.Upper", new Vector2(7, 0.05f));
        Assert.Equal(177, hinge.UpperAngle);

        // Not past the other limit.
        model.DragPathPoint("HingeJoint.Lower", new Vector2(8, 3));
        Assert.Equal(90, hinge.LowerAngle);
        model.DragPathPoint("HingeJoint.Upper", new Vector2(9, 0));
        Assert.Equal(90, hinge.UpperAngle);
        document.History.Undo();
        Assert.Equal(177, hinge.UpperAngle);

        model.RemovePathPoint("HingeJoint.Lower");
        Assert.False(hinge.Limit);
        document.History.Undo();
        Assert.True(hinge.Limit);
        Assert.Throws<InvalidOperationException>(() => model.RemovePathPoint("HingeJoint.Anchor"));
    });

    [Fact]
    public void Add_hinge_hinges_the_selection_at_its_middle_to_the_world_or_to_the_one_picked_first_as_one_undo_step() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var (document, selection, _) = Open(services);
        var command = services.GetServices<EditorCommand>().OfType<AddHingeCommand>().Single();
        var (cart, wheel) = (Named(document, "Cart"), Named(document, "Wheel"));

        selection.SelectEntities([cart, wheel]);
        Wait(command.ExecuteAsync());

        var hinge = wheel.GetComponent<HingeJoint>()!;
        Assert.Equal("Cart", hinge.To);
        Assert.Equal(Vector2.Zero, hinge.Anchor);
        Assert.True(wheel.GetComponent<Body>()!.Rotates);
        Assert.NotNull(wheel.GetComponent<BoxCollider>());
        document.History.Undo();
        Assert.Null(wheel.GetComponent<HingeJoint>());
        Assert.Null(wheel.GetComponent<Body>());
        Assert.Null(wheel.GetComponent<Collider>());

        // On its own, to the world; a body that doesn't rotate is made to.
        cart.GetComponent<Body>()!.Rotates = false;
        selection.Select(cart);
        Wait(command.ExecuteAsync());
        Assert.Null(cart.GetComponent<HingeJoint>()!.To);
        Assert.True(cart.GetComponent<Body>()!.Rotates);
    });

    private static void Wait(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }
}
