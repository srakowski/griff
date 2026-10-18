using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

// The field editors audit of the gameplay and UI components (Gameplay, UI, Story, Wiring, Data and Multiplayer): every
// field that takes text, a list, a point or a size, and the editor the Inspector gives it.
public sealed partial class FieldEditorAuditTests
{
    /// <summary>The categories the gameplay and UI audit covers.</summary>
    private static readonly string[] GameplayCategories = ["Gameplay", "UI", "Story", "Wiring", "Data", "Multiplayer"];


    [Fact]
    public void GameplayAndUiComponentsFields_GetTheEditorsTheAuditGaveThem() => EditorHost.Run(services =>
    {
        var registry = services.GetRequiredService<Griff.Editor.Core.Workspace.Workspace>().Components;
        var host = services.GetRequiredService<InspectorPanel>();
        var table = new List<string>();
        var plainKinds = new HashSet<FieldKind> { FieldKind.Bool, FieldKind.Number, FieldKind.Enum, FieldKind.Color };
        foreach (var descriptor in registry.All.Where(d => GameplayCategories.Contains(d.Category)).OrderBy(d => d.Category).ThenBy(d => d.DisplayName))
        {
            var component = descriptor.CreateInstance();
            foreach (var property in descriptor.Properties)
            {
                var field = new PropertyField(component, property, host);
                var name = $"{descriptor.DisplayName}.{property.Name}";
                if (plainKinds.Contains(field.Kind) || field.Kind == FieldKind.Vector && property.Offset is null && property.Resizable is null)
                    continue;
                table.Add($"{name}: {(field.Kind == FieldKind.Vector && property.Resizable is not null ? "Resizable" : EditorOf(field))}");
            }
        }

        Assert.Equal(GameplayExpected.ReplaceLineEndings("\n").Trim(), string.Join("\n", table));
    });

