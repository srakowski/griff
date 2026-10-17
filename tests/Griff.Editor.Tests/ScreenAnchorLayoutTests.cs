using System.Numerics;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>The Scene view shows what's pinned to the screen where the game does, and dragging it sets its anchor's offset.</summary>
public class ScreenAnchorLayoutTests
{
    // The Scene view's camera frame: 16:9, so a camera of Size 5 reaches 5 * 16 / 9 to each side.
    private const float HalfWidth = 5 * 16f / 9f;

    // A camera of Size 5 at 2,1, and a HUD pinned to the top-left corner, one unit in from each edge, saved at 0,0.
    private static (SceneDocument Document, Entity Camera, Entity Hud) Open(IServiceProvider? services = null, float hudSize = 0)
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        camera.Transform.Position = new Vector3(2, 1, 0);
        var lens = camera.AddComponent<Camera>();
        lens.Size = 5;
        lens.HudSize = hudSize;
        scene.Add(camera);
        var hud = new Entity("Score");
        var pin = hud.AddComponent<ScreenAnchor>();
        pin.Anchor = Anchor.TopLeft;
        pin.Offset = new Vector2(1, -1);
        scene.Add(hud);

        var document = new SceneDocument(scene, "Test.scene");
        services?.GetRequiredService<Workspace>().EnterPlayMode(document);
        return (document, camera, hud);
    }

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}.");

    [Fact]
    public void Opening_PutsWhatsPinnedWhereTheGameShowsIt_WithoutUnsavedChanges()
    {
        var (document, _, hud) = Open();

        Near(new Vector3(2 - HalfWidth + 1, 1 + 5 - 1, 0), hud.Transform.Position);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void MovingTheCamera_TakesWhatsPinnedAlong_AndUndoingPutsItBack()
    {
        var (document, camera, hud) = Open();

        document.Execute(new SetPositionsEdit([camera.Transform], [new Vector3(10, 0, 0)]));
        Near(new Vector3(10 - HalfWidth + 1, 5 - 1, 0), hud.Transform.Position);

        document.History.Undo();
        Near(new Vector3(2 - HalfWidth + 1, 1 + 5 - 1, 0), hud.Transform.Position);
    }

    [Fact]
    public void ThePrefabTab_LeavesItWhereItsSaved()
    {
        var scene = new Scene();
        var hud = new Entity("Score");
        hud.AddComponent<ScreenAnchor>().Offset = new Vector2(1, -1);
        scene.Add(hud);

        _ = new SceneDocument(scene, "Score.prefab");

        Assert.Equal(Vector3.Zero, hud.Transform.Position);
    }

    [Fact]
    public void Dragging_WhatsPinned_SetsItsOffset_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, _, hud) = Open(services);
        var model = services.GetRequiredService<SceneViewModel>();
        services.GetRequiredService<ISelection>().Select(hud);
        var pin = hud.GetComponent<ScreenAnchor>()!;

        var move = model.BeginMove()!;
        move.To(new Vector2(0.5f, 0), snap: false);
        move.To(new Vector2(1, -0.5f), snap: false);
        move.End();

        Assert.Equal(new Vector2(2, -1.5f), pin.Offset);
        Near(new Vector3(2 - HalfWidth + 2, 1 + 5 - 1.5f, 0), hud.Transform.Position);
        document.History.Undo();
        Assert.Equal(new Vector2(1, -1), pin.Offset);
        Near(new Vector3(2 - HalfWidth + 1, 1 + 5 - 1, 0), hud.Transform.Position);
    });

    [Fact]
    public void Dragging_WhatsPinned_WithOthers_MovesThemAllAsOneStep() => EditorHost.Run(services =>
    {
        var (document, _, hud) = Open(services);
        var crate = new Entity("Crate");
        document.Scene.Add(crate);
        services.GetRequiredService<ISelection>().SelectEntities([hud, crate]);

        services.GetRequiredService<SceneViewModel>().MoveSelection(new Vector2(1, 1));

        Assert.Equal(new Vector2(2, 0), hud.GetComponent<ScreenAnchor>()!.Offset);
        Assert.Equal(new Vector3(1, 1, 0), crate.Transform.Position);
        Assert.Equal("Move 2 entities", document.History.UndoDescription);
        document.History.Undo();
        Assert.Equal(new Vector2(1, -1), hud.GetComponent<ScreenAnchor>()!.Offset);
        Assert.Equal(Vector3.Zero, crate.Transform.Position);
    });

    [Fact]
    public void WithHudSize_ItShowsAtTheCamerasZoom_AndADragMovesItsOffsetOnTheScreen() => EditorHost.Run(services =>
    {
        // Laid out for Size 2.5 and seen at 5: drawn twice as big, its offset twice as long.
        var (_, _, hud) = Open(services, hudSize: 2.5f);
        Near(new Vector3(2 - HalfWidth + 2, 1 + 5 - 2, 0), hud.Transform.Position);
        Assert.Equal(2, hud.Transform.WorldMatrix.M11, 4);

        services.GetRequiredService<ISelection>().Select(hud);
        services.GetRequiredService<SceneViewModel>().MoveSelection(new Vector2(2, 0));

        Assert.Equal(new Vector2(2, -1), hud.GetComponent<ScreenAnchor>()!.Offset);
    });

    [Fact]
    public void APinnedPartsPosition_IsNoPrefabOverride()
    {
        var (_, _, hud) = Open();
        var registry = Griff.Scenes.Serialization.ComponentRegistry.CreateDefault();
        var position = registry.Get(typeof(Transform)).Properties.Single(p => p.Name == nameof(Transform.Position));

        Assert.True(PrefabOverrides.IsOwn(hud.Transform, position));
    }
}
