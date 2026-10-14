using System.Numerics;
using Griff.Editing;
using Griff.Scenes;

namespace Griff.Modules.Tactics;

/// <summary>
/// Creates a tactics unit: a box sprite in its side's color with a collider, a <see cref="Body"/> without gravity, a
/// <see cref="GridMovement"/>, a <see cref="Health"/> of 3, a <see cref="TacticsUnit"/> and a <see cref="TurnTaker"/> with
/// one action, on whichever side has the fewest units so far, so creating a few makes two even teams facing each other.
/// A scene with no turn order gets one (Blue, played by you, and Red, played by the computer; clear its Computer Sides for
/// two people taking turns at one keyboard) with an End Turn button, and one with no <see cref="TacticsControls"/> gets
/// those, to pick units with the mouse. All in one undo step.
/// </summary>
public sealed class TacticsUnitPreset : EntityPreset
{
    // The colors of the first sides, in turn order; any further sides are gray.
    private static readonly Color[] SideColors =
    [
        Color.FromBytes(0x4C, 0x8D, 0xF0), Color.FromBytes(0xE0, 0x55, 0x4A), Color.FromBytes(0x57, 0xAB, 0x5A),
        Color.FromBytes(0xE8, 0xC5, 0x4A),
    ];

    public override string Id => "Entity.CreateTacticsUnit";

    public override string Text => "Create Tactics Unit";

    public override int Order => 16;

    public override string EntityName(Scene scene) => $"{Side(scene).Name} Unit";

    private static TurnOrder? OrderIn(Scene scene) => scene.Entities.Select(e => e.GetComponent<TurnOrder>()).FirstOrDefault(o => o is not null);

    // Beside another unit or the map, never inside them (making several in a row selects each in turn), so each stands
    // where it's put.
    public override Entity? InsideOf(Entity? selected)
    {
        var parent = selected;
        while (parent is not null && (parent.GetComponent<TacticsUnit>() is not null || parent.GetComponent<Tilemap>() is not null || parent.GetComponent<TurnOrder>() is not null))
            parent = parent.Parent;
        return parent;
    }

    // The side with the fewest units (the first, of those), where it comes in the turn order, and how many it has.
    private static (string Name, int Index, int Count) Side(Scene scene)
    {
        var sides = OrderIn(scene)?.SideList is { Count: > 0 } list ? list : ["Blue", "Red"];
        var units = scene.Entities.Where(e => e.GetComponent<TacticsUnit>() is not null).Select(e => e.GetComponent<TurnTaker>()?.Side).ToList();
        return sides.Select((side, i) => (side, i, units.Count(u => TurnOrder.SameSide(u, side)))).MinBy(s => s.Item3);
    }

    public override void Configure(Entity entity, Scene scene)
    {
        var (side, index, count) = Side(scene);
        var size = new Vector2(0.8f, 0.8f);
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Size, sprite.Tint) = (size, index < SideColors.Length ? SideColors[index] : Color.FromBytes(0x9A, 0x9A, 0x9A));
        entity.AddComponent<BoxCollider>().Size = size;
        entity.AddComponent<Body>().GravityScale = 0;
        entity.AddComponent<GridMovement>().StepTime = 0.12f;
        entity.AddComponent<Health>().InvincibleTime = 0;
        entity.AddComponent<TacticsUnit>();
        var taker = entity.AddComponent<TurnTaker>();
        (taker.Side, taker.Actions) = (side, 1);

        // The first side down the left, the second down the right, a cell apart.
        var x = index % 2 == 0 ? -2.5f : 2.5f;
        entity.Transform.Position = new Vector3(x, 1.5f - 2 * count, 0);
    }

    public override IEnumerable<IEdit> EditScene(Scene scene)
    {
        if (OrderIn(scene) is null)
        {
            var turns = new Entity("Turns");
            var anchor = turns.AddComponent<ScreenAnchor>();
            (anchor.Anchor, anchor.Offset) = (Anchor.TopRight, new Vector2(-0.6f, -0.7f));
            var text = turns.AddComponent<Text>();
            (text.Value, text.Size, text.Align, text.Order) = ("Blue's turn", 0.6f, TextAlign.Right, 150);
            var order = turns.AddComponent<TurnOrder>();
            // The computer plays Red, taking as long as its units need.
            (order.Sides, order.ComputerSides, order.ComputerTurnTime) = ("Blue, Red", "Red", 0);
            yield return new AddEntityEdit(scene, turns);

            var size = new Vector2(3, 0.9f);
            var end = new Entity("End Turn");
            var endAnchor = end.AddComponent<ScreenAnchor>();
            (endAnchor.Anchor, endAnchor.Offset) = (Anchor.BottomRight, new Vector2(-1.9f, 0.8f));
            var sprite = end.AddComponent<Sprite>();
            (sprite.Size, sprite.Tint, sprite.Order) = (size, Color.FromBytes(0x3A, 0x3F, 0x4B), 150);
            var label = end.AddComponent<Text>();
            (label.Value, label.Size, label.Order) = ("End Turn", 0.45f, 151);
            var button = end.AddComponent<Button>();
            (button.Size, button.Action, button.Navigable) = (size, ButtonAction.EndTurn, false);
            yield return new AddEntityEdit(scene, end, turns);
        }

        if (!scene.Entities.Any(e => e.GetComponent<TacticsControls>() is not null))
        {
            var tactics = new Entity("Tactics");
            tactics.AddComponent<TacticsControls>();
            yield return new AddEntityEdit(scene, tactics);
        }
    }
}
