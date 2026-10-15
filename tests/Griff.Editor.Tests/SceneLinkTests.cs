using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.SceneView;
using Griff.Modules.Contraptions;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>The Scene view's arrows between the selection and the entities its fields name, or that name it.</summary>
public class SceneLinkTests
{
    // A lever that turns on the bridge, a lamp wired to the lever, and a camera following the player.
    private static (SceneDocument Document, ISelection Selection, SceneViewModel Model) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var lever = new Entity("Lever");
        lever.AddComponent(new Switch { Targets = "Bridge" });
        scene.Add(lever);
        scene.Add(new Entity("Bridge"));
        var lamp = new Entity("Lamp");
        lamp.AddComponent(new Wire { From = "Lever" });
        scene.Add(lamp);
        scene.Add(new Entity("Player"));
        var camera = new Entity("Camera");
        camera.AddComponent(new Follow { Target = "Player" });
        scene.Add(camera);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<SceneViewModel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    [Fact]
    public void Links_AreWhatTheSelectionNames_AndWhatNamesIt() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);

        Assert.Empty(model.Links());

        selection.Select(Named(document, "Lever"));
        Assert.Equal(["Lever to Bridge: Switch.Targets", "Lever to Lamp: Wire.From (Out to Power)"], model.Links().Select(l => l.Label));

        selection.Select(Named(document, "Player"));
        Assert.Equal(["Camera to Player: Follow.Target"], model.Links().Select(l => l.Label));
    });

    [Fact]
    public void Links_OfSeveralSelected_ListEachLinkOnce() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);

        selection.SelectEntities([Named(document, "Lever"), Named(document, "Bridge"), Named(document, "Lamp")]);

        Assert.Equal(["Lever to Bridge: Switch.Targets", "Lever to Lamp: Wire.From (Out to Power)"], model.Links().Select(l => l.Label));
    });

    [Fact]
    public void Links_FollowEdits() => EditorHost.Run(services =>
    {
        var (document, selection, model) = Open(services);
        var lever = Named(document, "Lever");
        selection.Select(lever);

        lever.GetComponent<Switch>()!.Targets = "Bridge, Player";
        Assert.Contains("Lever to Player: Switch.Targets", model.Links().Select(l => l.Label));

        lever.GetComponent<Switch>()!.Targets = "";
        Assert.Equal(["Lever to Lamp: Wire.From (Out to Power)"], model.Links().Select(l => l.Label));
    });
}
