using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Outputs in the editor: what a counter, bar or text shows is picked from what the entity it reads knows, flagged when it
/// knows nothing called that, undone like any edit, and drawn in the Scene view as an arrow from what it reads.
/// </summary>
public sealed class InspectorOutputTests
{
    // A turn order, a counter of its rounds and a text showing whose turn it is.
    private static (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var turns = new Entity("Turns");
        turns.AddComponent(new TurnOrder { Sides = "Knights, Orcs" });
        scene.Add(turns);
        var rounds = new Entity("Rounds");
        rounds.AddComponent(new Counter { Track = "Turns", Output = "Round" });
        scene.Add(rounds);
        var label = new Entity("Label");
        label.AddComponent(new Text { Track = "Turns", Output = "Turn", Value = "{0} to move" });
        scene.Add(label);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<InspectorPanel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static PropertyField Field(InspectorPanel inspector, string component, string label) =>
        inspector.Components.Single(c => c.Title == component).Properties.Single(p => p.Label == label);

    [Fact]
    public void ACountersOutput_IsPickedFromTheNumbersItsTrackKnows_AndUndoes() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var counter = Named(document, "Rounds").GetComponent<Counter>()!;
        selection.Select(counter.Entity!);

        var output = Field(inspector, "Counter", "Output");
        Assert.Equal(FieldKind.Output, output.Kind);
        Assert.True(output.HasPicker);
        Assert.False(output.IsUnmatched);

        output.IsPickerOpen = true;
        Assert.Equal(["Round", "Time Left", "Left:Enemy"], output.Picker!.Matches.Select(c => c.Name));
        Assert.Equal("Turn Order, a whole number for a side after the colon", output.Picker.Matches.Single(c => c.Name == "Left:Enemy").Detail);
        output.Picker.Pick("Time Left");
        Assert.Equal("Time Left", counter.Output);

        document.History.Undo();
        Assert.Equal("Round", counter.Output);
    });

    [Fact]
    public void AnOutputItsTrackHasnt_IsFlagged_AndATextShowsNamesToo() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Rounds"));

        Field(inspector, "Counter", "Output").TextValue = "Lives";
        var output = Field(inspector, "Counter", "Output");
        Assert.Equal(["Lives"], output.UnmatchedNames);
        Assert.Contains("Nothing it reads has an output called \"Lives\"", output.UnmatchedTip);

        // A counter counts numbers, so a name isn't one it can have; a text shows it.
        Field(inspector, "Counter", "Output").TextValue = "Turn";
        Assert.True(Field(inspector, "Counter", "Output").IsUnmatched);
        selection.Select(Named(document, "Label"));
        var shown = Field(inspector, "Text", "Output");
        Assert.False(shown.IsUnmatched);
        shown.IsPickerOpen = true;
        Assert.Contains("Turn", shown.Picker!.Matches.Select(c => c.Name));
    });

    [Fact]
    public void TheSceneView_DrawsAnArrowFromWhatACounterReads_LabelledWithIt() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        selection.Select(Named(document, "Turns"));

        var links = services.GetRequiredService<SceneViewModel>().Links();

        var rounds = links.Single(l => l.From.Name == "Rounds");
        Assert.Equal(("Turns", "Rounds", "Round"), (rounds.Tail.Name, rounds.Head.Name, rounds.Words));
        Assert.Equal("Turns to Rounds: Counter.Track (Round)", rounds.Label);
        var label = links.Single(l => l.From.Name == "Label");
        Assert.Equal(("Turns", "Label", "Turn"), (label.Tail.Name, label.Head.Name, label.Words));
    });
}
