using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>Drag and drop with the mouse: things picked up and carried, and targets they're dropped on.</summary>
public class DragTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly List<string> _heard = [];

    public DragTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Listen(params string[] messages)
    {
        foreach (var message in messages)
            _game.On(message, _ => _heard.Add(message));
    }

    private Draggable Thing(string name, float x, float y, Action<Draggable>? setUp = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(x, y, 0);
        entity.AddComponent<Sprite>().Order = 1;
        var thing = entity.AddComponent<Draggable>();
        thing.SlideTime = 0;
        setUp?.Invoke(thing);
        _scene.Add(entity);
        return thing;
    }

    private DropTarget Slot(string name, float x, float y, Action<DropTarget>? setUp = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(x, y, 0);
        entity.AddComponent<Sprite>().Size = new Vector2(1.5f);
        var slot = entity.AddComponent<DropTarget>();
        setUp?.Invoke(slot);
        _scene.Add(entity);
        return slot;
    }

    // Presses at one point, moves the mouse to another over a few frames, and lets go there.
    private void Drag(Vector2 from, Vector2 to)
    {
        _input.MouseWorld = from;
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        for (var i = 1; i <= 4; i++)
        {
            _input.MouseWorld = Vector2.Lerp(from, to, i / 4f);
            Run(Frame);
        }

        _input.SetMouseButton(MouseButton.Left, false);
        Run(Frame);
    }

    private static Vector2 At(Component component) => new(component.Entity!.Transform.Position.X, component.Entity.Transform.Position.Y);

    [Fact]
    public void A_thing_dropped_on_a_target_lands_in_its_middle_and_is_on_it()
    {
        var card = Thing("Card", 0, 0);
        var slot = Slot("Slot", 5, 2);
        Listen("Picked", "Let Go", "Landed");
        card.PickUpMessage = "Picked";
        card.DropMessage = "Let Go";
        slot.DropMessage = "Landed";
        Entity? landedOn = null;
        card.Dropped += target => landedOn = target;
        Run(Frame);

        // Held a little off its middle, it keeps that grip while carried.
        Drag(new Vector2(0.3f, 0.2f), new Vector2(5.4f, 2.5f));

        Assert.Equal(new Vector2(5, 2), At(card));
        Assert.False(card.IsHeld);
        Assert.Equal(slot.Entity, card.Target);
        Assert.Equal([card.Entity!], slot.Contents);
        Assert.True(slot.IsFull);
        Assert.Equal(slot.Entity, landedOn);
        Assert.Equal(["Picked", "Let Go", "Landed"], _heard);
    }

    [Fact]
    public void While_held_it_follows_the_mouse_and_draws_on_top_of_everything()
    {
        var card = Thing("Card", 0, 0);
        var other = Thing("Other", 8, 0);
        other.Entity!.GetComponent<Sprite>()!.Order = 10;
        Run(Frame);

        _input.MouseWorld = new Vector2(0.2f, 0);
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        _input.MouseWorld = new Vector2(3.2f, 1);
        Run(Frame);

        Assert.True(card.IsHeld);
        Assert.Equal(card.Entity, _game.Held);
        Assert.Equal(new Vector2(3, 1), At(card));
        Assert.Equal(card.Entity, SceneRenderer.DrawOrder(_scene).Last());
    }

    [Fact]
    public void The_one_drawn_on_top_is_picked_up_when_several_are_under_the_mouse()
    {
        var under = Thing("Under", 0, 0);
        var over = Thing("Over", 0.2f, 0);
        over.Entity!.GetComponent<Sprite>()!.Order = 5;
        Run(Frame);

        Drag(new Vector2(0.1f, 0), new Vector2(4.1f, 0));

        Assert.Equal(new Vector2(4.2f, 0), At(over));
        Assert.Equal(Vector2.Zero, At(under));
    }

    [Fact]
    public void A_target_turns_away_other_kinds_and_more_than_it_holds_and_they_go_back()
    {
        var gem = Thing("Gem", 0, 0, t => t.Kind = "Blue Gem");
        var red = Slot("Red Hole", 5, 0, s => s.Accepts = "Red Gem");
        Run(Frame);
        Drag(Vector2.Zero, new Vector2(5, 0));
        Assert.Equal(Vector2.Zero, At(gem));
        Assert.Empty(red.Contents);

        red.Accepts = "red gem, blue gem";
        var second = Thing("Second", 0, 3, t => t.Kind = "Red Gem");
        Drag(Vector2.Zero, new Vector2(5, 0));
        Assert.Equal(new Vector2(5, 0), At(gem));
        Drag(new Vector2(0, 3), new Vector2(5, 0));
        Assert.Equal(new Vector2(0, 3), At(second));
        Assert.Equal([gem.Entity!], red.Contents);
    }

    [Fact]
    public void A_full_target_that_swaps_sends_what_was_on_it_where_the_dropped_thing_came_from()
    {
        var sword = Thing("Sword", 0, 0);
        var shield = Thing("Shield", 5, 0);
        var first = Slot("Slot 1", 0, 0, s => s.Swap = true);
        var second = Slot("Slot 2", 5, 0, s => s.Swap = true);
        Run(Frame);

        Drag(Vector2.Zero, new Vector2(5, 0));

        Assert.Equal(new Vector2(5, 0), At(sword));
        Assert.Equal(Vector2.Zero, At(shield));
        Assert.Equal([sword.Entity!], second.Contents);
        Assert.Equal([shield.Entity!], first.Contents);
    }

    [Fact]
    public void Off_a_target_it_stays_lands_on_the_grid_or_goes_back_if_it_only_goes_on_targets()
    {
        var free = Thing("Free", 0, 0);
        var tile = Thing("Tile", 0, 3, t => t.Grid = 1);
        var piece = Thing("Piece", 0, 6, t => t.OnlyOnTargets = true);
        Run(Frame);

        Drag(Vector2.Zero, new Vector2(2.3f, 0.4f));
        Drag(new Vector2(0, 3), new Vector2(2.3f, 3.4f));
        Drag(new Vector2(0, 6), new Vector2(2.3f, 6.4f));

        Assert.Equal(new Vector2(2.3f, 0.4f), At(free));
        Assert.Equal(new Vector2(2.5f, 3.5f), At(tile));
        Assert.Equal(new Vector2(0, 6), At(piece));
    }

    [Fact]
    public void It_slides_into_place_rather_than_jumping()
    {
        var card = Thing("Card", 0, 0, t => t.SlideTime = 0.2f);
        Slot("Slot", 5, 0);
        Run(Frame);

        Drag(Vector2.Zero, new Vector2(5.5f, 0.5f));
        var partway = At(card);
        Assert.NotEqual(new Vector2(5, 0), partway);
        Assert.True(Vector2.Distance(partway, new Vector2(5, 0)) < Vector2.Distance(new Vector2(5.5f, 0.5f), new Vector2(5, 0)));

        Run(0.3f);
        Assert.Equal(new Vector2(5, 0), At(card));
    }

    [Fact]
    public void Every_target_filled_sends_the_all_full_message_once_and_a_piece_that_stays_cant_be_lifted_again()
    {
        var a = Thing("A", 0, 0, t => (t.Kind, t.StaysOnTarget) = ("A", true));
        var b = Thing("B", 0, 3, t => (t.Kind, t.StaysOnTarget) = ("B", true));
        Slot("Hole A", 5, 0, s => (s.Accepts, s.AllFullMessage) = ("A", "Solved"));
        Slot("Hole B", 5, 3, s => (s.Accepts, s.AllFullMessage) = ("B", "Solved"));
        Listen("Solved");
        Run(Frame);

        Drag(Vector2.Zero, new Vector2(5, 0));
        Assert.Empty(_heard);
        Drag(new Vector2(0, 3), new Vector2(5, 3));
        Assert.Equal(["Solved"], _heard);

        Drag(new Vector2(5, 3), new Vector2(0, 3));
        Run(0.5f);
        Assert.Equal(new Vector2(5, 3), At(b));
        Assert.Equal(["Solved"], _heard);
    }

    [Fact]
    public void Things_placed_on_a_target_in_the_editor_are_on_it_from_the_start_without_a_message()
    {
        var card = Thing("Card", 5.2f, 0);
        var slot = Slot("Slot", 5, 0, s => (s.DropMessage, s.TakeMessage) = ("Landed", "Taken"));
        Listen("Landed", "Taken");
        Run(Frame);
        Assert.Equal([card.Entity!], slot.Contents);
        Assert.Empty(_heard);

        Drag(new Vector2(5.2f, 0), new Vector2(0, 0));
        Assert.Empty(slot.Contents);
        Assert.Equal(["Taken"], _heard);
    }

    [Fact]
    public void A_target_lights_up_while_something_it_takes_is_held_over_it()
    {
        Thing("Card", 0, 0, t => t.Kind = "Card");
        var slot = Slot("Slot", 5, 0, s => (s.Accepts, s.HoverTint) = ("Card", Color.FromBytes(255, 0, 0)));
        var sprite = slot.Entity!.GetComponent<Sprite>()!;
        Run(Frame);

        _input.MouseWorld = Vector2.Zero;
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        _input.MouseWorld = new Vector2(5, 0);
        Run(Frame);
        Assert.Equal(Color.FromBytes(255, 0, 0), sprite.Tint);

        _input.MouseWorld = new Vector2(2, 0);
        Run(Frame);
        Assert.Equal(Color.White, sprite.Tint);
    }

    [Fact]
    public void A_thing_with_a_body_hangs_from_the_mouse_and_falls_when_let_go()
    {
        var crate = Thing("Crate", 0, 0);
        crate.Entity!.AddComponent<BoxCollider>();
        crate.Entity.AddComponent<Body>();
        Run(Frame);

        _input.MouseWorld = Vector2.Zero;
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        var grabbed = crate.Entity.Transform.Position.Y;
        Run(0.5f);
        Assert.Equal(grabbed, crate.Entity.Transform.Position.Y, 3);

        _input.SetMouseButton(MouseButton.Left, false);
        Run(0.5f);
        Assert.True(crate.Entity.Transform.Position.Y < -0.5f);
    }

    [Fact]
    public void Draggables_and_drop_targets_are_saved_with_the_scene()
    {
        Thing("Card", 1, 2, t => (t.Kind, t.Grid, t.OnlyOnTargets, t.DropMessage) = ("Card", 0.5f, true, "Played"));
        Slot("Slot", 5, 0, s => (s.Accepts, s.Capacity, s.Swap, s.AllFullMessage) = ("Card", 3, true, "Done"));

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var loaded = serializer.LoadFromString(serializer.SaveToString(_scene));

        var card = loaded.FindNamed("Card")!.GetComponent<Draggable>()!;
        Assert.Equal(("Card", 0.5f, true, "Played"), (card.Kind, card.Grid, card.OnlyOnTargets, card.DropMessage));
        var slot = loaded.FindNamed("Slot")!.GetComponent<DropTarget>()!;
        Assert.Equal(("Card", 3, true, "Done"), (slot.Accepts, slot.Capacity, slot.Swap, slot.AllFullMessage));
    }
}
