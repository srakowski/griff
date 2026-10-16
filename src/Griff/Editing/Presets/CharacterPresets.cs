using System.Numerics;
using Griff.Scenes;

namespace Griff.Editing.Presets;

/// <summary>What the player and enemy presets share.</summary>
internal static class CharacterParts
{
    /// <summary>A box sprite in the player's orange, with a collider on the Player layer.</summary>
    public static void AddPlayerShape(Entity entity, Vector2 size)
    {
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = Color.FromBytes(0xF0, 0x8C, 0x2E);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = size;
        collider.Layer = "Player";
    }
}

/// <summary>
/// Creates a platformer's player that runs and jumps with no code: a box sprite with a collider on the Player layer, a
/// <see cref="Body"/> and a <see cref="PlatformerMovement"/>. Its Inspector tunes how it moves.
/// </summary>
[Category("Characters")]
public sealed class PlatformerPlayerPreset : EntityPreset
{
    public override string Id => "Entity.CreatePlatformerPlayer";
    public override string Text => "Create _Platformer Player";
    public override int Order => 0;
    public override string? Icon => "Runner";
    public override string EntityName(Scene scene) => "Player";

    public override void Configure(Entity entity, Scene scene)
    {
        CharacterParts.AddPlayerShape(entity, new Vector2(0.8f, 1));
        entity.AddComponent<Body>();
        entity.AddComponent<PlatformerMovement>();
    }
}

/// <summary>Creates a top-down player that moves in eight directions with no code, ignoring gravity.</summary>
[Category("Characters")]
public sealed class TopDownPlayerPreset : EntityPreset
{
    public override string Id => "Entity.CreateTopDownPlayer";
    public override string Text => "Create Top-down Pla_yer";
    public override int Order => 1;
    public override string? Icon => "Walker";
    public override string EntityName(Scene scene) => "Player";

    public override void Configure(Entity entity, Scene scene)
    {
        CharacterParts.AddPlayerShape(entity, new Vector2(0.8f, 0.8f));
        entity.AddComponent<Body>().GravityScale = 0;
        entity.AddComponent<TopDownMovement>();
    }
}

/// <summary>
/// Creates a player that steps tile by tile on the scene's tilemap with no code, blocked by its walls: a box sprite with
/// a collider on the Player layer, a <see cref="Body"/> without gravity and a <see cref="GridMovement"/>.
/// </summary>
[Category("Characters")]
public sealed class GridPlayerPreset : EntityPreset
{
    public override string Id => "Entity.CreateGridPlayer";
    public override string Text => "Create _Grid Player";
    public override int Order => 2;
    public override string? Icon => "Person";
    public override string EntityName(Scene scene) => "Player";

    public override void Configure(Entity entity, Scene scene)
    {
        CharacterParts.AddPlayerShape(entity, new Vector2(0.8f, 0.8f));
        entity.AddComponent<Body>().GravityScale = 0;
        entity.AddComponent<GridMovement>();
        TurnOrderPreset.JoinTurns(scene, entity);
    }
}

/// <summary>
/// Creates an enemy that steps tile by tile after the player with no code, around walls: a box sprite with a collider on
/// the Enemy layer, a <see cref="Body"/> without gravity, a <see cref="GridMovement"/> a little slower than the player's,
/// a <see cref="GridChase"/>, and an <see cref="Attack"/> whose "Hitbox" child hurts the Player layer once it's next to
/// the player. Add a <see cref="GridPatrol"/> to have it walk a route until it spots the player.
/// </summary>
[Category("Characters")]
public sealed class GridEnemyPreset : EntityPreset
{
    public override string Id => "Entity.CreateGridEnemy";
    public override string Text => "Create Grid _Enemy";
    public override int Order => 3;
    public override string? Icon => "Robot";
    public override string EntityName(Scene scene) => "Enemy";

    public override void Configure(Entity entity, Scene scene)
    {
        var size = new Vector2(0.8f, 0.8f);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = Color.FromBytes(0x57, 0xAB, 0x5A);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = size;
        collider.Layer = "Enemy";
        entity.AddComponent<Body>().GravityScale = 0;
        entity.AddComponent<GridMovement>().StepTime = 0.3f;
        entity.AddComponent<GridChase>();

        // It swings at the player next to it (not diagonally), with a wind-up to see it coming.
        var attack = entity.AddComponent<Attack>();
        (attack.Action, attack.Target, attack.Range, attack.Windup, attack.Cooldown) = ("", "Player", 1.1f, 0.3f, 0.6f);
        TurnOrderPreset.JoinTurns(scene, entity);
    }

