using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Triggers in the editor: what one does is picked from what its targets can do, flagged when they can't, undone like
/// any edit, and drawn in the Scene view as an arrow from it to what it drives.
/// </summary>
public sealed class InspectorTriggerTests
{
    // A boss with health, a board with a scope, a door, and rules: a trigger that damages the boss when the board scores.
    private static (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var boss = new Entity("Boss");
        boss.AddComponent(new Health { Max = 10 });
        scene.Add(boss);
        var board = new Entity("Board A");
        board.AddComponent<MessageScope>();
        scene.Add(board);
        var door = new Entity("Door");
        door.AddComponent<BoxCollider>().IsTrigger = true;
        door.AddComponent<Door>();
        scene.Add(door);
        var rules = new Entity("Rules");
        rules.Transform.Position = new System.Numerics.Vector3(4, 0, 0);
        rules.AddComponent(new Trigger { Message = "Score", From = "Board A", Do = "Damage", Targets = "Boss", With = "2" });
        scene.Add(rules);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<Workspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<InspectorPanel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static PropertyField Field(InspectorPanel inspector, string label) =>
        inspector.Components.Single(c => c.Title == "Trigger").Properties.Single(p => p.Label == label);

    [Fact]
    public void ATriggersDo_IsPickedFromWhatItsTargetsCanDo_AndUndoes() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var trigger = Named(document, "Rules").GetComponent<Trigger>()!;
        selection.Select(trigger.Entity!);

        var section = inspector.Components.Single(c => c.Title == "Trigger");
        Assert.Equal("When Score arrives from Board A, Damage 2 on Boss.", section.SetupNote);
        var doField = Field(inspector, "Do");
        Assert.Equal(FieldKind.Action, doField.Kind);
        Assert.True(doField.HasPicker);
        Assert.False(doField.IsUnmatched);

        doField.IsPickerOpen = true;
        Assert.Equal(["Damage Health, an amount", "Heal Health, an amount", "Kill Health"], doField.Picker!.Matches.Select(c => $"{c.Name} {c.Detail}"));
        doField.Picker.Pick("Kill");
        Assert.Equal("Kill", trigger.Do);

        // Kill takes no value, so With isn't shown.
        Assert.DoesNotContain(inspector.Components.Single(c => c.Title == "Trigger").Properties, p => p.Label == "With");
        document.History.Undo();
        Assert.Equal("Damage", trigger.Do);
        Assert.Contains(inspector.Components.Single(c => c.Title == "Trigger").Properties, p => p.Label == "With");
    });

    [Fact]
    public void ADoNoTargetCanDo_IsFlagged_AndNewTargetsBringTheirActions() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var trigger = Named(document, "Rules").GetComponent<Trigger>()!;
        selection.Select(trigger.Entity!);

        Field(inspector, "Targets").TextValue = "Door";
        var doField = Field(inspector, "Do");
        Assert.Equal(["Damage"], doField.UnmatchedNames);
        Assert.Contains("Nothing it's done on has an action called \"Damage\"", doField.UnmatchedTip);

        doField.IsPickerOpen = true;
        Assert.Equal(["Open", "Lock"], doField.Picker!.Matches.Select(c => c.Name));
        doField.Picker.Pick("Open");
        Assert.False(Field(inspector, "Do").IsUnmatched);

        // With no targets, it's done on its own entity: a trigger can always set a flag, load a scene, and play, end or forget one.
        Field(inspector, "Targets").TextValue = "";
        Field(inspector, "Do").IsPickerOpen = true;
        Assert.Equal(["Set Flag", "Clear Flag", "Load Scene", "Play Minigame", "Win Minigame", "Lose Minigame", "Forget Scene"], Field(inspector, "Do").Picker!.Matches.Select(c => c.Name));
    });

    [Fact]
    public void TheSceneView_DrawsAnArrowFromATriggerToWhatItDrives_AndFromWhatItHears() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        selection.Select(Named(document, "Rules"));

        var links = services.GetRequiredService<SceneViewModel>().Links();

        var drives = links.Single(l => l.To.Name == "Boss");
        Assert.Equal(("Rules", "Boss", "Score: Damage 2"), (drives.Tail.Name, drives.Head.Name, drives.Words));
        var hears = links.Single(l => l.To.Name == "Board A");
        Assert.Equal(("Board A", "Rules", "Score"), (hears.Tail.Name, hears.Head.Name, hears.Words));
    });
}
