using System.Numerics;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Play;
using Griff.Modules.Fighting;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Clip Editor's hitboxes and hurtboxes: drawn, moved, resized, set and removed on the frame showing, copied from the
/// frame before, shown under onion skin, following a clip renamed or removed, each change one undo step, and all of it
/// reached by agents; and Game &gt; Show Hitboxes.
/// </summary>
public sealed class ClipEditorBoxTests
{
    // A fighter on an 8-frame sheet with Idle and a four-frame Punch, and the Clip Editor previewing Punch on its second frame.
    private static (SceneDocument Document, Entity Fighter, ClipEditor Clips) Open(IServiceProvider services, params string[] boxes)
    {
        var scene = new Scene();
        var fighter = new Entity("Fighter");
        fighter.AddComponent(new Sprite { Columns = 4, Rows = 2, Size = new Vector2(1.5f, 2) });
        fighter.AddComponent(new Animation { FramesPerSecond = 10, Clips = "Idle 0-1, Punch 2-5 @12 once", Clip = "Idle" });
        if (boxes.Length > 0)
            fighter.AddComponent(new Hitboxes { Boxes = boxes });
        scene.Add(fighter);

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        services.GetRequiredService<ISelection>().Select(fighter);
        var clips = services.GetRequiredService<ClipEditor>();
        clips.SelectClip("Punch");
        clips.ShowFrame(1);
        return (document, fighter, clips);
    }

    private static IReadOnlyList<string> Lines(Entity fighter) => fighter.GetComponent<Hitboxes>()!.Boxes;

