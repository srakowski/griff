using System.Numerics;
using Griff.Data;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Cards;

/// <summary>
/// Runs card games with no code: decks made (from a table, if they say so) and shuffled with the game's random numbers,
/// hands dealt and drawing, cards played onto play zones and discarded, and every card sliding to its place in whatever it's
/// in. The host does all of that; each machine reads its own player's mouse, keys and gamepad (CardsSystem.Input.cs) and
/// sends what they do to the host.
/// </summary>
internal sealed partial class CardsSystem : GameSystem
{
    // A player's machine (or this one) asks whoever runs the game to play a card or draw one.
    internal const string CardsCommand = "griff.cards";

    private const int PlayCommand = 0;
    private const int DrawCommand = 1;

    // Seconds between the cards a hand draws one after another.
    private const float DrawEvery = 0.1f;

    public override string Name => "Cards";

    private Scene Scene => Game.Scene;

    protected override void SceneChanged() => View = new CardsView();

    // After scripts and dragging.
    internal void Run()
    {
        var zones = Game.Showing<CardZone>();
        var cards = Game.Showing<Card>();
        if (zones.Count == 0 && cards.Count == 0)
        {
            View = View.Count == 0 ? View : new CardsView();
            return;
        }

        var decks = new List<Deck>();
        var hands = new List<Hand>();
        var tables = new List<PlayZone>();
        foreach (var zone in zones)
        {
            switch (zone)
            {
                case Deck deck: decks.Add(deck); break;
                case Hand hand: hands.Add(hand); break;
                case PlayZone table: tables.Add(table); break;
            }
        }

        // Where the game is run: decks made and shuffled, hands dealt, then what's been asked for. What a paused group holds
        // (see PauseGroup) keeps its requests for when it goes on.
        foreach (var deck in decks)
        {
            if (Game.Runs(deck.Entity!) && !deck.Entity!.IsPaused && !deck.Started)
                Start(deck);
        }

        foreach (var hand in hands)
        {
            if (Game.Runs(hand.Entity!) && !hand.Entity!.IsPaused && !hand.Dealt)
            {
                (hand.Dealt, hand.ToDraw, hand.NextDraw) = (true, hand.ToDraw + Math.Max(0, hand.StartWith), 0);
                foreach (var card in CardZone.CardsIn(hand.Entity))
                    card.FaceUp = true;
            }
        }

        foreach (var deck in decks)
        {
            if (Game.Runs(deck.Entity!) && !deck.Entity!.IsPaused)
                RunRequests(deck, decks, hands);
        }

        foreach (var hand in hands)
        {
            if (Game.Runs(hand.Entity!) && !hand.Entity!.IsPaused)
            {
                RunRequests(hand, decks);
                if (hand.TurnBegan)
                    EachTurn(hand, tables);
            }
        }

        foreach (var hand in hands)
        {
            if (Game.Runs(hand.Entity!) && !hand.Entity!.IsPaused)
                RunComputer(hand, decks, tables);
        }

        foreach (var table in tables)
        {
            if (Game.Runs(table.Entity!) && !table.Entity!.IsPaused && table.DiscardRequested)
            {
                table.DiscardRequested = false;
                if (PileOf(table.DiscardPile, decks) is { } pile)
                {
                    foreach (var card in CardZone.CardsIn(table.Entity))
                        Discard(card, pile, HandNamed(card.PlayedBy));
                }
            }
        }

        BuryTheDead(tables, decks);

        foreach (var zone in zones)
            LayOut(zone);

        Present(zones, decks, hands, tables, cards);
    }

    // ----- Starting -----

    // Its cards made from its table, each given its home, turned as it shows them, and shuffled.
    private void Start(Deck deck)
    {
        deck.Started = true;
        if (!string.IsNullOrWhiteSpace(deck.Table))
            Build(deck);
        var name = deck.Entity!.Name;
        foreach (var card in CardZone.CardsIn(deck.Entity))
        {
            if (string.IsNullOrWhiteSpace(card.Home))
                card.Home = name;
            card.FaceUp = deck.FaceUp;
        }

        if (deck.ShuffleOnStart)
            Shuffle(deck);
    }