    // Category by category, as the core table: a picker of names (Names:Kind, * for several), an entity, action, output,
    // sound or image picker, an editor of its own (Format, NameList, ItemCounts...), or a Scene view knob (Offset) or
    // resize handles (Resizable).
    private const string GameplayExpected = """
        TableValues.Table: Names:Table
        TableValues.Row: Names:Row
        TableValues.SavedRow: Names:Saved
        TableValues.Sets: TableSets
        TableValues.Applied: Text
        Counter.Message: Names:Message
        Counter.Format: Format
        Counter.SaveBestAs: Names:Saved
        Counter.NewBestMessage: Names:Message
        Counter.SaveAs: Names:Saved
        Counter.MultiplyBy: Entity
        Counter.Item: Names:Item
        Counter.Track: Entity
        Counter.Output: Output
        Counter.ReachMessage: Names:Message
        Draggable.Kind: Names:Kind
        Draggable.PickUpSound: Sound
        Draggable.DropSound: Sound
        Draggable.PickUpMessage: Names:Message
        Draggable.DropMessage: Names:Message
        DropTarget.Accepts: Names:Kind*
        DropTarget.DropMessage: Names:Message
        DropTarget.TakeMessage: Names:Message
        DropTarget.AllFullMessage: Names:Message
        FallingBoard.Pieces: LetterCounts
        FallingBoard.FirstTile: TileNumbers
        FallingBoard.RotateAction: Names:Action
        FallingBoard.DropAction: Names:Action
        FallingBoard.MoveSound: Sound
        FallingBoard.RotateSound: Sound
        FallingBoard.LockSound: Sound
        FallingBoard.ClearSound: Sound
        FallingBoard.LevelUpSound: Sound
        FallingBoard.GameOverSound: Sound
        FallingBoard.LinesMessage: Names:Message
        FallingBoard.ScoreMessage: Names:Message
        FallingBoard.LevelMessage: Names:Message
        FallingBoard.GameOverMessage: Names:Message
        GameSpeed.Speeds: Rows
        Grabber.Action: Names:Action
        Grabber.HoldOffset: Offset
        Grabber.RefuseSound: Sound
        Grabber.PickUpMessage: Names:Message
        Grabber.DropMessage: Names:Message
        Inventory.Items: ItemCounts
        Inventory.ItemUses: ItemUses
        Inventory.UseSound: Sound
        Inventory.SaveAs: Names:Saved
        Inventory.ForTeam: Names:Kind
        MatchBoard.Gems: TileNumbers
        MatchBoard.Collect: TileCounts
        MatchBoard.Cleared: Text
        MatchBoard.PickAction: Names:Action
        MatchBoard.SwapSound: Sound
        MatchBoard.MatchSound: Sound
        MatchBoard.NoMatchSound: Sound
        MatchBoard.ShuffleSound: Sound
        MatchBoard.MatchMessage: Names:Message
        MatchBoard.ChainMessage: Names:Message
        MatchBoard.SettledMessage: Names:Message
        MatchBoard.ShuffleMessage: Names:Message
        MatchBoard.ScoreMessage: Names:Message
        MatchBoard.WinSound: Sound
        MatchBoard.OutOfMovesSound: Sound
        MatchBoard.WinMessage: Names:Message
        MatchBoard.OutOfMovesMessage: Names:Message
        MessageScope.Target: Entity
        PauseGroup.PauseMessage: Names:Message
        PauseGroup.ResumeMessage: Names:Message
        Pickup.Layers: Names:Layer*
        Pickup.Message: Names:Message
        Pickup.Item: Names:Item
        Pickup.Price: Rows
        Pickup.Sound: Sound
        Pickup.Effect: Names:Prefab
        Selectable.Kind: Names:Kind
        Selectable.Size: Resizable
        SelectionControls.BusyFlag: Names:Flag
        SelectionControls.MarkerImage: Image
        SelectionControls.BoxImage: Image
        ShowWhen.NeedsFlag: Names:Flag
        Spawner.Prefab: Names:Prefab
        Spawner.NeedsFlag: Names:Flag
        Spawner.Clock: Entity
        TacticsControls.ConfirmAction: Names:Action
        TacticsControls.CancelAction: Names:Action
        TacticsControls.SelectSound: Sound
        TacticsControls.CancelSound: Sound
        TacticsUnit.DamageRoll: Dice
        TacticsUnit.Layers: Names:Layer*
        TacticsUnit.AttackSound: Sound
        TacticsUnit.HitEffect: Names:Prefab
        Team.Name: Names:Kind
        Trigger.Message: Names:Message
        Trigger.From: Entity
        Trigger.Do: Action
        Trigger.Targets: Entities
        Trigger.With: ActionValue
        TurnOrder.Teams: NameList
        TurnOrder.ComputerTeams: Names:Kind*
        TurnOrder.EndAction: Names:Action
        TurnOrder.Format: Format
        TurnOrder.Message: Names:Message
        TurnOrder.Sound: Sound
        TurnOrder.Within: Entity
        TurnOrder.DefeatMessage: Names:Message
        TurnOrder.Current: Names:Kind
        TurnTaker.Team: Names:Kind
        Multiplayer.Player: Entity
        Multiplayer.Teams: NameList
        Multiplayer.ExitScene: Names:Scene
        NameTag.Offline: Text
        PlayerSpawn.Team: Names:Kind
        Scoreboard.Title: Text
        Scoreboard.Offline: Text
        Backlog.Lines: Lines
        Backlog.Size: Resizable
        Dialogue.Lines: DialogueLines
        Dialogue.Box: Entity
        Dialogue.Layers: Names:Layer*
        Dialogue.Pauses: Entity
        Dialogue.Message: Names:Message
        Dialogue.Prompt: Entity
        DialogueBox.NameTag: Entity
        DialogueBox.Portrait: Entity
        Speaker.Name: Text
        Speaker.PortraitImage: Image
        Typewriter.Sound: Sound
        Bar.Track: Entity
        Bar.Output: Output
        Bar.Size: Resizable
        Bar.FillImage: Image
        Bar.BackgroundImage: Image
        Button.Size: Resizable
        Button.Scene: Names:Scene
        Button.SavedScene: Names:Saved
        Button.Panel: Entity
        Button.Do: Action
        Button.Targets: Entities
        Button.With: ActionValue
        Button.Slot: Names:Kind
        Button.InputAction: Names:Action
        Button.NeedsSaved: Names:Saved
        Button.ShortcutAction: Names:Action
        Button.Sound: Sound
        Choice.Options: NameList
        Choice.Format: Format
        Choice.SaveAs: Names:Saved
        Choice.Sound: Sound
        ControlsList.Actions: Names:Action*
        ControlsList.Hide: Names:Action*
        ControlsList.Labels: Text
        ControlsList.Template: Entity
        ControlsList.Font: Names:Font
        Layout.Size: Resizable
        Panel.Pauses: Entity
        Panel.OpenOn: Names:Action
        Panel.OpenMessage: Names:Message
        Panel.NeedsFlag: Names:Flag
        Panel.First: Entity
        Panel.Sound: Sound
        SaveSlot.Slot: Names:Kind
        SaveSlot.Format: Format
        SaveSlot.EmptyText: Text
        SaveSlot.PictureSize: Resizable
        SaveSlot.PictureOffset: Offset
        SaveSlot.TextOffset: Offset
        Slider.SaveAs: Names:Saved
        Slider.Sound: Sound
        Slider.Size: Resizable
        Slider.FillImage: Image
        Slider.BackgroundImage: Image
        Slider.KnobImage: Image
        Stars.Track: Entity
        Stars.Output: Output
        Stars.Saved: Names:Saved
        Stars.Image: Image
        Stars.Sound: Sound
        TextField.Value: Text
        TextField.Placeholder: Text
        TextField.Size: Resizable
        Toggle.SaveAs: Names:Saved
        Toggle.OnText: Text
        Toggle.OffText: Text
        TogglesList.Table: Names:Table
        TogglesList.Hide: Names:Row*
        TogglesList.LabelColumn: Names:Column
        TogglesList.DefaultColumn: Names:Column
        TogglesList.SaveAs: Text
        TogglesList.OnText: Text
        TogglesList.OffText: Text
        TogglesList.Template: Entity
        TogglesList.Font: Names:Font
        Tooltip.Value: OutputFormat
        Tooltip.Into: Entity
        Tooltip.Track: Entity
        Tooltip.Output: Output
        Tooltip.Font: Names:Font
        Thruster.Direction: Direction
        Wire.Input: Choice
        Wire.From: Entity
        Wire.Output: Output
        WireButton.Sound: Sound
        WireDisplay.Format: NumberFormat
        WireSensor.Layers: Names:Layer*
        """;
}
