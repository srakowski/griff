using System.Numerics;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Light radii, ranges and sight dragged with the Scene view's Distance handles.</summary>
public class SceneDistanceTests
{
    /// <summary>A script's own distance, centered on its Eye and only counting while Awake.</summary>
    public sealed class Watcher : Component
    {
        [Distance(Center = nameof(Eye), When = nameof(Awake))]
        public float Notice { get; set; } = 3;

        public Vector2 Eye { get; set; } = new(0, 1);

        public bool Awake { get; set; } = true;

        [Distance]
        public int NotANumberWithAFraction { get; set; } = 5;
    }

    // Lamp: a light (radius 4, offset 1, 0) at 2, 0, doubled in size. Guard: a chaser and a shooter with no target, at 0, 5.
    private static (SceneDocument Document, ISelection Selection, SceneViewModel Model) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var lamp = new Entity("Lamp");
        lamp.Transform.Position = new Vector3(2, 0, 0);
        lamp.Transform.Scale = new Vector3(2, 2, 1);
        lamp.AddComponent<Light>().Offset = new Vector2(1, 0);
        scene.Add(lamp);

        var guard = new Entity("Guard");
        guard.Transform.Position = new Vector3(0, 5, 0);
        guard.AddComponent<Chase>();
        guard.AddComponent<Shooter>();
        scene.Add(guard);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<Workspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<SceneViewModel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static void Near(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}.");

    [Fact]
    public void Distances_AreThePrimarysMarkedNumbers_AroundTheirCenters() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var (lamp, guard) = (Named(document, "Lamp"), Named(document, "Guard"));
        selection.Select(lamp);

        // A light's circle is around its offset, which turns and scales with the entity; its radius doesn't.
        var light = Assert.Single(model.Distances());
        Assert.Equal("Light.Radius", light.Key);
        Assert.Equal(4, light.Value);
        Near(new Vector2(4, 0), light.Center);
        Assert.Equal("Light.Radius = 4 around 4, 0", light.Label);

        // A shooter's range only counts with a target, and a zero (stop right on it) isn't drawn.
        selection.Select(guard);
        Assert.Equal(["Chase.Range"], model.Distances().Select(d => d.Key));
        guard.GetComponent<Shooter>()!.Target = "Player";
        guard.GetComponent<Chase>()!.StopDistance = 1.5f;
        Assert.Equal(["Chase.Range", "Chase.StopDistance", "Shooter.Range"], model.Distances().Select(d => d.Key));
        Assert.All(model.Distances(), d => Near(new Vector2(0, 5), d.Center));

        // Only the primary selection's show.
        selection.SelectEntities([guard, lamp]);
        Assert.Equal(["Light.Radius"], model.Distances().Select(d => d.Key));
        selection.Select(null);
        Assert.Empty(model.Distances());
    });

    [Fact]
    public void Scripts_MarkTheirOwnDistances() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        services.GetRequiredService<Workspace>().Components.Register<Watcher>();
        var lamp = Named(document, "Lamp");
        var watcher = lamp.AddComponent<Watcher>();
        selection.Select(lamp);

        // Only numbers with a fraction are distances; this one's around its eye, in the entity's units.
        var notice = model.Distances().Single(d => d.Component == watcher);
        Assert.Equal("Watcher.Notice", notice.Key);
        Near(new Vector2(2, 2), notice.Center);

        watcher.Awake = false;
        Assert.DoesNotContain(model.Distances(), d => d.Component == watcher);
    });

    [Fact]
    public void Dragging_SetsTheDistance_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var lamp = Named(document, "Lamp");
        var light = lamp.GetComponent<Light>()!;
        selection.Select(lamp);

        var drag = model.BeginDistance(model.Distances().Single())!;
        drag.To(5.333f, snap: false);
        Assert.Equal(5.33f, light.Radius);
        drag.To(6.3f, snap: true);
        Assert.Equal(6.5f, light.Radius);
        // Dragged into the middle it stops at a hundredth, rather than zero ("anywhere" for some).
        drag.To(-2, snap: false);
        Assert.Equal(0.01f, light.Radius);
        drag.To(7.25f, snap: false);
        drag.End();
        Assert.Equal(7.25f, light.Radius);
        Assert.Equal("Change Radius", document.History.UndoDescription);

        document.History.Undo();
        Assert.Equal(4, light.Radius);
        Assert.False(document.History.CanUndo);
    });

    [Fact]
    public void Commands_PickDistanceHandles() => EditorHost.Run(services =>
    {
        Open(services);
        var model = services.GetRequiredService<SceneViewModel>();
        var distance = services.GetRequiredService<CommandRegistry>().Get("View.DistanceHandles");
        Assert.False(distance.IsChecked);

        distance.ExecuteAsync().GetAwaiter().GetResult();
        Assert.Equal(SceneHandles.Distance, model.Handles);
        Assert.True(distance.IsChecked);
        // Nothing to resize while distances are out.
        Assert.Empty(model.ResizableShapes());
    });

    [Fact]
    public void Agents_ReachDistances() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var guard = Named(document, "Guard");
        selection.Select(guard);
        var surface = services.GetServices<Griff.Agent.IAgentToolSource>().OfType<SurfaceTools>().Single();
        var model = surface.All().Single(r => r.Name == nameof(SceneViewModel)).Target;

        // The chase range out to a point 3 right and 4 up of the guard: 5 away.
        using (var args = System.Text.Json.JsonDocument.Parse("""["chase.range", [3, 9]]"""))
            Griff.Agent.ObjectSurface.InvokeAsync(model, "DragDistance", args.RootElement).GetAwaiter().GetResult();
        Assert.Equal(5, guard.GetComponent<Chase>()!.Range);
        using (var args = System.Text.Json.JsonDocument.Parse("""["Chase.Range", [0, 7.2], true]"""))
            Griff.Agent.ObjectSurface.InvokeAsync(model, "DragDistance", args.RootElement).GetAwaiter().GetResult();
        Assert.Equal(2, guard.GetComponent<Chase>()!.Range);

        // A name that isn't there says what is.
        using (var args = System.Text.Json.JsonDocument.Parse("""["Light.Radius", [0, 0]]"""))
        {
            var error = Assert.Throws<Griff.Agent.AgentException>(() =>
                Griff.Agent.ObjectSurface.InvokeAsync(model, "DragDistance", args.RootElement).GetAwaiter().GetResult());
            Assert.Contains("it has Chase.Range", error.Message);
        }

        // What's there to drag, as agents see it.
        using (var none = System.Text.Json.JsonDocument.Parse("[]"))
        {
            var listed = Griff.Agent.ObjectSurface.InvokeAsync(model, "Distances", none.RootElement).GetAwaiter().GetResult();
            Assert.NotNull(listed);
            Assert.Contains("Chase.Range = 2 around 0, 5", listed.ToString());
        }
    });
}
