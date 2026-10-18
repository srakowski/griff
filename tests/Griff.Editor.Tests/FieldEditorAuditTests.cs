using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The field editors audit of the core components (Basics, Rendering, Physics, Movement, Combat, Level and Audio; the
/// players' and enemies' pieces are in Movement and Combat): every field that takes text, a list or a spatial vector, and
/// the editor the Inspector gives it. A field that slips back to a plain box, or a new one that lands as one, shows here;
/// a plain box ("Text", "Lines") is only for a field marked [PlainText("why")], which <see cref="FieldCoverageTests"/>
/// holds every component to. Fields of plain values (numbers, toggles, choices, colors) keep the editors their kinds get.
/// </summary>
public sealed partial class FieldEditorAuditTests
{
    /// <summary>The categories this audit covers.</summary>
    private static readonly string[] Categories = ["Basics", "Rendering", "Physics", "Movement", "Combat", "Level", "Audio"];

    // What the Inspector gives a field, as the audit table says it.
    internal static string EditorOf(PropertyField field) => field.Kind switch
    {
        FieldKind.Custom => field.Editor!.GetType().Name.Replace("Editor", "", StringComparison.Ordinal),
        FieldKind.Name => $"Names:{field.Property.Names!.Kind}{(field.NamesMany ? "*" : "")}",
        FieldKind.Entity => field.NamesMany ? "Entities" : "Entity",
        FieldKind.Action => "Action",
        FieldKind.Output => "Output",
        FieldKind.Sound => "Sound",
        FieldKind.Image => "Image",
        FieldKind.Vector when field.Property.Offset is not null => "Offset",
        var kind => kind.ToString(),
    };

    [Fact]
    public void CoreComponentsFields_GetTheEditorsTheAuditGaveThem() => EditorHost.Run(services =>
    {
        var registry = services.GetRequiredService<Griff.Editor.Core.Workspace.Workspace>().Components;
        var host = services.GetRequiredService<InspectorPanel>();
        var table = new List<string>();
        var plainKinds = new HashSet<FieldKind> { FieldKind.Bool, FieldKind.Number, FieldKind.Enum, FieldKind.Color };
        foreach (var descriptor in registry.All.Where(d => Categories.Contains(d.Category)).OrderBy(d => d.Category).ThenBy(d => d.DisplayName))
        {
            var component = descriptor.CreateInstance();
            foreach (var property in descriptor.Properties)
            {
                var field = new PropertyField(component, property, host);
                var name = $"{descriptor.DisplayName}.{property.Name}";
                if (plainKinds.Contains(field.Kind) || field.Kind == FieldKind.Vector && property.Offset is null)
                    continue;
                table.Add($"{name}: {EditorOf(field)}");
            }
        }

        Assert.Equal(Expected.ReplaceLineEndings("\n").Trim(), string.Join("\n", table));
    });

    // Category by category, every field taking text, a list or a point, and what it gets: a picker of names (Names:Kind,
    // * for several), an entity picker, a sound or image picker, or an editor of its own (Dice, Points, TileNumbers...).
    private const string Expected = """
        BackgroundMusic.Music: Sound
        BeatClock.BeatMessage: Names:Message
        BeatClock.BarMessage: Names:Message
        BeatPulse.Clock: Entity
        Calibration.TapAction: Names:Action
        SoundPlayer.Sound: Sound
        SoundPlayer.Message: Names:Message
        SoundSource.Sound: Sound
        Attack.Action: Names:Action
        Attack.Hitbox: Entity
        Attack.Sound: Sound
        Attack.Target: Entity
        Battler.Foe: Entities
        Battler.Title: Text
        Battler.DamageRoll: Dice
        Battler.Spell: Text
        Battler.SpellDamageRoll: Dice
        Battler.SpellHealRoll: Dice
        Battler.Item: Text
        Battler.ItemHealRoll: Dice
        Battler.ItemDamageRoll: Dice
        Battler.AttackAction: Names:Action
        Battler.SpellAction: Names:Action
        Battler.ItemAction: Names:Action
        Battler.AttackSound: Sound
        Battler.SpellSound: Sound
        Battler.ItemSound: Sound
        Battler.FailSound: Sound
        Battler.Effect: Names:Prefab
        Battler.Said: Text
        BumpAttack.DamageRoll: Dice
        BumpAttack.Layers: Names:Layer*
        BumpAttack.Sound: Sound
        BumpAttack.Effect: Names:Prefab
        DamageOnTouch.AmountRoll: Dice
        DamageOnTouch.Layers: Names:Layer*
        Health.DeathMessage: Names:Message
        Health.HurtSound: Sound
        Health.DeathSound: Sound
        Health.SaveAs: Names:Saved
        Shooter.Prefab: Names:Prefab
        Shooter.Target: Entity
        Shooter.Direction: Direction
        Shooter.Offset: Offset
        Shooter.Sound: Sound
        Shooter.Action: Names:Action
        Shooter.Clock: Entity
        Bouncer.Direction: Direction
        Bouncer.Layers: Names:Layer*
        Bouncer.Sound: Sound
        Checkpoint.Layers: Names:Layer*
        Checkpoint.Sound: Sound
        Checkpoint.Message: Names:Message
        Crumble.Layers: Names:Layer*
        Crumble.Sound: Sound
        Crumble.Effect: Names:Prefab
        Door.Scene: Names:Scene
        Door.ArriveAt: InScene
        Door.Target: Entity
        Door.Layers: Names:Layer*
        Door.Sound: Sound
        Door.Prompt: Entity
        Door.Key: Names:Item
        Door.NeedsFlag: Names:Flag
        DungeonGenerator.WallTile: TileNumbers
        DungeonGenerator.FloorTile: TileNumbers
        DungeonGenerator.Players: Entities
        DungeonGenerator.Exit: Entity
        DungeonGenerator.Spawns: PrefabCounts
        DungeonGenerator.NextFloorMessage: Names:Message
        DungeonGenerator.FloorMessage: Names:Message
        DungeonGenerator.FloorSound: Sound
        Goal.Accepts: Names:Kind*
        Goal.Sound: Sound
        Goal.CoverMessage: Names:Message
        Goal.UncoverMessage: Names:Message
        Goal.AllCoveredMessage: Names:Message
        Minigame.Scene: Names:Scene
        Minigame.WonMessage: Names:Message
        Minigame.LostMessage: Names:Message
        MinigameRoot.Scene: Names:Scene
        MinigameRoot.OpenedFrom: Text
        MinigameRoot.WonMessage: Names:Message
        MinigameRoot.LostMessage: Names:Message
        MinigameRoot.FocusBefore: Lines
        MinigameRoot.MusicBefore: Text
        MoveHistory.UndoAction: Names:Action
        MoveHistory.RestartAction: Names:Action
        MoveHistory.Within: Entity
        MoveHistory.UndoSound: Sound
        MoveHistory.RestartSound: Sound
        MoveHistory.UndoMessage: Names:Message
        MoveHistory.RestartMessage: Names:Message
        Pushable.Layers: Names:Layer*
        Pushable.Kind: Names:Kind
        RememberScene.Forget: Entities
        Room.Music: Sound
        Room.Message: Names:Message
        Switch.Targets: Entities
        Switch.Hides: Entities
        Switch.Layers: Names:Layer*
        Switch.Key: Names:Item
        Switch.NeedsFlag: Names:Flag
        Switch.Sound: Sound
        Switch.OnMessage: Names:Message
        Switch.OffMessage: Names:Message
        Switch.Prompt: Entity
        Switch.Clock: Entity
        WallRooms.WallTiles: TileNumbers
        WallRooms.WallLayers: Names:Layer*
        WallRooms.Message: Names:Message
        CameraControls.StayInside: Entity
        Chase.Target: Entity
        Chase.Around: Entity
        Follow.Target: Entity
        Follow.StayInside: Entity
        GridChase.Target: Entity
        GridMovement.Map: Entity
        GridMovement.StepSound: Sound
        GridMovement.BumpSound: Sound
        GridMovement.Destination: Text
        GridMovement.BlockingLayers: Names:Layer*
        GridMovement.ArrivedMessage: Names:Message
        GridMovement.BlockedMessage: Names:Message
        GridPatrol.Points: Points
        Mover.Points: Points
        Mover.Clock: Entity
        PlatformerMovement.JumpSound: Sound
        PlatformerMovement.LandSound: Sound
        PlatformerMovement.DashSound: Sound
        PlatformerMovement.DustEffect: Names:Prefab
        TopDownMovement.DashSound: Sound
        BoxCollider.Layer: Names:Layer
        BoxCollider.IgnoreLayers: Names:Layer*
        CircleCollider.Layer: Names:Layer
        CircleCollider.IgnoreLayers: Names:Layer*
        HingeJoint.To: Entity
        PolygonCollider.Points: Points
        PolygonCollider.Layer: Names:Layer
        PolygonCollider.IgnoreLayers: Names:Layer*
        Rope.To: Entity
        Rope.BreakMessage: Names:Message
        Animation.Clips: Clips
        Animation.Clip: Choice
        Light.Offset: Offset
        ParticleEmitter.Image: Image
        Sprite.Image: Image
        Text.Value: OutputFormat
        Text.Font: Names:Font
        Text.Track: Entity
        Text.Output: Output
        Tilemap.Image: Image
        Tilemap.PlaceholderColors: Rows
        Tilemap.MoreTilesets: Tilesets
        Tilemap.BackgroundTiles: TileNumbers
        Tilemap.OneWayTiles: TileNumbers
        Tilemap.AnimatedTiles: TileAnimations
        Tilemap.LadderTiles: TileNumbers
        Tilemap.SlopeUpTiles: TileNumbers
        Tilemap.SlopeDownTiles: TileNumbers
        Tilemap.AutoTiles: AutoTiles
        Tilemap.Layer: Names:Layer
        Tilemap.IgnoreLayers: Names:Layer*
        Tilemap.Tiles: Lines
        Tilemap.LinesUpWith: Entity
        Weather.ThunderSound: Sound
        """;
}