    // A card for each row of its table (Count Column copies), each set from its row by a Table Values.
    private void Build(Deck deck)
    {
        var where = $"Deck {deck.Entity!.Name}";
        Table? table;
        try
        {
            table = Tables.Find(deck.Table);
        }
        catch (InvalidDataException ex)
        {
            Game.Warn($"{where}: {ex.Message}");
            return;
        }

        if (table is null)
        {
            Game.Warn($"{where}: {Tables.NoTable(deck.Table)}");
            return;
        }

        var countColumn = string.IsNullOrWhiteSpace(deck.CountColumn) ? -1 : table.ColumnIndex(deck.CountColumn);
        var index = CardZone.CardsIn(deck.Entity).Count;
        foreach (var row in table.Rows)
        {
            var copies = countColumn >= 0 && !row[countColumn].IsEmpty ? (int)Math.Clamp(row[countColumn].AsWholeNumber, 0, 999) : 1;
            for (var i = 0; i < copies; i++)
            {
                if (MakeCard(deck, row.Name, where) is not { } entity)
                    return;
                var values = entity.GetComponent<TableValues>() ?? entity.AddComponent<TableValues>();
                (values.Table, values.Row) = (deck.Table!.Trim(), row.Name);
                var card = entity.GetComponent<Card>()!;
                card.Home = deck.Entity.Name;
                var at = deck.PileOffset(index++);
                entity.Transform.Position = new Vector3(at, entity.Transform.Position.Z);
                Game.Spawn(entity, deck.Entity);
                card.Resting = true;
            }
        }
    }

    // A copy of the deck's card prefab (or a plain card) named after a row, or null (said in Output) if the prefab won't load.
    private Entity? MakeCard(Deck deck, string name, string where)
    {
        Entity entity;
        if (string.IsNullOrWhiteSpace(deck.Prefab))
        {
            entity = new Entity(name);
        }
        else
        {
            try
            {
                entity = Game.PrefabLoader?.Invoke(deck.Prefab.Trim()) ?? throw new InvalidOperationException("this game can't load prefabs");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException or KeyNotFoundException or ArgumentException)
            {
                Game.Warn($"{where}: its Prefab \"{deck.Prefab.Trim()}\" won't load ({ex.Message}).");
                return null;
            }

            entity.Name = name;
        }

        if (entity.GetComponent<Card>() is null)
            entity.AddComponent<Card>();
        return entity;
    }

    // ----- What's asked for -----

    private void RunRequests(Deck deck, List<Deck> decks, List<Hand> hands)
    {
        if (deck.RestartRequested)
        {
            deck.RestartRequested = false;
            Restart(deck, decks, hands);
        }

        if (deck.ShuffleRequested)
        {
            deck.ShuffleRequested = false;
            Shuffle(deck);
        }

        if (deck.DrawRequests > 0)
        {
            var count = deck.DrawRequests;
            deck.DrawRequests = 0;
            if (HandFor(deck, decks, hands) is { } hand)
                hand.ToDraw += count;
        }
    }

    private void RunRequests(Hand hand, List<Deck> decks)
    {
        if (hand.Requests.Count > 0)
        {
            var requests = hand.Requests.ToList();
            hand.Requests.Clear();
            foreach (var (card, zone, target) in requests)
            {
                if (card is null)
                {
                    if (PileOf(hand.DiscardPile, decks) is { } pile)
                    {
                        foreach (var held in CardZone.CardsIn(hand.Entity))
                            Discard(held, pile, hand);
                    }
                }
                else if (card.Zone == hand && card.Entity!.IsActive)
                {
                    if (zone is null)
                    {
                        if (PileOf(hand.DiscardPile, decks) is { } pile)
                            Discard(card, pile, hand);
                    }
                    else
                    {
                        Play(card, hand, zone, null, target);
                    }
                }
            }
        }

        // Cards still to draw, one after another.
        if (hand.ToDraw <= 0)
            return;
        hand.NextDraw -= Time.Delta;
        if (hand.NextDraw > 0)
            return;
        hand.NextDraw = DrawEvery;
        hand.ToDraw--;
        if (!DrawOne(hand, decks))
            hand.ToDraw = 0;
    }

    // Draws its deck's top card into the hand, refilling the deck from its discard pile if it has run out. False if there
    // was no card to draw or no room for it.
    private bool DrawOne(Hand hand, List<Deck> decks)
    {
        if (DeckOf(hand, decks) is not { } deck)
            return false;
        if (hand.Limit > 0 && CardZone.CardsIn(hand.Entity).Count >= hand.Limit)
            return false;
        var cards = CardZone.CardsIn(deck.Entity);
        if (cards.Count == 0)
        {
            Refill(deck, decks);
            cards = CardZone.CardsIn(deck.Entity);
        }

        if (cards.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(deck.EmptyMessage))
                Game.Send(deck.EmptyMessage.Trim(), deck.Entity, from: deck.Entity);
            return false;
        }

