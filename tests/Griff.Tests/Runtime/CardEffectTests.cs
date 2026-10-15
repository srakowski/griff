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
/// Card effects with no code (damage, healing, draws, discards, mana costs, buffs, any action) done to the opponent, the
/// player, a chosen card or character, or everyone; cards from a table carrying them; and the computer playing its hidden
/// hand by a readable rule, showing each card before it plays it. Seeded, saved with the scene, and run by the host.
/// </summary>
public sealed class CardEffectTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public CardEffectTests() => _game = new Game(_scene, _input) { PrefabLoader = name => new Entity(name) };

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

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

    // A side's hero: a character with a Health on its side, at the left of the table.
    private Health AddHero(string name, string side, float y, float life = 20)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(-8, y, 0);
        var health = entity.AddComponent<Health>();
        health.Max = life;
        entity.AddComponent<TurnTaker>().Side = side;
        _scene.Add(entity);
        return health;
    }

    private Hand AddHand(string name, string side, float y, string deck = "Deck")
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(0, y, 0);
        var hand = entity.AddComponent<Hand>();
        (hand.Deck, hand.StartWith, hand.DiscardPile) = (deck, 0, "Discard Pile");
        entity.AddComponent<TurnTaker>().Side = side;
        _scene.Add(entity);
        return hand;
    }

    private PlayZone AddZone(string name, float y, string? from = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(0, y, 0);
        var zone = entity.AddComponent<PlayZone>();
        (zone.From, zone.DiscardPile, zone.Size) = (from, "Discard Pile", new Vector2(9, 2.4f));
        _scene.Add(entity);
        return zone;
    }

    private Deck AddDeck(string name = "Deck", int cards = 6)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(8, -1, 0);
        var deck = entity.AddComponent<Deck>();
        _scene.Add(entity);
        for (var i = 1; i <= cards; i++)
            _scene.Add(new Entity($"{name} {i}").With(e => e.AddComponent<Card>().Value = i), entity);
        var pile = new Entity("Discard Pile");
        pile.Transform.Position = new Vector3(8, 2, 0);
        var discard = pile.AddComponent<Deck>();
        (discard.FaceUp, discard.TakesDrops, discard.ShuffleOnStart, discard.ClickToDraw) = (true, true, false, false);
        _scene.Add(pile);
        return deck;
    }

    // A card in a hand or zone, costing `cost`, with one effect set up by `effect`.
    private Card AddCard(CardZone into, string name, int cost = 0, Action<CardEffect>? effect = null, float life = 0)
    {
        var entity = new Entity(name);
        var card = entity.AddComponent<Card>();
        card.Cost = cost;
        if (effect is not null)
            effect(entity.AddComponent<CardEffect>());
        if (life > 0)
        {
            var health = entity.AddComponent<Health>();
            (health.Max, health.DestroyOnDeath) = (life, false);
        }

        if (into is PlayZone row)
            card.PlayedBy = row.From;
        _scene.Add(entity, into.Entity);
        return card;
    }

    private TurnOrder AddTurns(string computer)
    {
        var entity = new Entity("Turns");
        var order = entity.AddComponent<TurnOrder>();
        (order.Sides, order.ComputerSides, order.Between, order.ComputerTurnTime) = ("Blue, Red", computer, 0, 0);
        _scene.Add(entity);
        return order;
    }

    [Fact]
    public void Played_cards_deal_damage_heal_draw_and_gain_mana_and_cost_mana_from_the_hand()
    {
        AddDeck();
        var blue = AddHero("Blue Hero", "Blue", -3);
        var red = AddHero("Red Hero", "Red", 3);
        var hand = AddHand("Blue Hand", "Blue", -3.5f);
        AddHand("Red Hand", "Red", 6.5f);
        var table = AddZone("Table", 0.5f);
        hand.MaxMana = 3;
        var bolt = AddCard(hand, "Bolt", cost: 2, effect: e => e.Damage = 3);
        var bolt2 = AddCard(hand, "Bolt 2", cost: 2, effect: e => e.Damage = 3);
        var mend = AddCard(hand, "Mend", effect: e => (e.Heal, e.Target) = (4, CardTarget.Self));
        var study = AddCard(hand, "Study", effect: e => e.Draw = 2);
        var coin = AddCard(hand, "Coin", effect: e => e.GainMana = 1);
        Run(0.5f);
        blue.Current = 10;
        Assert.Equal(3, hand.Mana);
        Assert.Equal("Deal 3 damage.", bolt.ShownText);
        Assert.Equal("Heal 4.", mend.ShownText);

        hand.Play(bolt, table);
        Run(Frame);
        Assert.Equal((17f, 1), (red.Current, hand.Mana));
        Assert.Equal("Blue plays Bolt: 3 damage to Red Hero.", hand.Said);
        Assert.Equal("Blue Hand", bolt.PlayedBy);

        // It costs more than the hand has left: it stays in the hand.
        hand.Play(bolt2, table);
        Run(Frame);
        Assert.Equal(hand, bolt2.Zone);
        Assert.True(bolt2.Unaffordable);

        // A coin pays for it, and the second bolt lands though the hero was hit a moment ago.
        hand.Play(coin, table);
        Run(Frame);
        hand.Play(bolt2, table);
        Run(Frame);
        Assert.Equal((14f, 0), (red.Current, hand.Mana));

        hand.Play(mend, table);
        hand.Play(study, table);
        Run(0.5f);
        Assert.Equal(14f, blue.Current);
        Assert.Equal(2, hand.Count);
    }

    [Fact]
    public void Effects_reach_all_foes_friends_or_everyone_buff_make_the_opponent_discard_and_do_any_action()
    {
        Rand.Seed(7);
        AddDeck();
        var blue = AddHero("Blue Hero", "Blue", -3);
        var red = AddHero("Red Hero", "Red", 3);
        var hand = AddHand("Blue Hand", "Blue", -3.5f);
        var redHand = AddHand("Red Hand", "Red", 6.5f);
        var table = AddZone("Table", 0.5f);
        var redRow = AddZone("Red Row", 3.5f, from: "Red Hand");
        var blueRow = AddZone("Blue Row", -1, from: "Blue Hand");
        var imp = AddCard(redRow, "Imp", life: 2);
        var ogre = AddCard(redRow, "Ogre", life: 5);
        var knight = AddCard(blueRow, "Knight", life: 3);
        knight.Value = 2;
        foreach (var name in new[] { "R1", "R2", "R3" })
            AddCard(redHand, name);
        var score = new Entity("Score").With(e => e.AddComponent<Counter>());
        _scene.Add(score);
        var quake = AddCard(hand, "Quake", effect: e => (e.Damage, e.Target) = (2, CardTarget.Everyone));
        var rally = AddCard(hand, "Rally", effect: e => (e.Buff, e.Target) = (2, CardTarget.AllFriends));
        var rot = AddCard(hand, "Mind Rot", effect: e => e.Discard = 1);
        var signal = AddCard(hand, "Signal", effect: e => (e.Do, e.DoOn, e.With) = ("Add", "Score", "5"));
        var storm = AddCard(hand, "Storm", effect: e => (e.Damage, e.Target) = (1, CardTarget.AllFoes));
        Run(0.5f);
        Assert.Equal("Deal 2 damage to everyone.", quake.ShownText);
        Assert.Equal("+2 Value to all friends.", rally.ShownText);
        Assert.Equal("Your opponent discards a card.", rot.ShownText);

        hand.Play(quake, table);
        Run(Frame * 2);
        Assert.Equal((18f, 18f), (blue.Current, red.Current));
        Assert.Equal((3f, 1f), (ogre.Entity!.GetComponent<Health>()!.Current, knight.Entity!.GetComponent<Health>()!.Current));
        // The imp died: it's on the discard pile, mended for next time.
        Assert.Equal("Discard Pile", imp.Zone?.Entity?.Name);
        Assert.Equal(2f, imp.Entity!.GetComponent<Health>()!.Current);

        hand.Play(rally, table);
        hand.Play(storm, table);
        Run(Frame);
        Assert.Equal(4, knight.Value);
        Assert.Equal((17f, 2f, 1f), (red.Current, ogre.Entity.GetComponent<Health>()!.Current, knight.Entity.GetComponent<Health>()!.Current));

        hand.Play(rot, table);
        Run(Frame);
        Assert.Equal(2, redHand.Count);
        Assert.StartsWith("Blue plays Mind Rot: Red discards 1", hand.Said, StringComparison.Ordinal);

        hand.Play(signal, table);
        Run(Frame);
        Assert.Equal(5, score.GetComponent<Counter>()!.Value);
    }

    [Fact]
    public void A_chosen_target_is_picked_with_the_mouse_or_the_keys_after_the_card_is_let_go()
    {
        AddDeck();
        AddHero("Blue Hero", "Blue", -3);
        var red = AddHero("Red Hero", "Red", 3);
        var hand = AddHand("Blue Hand", "Blue", -3.5f);
        AddHand("Red Hand", "Red", 7);
        var table = AddZone("Table", 0.5f);
        var redRow = AddZone("Red Row", 3.5f, from: "Red Hand");
        var ogre = AddCard(redRow, "Ogre", life: 5);
        var zap = AddCard(hand, "Zap", effect: e => (e.Damage, e.Target) = (2, CardTarget.Chosen));
        var zap2 = AddCard(hand, "Zap 2", effect: e => (e.Damage, e.Target) = (2, CardTarget.Chosen));
        var zap3 = AddCard(hand, "Zap 3", effect: e => (e.Damage, e.Target, e.ChooseFrom) = (2, CardTarget.Chosen, "Red Row"));
        Run(0.5f);
        Assert.Equal("Deal 2 damage to a target.", zap.ShownText);

        // Let go on the table, it waits to be aimed; a right click puts it back.
        Drag(At(zap.Entity!), new Vector2(0, 0.5f));
        Assert.Equal(hand, zap.Zone);
        Assert.NotNull(hand.Aim);
        Assert.Contains(hand.AimMarks, m => m.Center == At(ogre.Entity!));
        Click(new Vector2(2, -6), MouseButton.Right);
        Assert.Null(hand.Aim);
        Assert.Equal(hand, zap.Zone);

        // Let go and then click the ogre: played at it.
        Drag(At(zap.Entity!), new Vector2(0, 0.5f));
        Click(At(ogre.Entity!));
        Assert.Equal(table, zap.Zone);
        Assert.Equal(3f, ogre.Entity!.GetComponent<Health>()!.Current);
        Assert.Equal("Blue plays Zap on Ogre: 2 damage to Ogre.", hand.Said);

        // Let go straight on the red hero: played at them from the table.
        Run(0.5f);
        Drag(At(zap2.Entity!), At(red.Entity!));
        Assert.Equal(table, zap2.Zone);
        Assert.Equal(18f, red.Current);

        // With the keys: pick it, play it, and choose among what's in the Red Row only.
        Run(0.5f);
        Press(Key.Space);
        Press(Key.Space);
        Assert.Equal(zap3, hand.Picked);
        Press(Key.Space);
        Assert.NotNull(hand.Aim);
        Assert.Equal([At(ogre.Entity!)], hand.AimMarks.Select(m => m.Center));
        Press(Key.Space);
        Assert.Equal(table, zap3.Zone);
        Assert.Equal(1f, ogre.Entity.GetComponent<Health>()!.Current);
    }

    [Fact]
    public void Mana_refills_and_grows_each_turn_and_creatures_on_the_table_act_each_turn()
    {
        var order = AddTurns(computer: "");
        AddDeck();
        AddHero("Blue Hero", "Blue", -3);
        var red = AddHero("Red Hero", "Red", 3);
        var hand = AddHand("Blue Hand", "Blue", -3.5f);
        var redHand = AddHand("Red Hand", "Red", 7);
        var table = AddZone("Table", 0.5f);
        (hand.ManaGrowth, hand.ManaLimit) = (1, 2);
        var totem = AddCard(hand, "Totem", cost: 1, effect: e => (e.When, e.Damage) = (CardWhen.EachTurn, 1));
        AddCard(redHand, "Nothing");
        Run(0.2f);
        Assert.Equal((1, 1), (hand.Mana, hand.MaxMana));
        Assert.Equal("Each turn: deal 1 damage.", totem.ShownText);

        hand.Play(totem, table);
        Run(Frame);
        Assert.Equal((0, 20f), (hand.Mana, red.Current));
        void NextRound()
        {
            order.EndTurn();
            Run(Frame * 2);
            order.EndTurn();
            Run(Frame * 2);
        }

        NextRound();
        Assert.Equal("Blue", order.Current);
        Assert.Equal((2, 2), (hand.Mana, hand.MaxMana));
        Assert.Equal(19f, red.Current);
        NextRound();
        Assert.Equal((2, 2), (hand.Mana, hand.MaxMana));
        Assert.Equal(18f, red.Current);
    }

    // Blue against the computer's Red: each with a hero, a hand and mana; Red's hand hidden.
    private (TurnOrder Order, Health Blue, Health Red, Hand Hand) Duel()
    {
        var order = AddTurns(computer: "Red");
        AddDeck();
        var blue = AddHero("Blue Hero", "Blue", -3);
        var red = AddHero("Red Hero", "Red", 3);
        AddHand("Blue Hand", "Blue", -3.5f);
        var hand = AddHand("Red Hand", "Red", 6.5f);
        AddZone("Table", 0.5f);
        (hand.Hidden, hand.MaxMana, hand.ThinkTime) = (true, 3, 0.4f);
        AddCard(hand, "Fireball", cost: 3, effect: e => e.Damage = 4);
        AddCard(hand, "Spark", cost: 1, effect: e => e.Damage = 1);
        AddCard(hand, "Potion", effect: e => (e.Heal, e.Target) = (6, CardTarget.Self));
        return (order, blue, red, hand);
    }

    [Fact]
    public void The_computer_plays_its_best_damage_it_can_afford_shows_the_card_first_and_keeps_its_healing()
    {
        var (order, blue, _, hand) = Duel();
        Run(0.2f);
        Assert.Equal("Blue", order.Current);
        order.EndTurn();
        Run(Frame * 2);
        Assert.Equal("Red", order.Current);

        // It thinks, then shows the card it's going to play lifted out of its hand, then plays it.
        Run(0.25f);
        var fireball = hand.Cards.Single(c => c.Entity!.Name == "Fireball");
        Assert.True(fireball.Readied);
        Assert.Equal(20f, blue.Current);
        Run(0.25f);
        Assert.Equal(16f, blue.Current);
        Assert.Equal("Red plays Fireball: 4 damage to Blue Hero.", hand.Said);

        // No mana left for the spark, and its hero isn't hurt, so it keeps the potion and ends its turn.
        Run(1);
        Assert.Equal("Blue", order.Current);
        Assert.Equal(["Spark", "Potion"], hand.Cards.Select(c => c.Entity!.Name));
    }

    [Fact]
    public void The_computer_heals_when_low_and_finishes_the_opponent_off_when_it_can()
    {
        var (order, blue, red, hand) = Duel();
        Run(0.2f);
        red.Current = 5;
        order.EndTurn();
        Run(1.5f);
        // Hurt, it heals first, then spends its mana on the fireball.
        Assert.Equal(11f, red.Current);
        Assert.Equal(16f, blue.Current);
        Assert.Equal(["Spark"], hand.Cards.Select(c => c.Entity!.Name));
        Assert.Equal("Blue", order.Current);

        var (order2, blue2, red2, hand2) = (order, blue, red, hand);
        red2.Current = 2;
        blue2.Current = 1;
        AddCard(hand2, "Potion 2", effect: e => (e.Heal, e.Target) = (6, CardTarget.Self));
        order2.EndTurn();
        Run(0.6f);
        // The spark finishes Blue off before it thinks of healing.
        Assert.Equal(0f, blue2.Current);
        Assert.Contains("Spark", hand2.Said, StringComparison.Ordinal);
    }

    [Fact]
    public void The_computer_plays_the_same_way_every_time_in_a_seeded_game()
    {
        List<string> Play(int seed)
        {
            var test = new CardEffectTests();
            var (order, _, _, hand) = test.Duel();
            foreach (var effect in hand.Cards.SelectMany(c => c.Entity!.GetComponents<CardEffect>()))
                effect.DamageRoll = effect.Damage > 0 ? "1d6" : null;
            Rand.Seed(seed);
            var said = new List<string>();
            for (var round = 0; round < 3; round++)
            {
                test.Run(0.1f);
                order.EndTurn();
                test.Run(2);
                said.Add(hand.Said ?? "");
            }

            return said;
        }

        var first = Play(3);
        Assert.All(first, said => Assert.StartsWith("Red plays ", said, StringComparison.Ordinal));
        Assert.Equal(first, Play(3));
    }

    [Fact]
    public void Cards_from_a_table_carry_their_effects_and_costs_as_columns()
    {
        var table = new Table("Cards",
            [new TableColumn("Title", ColumnType.Text), new TableColumn("Cost", ColumnType.WholeNumber), new TableColumn("Damage", ColumnType.Dice),
             new TableColumn("Heal", ColumnType.Number), new TableColumn("Target", ColumnType.Text), new TableColumn("Count", ColumnType.WholeNumber)],
            [
                ("Strike", [TableValue.Of("Strike"), TableValue.Of(1L), TableValue.Of(Dice.Parse("1d4+1")), TableValue.Empty, TableValue.Of("Chosen"), TableValue.Of(2L)]),
                ("Salve", [TableValue.Of("Salve"), TableValue.Of(2L), TableValue.Empty, TableValue.Of(3.0), TableValue.Of("Self"), TableValue.Of(1L)]),
            ]);
        _game.Tables = new TableSet([table]);
        _game.PrefabLoader = name =>
        {
            var entity = new Entity(name);
            entity.AddComponent<Card>();
            entity.AddComponent<CardEffect>();
            return entity;
        };
        var entity = new Entity("Deck");
        var deck = entity.AddComponent<Deck>();
        (deck.Table, deck.Prefab, deck.ShuffleOnStart) = ("Cards", "Card", false);
        _scene.Add(entity);
        Run(Frame);

        var salve = deck.Top!;
        var effect = salve.Entity!.GetComponent<CardEffect>()!;
        Assert.Equal((2, 3f, CardTarget.Self), (salve.Cost, effect.Heal, effect.Target));
        Assert.Equal("Heal 3.", salve.ShownText);
        var strike = deck.Cards[0].Entity!.GetComponent<CardEffect>()!;
        Assert.Equal(("1d4+1", CardTarget.Chosen, 1), (strike.DamageRoll, strike.Target, deck.Cards[0].Cost));
        Assert.Equal("Deal 1d4+1 damage to a target.", deck.Cards[0].ShownText);
    }

    [Fact]
    public void What_effects_change_is_saved_with_the_scene_so_a_rewind_goes_back_to_it()
    {
        AddDeck();
        AddHero("Blue Hero", "Blue", -3);
        AddHero("Red Hero", "Red", 3);
        var hand = AddHand("Blue Hand", "Blue", -3.5f);
        AddHand("Red Hand", "Red", 7);
        var table = AddZone("Table", 0.5f);
        hand.MaxMana = 3;
        var bolt = AddCard(hand, "Bolt", cost: 3, effect: e => (e.Damage, e.DamageRoll, e.Target) = (3, "2d6", CardTarget.AllFoes));
        Run(0.2f);
        hand.Play(bolt, table);
        Run(Frame);
        Assert.Equal(0, hand.Mana);

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var copy = serializer.LoadFromString(serializer.SaveToString(_scene));
        var game = new Game(copy, new InputState());
        game.Tick(Frame);
        var restored = copy.Entities.Single(e => e.Name == "Blue Hand").GetComponent<Hand>()!;
        Assert.Equal((0, 3, hand.Said), (restored.Mana, restored.MaxMana, restored.Said));
        var card = copy.Entities.Single(e => e.Name == "Bolt");
        Assert.Equal(("Blue Hand", 3), (card.GetComponent<Card>()!.PlayedBy, card.GetComponent<Card>()!.Cost));
        Assert.Equal((CardTarget.AllFoes, "2d6"), (card.GetComponent<CardEffect>()!.Target, card.GetComponent<CardEffect>()!.DamageRoll));
        Assert.Equal(_scene.Entities.Single(e => e.Name == "Red Hero").GetComponent<Health>()!.Current,
            copy.Entities.Single(e => e.Name == "Red Hero").GetComponent<Health>()!.Current);
    }

    // Blue (the host) and Red, each with a hero and a hand; Red holds a card that's aimed, and there's a table between.
    private static Scene AimTable()
    {
        var scene = new Scene();
        var settings = new Entity("Game");
        (settings.AddComponent<Multiplayer>().Player, settings.GetComponent<Multiplayer>()!.Teams) = ("", "Blue, Red");
        scene.Add(settings);
        foreach (var (side, y) in new[] { ("Blue", -3.5f), ("Red", 3.5f) })
        {
            var hero = new Entity(side + " Hero");
            hero.Transform.Position = new Vector3(-8, y, 0);
            hero.AddComponent<Health>().Max = 20;
            hero.AddComponent<TurnTaker>().Side = side;
            scene.Add(hero);
            var hand = new Entity(side + " Hand");
            hand.Transform.Position = new Vector3(0, y, 0);
            hand.AddComponent<Hand>().StartWith = 0;
            hand.AddComponent<TurnTaker>().Side = side;
            scene.Add(hand);
        }

        var zap = new Entity("Zap");
        zap.AddComponent<Card>();
        var effect = zap.AddComponent<CardEffect>();
        (effect.Damage, effect.Target) = (3, CardTarget.Chosen);
        scene.Add(zap, scene.Entities.Single(e => e.Name == "Red Hand"));
        var table = new Entity("Table");
        table.Transform.Position = new Vector3(0, 0, 0);
        table.AddComponent<PlayZone>();
        scene.Add(table);
        return scene;
    }

    [Fact]
    public void In_a_network_game_a_player_aims_a_card_and_the_host_plays_it_at_what_they_chose()
    {
        var world = new NetWorld(AimTable);
        world.Run(1);
        var (host, client) = (world.Host, world.Client);
        Entity On(Game game, string name) => game.Scene.Entities.Single(e => e.Name == name);
        var keys = NetWorld.Keys(client);
        void Mouse(Vector2 to, bool? down = null)
        {
            keys.MouseWorld = to;
            if (down is { } press)
                keys.SetMouseButton(MouseButton.Left, press);
            world.Run(NetWorld.Frame);
        }

        var from = At(On(client, "Zap"));
        Mouse(from);
        Mouse(from, down: true);
        for (var i = 1; i <= 6; i++)
            Mouse(Vector2.Lerp(from, Vector2.Zero, i / 6f));
        Mouse(Vector2.Zero, down: false);
        world.Run(0.2);
        Assert.Equal("Red Hand", On(host, "Zap").Parent?.Name);

        var hero = At(On(client, "Blue Hero"));
        Mouse(hero);
        Mouse(hero, down: true);
        Mouse(hero, down: false);
        world.Run(0.5);
        Assert.Equal("Table", On(host, "Zap").Parent?.Name);
        Assert.Equal(17f, On(host, "Blue Hero").GetComponent<Health>()!.Current);
        Assert.Equal(17f, On(client, "Blue Hero").GetComponent<Health>()!.Current);
        Assert.Equal("Red plays Zap on Blue Hero: 3 damage to Blue Hero.", On(client, "Red Hand").GetComponent<Hand>()!.Said);
    }
}

file static class EffectTestExtensions
{
    public static Entity With(this Entity entity, Action<Entity> set)
    {
        set(entity);
        return entity;
    }
}
