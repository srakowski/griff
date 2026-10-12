using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Drag and drop with buttons: a grabber picks things up, carries them and puts them down.</summary>
public class GrabberTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;
    private readonly List<string> _heard = [];

    public GrabberTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    // Taps Interact (E), the grabber's action.
    private void Press()
    {
        _input.SetKey(Key.E, true);
        Run(Frame);
        _input.SetKey(Key.E, false);
        Run(Frame);
    }

    private Grabber Hand(float x, float y, Action<Grabber>? setUp = null)
    {
        var entity = new Entity("Cursor");
        entity.Transform.Position = new Vector3(x, y, 0);
        var grabber = entity.AddComponent<Grabber>();
        setUp?.Invoke(grabber);
        _scene.Add(entity);
        return grabber;
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

    private static void MoveTo(Component component, float x, float y) => component.Entity!.Transform.Position = new Vector3(x, y, 0);

    private static Vector2 At(Component component) => new(component.Entity!.Transform.Position.X, component.Entity.Transform.Position.Y);

    [Fact]
    public void A_press_picks_up_what_is_under_the_hand_and_carries_it_and_the_next_puts_it_on_a_target()
    {
        var hand = Hand(0.2f, 0.1f);
        var card = Thing("Card", 0, 0);
        var slot = Slot("Slot", 5, 2);
        Run(Frame);
        Assert.Equal(card.Entity, hand.InReach);

        Press();
        Assert.True(card.IsHeld);
        Assert.Equal(card.Entity, hand.Holding);
        Assert.Equal(new Vector2(0.2f, 0.1f), At(card), new VectorComparer());

        MoveTo(hand, 4.8f, 2.3f);
        Run(Frame);
        Assert.Equal(new Vector2(4.8f, 2.3f), At(card), new VectorComparer());

        Press();
        Assert.False(card.IsHeld);
        Assert.Null(hand.Holding);
        Assert.Equal(new Vector2(5, 2), At(card), new VectorComparer());
        Assert.Equal([card.Entity!], slot.Contents);
    }

    [Fact]
    public void It_reaches_for_the_nearest_thing_when_nothing_is_under_its_hand_and_no_further()
    {
        var hand = Hand(0, 0, g => g.Reach = 2);
        var far = Thing("Far", 1.8f, 0);
        var near = Thing("Near", 0, 1.4f);
        Thing("Too Far", -2.5f, 0);
        Run(Frame);
        Assert.Equal(near.Entity, hand.InReach);
        Press();
        Assert.True(near.IsHeld);
        Assert.False(far.IsHeld);

        var other = Hand(10, 10, g => g.Reach = 0);
        Thing("Beside", 11, 10);
        Run(Frame);
        Assert.Null(other.InReach);
    }

    [Fact]
    public void A_press_with_nothing_in_reach_or_nowhere_to_put_it_does_nothing()
    {
        var hand = Hand(0, 0);
        Press();
        Assert.Null(hand.Holding);

        var gem = Thing("Gem", 0, 0, d => d.Kind = "Gem");
        Slot("Cards Only", 5, 0, s => s.Accepts = "Card");
        Press();
        MoveTo(hand, 5, 0);
        Run(Frame);
        Press();
        Assert.True(gem.IsHeld, "a target for other kinds turns it away, and it stays in hand");
        Assert.Equal(gem.Entity, hand.Holding);

        gem.OnlyOnTargets = true;
        MoveTo(hand, -5, 0);
        Run(Frame);
        Press();
        Assert.True(gem.IsHeld, "something that only goes on targets isn't put down off them");
        Assert.Equal(new Vector2(-5, 0), At(gem), new VectorComparer());
    }

    [Fact]
    public void What_it_would_pick_up_lights_up_and_goes_back_once_it_is_out_of_reach_or_held()
    {
        var hand = Hand(0, 0, g => g.Highlight = Color.FromBytes(255, 0, 0));
        var card = Thing("Card", 0.5f, 0);
        var sprite = card.Entity!.GetComponent<Sprite>()!;
        sprite.Tint = Color.FromBytes(0, 0, 255);
        Run(Frame);
        Assert.Equal(Color.FromBytes(255, 0, 0), sprite.Tint);

        MoveTo(hand, 10, 0);
        Run(Frame);
        Assert.Equal(Color.FromBytes(0, 0, 255), sprite.Tint);

        MoveTo(hand, 0.5f, 0);
        Run(Frame);
        Press();
        Assert.True(card.IsHeld);
        Assert.Equal(Color.FromBytes(0, 0, 255), sprite.Tint);
    }

    [Fact]
    public void Carrying_only_while_held_puts_it_down_when_the_button_is_let_go()
    {
        var hand = Hand(0, 0, g => g.HoldToCarry = true);
        var card = Thing("Card", 0, 0);
        _input.SetKey(Key.E, true);
        Run(Frame * 3);
        Assert.True(card.IsHeld);
        MoveTo(hand, 3, 1);
        Run(Frame);
        _input.SetKey(Key.E, false);
        Run(Frame);
        Assert.False(card.IsHeld);
        Assert.Equal(new Vector2(3, 1), At(card), new VectorComparer());
    }

    [Fact]
    public void Putting_something_on_a_full_slot_that_swaps_picks_up_what_was_there()
    {
        var hand = Hand(0, 0);
        var sword = Thing("Sword", 0, 0);
        var shield = Thing("Shield", 5, 0);
        var slot = Slot("Slot", 5, 0, s => s.Swap = true);
        Run(Frame);
        Assert.Equal([shield.Entity!], slot.Contents);

        Press();
        MoveTo(hand, 5, 0);
        Run(Frame);
        Press();
        Assert.Equal(shield.Entity, hand.Holding);
        Assert.True(shield.IsHeld);
        Assert.Equal([sword.Entity!], slot.Contents);
    }

    [Fact]
    public void It_holds_things_at_its_hold_offset_turned_the_way_it_faces()
    {
        var hand = Hand(0, 0, g => (g.HoldOffset, g.Reach) = (new Vector2(1, 0.5f), 2));
        var sprite = hand.Entity!.AddComponent<Sprite>();
        var crate = Thing("Crate", 1, 0);
        Press();
        Assert.Equal(new Vector2(1, 0.5f), At(crate), new VectorComparer());

        sprite.FlipX = true;
        Run(Frame);
        Assert.Equal(new Vector2(-1, 0.5f), At(crate), new VectorComparer());
    }

    [Fact]
    public void What_a_moving_character_carries_keeps_up_with_it()
    {
        var hand = Hand(0, 0, g => g.HoldOffset = new Vector2(0, 1));
        hand.Entity!.AddComponent<Body>().GravityScale = 0;
        hand.Entity.AddComponent<BoxCollider>();
        hand.Entity.AddComponent<TopDownMovement>();
        var crate = Thing("Crate", 0, 1);
        crate.Entity!.AddComponent<BoxCollider>().IsTrigger = true;
        Press();
        Assert.True(crate.IsHeld);

        _input.SetKey(Key.Right, true);
        Run(0.5f);
        var at = hand.Entity.Transform.Position;
        Assert.True(at.X > 1, "it walked");
        Assert.Equal(new Vector2(at.X, at.Y + 1), At(crate), new VectorComparer());
    }

    [Fact]
    public void It_sends_messages_and_raises_events_and_scripts_can_press_it()
    {
        foreach (var message in new[] { "Grabbed", "Placed", "Lifted", "Landed" })
            _game.On(message, _ => _heard.Add(message));
        var hand = Hand(0, 0, g => (g.PickUpMessage, g.DropMessage) = ("Grabbed", "Placed"));
        var card = Thing("Card", 0, 0, d => (d.PickUpMessage, d.DropMessage) = ("Lifted", "Landed"));
        var slot = Slot("Slot", 3, 0);
        Entity? picked = null, dropped = null, on = null;
        hand.PickedUp += e => picked = e;
        hand.Dropped += (e, target) => (dropped, on) = (e, target);

        hand.Press();
        Run(Frame);
        Assert.Equal(card.Entity, picked);
        MoveTo(hand, 3, 0);
        hand.Press();
        Run(Frame);
        Assert.Equal(card.Entity, dropped);
        Assert.Equal(slot.Entity, on);
        Assert.Equal(["Lifted", "Grabbed", "Landed", "Placed"], _heard);
    }

    [Fact]
    public void A_grabber_that_goes_away_lets_go()
    {
        var hand = Hand(0, 0);
        var card = Thing("Card", 0, 0);
        Press();
        Assert.True(card.IsHeld);
        _game.Destroy(hand.Entity!);
        Run(Frame * 2);
        Assert.False(card.IsHeld);
    }

    [Fact]
    public void A_grabber_ignores_the_mouse_and_the_mouse_cannot_take_what_it_holds()
    {
        var hand = Hand(0, 0, g => g.Action = "Jump");
        var card = Thing("Card", 0, 0);
        _input.SetKey(Key.Space, true);
        Run(Frame);
        _input.SetKey(Key.Space, false);
        Assert.Equal(card.Entity, hand.Holding);

        _input.MouseWorld = Vector2.Zero;
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        Assert.Null(_game.Held);
        Assert.Equal(card.Entity, hand.Holding);
    }

    private sealed class VectorComparer : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 0.01f;

        public int GetHashCode(Vector2 v) => 0;
    }
}
