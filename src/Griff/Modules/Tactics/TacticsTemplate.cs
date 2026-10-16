using System.Numerics;
using Griff.Audio;
using Griff.Input;
using Griff.Scenes;
using Griff.Templates;
using static Griff.Templates.ProjectTemplates;

namespace Griff.Modules.Tactics;

// The Tactics template: a menu, and a battle on a field of grass, trees and rocks between your knights and archers and
// the computer's orcs and goblin archers, side by side in turns. All of it no-code components: Tactics Units on a
// tilemap, a Turn Order with the computer playing Red, Tactics Controls to pick and send units with the mouse, and
// Victory and Defeat panels that open on the turn order's Defeat Message.
internal sealed class TacticsTemplate : ProjectTemplate
{
    public override string Description => "Lead two knights and two archers against an orc warband on a field of trees and rocks, side by side in turns: click a unit to see how far it can walk and hit, send it, and attack. The computer plays the orcs. Click a unit, then a blue cell to walk there and a red foe to attack, right-click to take a walk back, Tab to end your turn.";

    public override int Order => 120;

    protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Heroic, seed: 15) with { Volume = 0.5f };

    protected override Scene BuildStartScene() => TacticsMenu();

    protected override Dictionary<string, Scene> BuildMoreScenes() => new() { ["Scenes/Battle.scene"] = TacticsBattle() };

    protected override Dictionary<string, Entity> BuildPrefabs() => TacticsPrefabs();

    public override IReadOnlyDictionary<string, byte[]> Images() => TacticsArt.Files();

    // Units and the controls that pick them make sounds too.
    protected internal override void WireSounds(Entity entity)
    {
        base.WireSounds(entity);
        if (entity.GetComponent<TacticsUnit>() is { } unit)
            TemplateSounds.Set(unit.AttackSound, s => unit.AttackSound = s, unit.Range > 1 ? TemplateSounds.Shoot : TemplateSounds.Swing);
        if (entity.GetComponent<TacticsControls>() is { } controls)
        {
            TemplateSounds.Set(controls.SelectSound, s => controls.SelectSound = s, TemplateSounds.Select);
            TemplateSounds.Set(controls.CancelSound, s => controls.CancelSound = s, TemplateSounds.Switch);
        }
    }

    private static readonly Color TacticsBlue = Color.FromBytes(0x4C, 0x8D, 0xF0);
    private static readonly Color TacticsRed = Color.FromBytes(0xD8, 0x4A, 0x40);
    private static readonly Color TacticsGray = Color.FromBytes(0x3A, 0x3F, 0x4B);
    private static readonly Color TacticsLight = Color.FromBytes(0xEC, 0xE8, 0xDC);
    private static readonly Color TacticsDim = Color.FromBytes(0x9A, 0xA4, 0x90);
    private static readonly Color TacticsNight = Color.FromBytes(0x12, 0x1C, 0x14);

    // The field: 0 grass to walk on, 1 trees and 2 rocks in the way. Blue starts on the left, Red on the right.
    private static readonly string[] TacticsField =
    [
        "0000110000002200",
        "0000100000000020",
        "0000000000110000",
        "0002200000100000",
        "0000000000000000",
        "0000000000000000",
        "0000010000022000",
        "0000110000000000",
        "0200000000010000",
        "0200000000011000",
    ];

    private static Scene TacticsMenu()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 6;
        view.Background = TacticsNight;
        view.Transition = SceneTransition.Fade;
        scene.Add(camera);

        scene.Add(Label("Title", new Vector2(0, 3.4f), "BORDER SKIRMISH", 1.3f, TacticsLight));
        scene.Add(Label("Subtitle", new Vector2(0, 2.3f), "Two knights and two archers against an orc warband.", 0.5f, TacticsDim));
        scene.Add(Label("Goal", new Vector2(0, 1.5f), "Beat every orc and goblin on the field.", 0.5f, TacticsDim));

        scene.Add(MenuButton("Play", new Vector2(0, -0.2f), Scenes.ButtonAction.LoadScene, "Battle", Key.Enter, TacticsBlue));
        scene.Add(MenuButton("Quit", new Vector2(0, -1.6f), Scenes.ButtonAction.Quit, null, Key.Escape, TacticsGray));

        var hint = Label("Hint", Vector2.Zero, "Enter to play  ·  Arrows to choose", 0.4f, TacticsDim);
        var anchor = hint.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Bottom;
        anchor.Offset = new Vector2(0, 0.6f);
        scene.Add(hint);
        return scene;
    }

    private static Scene TacticsBattle()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        camera.Transform.Position = new Vector3(0, -0.4f, 0);
        var view = camera.AddComponent<Camera>();
        view.Size = 6.6f;
        view.Background = TacticsNight;
        view.Transition = SceneTransition.Fade;
        scene.Add(camera);

        // Grass is walked on; trees and rocks are in the way. The middle of the field is the middle of the view.
        var field = new Entity("Field");
        field.Transform.Position = new Vector3(-TacticsField[0].Length / 2f, TacticsField.Length / 2f, 0);
        var map = field.AddComponent<Tilemap>();
        map.Image = TacticsArt.FieldTiles;
        map.Columns = TacticsArt.FieldColumns;
        map.BackgroundTiles = TacticsArt.GrassTile.ToString(System.Globalization.CultureInfo.InvariantCulture);
        map.Tiles = TacticsField;
        scene.Add(field);

        Vector3 Cell(int x, int y) => new(field.Transform.Position.X + x + 0.5f, field.Transform.Position.Y - y - 0.5f, 0);
        void Place(Entity unit, int x, int y)
        {
            unit.Transform.Position = Cell(x, y);
            scene.Add(unit);
        }

        // Yours: knights who walk far and hit hard up close, archers who shoot two or three cells off.
        Place(TacticsKnight("Knight"), 2, 4);
        Place(TacticsKnight("Knight 2"), 2, 5);
        Place(TacticsArcher("Archer"), 1, 3);
        Place(TacticsArcher("Archer 2"), 1, 6);

        // The computer's: orcs that charge, a goblin guarding the far side, and one holding the rocks.
        Place(TacticsOrc("Orc"), 13, 3);
        Place(TacticsOrc("Orc 2"), 13, 5);
        Place(TacticsOrc("Orc 3"), 13, 7);
        Place(TacticsGoblin("Goblin", TacticsStance.Guard), 15, 4);
        Place(TacticsGoblin("Goblin 2", TacticsStance.Hold), 15, 6);

        // Blue, then Red, which the computer plays for as long as it needs; whoever loses every unit is beaten.
        var turns = new Entity("Turns");
        var order = turns.AddComponent<TurnOrder>();
        (order.Teams, order.ComputerTeams, order.ComputerTurnTime, order.Between) = ("Blue, Red", "Red", 0, 0.6f);
        order.Format = "{0}'s turn  ·  Round {1}";
        var turnText = turns.AddComponent<Text>();
        (turnText.Value, turnText.Size, turnText.Align, turnText.Color, turnText.Order) = ("Blue's turn  ·  Round 1", 0.5f, TextAlign.Right, TacticsLight, 150);
        var corner = turns.AddComponent<ScreenAnchor>();
        (corner.Anchor, corner.Offset) = (Anchor.TopRight, new Vector2(-0.6f, -0.6f));
        scene.Add(turns);

        var size = new Vector2(3, 0.9f);
        var end = new Entity("End Turn");
        var endAnchor = end.AddComponent<ScreenAnchor>();
        (endAnchor.Anchor, endAnchor.Offset) = (Anchor.BottomRight, new Vector2(-1.9f, 1.3f));
        var endSprite = end.AddComponent<Sprite>();
        (endSprite.Size, endSprite.Tint, endSprite.Order) = (size, TacticsGray, 150);
        var endLabel = end.AddComponent<Text>();
        (endLabel.Value, endLabel.Size, endLabel.Order) = ("End Turn", 0.45f, 151);
        var button = end.AddComponent<Scenes.Button>();
        (button.Size, button.Action, button.Navigable, button.Fade) = (size, Scenes.ButtonAction.EndTurn, false, 0.3f);
        scene.Add(end, turns);

        var tactics = new Entity("Tactics");
        tactics.AddComponent<TacticsControls>();
        scene.Add(tactics);

        // "{EndTurn}" shows Tab on the keyboard and Back on a gamepad.
        var hint = Label("Hint", Vector2.Zero, "Click a unit, then a blue cell  ·  a red foe to attack  ·  the unit again to wait  ·  right-click to take back  ·  {EndTurn} to end the turn", 0.38f, TacticsDim);
        var bottom = hint.AddComponent<ScreenAnchor>();
        bottom.Anchor = Anchor.Bottom;
        bottom.Offset = new Vector2(0, 0.45f);
        scene.Add(hint);

        scene.Add(TacticsEndPanel("Victory", "RedDefeated", "Victory!", "The field is yours.", TacticsBlue));
        scene.Add(TacticsEndPanel("Defeat", "BlueDefeated", "Defeat", "Your company has fallen.", TacticsRed));
        AddPauseMenu(scene, menuScene: "Main");
        return scene;
    }

    private static Entity TacticsKnight(string name) =>
        TacticsFighter(name, TacticsArt.Knight, "Blue", hitPoints: 6, damage: 2, move: 4, range: 1, minRange: 1);

    private static Entity TacticsArcher(string name) =>
        TacticsFighter(name, TacticsArt.Archer, "Blue", hitPoints: 4, damage: 2, move: 3, range: 3, minRange: 2);

    private static Entity TacticsOrc(string name) =>
        TacticsFighter(name, TacticsArt.Orc, "Red", hitPoints: 5, damage: 2, move: 4, range: 1, minRange: 1);

    private static Entity TacticsGoblin(string name, TacticsStance stance)
    {
        var goblin = TacticsFighter(name, TacticsArt.Goblin, "Red", hitPoints: 3, damage: 1, move: 3, range: 3, minRange: 2);
        goblin.GetComponent<TacticsUnit>()!.Stance = stance;
        return goblin;
    }

    // A unit: steps across the field on its team's turn and fights what it can reach. Blue's are on the Player layer
    // (they cry out when hurt) and face right; Red's on the Enemy layer, facing left.
    private static Entity TacticsFighter(string name, string image, string side, float hitPoints, float damage, int move, int range, int minRange)
    {
        var blue = side == "Blue";
        var unit = new Entity(name);
        var sprite = unit.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order, sprite.FlipX) = (image, Vector2.One, 5, !blue);
        var collider = unit.AddComponent<BoxCollider>();
        (collider.Size, collider.Layer) = (new Vector2(0.8f), blue ? "Player" : "Enemy");
        unit.AddComponent<Body>().GravityScale = 0;
        unit.AddComponent<GridMovement>().StepTime = 0.12f;
        var health = unit.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime) = (hitPoints, hitPoints, 0);
        var fighter = unit.AddComponent<TacticsUnit>();
        (fighter.Move, fighter.Range, fighter.MinRange, fighter.Damage, fighter.HitEffect) = (move, range, minRange, damage, "Sparkle");
        var taker = unit.AddComponent<TurnTaker>();
        (taker.Team, taker.Actions) = (side, 1);
        return unit;
    }

    // Opens when a side is beaten (the turn order's Defeat Message, "RedDefeated" or "BlueDefeated"): again, or the menu.
    private static Entity TacticsEndPanel(string name, string message, string heading, string line, Color color)
    {
        var panel = new Entity(name);
        panel.Transform.Position = new Vector3(0, 0, 2);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var end = panel.AddComponent<Panel>();
        end.OpenMessage = message;
        end.CloseOnBack = false;
        var backdrop = panel.AddComponent<Sprite>();
        backdrop.Size = new Vector2(10, 5.6f);
        backdrop.Tint = new Color(0.06f, 0.08f, 0.06f, 0.94f);
        backdrop.Order = 400;

        var title = Label("Heading", new Vector2(0, 1.6f), heading, 1.3f, color);
        title.GetComponent<Text>()!.Order = 401;
        WithChild(panel, title);
        var text = Label("Line", new Vector2(0, 0.4f), line, 0.55f, TacticsLight);
        text.GetComponent<Text>()!.Order = 401;
        WithChild(panel, text);

        WithChild(panel, MenuButton("Again", new Vector2(-2.2f, -1.4f), Scenes.ButtonAction.RestartScene, null, Key.Enter, color, order: 410));
        WithChild(panel, MenuButton("Menu", new Vector2(2.2f, -1.4f), Scenes.ButtonAction.LoadScene, "Main", Key.Escape, TacticsGray, order: 410));
        return panel;
    }

    private static Dictionary<string, Entity> TacticsPrefabs() => new()
    {
        ["Prefabs/Sparkle.prefab"] = Sparkle(),
    };
}
