using System.Numerics;
using Griff.Data;
using Griff.Input;
using Griff.Modules.Cards;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Networking;

namespace Griff.Tests.Runtime;

/// <summary>
/// Card games: decks shuffled with the game's random numbers (or made from a table), hands dealt and fanned out, cards
/// played by dragging them onto play zones (or clicking, or with the keys) that take only what their rules allow,
/// discarded and refilled, sliding between them; actions, outputs, turns, focus, rewinds.
/// </summary>
public sealed class CardTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public CardTests() => _game = new Game(_scene, _input) { PrefabLoader = name => new Entity(name) };

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private Deck AddDeck(string name = "Deck", int cards = 10, string kind = "Red", float x = -6)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(x, 1, 0);
        var deck = entity.AddComponent<Deck>();
        _scene.Add(entity);
        for (var i = 1; i <= cards; i++)
        {
            var card = new Entity($"{kind} {i}");
            var c = card.AddComponent<Card>();
            (c.Kind, c.Value) = (kind, i);
            _scene.Add(card, entity);
        }

        return deck;
    }

    private Deck AddPile(string name = "Discard Pile")
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(6, 1, 0);
        var pile = entity.AddComponent<Deck>();
        (pile.FaceUp, pile.TakesDrops, pile.ShuffleOnStart, pile.ClickToDraw) = (true, true, false, false);
        _scene.Add(entity);
        return pile;
    }

    private Hand AddHand(string name = "Hand", string deck = "Deck", int startWith = 5, string? side = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(0, -3.5f, 0);
        var hand = entity.AddComponent<Hand>();
        (hand.Deck, hand.StartWith) = (deck, startWith);
        if (side is not null)
        {
            var taker = entity.AddComponent<TurnTaker>();
            (taker.Team, taker.Actions) = (side, 1);
        }

        _scene.Add(entity);
        return hand;
    }

    private PlayZone AddZone(string name = "Table", float y = 1, string? accepts = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(0, y, 0);
        var zone = entity.AddComponent<PlayZone>();
        zone.Accepts = accepts;
        _scene.Add(entity);
        return zone;
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    private static List<string> Names(CardZone zone) => zone.Cards.Select(c => c.Entity!.Name).ToList();

    private void Drag(Vector2 from, Vector2 to)
    {
        _input.MouseWorld = from;
        Run(Frame);
        _input.SetMouseButton(MouseButton.Left, true);
        Run(Frame);
        for (var i = 1; i <= 6; i++)
        {
            _input.MouseWorld = Vector2.Lerp(from, to, i / 6f);
            Run(Frame);
        }

        _input.SetMouseButton(MouseButton.Left, false);
        Run(Frame);
    }

    private void Click(Vector2 at, MouseButton button = MouseButton.Left)
    {
        _input.MouseWorld = at;
        Run(Frame);
        _input.SetMouseButton(button, true);
        Run(Frame);
        _input.SetMouseButton(button, false);
        Run(Frame);
    }

    private void Press(Key key)
    {
        _input.SetKey(key, true);
        Run(Frame);
        _input.SetKey(key, false);
        Run(Frame);
    }

    [Fact]
    public void A_deck_is_shuffled_by_the_games_random_numbers_and_a_hand_is_dealt_one_card_after_another()
    {
        List<string> Deal(int seed)
        {
            var scene = new Scene();
            var deck = new Entity("Deck");
            deck.AddComponent<Deck>();
            scene.Add(deck);
            for (var i = 1; i <= 20; i++)
                scene.Add(new Entity($"Card {i}").With(e => e.AddComponent<Card>()), deck);
            var hand = new Entity("Hand");
            hand.AddComponent<Hand>().StartWith = 5;
            scene.Add(hand);
            Rand.Seed(seed);
            var game = new Game(scene, new InputState());
            for (var i = 0; i < 60; i++)
                game.Tick(Frame);
            return hand.GetComponent<Hand>()!.Cards.Select(c => c.Entity!.Name).ToList();
        }

        Assert.Equal(Deal(7), Deal(7));
        Assert.NotEqual(Deal(7), Deal(8));
        Assert.NotEqual(Enumerable.Range(1, 5).Select(i => $"Card {i}"), Deal(7));

        var deck = AddDeck();
        var hand = AddHand();
        Run(Frame);
        Assert.Equal(1, deck.Shuffles);
        Assert.True(hand.Cards.Count is >= 0 and <= 1, "the hand is dealt a card at a time");
        Run(0.25f);
        Assert.InRange(hand.Cards.Count, 2, 4);
        Run(1);
        Assert.Equal(5, hand.Count);
        Assert.Equal(5, deck.Count);
        Assert.All(hand.Cards, c => Assert.True(c.FaceUp));
        Assert.All(deck.Cards, c => Assert.False(c.FaceUp));
        Assert.All(hand.Cards, c => Assert.True(c.IsResting, $"{c.Entity!.Name} has slid into the hand"));
    }

    [Fact]
    public void A_hand_fans_its_cards_out_about_its_middle_and_squeezes_them_to_fit()
    {
        AddDeck(cards: 12);
        var hand = AddHand(startWith: 3);
        Run(2);

        var cards = hand.Cards;
        var (left, middle, right) = (cards[0].Entity!.Transform, cards[1].Entity!.Transform, cards[2].Entity!.Transform);
        Assert.Equal(new Vector3(0, 0, 0), middle.Position);
        Assert.Equal(-hand.Spacing, left.Position.X, 3);
        Assert.Equal(hand.Spacing, right.Position.X, 3);
        Assert.True(left.Position.Y < 0 && right.Position.Y < 0, "the ends dip, so the fan curves");
        Assert.Equal(hand.Fan, left.Rotation.Z, 3);
        Assert.Equal(-hand.Fan, right.Rotation.Z, 3);

        hand.Draw(9);
        Run(2);
        Assert.Equal(10, hand.Count);
        var xs = hand.Cards.Select(c => c.Entity!.Transform.Position.X).ToList();
        Assert.True(xs[^1] - xs[0] <= hand.MaxWidth - 1.6f + 0.01f, "more cards than fit squeeze together");
        Assert.Equal(10, hand.Limit);
        hand.Draw();
        Run(1);
        Assert.Equal(10, hand.Count);
    }

    [Fact]
    public void Dragging_a_card_onto_a_play_zone_plays_it_and_it_slides_into_place()
    {
        AddDeck();
        var hand = AddHand(startWith: 3);
        var zone = AddZone(accepts: "Red");
        var played = new List<string>();
        zone.PlayMessage = "Played";
        _game.On("Played", data => played.Add(((Entity)data!).Name));
        Run(2);
        var card = hand.Cards[1];

        Drag(At(card.Entity!), new Vector2(0.5f, 1.2f));
        Assert.Equal(zone, card.Zone);
        Assert.Equal(2, hand.Cards.Count);
        Assert.Equal(1, zone.Played);
        Assert.Equal([card.Entity!.Name], played);
        Run(1);
        Assert.True(card.IsResting);
        Assert.Equal(new Vector2(0, 1), At(card.Entity!));
        Assert.Equal(1, zone.Count);
        Assert.Equal(2, hand.Count);
    }

    [Fact]
    public void A_zone_turns_away_cards_its_rules_dont_allow_and_they_go_back_to_the_hand()
    {
        AddDeck(kind: "Blue");
        var hand = AddHand(startWith: 4);
        var zone = AddZone(accepts: "Red");
        Run(2);
        var card = hand.Cards[0];
        var home = At(card.Entity!);

        Drag(home, new Vector2(0, 1));
        Assert.Equal(hand, card.Zone);
        Run(1);
        Assert.Equal(home, At(card.Entity!));

        // Its kind, then room: a limit of one.
        zone.Accepts = "Blue";
        zone.Limit = 1;
        Drag(At(hand.Cards[0].Entity!), new Vector2(0, 1));
        Run(0.5f);
        Drag(At(hand.Cards[0].Entity!), new Vector2(0, 1));
        Run(0.5f);
        Assert.Equal(1, zone.Count);
        Assert.Equal(3, hand.Count);
        Assert.True(zone.IsFull);

        // Only from the hands it names.
        zone.Limit = 0;
        zone.From = "Other Hand";
        Drag(At(hand.Cards[0].Entity!), new Vector2(0, 1));
        Run(0.5f);
        Assert.Equal(1, zone.Count);
        zone.From = "Hand";
        Drag(At(hand.Cards[0].Entity!), new Vector2(0, 1));
        Run(0.5f);
        Assert.Equal(2, zone.Count);
    }

    [Fact]
    public void Clicking_a_card_and_then_a_zone_plays_it_as_a_tap_and_a_tap_does_on_a_touch_screen()
    {
        AddDeck();
        var hand = AddHand(startWith: 3);
        var zone = AddZone();
        Run(2);
        var card = hand.Cards[2];

        Click(At(card.Entity!));
        Assert.Equal(card, hand.Picked);
        Run(0.3f);
        Assert.True(card.Lift.Value > 0.9f, "a picked card is lifted out of the fan");
        Click(new Vector2(3, 1.5f));
        Assert.Equal(zone, card.Zone);
        Assert.Null(hand.Picked);

        // Clicking a picked card again, or right-clicking, puts it back.
        Click(At(hand.Cards[0].Entity!));
        Click(At(hand.Cards[0].Entity!) + new Vector2(0, 0.3f));
        Assert.Null(hand.Picked);
        Click(At(hand.Cards[0].Entity!));
        Click(new Vector2(0, -1), MouseButton.Right);
        Assert.Null(hand.Picked);
        Assert.Equal(2, hand.Count);
    }

    [Fact]
    public void The_keys_pick_a_card_choose_a_zone_and_play_it()
    {
        AddDeck();
        var hand = AddHand(startWith: 3);
        var left = AddZone("Left Zone", 1);
        left.Entity!.Transform.Position = new Vector3(-5, 1, 0);
        left.Size = new Vector2(4, 2.8f);
        var right = AddZone("Right Zone", 1);
        right.Entity!.Transform.Position = new Vector3(5, 1, 0);
        right.Size = new Vector2(4, 2.8f);
        Run(2);

        Press(Key.Right);
        Press(Key.Right);
        Press(Key.Space);
        var card = hand.Cards[2];
        Assert.Equal(card, hand.Picked);
        Press(Key.Right);
        Press(Key.Space);
        Assert.Equal(right, card.Zone);

        // The Cancel Action puts it back.
        Press(Key.Left);
        Press(Key.Space);
        Assert.NotNull(hand.Picked);
        Press(Key.LeftShift);
        Assert.Null(hand.Picked);
        Assert.Equal(2, hand.Count);

        // A gamepad: the d-pad and A.
        _input.SetGamepad([GamepadButton.DPadLeft], Vector2.Zero);
        Run(Frame);
        _input.SetGamepad([], Vector2.Zero);
        Run(Frame);
        _input.SetGamepad([GamepadButton.A], Vector2.Zero);
        Run(Frame);
        _input.SetGamepad([], Vector2.Zero);
        Run(Frame);
        Assert.NotNull(hand.Picked);
        _input.SetGamepad([GamepadButton.A], Vector2.Zero);
        Run(Frame);
        _input.SetGamepad([], Vector2.Zero);
        Run(Frame);
        Assert.Equal(1, hand.Count);
    }

    [Fact]
    public void Clicking_the_deck_draws_a_card_and_an_empty_deck_is_refilled_from_its_discard_pile()
    {
        var deck = AddDeck(cards: 4);
        var pile = AddPile();
        deck.RefillFrom = "Discard Pile";
        var hand = AddHand(startWith: 2);
        hand.DiscardPile = "Discard Pile";
        var empty = 0;
        deck.EmptyMessage = "Out";
        _game.On("Out", _ => empty++);
        Run(1);

        Click(At(deck.Entity!));
        Assert.Equal(3, hand.Count);
        Press(Key.E);
        Assert.Equal(4, hand.Count);
        Assert.Equal(0, deck.Count + CardZone.CardsIn(deck.Entity).Count);

        // Discarded: the hand goes onto the pile, face up, and the deck shuffles it back in to draw again.
        hand.Discard();
        Run(1);
        Assert.Equal(4, pile.Count);
        Assert.All(pile.Cards, c => Assert.True(c.FaceUp));
        var shuffles = deck.Shuffles;
        hand.Draw();
        Run(0.5f);
        Assert.Equal(1, hand.Count);
        Assert.Equal(3, deck.Count);
        Assert.Equal(0, pile.Count);
        Assert.Equal(shuffles + 1, deck.Shuffles);
        Assert.All(deck.Cards, c => Assert.False(c.FaceUp));

        // With nothing to refill from, it says it's empty.
        deck.RefillFrom = null;
        hand.Draw(4);
        Run(1);
        Assert.Equal(4, hand.Count);
        Assert.Equal(1, empty);
    }

    [Fact]
    public void A_card_dropped_on_a_discard_pile_is_discarded_and_the_pile_shows_it_on_top()
    {
        AddDeck();
        var pile = AddPile();
        var hand = AddHand(startWith: 2);
        Run(1.5f);
        var card = hand.Cards[0];
        var discarded = 0;
        card.Discarded += (_, _) => discarded++;

        Drag(At(card.Entity!), At(pile.Entity!));
        Assert.Equal(pile, card.Zone);
        Assert.Equal(1, discarded);
        Run(1);
        Assert.True(card.FaceUp);
        Assert.False(card.InPile, "the top card of a face-up pile draws itself");
        Assert.Equal(1, pile.PileCount);
    }

    [Fact]
    public void Its_actions_and_outputs_are_there_for_triggers_wires_and_counters()
    {
        Assert.Equal(["Draw", "Shuffle", "Restart"], ComponentActions.Of(typeof(Deck)).Select(a => a.Name));
        Assert.Equal(["Draw", "Discard", "GainMana", "Focus"], ComponentActions.Of(typeof(Hand)).Select(a => a.Name));
        Assert.Equal(["Discard"], ComponentActions.Of(typeof(PlayZone)).Select(a => a.Name));
        Assert.Contains("Count", ComponentOutputs.Of(typeof(Deck)).Select(o => o.Name));
        Assert.Contains("Count", ComponentOutputs.Of(typeof(Hand)).Select(o => o.Name));
        Assert.Equal(["Played", "Count"], ComponentOutputs.Of(typeof(PlayZone)).Select(o => o.Name).Order().Reverse());

        var deck = AddDeck();
        var pile = AddPile();
        var hand = AddHand(startWith: 0);
        var zone = AddZone();
        zone.DiscardPile = "Discard Pile";
        Run(Frame);
        _game.Do(hand.Entity!, "Draw", "3");
        Run(1);
        Assert.Equal(3, ComponentOutputs.Read(hand.Entity!, "Count"));
        Assert.Equal(7, ComponentOutputs.Read(deck.Entity!, "Count"));
        _game.Do(deck.Entity!, "Draw", "2");
        Run(1);
        Assert.Equal(5, hand.Count);

        hand.Play(hand.Cards[0], zone);
        hand.Play(hand.Cards[1], zone);
        Run(1);
        Assert.Equal(2, ComponentOutputs.Read(zone.Entity!, "Played"));
        _game.Do(zone.Entity!, "Discard");
        Run(1);
        Assert.Equal(0, zone.Count);
        Assert.Equal(2, zone.Played);
        Assert.Equal(2, pile.Count);

        var before = deck.Shuffles;
        _game.Do(deck.Entity!, "Shuffle");
        Run(Frame);
        Assert.Equal(before + 1, deck.Shuffles);

        // Restart gathers every card of the deck back in and deals again.
        _game.Do(deck.Entity!, "Restart");
        Run(Frame);
        Assert.Equal(10, deck.Count);
        Assert.Equal(0, pile.Count);
        Assert.Equal(0, hand.Count);
    }

    [Fact]
    public void A_deck_can_be_made_from_a_table_a_card_for_each_row()
    {
        var table = new Table("Cards",
            [new TableColumn("Title", ColumnType.Text), new TableColumn("Text", ColumnType.Text), new TableColumn("Value", ColumnType.WholeNumber),
             new TableColumn("Kind", ColumnType.Text), new TableColumn("Count", ColumnType.WholeNumber)],
            [
                ("Strike", [TableValue.Of("Strike"), TableValue.Of("Deal 3 damage."), TableValue.Of(1L), TableValue.Of("Attack"), TableValue.Of(3L)]),
                ("Guard", [TableValue.Of("Guard"), TableValue.Of("Block 4."), TableValue.Of(1L), TableValue.Of("Skill"), TableValue.Of(2L)]),
                ("Fireball", [TableValue.Of("Fireball"), TableValue.Of("Deal 8 damage to everything."), TableValue.Of(3L), TableValue.Of("Spell"), TableValue.Empty]),
            ]);
        _game.Tables = new TableSet([table]);
        var entity = new Entity("Deck");
        var deck = entity.AddComponent<Deck>();
        (deck.Table, deck.ShuffleOnStart) = ("Cards", false);
        _scene.Add(entity);
        Run(Frame);

        Assert.Equal(["Strike", "Strike", "Strike", "Guard", "Guard", "Fireball"], Names(deck));
        var fireball = deck.Top!;
        Assert.Equal(("Fireball", "Deal 8 damage to everything.", 3, "Spell", "Deck"), (fireball.Title, fireball.Text, fireball.Value, fireball.Kind, fireball.Home));
        Assert.False(fireball.FaceUp);
        Assert.Equal(6, deck.Count);

        // Made once: a rewind to after it doesn't make them again.
        Run(1);
        Assert.Equal(6, deck.Count);

        deck.Table = "Missing";
        deck.Started = false;
        var warnings = new List<string>();
        _game.Warning += (_, w) => warnings.Add(w);
        Run(Frame);
        Assert.Contains(warnings, w => w.Contains("Deck Deck", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hand_with_a_turn_taker_plays_on_its_sides_turn_using_an_action_and_the_computers_is_hidden()
    {
        var turns = new Entity("Turns");
        var order = turns.AddComponent<TurnOrder>();
        (order.Teams, order.ComputerTeams, order.Between) = ("Blue, Red", "Red", 0);
        _scene.Add(turns);
        AddDeck();
        AddDeck("Red Deck", kind: "Black", x: 6);
        var blue = AddHand(startWith: 3, side: "Blue");
        var red = AddHand("Red Hand", "Red Deck", 3, side: "Red");
        red.Hidden = blue.Hidden = true;
        red.Entity!.Transform.Position = new Vector3(0, 3.5f, 0);
        var zone = AddZone();
        Run(2);

        Assert.All(red.Cards, c => Assert.True(c.HiddenHere, "the computer's hand shows its backs"));
        Assert.All(blue.Cards, c => Assert.False(c.HiddenHere));
        Assert.True(blue.CanPlay);
        Drag(At(blue.Cards[0].Entity!), new Vector2(0, 1));
        Assert.Equal(1, zone.Count);
        Assert.Equal(1, blue.Entity!.GetComponent<TurnTaker>()!.ActionsUsed);
        Run(Frame);
        Assert.Equal("Red", order.Current);

        // Its one action used, its side's turn is over; on the computer's turn nobody plays its hand with the mouse.
        Drag(At(blue.Cards[0].Entity!), new Vector2(0, 1));
        Assert.Equal(1, zone.Count);
        Drag(At(red.Cards[0].Entity!), new Vector2(0, 1));
        Assert.Equal(1, zone.Count);
    }

    [Fact]
    public void Without_focus_a_hand_ignores_the_mouse_and_keys()
    {
        AddDeck();
        var hand = AddHand(startWith: 3);
        var zone = AddZone();
        Run(2);
        hand.Controllable = false;

        Drag(At(hand.Cards[1].Entity!), new Vector2(0, 1));
        Press(Key.Space);
        Press(Key.Space);
        Assert.Equal(0, zone.Count);

        _game.Do(hand.Entity!, "Focus");
        Run(Frame);
        Assert.True(hand.Controllable);
        Drag(At(hand.Cards[1].Entity!), new Vector2(0, 1));
        Assert.Equal(1, zone.Count);
    }

    private sealed class Cost(int mana) : Component, ICardPart
    {
        public static int Mana { get; set; }

        public List<string> Heard { get; } = [];

        public bool CanPlay(Card card, PlayZone zone) => Mana >= mana;

        public void Drawn(Card card, Hand hand) => Heard.Add("drawn");

        public void Played(Card card, PlayZone zone)
        {
            Mana -= mana;
            Heard.Add("played");
        }

        public void Discarded(Card card, Deck pile) => Heard.Add("discarded");
    }

    [Fact]
    public void Parts_of_a_card_hear_what_happens_to_it_and_can_keep_it_from_being_played()
    {
        var deck = AddDeck(cards: 1);
        var pile = AddPile();
        var cost = (Cost)deck.Cards[0].Entity!.AddComponent(new Cost(2));
        var hand = AddHand(startWith: 1);
        hand.DiscardPile = "Discard Pile";
        var zone = AddZone();
        Cost.Mana = 1;
        Run(1);
        Assert.Equal(["drawn"], cost.Heard);

        Drag(At(hand.Cards[0].Entity!), new Vector2(0, 1));
        Assert.Equal(0, zone.Count);
        Cost.Mana = 3;
        Drag(At(hand.Cards[0].Entity!), new Vector2(0, 1));
        Assert.Equal(1, zone.Count);
        Assert.Equal(1, Cost.Mana);
        zone.DiscardPile = "Discard Pile";
        zone.Discard();
        Run(Frame);
        Assert.Equal(["drawn", "played", "discarded"], cost.Heard);
        Assert.Equal(1, pile.Count);
    }

    // A card's price beside its mana is items, paid from its team's: here a bank of coins for the Blue team.
    [Fact]
    public void A_cards_price_is_paid_from_its_teams_items_and_one_it_cant_pay_isnt_played()
    {
        var deck = AddDeck(cards: 1);
        deck.Cards[0].Price = "2 Coin";
        var hand = AddHand(startWith: 1);
        hand.Entity!.AddComponent(new Team { Name = "Blue" });
        var zone = AddZone();
        var bank = new Entity("Bank");
        var coins = (Inventory)bank.AddComponent(new Inventory { ForTeam = "Blue", Items = ["Coin 1"] });
        _scene.Add(bank);
        Run(1);

        Drag(At(hand.Cards[0].Entity!), new Vector2(0, 1));
        Assert.Equal(0, zone.Count);
        Assert.True(hand.Cards[0].Unaffordable);
        coins.Add("Coin", 2);
        Run(Frame);
        Assert.False(hand.Cards[0].Unaffordable);
        Drag(At(hand.Cards[0].Entity!), new Vector2(0, 1));
        Assert.Equal(1, zone.Count);
        Assert.Equal(1, coins.Count("Coin"));
    }

    // Two players, Blue (the host) and Red, each with a hidden hand drawn from one deck, and a table between them.
    private static Scene CardTable()
    {
        var scene = new Scene();
        var settings = new Entity("Game");
        (settings.AddComponent<Multiplayer>().Player, settings.GetComponent<Multiplayer>()!.Teams) = ("", "Blue, Red");
        scene.Add(settings);
        var deck = new Entity("Deck");
        deck.Transform.Position = new Vector3(-6, 1, 0);
        deck.AddComponent<Deck>();
        scene.Add(deck);
        for (var i = 1; i <= 12; i++)
            scene.Add(new Entity($"Card {i}").With(e => e.AddComponent<Card>().Value = i), deck);
        foreach (var (side, y) in new[] { ("Blue", -3.5f), ("Red", 3.5f) })
        {
            var hand = new Entity(side + " Hand");
            hand.Transform.Position = new Vector3(0, y, 0);
            var h = hand.AddComponent<Hand>();
            (h.StartWith, h.Hidden) = (3, true);
            hand.AddComponent<TurnTaker>().Team = side;
            scene.Add(hand);
        }

        var table = new Entity("Table");
        table.Transform.Position = new Vector3(0, 0.5f, 0);
        table.AddComponent<PlayZone>();
        scene.Add(table);
        return scene;
    }

    [Fact]
    public void In_a_network_game_the_deck_and_the_other_sides_hand_arent_sent_and_a_player_plays_their_own_cards()
    {
        var world = new NetWorld(CardTable);
        world.Run(1.5);
        var (host, client) = (world.Host, world.Client);
        Assert.Equal(("Blue", "Red"), (host.Net.LocalPlayer!.Team, client.Net.LocalPlayer!.Team));
        Entity On(Game game, string name) => game.Scene.Entities.Single(e => e.Name == name);

        // The deck's cards stay on the host: the player's machine knows only how many there are.
        Assert.Equal(6, On(host, "Deck").GetComponent<Deck>()!.Count);
        Assert.Empty(CardZone.CardsIn(On(client, "Deck")));
        Assert.Equal(6, On(client, "Deck").GetComponent<Deck>()!.Count);
        Assert.Equal(6, On(client, "Deck").GetComponent<Deck>()!.PileCount);

        // Their own hand comes to them; the host's hidden hand is only a count, drawn as backs.
        var mine = On(client, "Red Hand").GetComponent<Hand>()!;
        var theirs = On(client, "Blue Hand").GetComponent<Hand>()!;
        Assert.Equal(3, mine.Cards.Count);
        Assert.All(mine.Cards, c => Assert.False(c.HiddenHere));
        Assert.Empty(theirs.Cards);
        Assert.Equal((3, 0), (theirs.Count, theirs.Shown));
        Assert.Equal(CardZone.CardsIn(On(host, "Red Hand")).Select(c => c.Entity!.Name), mine.Cards.Select(c => c.Entity!.Name));
        // On the host, the player's hand is there but face down.
        Assert.All(On(host, "Red Hand").GetComponent<Hand>()!.Cards, c => Assert.True(c.HiddenHere));

        // The player drags one of theirs onto the table: the host plays it, and everyone sees it there.
        var card = mine.Cards[1];
        var name = card.Entity!.Name;
        var keys = NetWorld.Keys(client);
        var from = At(card.Entity!);
        keys.MouseWorld = from;
        world.Run(NetWorld.Frame);
        keys.SetMouseButton(MouseButton.Left, true);
        world.Run(NetWorld.Frame);
        for (var i = 1; i <= 6; i++)
        {
            keys.MouseWorld = Vector2.Lerp(from, new Vector2(0, 0.5f), i / 6f);
            world.Run(NetWorld.Frame);
        }

        keys.SetMouseButton(MouseButton.Left, false);
        world.Run(1);
        Assert.Equal([name], CardZone.CardsIn(On(host, "Table")).Select(c => c.Entity!.Name));
        Assert.Equal([name], CardZone.CardsIn(On(client, "Table")).Select(c => c.Entity!.Name));
        Assert.Equal(2, CardZone.CardsIn(On(client, "Red Hand")).Count);
        Assert.Equal(1, On(host, "Table").GetComponent<PlayZone>()!.Played);
    }

    [Fact]
    public void Two_people_at_one_screen_each_see_their_hidden_hand_only_on_their_turn()
    {
        var turns = new Entity("Turns");
        var order = turns.AddComponent<TurnOrder>();
        (order.Teams, order.ComputerTeams, order.Between) = ("Blue, Red", "", 0);
        _scene.Add(turns);
        AddDeck(cards: 12);
        var blue = AddHand(startWith: 3, side: "Blue");
        var red = AddHand("Red Hand", "Deck", 3, side: "Red");
        red.Entity!.Transform.Position = new Vector3(0, 3.5f, 0);
        blue.Hidden = red.Hidden = true;
        Run(2);

        Assert.All(blue.Cards, c => Assert.False(c.HiddenHere));
        Assert.All(red.Cards, c => Assert.True(c.HiddenHere));
        order.EndTurn();
        Run(Frame * 2);
        Assert.Equal("Red", order.Current);
        Assert.All(blue.Cards, c => Assert.True(c.HiddenHere));
        Assert.All(red.Cards, c => Assert.False(c.HiddenHere));
    }

    [Fact]
    public void Create_Deck_makes_a_game_to_play_straight_away()
    {
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>();
        _scene.Add(camera);
        var preset = Griff.Editing.EntityPreset.All.Single(p => p.Id == "Entity.CreateDeck");
        var deck = new Entity(preset.EntityName(_scene));
        preset.Configure(deck, _scene);
        var children = preset.CreateChildren(_scene).ToList();
        var edits = preset.EditScene(_scene).ToList();
        _scene.Add(deck);
        foreach (var child in children)
            _scene.Add(child, deck);
        foreach (var edit in edits)
            edit.Apply();

        Assert.Equal(20, CardZone.CardsIn(deck).Count);
        Assert.Equal(["Main Camera", "Deck", "Discard Pile", "Play Zone", "Hand"], _scene.Roots.Select(e => e.Name));
        var hand = _scene.Roots.Single(e => e.Name == "Hand").GetComponent<Hand>()!;
        var pile = _scene.Roots.Single(e => e.Name == "Discard Pile").GetComponent<Deck>()!;
        Assert.Equal(("Deck", "Discard Pile"), (hand.Deck, hand.DiscardPile));
        Assert.Equal("Discard Pile", deck.GetComponent<Deck>()!.RefillFrom);
        Assert.True(pile.FaceUp && pile.TakesDrops);
        // A second deck is only a deck.
        Assert.Equal("Deck 2", preset.EntityName(_scene));
        Assert.Empty(preset.EditScene(_scene));

        Run(2);
        Assert.Equal(5, hand.Count);
        Drag(At(hand.Cards[2].Entity!), new Vector2(0, 1));
        Assert.Equal(1, _scene.Roots.Single(e => e.Name == "Play Zone").GetComponent<PlayZone>()!.Count);
    }

    [Fact]
    public void What_the_cards_are_doing_is_saved_with_the_scene_so_a_rewind_goes_back_to_it()
    {
        AddDeck();
        var hand = AddHand(startWith: 4);
        var zone = AddZone();
        Run(2);
        hand.Play(hand.Cards[0], zone);
        Run(1);
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var saved = serializer.SaveToString(_scene);
        var deckOrder = Names(_scene.Entities.Single(e => e.Name == "Deck").GetComponent<Deck>()!);
        var held = Names(hand);

        var copy = serializer.LoadFromString(saved);
        var game = new Game(copy, new InputState());
        game.Tick(Frame);
        var deck = copy.Entities.Single(e => e.Name == "Deck").GetComponent<Deck>()!;
        Assert.Equal(deckOrder, Names(deck));
        Assert.Equal(held, Names(copy.Entities.Single(e => e.Name == "Hand").GetComponent<Hand>()!));
        Assert.Equal(1, copy.Entities.Single(e => e.Name == "Table").GetComponent<PlayZone>()!.Played);
        Assert.True(deck.Started);
        Assert.Equal(1, deck.Shuffles);
        Assert.Equal(3, copy.Entities.Single(e => e.Name == "Hand").GetComponent<Hand>()!.Count);
    }
}

file static class EntityTestExtensions
{
    public static Entity With(this Entity entity, Action<Entity> set)
    {
        set(entity);
        return entity;
    }
}