    public override IEnumerable<Entity> CreateChildren(Scene scene)
    {
        var hitbox = new Entity("Hitbox");
        hitbox.Transform.Position = new Vector3(0.9f, 0, 0);
        var box = hitbox.AddComponent<BoxCollider>();
        (box.Size, box.IsTrigger, box.Layer) = (new Vector2(0.8f, 0.6f), true, "Attack");
        hitbox.AddComponent<DamageOnTouch>().Layers = "Player";
        yield return hitbox;
    }
}

/// <summary>
/// Creates turn-based play: a <see cref="TurnOrder"/> whose text in the top-right corner says whose turn it is, with an
/// End Turn button in the bottom-right corner. Give whatever takes turns a <see cref="TurnTaker"/> naming its side. In a
/// scene with grid movers it sets up a roguelike instead, where everything moves when the player moves: each grid mover
/// gets a turn taker with one action (players on the Player side, chasers and patrols on the Enemy side), turns follow
/// straight on, the text counts turns and the button waits a turn. Grid players and enemies created later join in.
/// </summary>
[Category("Gameplay")]
public sealed class TurnOrderPreset : EntityPreset
{
    public override string Id => "Entity.CreateTurnOrder";
    public override string Text => "Create T_urn Order";
    public override int Order => 0;
    public override string? Icon => "Turns";
    public override string EntityName(Scene scene) => "Turns";

    public override void Configure(Entity entity, Scene scene)
    {
        var anchor = entity.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.TopRight, new Vector2(-0.6f, -0.7f));
        var text = entity.AddComponent<Griff.Scenes.Text>();
        (text.Value, text.Size, text.Align) = ("Player's turn", 0.6f, TextAlign.Right);
        var order = entity.AddComponent<TurnOrder>();
        if (GridMovers(scene).Any())
        {
            (order.Between, order.Format) = (0, "Turn {1}");
            text.Value = "Turn 1";
        }
    }

    public override IEnumerable<Entity> CreateChildren(Scene scene)
    {
        var size = new Vector2(3, 0.9f);
        var end = new Entity("End Turn");
        var anchor = end.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.BottomRight, new Vector2(-1.9f, 0.8f));
        var sprite = end.AddComponent<Sprite>();
        (sprite.Size, sprite.Tint) = (size, Color.FromBytes(0x3A, 0x3F, 0x4B));
        var label = end.AddComponent<Griff.Scenes.Text>();
        (label.Value, label.Size) = (GridMovers(scene).Any() ? "Wait" : "End Turn", 0.45f);
        var button = end.AddComponent<Griff.Scenes.Button>();
        (button.Size, button.Action, button.Navigable) = (size, ButtonAction.EndTurn, false);
        yield return end;
    }

    // The text and the button draw above what the turn order is put inside.
    public override void Arrange(Entity entity, IReadOnlyList<Entity> children, Entity? inside)
    {
        var above = Runtime.Views.OrderAbove(inside, 150);
        entity.GetComponent<Griff.Scenes.Text>()!.Order = above;
        var end = children[0];
        end.GetComponent<Sprite>()!.Order = above;
        end.GetComponent<Griff.Scenes.Text>()!.Order = above + 1;
    }

    // The grid movers that don't take turns yet join in, a step a turn.
    public override IEnumerable<IEdit> EditScene(Scene scene) =>
        GridMovers(scene).Where(e => TurnTaker.Of(e) is null).Select(e => new AddComponentEdit(e, GridTurnTaker(e, null))).ToList();

    /// <summary>
    /// Gives a grid player or enemy being created a turn taker with one action, if its scene already has a turn order, so
    /// it takes turns with everything else.
    /// </summary>
    internal static void JoinTurns(Scene? scene, Entity entity)
    {
        if (scene?.Entities.Select(e => e.GetComponent<TurnOrder>()).FirstOrDefault(o => o is not null) is { } order)
            entity.AddComponent(GridTurnTaker(entity, order));
    }

    // Grid players, enemies and other movers: not the crates they push, which don't take turns, nor those whose walk is
    // all one go (tactics units), which take theirs side by side.
    internal static IEnumerable<Entity> GridMovers(Scene scene) =>
        scene.Entities.Where(e => e.GetComponent<GridMovement>() is not null && e.GetComponent<Pushable>() is null &&
                                  !e.Components.OfType<IGridDriver>().Any(d => !d.StepTakesAction));

    // One action a turn, on the first side a person plays if it's a player, or the first the computer plays if it moves on
    // its own.
    private static TurnTaker GridTurnTaker(Entity entity, TurnOrder? order)
    {
        var driven = entity.GetComponent<GridChase>() is not null || entity.GetComponent<GridPatrol>() is not null;
        var sides = order?.TeamList ?? [];
        var side = driven
            ? sides.FirstOrDefault(s => order!.IsComputer(s)) ?? "Enemy"
            : sides.FirstOrDefault(s => !order!.IsComputer(s)) ?? "Player";
        return new TurnTaker { Team = side, Actions = 1 };
    }
}
