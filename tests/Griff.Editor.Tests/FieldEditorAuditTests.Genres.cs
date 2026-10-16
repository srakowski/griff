using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

// The field editors audit of the genre modules' components (Arcade, Strategy, First Person, Blocks, Contraptions,
// physics games, cards, adventures, visual novels, rhythm and fighting; Arcade's pieces are presets with no fields
// of their own, the roguelike's, puzzles' and battles' were audited with Level and Combat, and the falling-blocks,
// match-3 and tactics boards with Gameplay): every field that takes text, a list
// or a spatial vector, and the editor the Inspector gives it. A field that slips back to a plain box, or a new one that
// lands as one, shows here. Fields of plain values (numbers, toggles, choices, colors, sizes) keep their kinds' editors.
public sealed partial class FieldEditorAuditTests
{
    // The core audit's categories (FieldEditorAuditTests), and Gameplay and Wiring, which the gameplay and UI audit covers
    // (the falling-blocks, match-3 and tactics boards and units among them).
    private static readonly string[] GenreElsewhere = ["Basics", "Rendering", "Physics", "Movement", "Combat", "Level", "Audio", "Gameplay", "Wiring"];


    // What the Inspector gives a field, as the audit table says it: the core audit's words, a rows editor by its kind of rows
    // ("Rows:Recipes"), and a kind of name by its scope ("Names:Kind(Team)").
    private static string GenreEditorOf(PropertyField field) => field switch
    {
        { Kind: FieldKind.Custom, Editor: RowsEditor rows } => $"Rows:{rows.Attribute.GetType().Name.Replace("Attribute", "", StringComparison.Ordinal)}",
        { Kind: FieldKind.Name, Property.Names: { Kind: NameKind.Kind, Scope: { } scope } } => $"Names:Kind({scope}){(field.NamesMany ? "*" : "")}",
        _ => EditorOf(field),
    };

    [Fact]
    public void GenreComponentsFields_GetTheEditorsTheAuditGaveThem() => EditorHost.Run(services =>
    {
        var registry = services.GetRequiredService<Griff.Editor.Core.Workspace.IWorkspace>().Components;
        var host = services.GetRequiredService<InspectorPanel>();
        var table = new List<string>();
        var plainKinds = new HashSet<FieldKind> { FieldKind.Bool, FieldKind.Number, FieldKind.Enum, FieldKind.Color };
        var genres = registry.All.Where(d => d.Type.Namespace?.StartsWith("Griff.Modules.", StringComparison.Ordinal) == true && !GenreElsewhere.Contains(d.Category));
        foreach (var descriptor in genres.OrderBy(d => d.Category).ThenBy(d => d.DisplayName))
        {
            var component = descriptor.CreateInstance();
            foreach (var property in descriptor.Properties)
            {
                var field = new PropertyField(component, property, host);
                var name = $"{descriptor.DisplayName}.{property.Name}";
                if (plainKinds.Contains(field.Kind) || field.Kind == FieldKind.Vector && property.Offset is null)
                    continue;
                table.Add($"{name}: {GenreEditorOf(field)}");
            }
        }

        Assert.Equal(GenreExpected.ReplaceLineEndings("\n").Trim(), string.Join("\n", table));
    });