        var card = cards[^1];
        MoveTo(card, hand);
        Game.PlayIfSet(deck.DrawSound);
        if (!string.IsNullOrWhiteSpace(hand.DrawMessage))
            Game.Send(hand.DrawMessage.Trim(), card.Entity, from: hand.Entity);
        card.RaiseDrawn(hand);
        hand.RaiseDrew(card);
        foreach (var part in PartsOf(card))
            part.Drawn(card, hand);
        RunEffects(card, hand, CardWhen.Drawn, null);
        return true;
    }

    // Its discard pile's cards shuffled back in, face down.
    private void Refill(Deck deck, List<Deck> decks)
    {
        if (string.IsNullOrWhiteSpace(deck.RefillFrom) || Named(deck.RefillFrom, decks) is not { } pile || pile == deck)
            return;
        var cards = CardZone.CardsIn(pile.Entity);
        if (cards.Count == 0)
            return;
        foreach (var card in cards)
            MoveTo(card, deck);
        Shuffle(deck);
    }

    // The cards in it put in an order picked by the game's random numbers (Rand), so a seeded game shuffles the same.
    private void Shuffle(Deck deck)
    {
        var cards = CardZone.CardsIn(deck.Entity);
        for (var i = cards.Count - 1; i > 0; i--)
        {
            var j = Rand.Range(0, i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }

        foreach (var card in cards)
            Scene.Move(card.Entity!, deck.Entity);
        deck.Shuffles++;
        Game.PlayIfSet(deck.ShuffleSound);
        if (!string.IsNullOrWhiteSpace(deck.ShuffleMessage))
            Game.Send(deck.ShuffleMessage.Trim(), deck.Entity, from: deck.Entity);
        deck.RaiseShuffled();
    }

    // Every card that belongs to it back in, shuffled, and the hands that draw from it dealt again.
    private void Restart(Deck deck, List<Deck> decks, List<Hand> hands)
    {
        var name = deck.Entity!.Name;
        foreach (var card in Scene.All<Card>())
        {
            if (card.Entity!.Scene == Scene && string.Equals(card.Home?.Trim(), name, StringComparison.Ordinal) && card.Zone != deck)
                MoveTo(card, deck);
        }

        Shuffle(deck);
        foreach (var hand in hands)
        {
            if (DeckOf(hand, decks) == deck)
                (hand.Dealt, hand.ToDraw, hand.Picked) = (false, 0, null);
        }
    }

    // ----- Playing -----

    // Plays a card from a hand onto a play zone (or drops it on a deck that takes drops), from where it was let go if
    // that's known, at the target chosen for it if it chooses one (the computer's pick without). False if the zone won't
    // take it.
    private bool Play(Card card, Hand hand, CardZone zone, Vector2? at, Entity? target)
    {
        var chooses = zone is PlayZone && Chooses(card);
        if (chooses && target is null)
            target = ChooseTarget(card, hand);
        if (!CanGo(card, hand, zone) || (chooses && (target is null || !MayChoose(card, target))))
        {
            if (zone is PlayZone refused)
                Game.PlayIfSet(refused.RefuseSound);
            return false;
        }

        if (!chooses)
            target = null;

        // On from where it was let go, upright.
        if (at is { } point && card.Entity!.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var toLocal))
        {
            var local = Vector2.Transform(point, toLocal);
            var turned = MathF.Atan2(parent.Transform.WorldMatrix.M12, parent.Transform.WorldMatrix.M11) * 180 / MathF.PI;
            card.Entity.Transform.Position = new Vector3(local, card.Entity.Transform.Position.Z);
            card.Entity.Transform.Rotation = new Vector3(0, 0, -turned);
        }

        MoveTo(card, zone);
        ResetLook(card);
        if (zone is Deck pile)
        {
            Discarded(card, pile, hand);
            return true;
        }

        var table = (PlayZone)zone;
        table.Played++;
        hand.Mana = Math.Max(0, hand.Mana - Math.Max(0, card.Cost));
        card.PlayedBy = hand.Entity!.Name;
        Game.UseAction(hand.Entity!);
        Game.PlayIfSet(table.PlaySound);
        if (!string.IsNullOrWhiteSpace(table.PlayMessage))
            Game.Send(table.PlayMessage.Trim(), card.Entity, from: table.Entity);
        card.RaisePlayed(table);
        table.RaisePlayed(card);
        foreach (var part in PartsOf(card))
            part.Played(card, table);
        var did = RunEffects(card, hand, CardWhen.Played, target);
        var on = target is null ? "" : $" on {Called(target)}";
        hand.Said = did.Length == 0 ? $"{Called(hand)} plays {card.ShownTitle}{on}." : $"{Called(hand)} plays {card.ShownTitle}{on}: {did}.";
        return true;
    }

    // Its side's turn has begun: the cards played from it that are on the table with Each Turn effects go off.
    private void EachTurn(Hand hand, List<PlayZone> tables)
    {
        hand.TurnBegan = false;
        var name = hand.Entity!.Name;
        foreach (var table in tables)
        {
            foreach (var card in CardZone.CardsIn(table.Entity))
            {
                if (string.Equals(card.PlayedBy?.Trim(), name, StringComparison.Ordinal) && card.Entity!.IsActive)
                    RunEffects(card, hand, CardWhen.EachTurn, null);
            }
        }
    }

    // Whether a card from a hand can go onto a zone: a play zone that takes its kind, has room, takes from that hand, whose
    // cost the hand can pay, with something to aim at if it chooses a target, and whose card parts all say it can be
    // played; or a deck that takes drops.
    private bool CanGo(Card card, Hand hand, CardZone zone)
    {
        if (!zone.Entity!.IsActive || zone.Entity.IsPaused || card.Zone == zone)
            return false;
        if (zone is Deck deck)
            return deck.TakesDrops;
        if (zone is not PlayZone table || !table.AcceptsKind(card.Kind) || !table.TakesFrom(hand))
            return false;
        if (table.Limit > 0 && CardZone.CardsIn(table.Entity).Count >= table.Limit)
            return false;
        if (card.Cost > 0 && hand.Mana < card.Cost)
            return false;
        if (Chooses(card) && Candidates(card).Count == 0)
            return false;
        foreach (var part in PartsOf(card))
        {
            if (!part.CanPlay(card, table))
                return false;
        }

        return true;
    }

    // Onto a discard pile, from `hand` (the one it was in, or played from) if known.
    private void Discard(Card card, Deck pile, Hand? hand)
    {
        MoveTo(card, pile);
        ResetLook(card);
        Discarded(card, pile, hand);
    }

    private void Discarded(Card card, Deck pile, Hand? hand)
    {
        card.RaiseDiscarded(pile);
        foreach (var part in PartsOf(card))
            part.Discarded(card, pile);
        RunEffects(card, hand, CardWhen.Discarded, null);
    }

    // Into a zone, on top of (or after) what's there, staying where it is in the world to slide to its place from there,
    // turned as the zone shows its cards.
    private void MoveTo(Card card, CardZone zone)
    {
        var entity = card.Entity!;
        var world = entity.Transform.WorldMatrix;
        var z = entity.Transform.Position.Z;
        var from = card.Zone;
        Scene.Move(entity, zone.Entity);
        // Counted at once, so what reads them this frame sees the card where it is now.
        if (from is not null)
            from.Count = CardZone.CardsIn(from.Entity).Count;
        zone.Count = CardZone.CardsIn(zone.Entity).Count;
        if (Matrix4x4.Invert(zone.Entity!.Transform.WorldMatrix, out var toLocal))
        {
            var local = world * toLocal;
            entity.Transform.Position = new Vector3(local.M41, local.M42, z);
            entity.Transform.Rotation = new Vector3(0, 0, MathF.Atan2(local.M12, local.M11) * 180 / MathF.PI);
        }

        card.FaceUp = zone.PutsFaceUp;
        card.Resting = false;
    }

    private static IEnumerable<ICardPart> PartsOf(Card card) => card.Entity!.Components.OfType<ICardPart>();

    // ----- Laying out -----

    // Each card slides toward its place in its zone (easing in, so it settles), where the game is run; how many it holds.
    private void LayOut(CardZone zone)
    {
        var cards = CardZone.CardsIn(zone.Entity);
        var runs = Game.Runs(zone.Entity!);
        if (runs)
            zone.Count = cards.Count;
        if (!runs || zone.Entity!.IsPaused)
            return;
        var slide = zone.Slide;
        var k = slide > 0 ? 1 - MathF.Exp(-Time.Delta * 5 / slide) : 1;
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var (at, angle) = zone.Slot(i, cards.Count, card.Size);
            var transform = card.Entity!.Transform;
            var position = transform.Position;
            var now = new Vector2(position.X, position.Y);
            var to = Vector2.Lerp(now, at, k);
            if (Vector2.DistanceSquared(to, at) < 0.002f * 0.002f)
                to = at;
            var turn = transform.Rotation.Z;
            var off = Wrap(angle - turn);
            var turned = MathF.Abs(off * (1 - k)) < 0.05f ? angle : turn + off * k;
            if (to != now)
                transform.Position = new Vector3(to, position.Z);
            if (turned != turn || transform.Rotation.X != 0 || transform.Rotation.Y != 0)
                transform.Rotation = new Vector3(0, 0, turned);
            card.Resting = to == at && turned == angle;
        }

        static float Wrap(float degrees)
        {
            degrees %= 360;
            return degrees > 180 ? degrees - 360 : degrees < -180 ? degrees + 360 : degrees;
        }
    }

    // ----- Finding things -----

    // The deck a hand draws from: the one its Deck names, or the scene's first face-down deck.
    private Deck? DeckOf(Hand hand, List<Deck> decks) =>
        string.IsNullOrWhiteSpace(hand.Deck) ? decks.FirstOrDefault(d => !d.FaceUp) : Named(hand.Deck, decks);

    // A discard pile: the one named, or the scene's first face-up deck.
    private Deck? PileOf(string? name, List<Deck> decks) =>
        string.IsNullOrWhiteSpace(name) ? decks.FirstOrDefault(d => d.FaceUp) : Named(name, decks);

    private Deck? Named(string name, List<Deck> decks)
    {
        name = name.Trim();
        return decks.FirstOrDefault(d => d.Entity!.Name == name) ?? Scene.FindNamed(name)?.GetComponent<Deck>();
    }

    // The hand a deck's Draw action draws into: of those that draw from it, the first whose side's turn it is, else the first.
    private Hand? HandFor(Deck deck, List<Deck> decks, List<Hand> hands)
    {
        Hand? first = null;
        foreach (var hand in hands)
        {
            if (DeckOf(hand, decks) != deck)
                continue;
            if (hand.Side.Length > 0 && Game.TurnOrderOf(hand.Entity) is { } order && order.IsTurn(hand.Side))
                return hand;
            first ??= hand;
        }

        return first;
    }

    // Whether a player may play a hand: not one the computer plays, and in a network game with teams only their side's;
    // and, with `now`, only on its side's turn (with actions left).
    private bool MayPlay(Hand hand, NetPlayer? player, bool now)
    {
        var side = hand.Side;
        if (side.Length > 0 && Game.TurnOrderOf(hand.Entity) is { } order && order.IsComputer(side))
            return false;
        if (Game.Net.IsOnline && player is { Team.Length: > 0 } && side.Length > 0 && !TurnOrder.SameSide(side, player.Team))
            return false;
        return !now || !Game.Waiting(hand.Entity);
    }

    // ----- From players' machines -----

    // A play or a draw a player asked for, carried out where the game is run if it's theirs to make.
    internal void RunCommand(NetPlayer? player, object? args)
    {
        if (args is not object?[] { Length: 6 } a || a[0] is not int kind || a[1] is not Entity first || a[2] is not Entity second ||
            first.Scene != Scene || second.Scene != Scene || !Game.Runs(first))
            return;
        var decks = Game.Showing<Deck>().ToList();
        if (kind == PlayCommand && first.GetComponent<Card>() is { } card && card.Zone is Hand hand && second.GetComponent<CardZone>() is { } zone &&
            MayPlay(hand, player, now: true) && !hand.Entity!.IsPaused)
        {
            Vector2? at = a[3] is float x && a[4] is float y && float.IsFinite(x) && float.IsFinite(y) ? new Vector2(x, y) : null;
            var target = a[5] is Entity chosen && chosen.Scene == Scene ? chosen : null;
            if (!Chooses(card) || target is not null)
                Play(card, hand, zone, at, target);
        }
        else if (kind == DrawCommand && first.GetComponent<Deck>() is { ClickToDraw: true } deck && second.GetComponent<Hand>() is { } drawer &&
                 DeckOf(drawer, decks) == deck && MayPlay(drawer, player, now: true) && !drawer.Entity!.IsPaused)
        {
            DrawOne(drawer, decks);
        }
    }

    private void SendPlay(Card card, CardZone zone, Vector2? at, Entity? target) =>
        Game.Net.SendCommand(CardsCommand, new object?[] { PlayCommand, card.Entity, zone.Entity, at?.X ?? float.NaN, at?.Y ?? float.NaN, target });

    private void SendDraw(Deck deck, Hand hand) =>
        Game.Net.SendCommand(CardsCommand, new object?[] { DrawCommand, deck.Entity, hand.Entity, 0f, 0f, null });
}