    // A list box bound to one of the Clip Editor's lists and what it has picked, both ways, as its window's are.
    private static System.Windows.Controls.ListBox Bound(ClipEditor clips, string items, string picked)
    {
        var list = new System.Windows.Controls.ListBox { DataContext = clips };
        list.SetBinding(System.Windows.Controls.ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding(items));
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
            new System.Windows.Data.Binding(picked) { Mode = System.Windows.Data.BindingMode.TwoWay });
        return list;
    }

    [Fact]
    public void DrawingTheFirstBoxAddsAHitboxes_AndEveryChangeIsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, fighter, clips) = Open(services);
        Assert.True(clips.CanEditBoxes);
        Assert.Null(clips.Hitboxes);
        Assert.Equal("Punch", clips.BoxClip);
        Assert.Equal(new Vector2(1.5f, 2), clips.FrameSize);
        Assert.Equal("Frame 2: no boxes. Pick Hitbox or Hurtbox and drag over the picture.", clips.BoxesSummary);

        clips.AddBox(FrameBoxKind.Hit, 0.5f, 0.2f, 0.4f, 0.3f);
        Assert.Equal(["Punch 1 hit at 0.5,0.2 size 0.4x0.3"], Lines(fighter));
        Assert.Equal("Add Hitbox to Punch Frame 2", document.History.UndoDescription);
        Assert.Equal(0, clips.SelectedBox!.Index);
        Assert.Equal("Hitbox 1: 1 damage", clips.SelectedBox.Label);
        Assert.False(clips.IsPlaying);

        clips.AddCircle(FrameBoxKind.Hurt, 0, 0.55f, 0.28f);
        Assert.Equal("Punch 1 hurt at 0,0.55 radius 0.28", Lines(fighter)[1]);
        Assert.Equal(1, clips.SelectedBox!.Index);
        Assert.Equal("Frame 2: 1 hitbox, 1 hurtbox", clips.BoxesSummary);
        clips.BoxMultiplier = 1.5f;
        Assert.Equal("Set Hurtbox Multiplier to 1.5", document.History.UndoDescription);
        Assert.Equal("Hurtbox 1 (circle), takes ×1.5", clips.SelectedBox!.Label);

        clips.SelectBox(0);
        clips.BoxDamage = 3;
        clips.BoxKnockbackX = 4;
        clips.BoxKnockbackY = 2;
        clips.BoxStun = 12;
        clips.BoxGroup = 2;
        Assert.Equal("Set Hitbox Group to 2", document.History.UndoDescription);
        Assert.Equal("Punch 1 hit at 0.5,0.2 size 0.4x0.3 damage 3 knockback 4,2 stun 12 group 2", Lines(fighter)[0]);
        Assert.Equal("Hitbox 1: 3 damage, knockback 4, 2, 12 stun, group 2", clips.SelectedBox!.Label);
        Assert.Throws<InvalidOperationException>(() => clips.BoxStun = -1);
        Assert.Throws<InvalidOperationException>(() => clips.AddBox(FrameBoxKind.Hit, 0, 0, 0, 1));

        clips.MoveBox(0, 0.6f, 0.25f);
        Assert.Equal("Move Hitbox on Punch Frame 2", document.History.UndoDescription);
        clips.ResizeBox(0, 0.5f, 0.5f);
        Assert.StartsWith("Punch 1 hit at 0.6,0.25 size 0.5x0.5", Lines(fighter)[0]);
        clips.SetBoxBounds(1, 0.1f, 0.6f, 0.8f, 0.4f);
        Assert.Equal("Punch 1 hurt at 0.1,0.6 radius 0.4 multiplier 1.5", Lines(fighter)[1]);
        clips.RemoveBox(0);
        Assert.Equal("Remove Hitbox from Punch Frame 2", document.History.UndoDescription);
        Assert.Single(Lines(fighter));
        Assert.Null(clips.SelectedBox);

        // Undoing it all takes the Hitboxes away again, one step at a time.
        while (document.History.CanUndo)
            document.History.Undo();
        Assert.Null(fighter.GetComponent<Hitboxes>());
        Assert.Empty(clips.Boxes);
        Assert.Contains("scene", EditorHost.AgentRedo(services));
        Assert.Equal(["Punch 1 hit at 0.5,0.2 size 0.4x0.3"], Lines(fighter));
        Assert.True(EditorHost.Undo(services, clips));
        Assert.Null(fighter.GetComponent<Hitboxes>());
    });

    [Fact]
    public void EachFrameHasItsOwnBoxes_AndOtherLinesAreKeptAsTheyAre() => EditorHost.Run(services =>
    {
        var (document, fighter, clips) = Open(services,
            "Idle 0 hurt at 0,0 size 0.5x1.2",
            "a line that doesn't read",
            "Punch 1 hit at 0.5,0.2 size 0.4x0.3",
            "Punch 2 hurt   at 0,0 size 0.5x1.2");
        Assert.Equal(["Hitbox 1: 1 damage"], clips.Boxes.Select(b => b.Label));
        clips.Step(1);
        Assert.Equal(["Hurtbox 1"], clips.Boxes.Select(b => b.Label));
        clips.SelectClip("Idle");
        Assert.Equal("Idle", clips.BoxClip);
        Assert.Single(clips.Boxes);

        clips.SelectClip("Punch");
        clips.ShowFrame(1);
        clips.AddBox(FrameBoxKind.Hurt, 0, 0, 0.5f, 1.2f);
        Assert.Equal(
        [
            "Idle 0 hurt at 0,0 size 0.5x1.2",
            "a line that doesn't read",
            "Punch 1 hit at 0.5,0.2 size 0.4x0.3",
            "Punch 1 hurt at 0,0 size 0.5x1.2",
            "Punch 2 hurt   at 0,0 size 0.5x1.2",
        ], Lines(fighter));
        Assert.Equal(2, clips.Boxes.Count);
    });

    [Fact]
    public void CopiesTheFrameBefore_AndShowsItUnderOnionSkin() => EditorHost.Run(services =>
    {
        var (document, fighter, clips) = Open(services, "Punch 0 hurt at 0,0 size 0.5x1.2", "Punch 1 hit at 0.5,0.2 size 0.4x0.3 damage 2");
        clips.ShowFrame(0);
        Assert.False(clips.CopyPreviousFrameCommand.CanExecute(null));
        Assert.Throws<InvalidOperationException>(clips.CopyPreviousFrame);

        clips.ShowFrame(2);
        Assert.Empty(clips.OnionBoxes);
        clips.OnionSkin = true;
        Assert.Equal(["Hitbox 1: 2 damage"], clips.OnionBoxes.Select(b => b.Label));
        Assert.True(clips.CopyPreviousFrameCommand.CanExecute(null));
        clips.CopyPreviousFrame();
        Assert.Equal("Copy Boxes from Frame 2 to Punch Frame 3", document.History.UndoDescription);
        Assert.Equal("Punch 2 hit at 0.5,0.2 size 0.4x0.3 damage 2", Lines(fighter)[2]);
        Assert.Equal(["Hitbox 1: 2 damage"], clips.Boxes.Select(b => b.Label));

        // Stepping on, the onion skin shows the frame before that one.
        clips.Step(1);
        Assert.Equal(["Hitbox 1: 2 damage"], clips.OnionBoxes.Select(b => b.Label));
        Assert.Empty(clips.Boxes);
        clips.OnionSkin = false;
        Assert.Empty(clips.OnionBoxes);
    });

    [Fact]
    public void ClipsRenamedOrRemovedTakeTheirBoxesAlong_InTheSameUndoStep() => EditorHost.Run(services =>
    {
        var (document, fighter, clips) = Open(services, "Idle 0 hurt at 0,0 size 0.5x1.2", "Punch 1 hit at 0.5,0.2 size 0.4x0.3");
        clips.RenameClip("Jab");
        Assert.Equal(["Idle 0 hurt at 0,0 size 0.5x1.2", "Jab 1 hit at 0.5,0.2 size 0.4x0.3"], Lines(fighter));
        clips.RemoveClip();
        Assert.Equal(["Idle 0 hurt at 0,0 size 0.5x1.2"], Lines(fighter));
        document.History.Undo();
        Assert.Equal(["Idle 0 hurt at 0,0 size 0.5x1.2", "Jab 1 hit at 0.5,0.2 size 0.4x0.3"], Lines(fighter));
        document.History.Undo();
        Assert.Equal(["Idle 0 hurt at 0,0 size 0.5x1.2", "Punch 1 hit at 0.5,0.2 size 0.4x0.3"], Lines(fighter));
    });

    [Fact]
    public void AgentsDrawAndSetBoxes_ThroughTheClipEditor() => EditorHost.Run(services =>
    {
        var (document, fighter, clips) = Open(services);
        var tools = services.GetRequiredService<AgentServer>().Tools;

        // What ui_invoke runs (on the UI thread), then ui_set.
        EditorHost.Pump(ObjectSurface.InvokeAsync(clips, "AddBox", System.Text.Json.JsonDocument.Parse("""["Hit", 0.5, 0.2, 0.4, 0.3]""").RootElement));
        tools["ui_set"].Invoke(ToolArgs.Parse("""{"target": "ClipEditor", "path": "BoxDamage", "value": 4}""")).GetAwaiter().GetResult();
        tools["ui_set"].Invoke(ToolArgs.Parse("""{"target": "ClipEditor", "path": "OnionSkin", "value": true}""")).GetAwaiter().GetResult();
        Assert.Equal(["Punch 1 hit at 0.5,0.2 size 0.4x0.3 damage 4"], Lines(fighter));
        Assert.True(clips.OnionSkin);
        var seen = tools["ui_inspect"].Invoke(ToolArgs.Parse("""{"target": "ClipEditor", "path": "Boxes"}""")).GetAwaiter().GetResult();
        Assert.Contains("Hitbox 1: 4 damage", seen.Text);
        Assert.Equal("Set Hitbox Damage to 4", document.History.UndoDescription);
    });

    [Fact]
    public void WithItsWindowOpen_AChangeKeepsTheFrameAndTheBoxPicked() => EditorHost.Run(services =>
    {
        var (_, fighter, clips) = Open(services, "Punch 2 hurt at 0,0 size 0.5x1.2");
        // The window's lists (bound as its list boxes are) let go of what they show picked as they're made again; that
        // mustn't move the preview or lose the box picked.
        var lists = new[] { Bound(clips, nameof(ClipEditor.Clips), nameof(ClipEditor.Selected)), Bound(clips, nameof(ClipEditor.Boxes), nameof(ClipEditor.SelectedBox)) };
        try
        {
            clips.ShowFrame(2);
            clips.SelectBox(0);
            clips.MoveBox(0, 0.1f, 0);
            Assert.Equal(2, clips.Frame);
            Assert.Equal("Punch", clips.Selected!.Name);
            Assert.Equal(0, clips.SelectedBox!.Index);
            clips.AddBox(FrameBoxKind.Hit, 0.5f, 0.2f, 0.3f, 0.3f);
            Assert.Equal(2, clips.Frame);
            Assert.Equal(1, clips.SelectedBox!.Index);
            Assert.Equal(["Punch 2 hurt at 0.1,0 size 0.5x1.2", "Punch 2 hit at 0.5,0.2 size 0.3x0.3"], Lines(fighter));
        }
        finally
        {
            GC.KeepAlive(lists);
        }
    });

    [Fact]
    public void ShowHitboxes_IsAGameMenuToggle() => EditorHost.Run(services =>
    {
        var command = services.GetRequiredService<CommandRegistry>().Get("Game.ShowHitboxes");
        var play = services.GetRequiredService<PlayMode>();
        Assert.Equal("Show _Hitboxes", command.Text);
        Assert.False(command.IsChecked);

        EditorHost.Pump(command.ExecuteAsync());
        Assert.True(play.ShowHitboxes);
        Assert.True(command.IsChecked);
        EditorHost.Pump(command.ExecuteAsync());
        Assert.False(play.ShowHitboxes);
    });
}