    // Genre by genre, every field taking text, a list or a point, and what it gets: a picker of names (Names:Kind, * for
    // several), an entity, action, sound or image picker, rows of cells (Rows:...), or an editor of its own.
    private const string GenreExpected = """
        Hotspot.Name: Text
        Hotspot.Points: Points
        Hotspot.StandAt: Offset
        Hotspot.NeedsFlag: Names:Flag
        Hotspot.LookLine: Text
        Hotspot.LookDo: Action
        Hotspot.LookTargets: Entities
        Hotspot.LookWith: ActionValue
        Hotspot.LookFlag: Names:Flag
        Hotspot.LookMessage: Names:Message
        Hotspot.UseLine: Text
        Hotspot.UseDo: Action
        Hotspot.UseTargets: Entities
        Hotspot.UseWith: ActionValue
        Hotspot.UseFlag: Names:Flag
        Hotspot.UseMessage: Names:Message
        Hotspot.UseGives: Rows:Counts
        Hotspot.GoesTo: Names:Scene
        Hotspot.ArriveAt: InScene
        Hotspot.WrongItemLine: Text
        InventoryBar.Descriptions: Rows:ItemLines
        InventoryBar.Icons: Rows:ItemIcons
        InventoryBar.Combinations: Rows:Combinations
        InventoryBar.Table: Names:Table
        InventoryBar.CombinationsTable: Names:Table
        InventoryBar.WrongLine: Text
        InventoryBar.GetSound: Sound
        InventoryBar.PickSound: Sound
        InventoryBar.CombineSound: Sound
        InventoryBar.Holding: Text
        UseWith.Item: Names:Item*
        UseWith.NeedsFlag: Names:Flag
        UseWith.Line: Text
        UseWith.Do: Action
        UseWith.Targets: Entities
        UseWith.With: ActionValue
        UseWith.Flag: Names:Flag
        UseWith.Message: Names:Message
        UseWith.Gives: Rows:Counts
        UseWith.Sound: Sound
        WalkArea.Points: Points
        Walker.Camera: Entity
        Walker.UseAction: Names:Action
        Walker.LookAction: Names:Action
        Walker.ItemsAction: Names:Action
        Walker.CancelAction: Names:Action
        Walker.SaveRoomAs: Names:Saved
        Walker.Route: Text
        Walker.Heading: Text
        Walker.HeadingItem: Text
        Walker.Pointed: Text
        Walker.Said: Text
        BlockBuilder.Item: Names:Item
        BlockBuilder.World: Entity
        Blocks.Items: Rows:BlockItems
        Blocks.Drops: Rows:TileDrops
        Blocks.Hardness: Rows:TileAmounts
        Blocks.Unbreakable: TileNumbers
        Blocks.FallingTiles: TileNumbers
        Blocks.GlowingTiles: TileNumbers
        Blocks.SaveAs: Names:Saved
        Blocks.BreakEffect: Names:Prefab
        Blocks.DigSound: Sound
        Blocks.BreakSound: Sound
        Blocks.PlaceSound: Sound
        Crafting.Recipes: Rows:Recipes
        Crafting.Sound: Sound
        Hotbar.Colors: Rows:ItemColors
        WorldGenerator.GrassTile: TileNumbers
        WorldGenerator.DirtTile: TileNumbers
        WorldGenerator.StoneTile: TileNumbers
        WorldGenerator.Ores: Rows:TileAmounts
        WorldGenerator.SandTile: TileNumbers
        WorldGenerator.TrunkTile: TileNumbers
        WorldGenerator.LeavesTile: TileNumbers
        WorldGenerator.BottomTile: TileNumbers
        WorldGenerator.Walls: Entity
        WorldGenerator.Spawn: Entities
        Card.Title: Text
        Card.Text: Text
        Card.Kind: Names:Kind(Card)
        Card.Price: Rows:Counts
        Card.Image: Image
        Card.Font: Names:Font
        Card.Home: Entity
        Card.PlayedBy: Entity
        CardEffect.ChooseFrom: Entities
        CardEffect.DamageRoll: Dice
        CardEffect.HealRoll: Dice
        CardEffect.BuffStat: Stat
        CardEffect.Do: Action
        CardEffect.DoOn: Entities
        CardEffect.With: ActionValue
        CardEffect.Sound: Sound
        CardEffect.Effect: Names:Prefab
        Deck.RefillFrom: Entity
        Deck.Table: Names:Table
        Deck.Prefab: Names:Prefab
        Deck.PrefabColumn: Names:Column
        Deck.CountColumn: Names:Column
        Deck.Back: Image
        Deck.DrawSound: Sound
        Deck.ShuffleSound: Sound
        Deck.ShuffleMessage: Names:Message
        Deck.EmptyMessage: Names:Message
        Hand.Deck: Entity
        Hand.DiscardPile: Entity
        Hand.Hero: Entity
        Hand.PickAction: Names:Action
        Hand.CancelAction: Names:Action
        Hand.DrawAction: Names:Action
        Hand.PickSound: Sound
        Hand.DrawMessage: Names:Message
        Hand.Said: Text
        PlayZone.Accepts: Names:Kind(Card)*
        PlayZone.From: Entities
        PlayZone.DiscardPile: Entity
        PlayZone.Label: Text
        PlayZone.PlaySound: Sound
        PlayZone.RefuseSound: Sound
        PlayZone.PlayMessage: Names:Message
        SandboxTools.Tools: Rows:ListOf
        SandboxTools.Props: Names:Prefab*
        SandboxTools.Within: Entity
        SandboxTools.Sound: Sound
        Weld.To: Entity
        CharacterSelect.Characters: Names:Prefab*
        CharacterSelect.Scene: Names:Scene
        CharacterSelect.SaveAs: Names:Saved
        CharacterSelect.PickActions: Names:Action*
        CharacterSelect.BackAction: Names:Action
        CharacterSelect.FirstCursor: Entity
        CharacterSelect.SecondCursor: Entity
        CharacterSelect.FirstPreview: Entity
        CharacterSelect.SecondPreview: Entity
        CharacterSelect.MoveSound: Sound
        CharacterSelect.PickSound: Sound
        ComputerFighter.UseMoves: Choices
        ComputerFighter.SawMove: Text
        FightingCharacter.Moves: Moves
        FightingCharacter.MovesTable: Names:Table
        FightingCharacter.Buttons: Rows:Buttons
        FightingCharacter.Opponent: Entity
        FightingCharacter.BlockSound: Sound
        FightingCharacter.BlockEffect: Names:Prefab
        FightingCharacter.BlockMessage: Names:Message
        FightingCharacter.Move: Text
        FightingCharacter.ComboOn: Text
        FightingCharacter.History: Text
        Hitboxes.Boxes: FrameBoxes
        Hitboxes.Team: Names:Kind(Team)
        Hitboxes.Layers: Names:Layer*
        Hitboxes.HitSound: Sound
        Hitboxes.Effect: Names:Prefab
        Hitboxes.HitMessage: Names:Message
        Hitboxes.GotHitMessage: Names:Message
        Hitboxes.SwingClip: Text
        Hitboxes.Struck: Lines
        VersusMatch.First: Entity
        VersusMatch.Second: Entity
        VersusMatch.Picks: Names:Saved
        VersusMatch.RoundSound: Sound
        VersusMatch.FightSound: Sound
        VersusMatch.KoSound: Sound
        VersusMatch.WinSound: Sound
        VersusMatch.RoundMessage: Names:Message
        VersusMatch.RoundOverMessage: Names:Message
        VersusMatch.WinMessage: Names:Message
        VersusMatch.Announcement: Text
        VersusMatch.Winner: Text
        VersusMatch.FirstName: Text
        VersusMatch.SecondName: Text
        FirstPersonMovement.StepSound: Sound
        FirstPersonView.Viewer: Entity
        Weapon.Name: Text
        Weapon.Action: Names:Action
        Weapon.Ammo: Names:Item
        Weapon.NeedsItem: Names:Item
        Weapon.Projectile: Names:Prefab
        Weapon.ImpactEffect: Names:Prefab
        Weapon.Sound: Sound
        Weapon.EmptySound: Sound
        Weapon.Image: Image
        Weapon.FireImage: Image
        Breakable.Effect: Names:Prefab
        Breakable.Sound: Sound
        Breakable.HitSound: Sound
        Breakable.BreakMessage: Names:Message
        Breakable.DamageMessage: Names:Message
        Bumper.Direction: Direction
        Bumper.Layers: Names:Layer*
        Bumper.Message: Names:Message
        Bumper.Sound: Sound
        Drain.Plunger: Entity
        Drain.Layers: Names:Layer*
        Drain.Within: Entity
        Drain.Sound: Sound
        Drain.SaveSound: Sound
        Drain.DrainMessage: Names:Message
        Drain.SaveMessage: Names:Message
        Drain.GameOverMessage: Names:Message
        Drain.Serving: Text
        Flipper.Action: Names:Action
        Flipper.Sound: Sound
        Flipper.Message: Names:Message
        Lane.Message: Names:Message
        Lane.Layers: Names:Layer*
        Lane.Sound: Sound
        Lane.CompleteMessage: Names:Message
        LevelGoal.Targets: Entity
        LevelGoal.Slingshot: Entity
        LevelGoal.Score: Entity
        LevelGoal.SaveAs: Names:Saved
        LevelGoal.WonMessage: Names:Message
        LevelGoal.LostMessage: Names:Message
        LevelGoal.BonusSound: Sound
        LevelGoal.WinSound: Sound
        LevelGoal.LoseSound: Sound
        Plunger.Direction: Direction
        Plunger.Layers: Names:Layer*
        Plunger.Action: Names:Action
        Plunger.PullSound: Sound
        Plunger.LaunchSound: Sound
        Plunger.LaunchMessage: Names:Message
        Slingshot.Shot: Names:Prefab
        Slingshot.Pouch: Offset
        Slingshot.DefaultPull: Offset
        Slingshot.LaunchAction: Names:Action
        Slingshot.CancelAction: Names:Action
        Slingshot.LaunchSound: Sound
        Slingshot.StretchSound: Sound
        Slingshot.LaunchMessage: Names:Message
        Slingshot.OutOfShotsMessage: Names:Message
        Slingshot.Flying: Text
        Target.Message: Names:Message
        Target.Layers: Names:Layer*
        Target.Sound: Sound
        Target.CompleteMessage: Names:Message
        Tilt.Within: Entity
        Tilt.NudgeDirection: Direction
        Tilt.NudgeAction: Names:Action
        Tilt.NudgeSound: Sound
        Tilt.WarningSound: Sound
        Tilt.TiltSound: Sound
        Tilt.WarningMessage: Names:Message
        Tilt.TiltMessage: Names:Message
        NoteLanes.Song: Sound
        NoteLanes.SavedSong: Names:Saved
        NoteLanes.Chart: Names:Table
        NoteLanes.Difficulty: Rows:Difficulties
        NoteLanes.SavedDifficulty: Names:Saved
        NoteLanes.Clock: Entity
        NoteLanes.Actions: Names:Action*
        NoteLanes.Colors: Rows:ListOf
        NoteLanes.HitSound: Sound
        NoteLanes.MissSound: Sound
        NoteLanes.HitMessage: Names:Message
        NoteLanes.PerfectMessage: Names:Message
        NoteLanes.MissMessage: Names:Message
        NoteLanes.CompleteMessage: Names:Message
        NoteLanes.SaveBestAs: Names:Saved
        NoteLanes.Played: Text
        SongSelect.Songs: Rows:Songs
        SongSelect.Difficulties: Rows:Difficulties
        SongSelect.Scene: Names:Scene
        SongSelect.Back: Names:Scene
        SongSelect.SaveAs: Names:Saved
        SongSelect.SaveDifficultyAs: Names:Saved
        SongSelect.BestFrom: Names:Saved
        SongSelect.PlayActions: Names:Action*
        SongSelect.Cursor: Entity
        SongSelect.MoveSound: Sound
        SongSelect.PickSound: Sound
        Building.Team: Names:Kind(Team)
        Building.Cost: Rows:Counts
        Building.Hotkey: Choice
        Commander.Team: Names:Kind(Team)
        Commander.VictoryPanel: Entity
        Commander.DefeatPanel: Entity
        Commander.OrderSound: Sound
        Commander.ReadySound: Sound
        Faction.Team: Names:Kind(Team)
        Faction.Resources: ItemCounts
        Faction.DefeatMessage: Names:Message
        Fighter.Projectile: Names:Prefab
        Fighter.Sound: Sound
        Opponent.Builds: Names:Prefab*
        Opponent.Army: Names:Prefab*
        Producer.Trains: Names:Prefab*
        Resource.Kind: Names:Item
        Unit.Team: Names:Kind(Team)
        Unit.Cost: Rows:Counts
        Unit.Hotkey: Choice
        Worker.Builds: Names:Prefab*
        Character.Expressions: Rows:Expressions
        Character.Expression: Choice
        Character.Position: Choice
        Stage.Positions: Rows:Positions
        Stage.Backgrounds: Rows:Backgrounds
        Stage.Background: Choice
        Stage.AdvanceAction: Names:Action
        Stage.SkipAction: Names:Action
        Stage.AutoAction: Names:Action
        Stage.LeavingBackground: Text
        """;
}
