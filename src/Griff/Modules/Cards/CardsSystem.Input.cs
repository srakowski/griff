using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Cards;

/// <summary>
/// What this machine's player is doing with the cards: the one under the mouse, the one pressed on and whether it's being
/// dragged, the one the keys are on and the play zone they point at, and plays sent to the host it hasn't carried out yet.
/// </summary>
internal sealed class CardsView
{
    public int Count => Pending.Count + (Hovered is null ? 0 : 1) + (Pressed is null ? 0 : 1) + (Picked is null ? 0 : 1);

    public Card? Hovered { get; set; }

    public Card? Pressed { get; set; }

    public Vector2 PressedAt { get; set; }

    // From the mouse to the middle of the card it drags.
    public Vector2 Grip { get; set; }

    public bool Dragging { get; set; }

    public Card? Picked { get; set; }

    // The keys (or a gamepad) are in use rather than the mouse: their cursor shows.
    public bool Keys { get; set; }

    public Hand? KeyHand { get; set; }

    public int Cursor { get; set; } = -1;

    public int Target { get; set; }

    public int Stick { get; set; }

    public Vector2? LastMouse { get; set; }

    // Plays this player's machine has sent the host, with the seconds it waits for them before the card eases back.
    public Dictionary<Card, float> Pending { get; } = [];
}

internal sealed partial class CardsSystem
{
    // How far the mouse moves with a card pressed before it's dragged rather than clicked.
    private const float DragFrom = 0.2f;

    // Seconds a card dropped on a zone waits for the host to play it before easing back to the hand.
    private const float PendingFor = 0.75f;

    private static readonly Color NoHighlight = Color.Transparent;

    public CardsView View { get; private set; } = new();

    private IInput Input => Game.Input;

    // ----- Each machine: how things look here, and its player's input -----

    private void Present(ComponentList<CardZone> zones, List<Deck> decks, List<Hand> hands, List<PlayZone> tables, ComponentList<Card> cards)
    {
        var seconds = Time.UnscaledDelta;
        var homes = new Dictionary<string, Deck>(StringComparer.Ordinal);
        foreach (var deck in decks)
        {
            homes.TryAdd(deck.Entity!.Name, deck);
            var inIt = CardZone.CardsIn(deck.Entity);
            var moving = 0;
            Card? top = null;
            foreach (var card in inIt)
            {
                if (card.Resting)
                    top = card;
                else
                    moving++;
            }

            deck.PileCount = Math.Max(0, deck.Count - moving);
            foreach (var card in inIt)
                card.InPile = card.Resting && !(deck.FaceUp && card == top);
            if (deck.Shuffles != deck.ShufflesSeen)
            {
                if (deck.ShufflesSeen >= 0)
                    deck.Jiggle = CardLook.JiggleTime;
                deck.ShufflesSeen = deck.Shuffles;
            }
            else
            {
                deck.Jiggle = Math.Max(0, deck.Jiggle - seconds);
            }
        }

        foreach (var hand in hands)
        {
            hand.Shown = CardZone.CardsIn(hand.Entity).Count;
            var back = DeckOf(hand, decks);
            (hand.BackImage, hand.BackColor) = back is null ? (null, CardLook.DefaultBack) : (back.Back, back.BackColor);
            hand.CanPlay = MayPlay(hand, Game.Net.LocalPlayer, now: true);
        }

        foreach (var card in cards)
        {
            var zone = card.Zone;
            if (zone is not Deck)
                card.InPile = false;
            if (card.Resting || zone is null)
                (card.RestedIn, card.Travelling) = (card.Entity!.Parent, false);
            else
                card.Travelling = card.Entity!.Parent != card.RestedIn;
            var home = !string.IsNullOrWhiteSpace(card.Home) && homes.TryGetValue(card.Home.Trim(), out var h) ? h : zone as Deck;
            (card.BackImage, card.BackColor) = home is null ? (null, CardLook.DefaultBack) : (home.Back, home.BackColor);
            card.HiddenHere = zone is Hand held && HiddenHere(held);
        }

        if (!Game.PausedThisFrame && !Game.LocalInputBlocked && Game.TopPanel is null)
            Control(hands, decks, tables);
        else
            LetGo();

        Show(zones, hands, cards, seconds);
    }

    // A hidden hand of a side this machine's player isn't on (online); at one screen, of a side the computer plays, or with
    // several people taking turns, of a side whose turn it isn't.
    private bool HiddenHere(Hand hand)
    {
        if (!hand.Hidden || hand.Side is not { Length: > 0 } side)
            return false;
        if (Game.Net.IsOnline)
            return !TurnOrder.SameSide(side, Game.Net.LocalPlayer?.Team ?? "");
        if (Game.TurnOrderOf(hand.Entity) is not { } order)
            return false;
        if (order.IsComputer(side))
            return true;
        return !order.IsTurn(side) && order.SideList.Count(s => !order.IsComputer(s)) > 1;
    }

    private void Control(List<Hand> hands, List<Deck> decks, List<PlayZone> tables)
    {
        var me = Game.Net.LocalPlayer;
        var mine = hands.Where(h => h.Controllable && !h.Entity!.IsPaused && MayPlay(h, me, now: false)).ToList();

        // A card taken up that can't be played any more (played by a script, its turn over) is put back.
        if (View.Picked is { } picked && (picked.Entity?.Scene != Scene || picked.Zone is not Hand from || !mine.Contains(from) || !MayPlay(from, me, now: true)))
            Unpick();
        if (View.Pressed is { } pressed && (pressed.Entity?.Scene != Scene || pressed.Zone is not Hand))
            (View.Pressed, View.Dragging) = (null, false);

        Mouse(mine, decks, tables);
        Keys(mine, decks, tables);
    }

    // Press on a card and drag it onto a zone, or click it and then the zone; click a deck to draw.
    private void Mouse(List<Hand> mine, List<Deck> decks, List<PlayZone> tables)
    {
        var input = Input;
        var mouse = input.MouseWorld;
        var inView = input.IsMouseInView;
        var overUi = Game.MouseOnUi;
        if (inView && View.LastMouse is { } last && Vector2.DistanceSquared(last, mouse) > 1e-6f)
            View.Keys = false;
        View.LastMouse = mouse;

        View.Hovered = View.Dragging ? View.Pressed : inView && !overUi ? CardUnder(mine, mouse) : null;

        if (input.WasPressed(MouseButton.Left) && inView && !overUi)
        {
            if (View.Hovered is { Zone: Hand hand } card && MayPlay(hand, Game.Net.LocalPlayer, now: true))
            {
                (View.Pressed, View.PressedAt, View.Dragging) = (card, mouse, false);
                View.Grip = Visual(card) - mouse;
            }
            else if (View.Picked is { Zone: Hand from } picked && ZoneAt(mouse, decks, tables, z => CanGo(picked, from, z)) is { } zone)
            {
                PlayHere(picked, zone, null);
            }
            else if (ZoneAt(mouse, decks, tables, z => z is Deck { ClickToDraw: true }) is Deck deck &&
                     mine.FirstOrDefault(h => DeckOf(h, decks) == deck && MayPlay(h, Game.Net.LocalPlayer, now: true)) is { } drawer)
            {
                Unpick();
                SendDraw(deck, drawer);
            }
            else
            {
                Unpick();
            }
        }

        if (View.Pressed is { Zone: Hand hand2 } pressed)
        {
            if (input.IsDown(MouseButton.Left))
            {
                if (!View.Dragging && Vector2.Distance(mouse, View.PressedAt) > DragFrom)
                {
                    Pick(pressed);
                    View.Dragging = true;
                    pressed.Dragged = true;
                }
            }
            else
            {
                if (View.Dragging)
                {
                    var at = Visual(pressed);
                    var zone = ZoneAt(mouse, decks, tables, z => CanGo(pressed, hand2, z)) ?? ZoneAt(at, decks, tables, z => CanGo(pressed, hand2, z));
                    if (zone is not null)
                        PlayHere(pressed, zone, at);
                    else
                        Unpick();
                }
                else if (View.Picked == pressed)
                {
                    Unpick();
                }
                else
                {
                    Pick(pressed);
                }

                if (View.Pressed is { } still)
                    still.Dragged = false;
                (View.Pressed, View.Dragging) = (null, false);
            }
        }

        if (input.WasPressed(MouseButton.Right) && inView)
            Unpick();
    }

    // Left and right pick a card of the hand, the Pick Action takes it up; then left and right choose where it goes and the
    // Pick Action plays it there. The Cancel Action puts it back; the Draw Action draws from the hand's deck.
    private void Keys(List<Hand> mine, List<Deck> decks, List<PlayZone> tables)
    {
        var me = Game.Net.LocalPlayer;
        var hand = View.Picked?.Zone as Hand ?? mine.FirstOrDefault(h => MayPlay(h, me, now: true)) ?? mine.FirstOrDefault();
        if (View.KeyHand != hand)
            (View.KeyHand, View.Cursor) = (hand, -1);
        var way = Way();
        if (hand is null)
            return;
        var input = Input;
        bool Pressed(string? action) => !string.IsNullOrWhiteSpace(action) && input.WasPressed(action.Trim());

        if (Pressed(hand.DrawAction) && DeckOf(hand, decks) is { ClickToDraw: true } deck && MayPlay(hand, me, now: true))
            SendDraw(deck, hand);
        if (Pressed(hand.CancelAction))
        {
            Unpick();
            return;
        }

        var pick = Pressed(hand.PickAction);
        if (View.Picked is { } picked)
        {
            var targets = Targets(picked, hand, decks, tables);
            if (targets.Count == 0)
            {
                if (pick)
                    Unpick();
                return;
            }

            if (way != 0)
            {
                View.Keys = true;
                View.Target = ((View.Target + way) % targets.Count + targets.Count) % targets.Count;
            }

            View.Target = Math.Clamp(View.Target, 0, targets.Count - 1);
            if (pick)
                PlayHere(picked, targets[View.Target], null);
            return;
        }

        var cards = CardZone.CardsIn(hand.Entity);
        if (cards.Count == 0)
        {
            View.Cursor = -1;
            return;
        }

        if (way != 0)
        {
            View.Cursor = !View.Keys || View.Cursor < 0 ? cards.Count / 2 : Math.Clamp(View.Cursor + way, 0, cards.Count - 1);
            View.Keys = true;
        }

        if (!pick)
            return;
        if (!View.Keys || View.Cursor < 0)
        {
            // The first press only shows where the keys are.
            View.Keys = true;
            View.Cursor = View.Cursor < 0 ? cards.Count / 2 : Math.Clamp(View.Cursor, 0, cards.Count - 1);
            return;
        }

        View.Cursor = Math.Clamp(View.Cursor, 0, cards.Count - 1);
        if (!MayPlay(hand, me, now: true))
            return;
        var card = cards[View.Cursor];
        Pick(card);
        // Pointing first at the zone nearest the middle of the table.
        var options = Targets(card, hand, decks, tables);
        View.Target = options.Count == 0 ? 0 : options.IndexOf(options.OrderBy(z => Math.Abs(Center(z.Entity!).X)).First());
    }

    // Left or right pressed this frame (the arrows, A and D, the d-pad or the stick pushed over): -1, 1, or 0.
    private int Way()
    {
        var input = Input;
        var way = 0;
        if (input.WasPressed(Key.Left) || input.WasPressed(Key.A) || input.WasPressed(GamepadButton.DPadLeft))
            way = -1;
        if (input.WasPressed(Key.Right) || input.WasPressed(Key.D) || input.WasPressed(GamepadButton.DPadRight))
            way = 1;
        var stick = input.LeftStick.X > Game.NavigateStick ? 1 : input.LeftStick.X < -Game.NavigateStick ? -1 : 0;
        if (stick != View.Stick)
        {
            View.Stick = stick;
            if (stick != 0)
                way = stick;
        }

        return way;
    }

    // The zones a card from a hand can go to now, left to right.
    private static List<CardZone> Targets(Card card, Hand hand, List<Deck> decks, List<PlayZone> tables) =>
        [.. tables.Cast<CardZone>().Concat(decks).Where(z => CanGo(card, hand, z)).OrderBy(z => Center(z.Entity!).X)];

    private void Pick(Card card)
    {
        if (View.Picked == card)
            return;
        Unpick();
        View.Picked = card;
        if (card.Zone is Hand hand)
        {
            hand.Picked = card;
            Game.PlayIfSet(hand.PickSound);
        }
    }

    private void Unpick()
    {
        if (View.Picked is { } picked)
        {
            picked.Dragged = false;
            if (picked.Zone is Hand hand && hand.Picked == picked)
                hand.Picked = null;
        }

        View.Picked = null;
        View.Target = 0;
    }

    // Lets go of whatever the mouse had (a panel opened, the game paused).
    private void LetGo()
    {
        if (View.Pressed is { } pressed)
            pressed.Dragged = false;
        (View.Pressed, View.Dragging, View.Hovered) = (null, false, null);
        Unpick();
    }

    // Asks the host to play it; on a player's machine the card stays where it was let go until the host has.
    private void PlayHere(Card card, CardZone zone, Vector2? at)
    {
        Unpick();
        if (Game.Net.IsClient)
            View.Pending[card] = PendingFor;
        SendPlay(card, zone, at);
    }

    // ----- Where things are -----

    // The card under a point, in one of these hands: the one lifted (it's on top), else the one drawn on top.
    private Card? CardUnder(List<Hand> hands, Vector2 point)
    {
        if (View.Hovered is { Zone: Hand hovered } lifted && hands.Contains(hovered) && Covers(lifted, point, asLifted: true))
            return lifted;
        foreach (var hand in hands)
        {
            var cards = CardZone.CardsIn(hand.Entity);
            for (var i = cards.Count - 1; i >= 0; i--)
            {
                if (Covers(cards[i], point, asLifted: false))
                    return cards[i];
            }
        }

        return null;
    }

    // Whether a point is on a card where it lies (or, lifted, where it's drawn lifted out of its hand).
    private static bool Covers(Card card, Vector2 point, bool asLifted)
    {
        var world = card.Entity!.Transform.WorldMatrix;
        if (asLifted)
        {
            var center = Visual(card);
            var scale = 1 + (card.LiftScale - 1) * card.Lift.Value;
            var half = Vector2.Abs(card.Size) * scale / 2;
            return MathF.Abs(point.X - center.X) <= half.X && MathF.Abs(point.Y - center.Y) <= half.Y;
        }

        if (!Matrix4x4.Invert(world, out var toLocal))
            return false;
        var local = Vector2.Transform(point, toLocal);
        return MathF.Abs(local.X) <= MathF.Abs(card.Size.X) / 2 && MathF.Abs(local.Y) <= MathF.Abs(card.Size.Y) / 2;
    }

    // The zone under a point that `takes` says yes to: a play zone or a deck, the one drawn on top.
    private static CardZone? ZoneAt(Vector2 point, List<Deck> decks, List<PlayZone> tables, Func<CardZone, bool> takes)
    {
        CardZone? found = null;
        foreach (var table in tables)
        {
            if (Inside(table.Entity!, table.Size, point) && takes(table))
                found = table;
        }

        foreach (var deck in decks)
        {
            if (Inside(deck.Entity!, deck.Size + new Vector2(0.2f), point) && takes(deck))
                found = deck;
        }

        return found;
    }

    private static bool Inside(Entity entity, Vector2 size, Vector2 point)
    {
        if (!Matrix4x4.Invert(entity.Transform.WorldMatrix, out var toLocal))
            return false;
        var local = Vector2.Transform(point, toLocal);
        return MathF.Abs(local.X) <= MathF.Abs(size.X) / 2 && MathF.Abs(local.Y) <= MathF.Abs(size.Y) / 2;
    }

    private static Vector2 Center(Entity entity)
    {
        var at = entity.Transform.WorldPosition;
        return new Vector2(at.X, at.Y);
    }

    // Where a card's middle is drawn: where it lies, lifted out of its hand and moved by a drag.
    private static Vector2 Visual(Card card) => Center(card.Entity!) + new Vector2(0, card.LiftRise * card.Lift.Value) + card.Offset.Value;

    // ----- How it looks here -----

    // Each card lifted (under the mouse, picked, dragged, the keys on it) or not, outlined, and drawn where it's dragged or
    // easing back from a drag; play zones and decks lit where a held card could go and would go.
    private void Show(ComponentList<CardZone> zones, List<Hand> hands, ComponentList<Card> cards, float seconds)
    {
        var mouse = Input.MouseWorld;
        var cursorCard = View.Keys && View.Picked is null && View.KeyHand is { } keyHand && View.Cursor >= 0 &&
                         CardZone.CardsIn(keyHand.Entity) is var keyCards && View.Cursor < keyCards.Count ? keyCards[View.Cursor] : null;
        var ease = 1 - MathF.Exp(-seconds * 18);

        foreach (var (pending, left) in View.Pending.ToList())
        {
            if (pending.Entity?.Scene != Scene || pending.Zone is not Hand || left - seconds <= 0)
            {
                View.Pending.Remove(pending);
                if (pending.Zone is not Hand)
                    ResetLook(pending);
            }
            else
            {
                View.Pending[pending] = left - seconds;
            }
        }

        foreach (var card in cards)
        {
            var hand = card.Zone as Hand;
            if (hand is not null)
                (card.LiftRise, card.LiftScale) = (hand.HoverRise, hand.HoverScale);
            var pending = View.Pending.ContainsKey(card);
            var dragged = View.Dragging && View.Pressed == card;
            var lift = hand is null ? 0
                : dragged || pending || View.Picked == card ? 1
                : (View.Hovered == card && !View.Dragging && !View.Keys) || cursorCard == card ? 0.7f
                : 0;
            card.Highlight = hand is not null && (View.Picked == card || cursorCard == card) ? hand.HighlightColor : NoHighlight;
            Ease(ref card.Lift, card, lift, ease);
            if (dragged)
            {
                var to = mouse + View.Grip - Center(card.Entity!) - new Vector2(0, card.LiftRise * card.Lift.Value);
                if (to != card.Offset.Value)
                    card.Offset.Set(card, to);
            }
            else if (!pending)
            {
                var offset = card.Offset.Value;
                if (offset != Vector2.Zero)
                {
                    var next = offset.LengthSquared() < 0.0001f ? Vector2.Zero : offset * (1 - ease);
                    card.Offset.Set(card, next);
                }
            }
        }

        // Where the card held (dragged or picked) could go, and where it would.
        var held = View.Dragging ? View.Pressed : View.Picked;
        var heldHand = held?.Zone as Hand;
        CardZone? aimed = null;
        if (held is not null && heldHand is not null)
        {
            if (View.Dragging)
            {
                var at = Visual(held);
                var decks = zones.OfType<Deck>().ToList();
                var tables = zones.OfType<PlayZone>().ToList();
                aimed = ZoneAt(mouse, decks, tables, z => CanGo(held, heldHand, z)) ?? ZoneAt(at, decks, tables, z => CanGo(held, heldHand, z));
            }
            else if (View.Keys)
            {
                var targets = Targets(held, heldHand, zones.OfType<Deck>().ToList(), zones.OfType<PlayZone>().ToList());
                aimed = targets.Count > 0 ? targets[Math.Clamp(View.Target, 0, targets.Count - 1)] : null;
            }
        }

        foreach (var zone in zones)
        {
            var lit = held is null || heldHand is null || !CanGo(held, heldHand, zone) ? 0 : zone == aimed ? 2 : 1;
            switch (zone)
            {
                case PlayZone table:
                    table.Lit = lit;
                    break;
                case Deck deck:
                    (deck.Lit, deck.LitColor) = (lit, heldHand?.HighlightColor ?? NoHighlight);
                    break;
            }
        }
    }

    private static void Ease(ref Smoothed<float> value, Card card, float to, float ease)
    {
        var now = value.Value;
        if (now == to)
            return;
        var next = now + (to - now) * ease;
        if (MathF.Abs(next - to) < 0.002f)
            next = to;
        value.Set(card, next);
    }

    // Back as it lies: not lifted, dragged or outlined (it has gone somewhere else).
    private void ResetLook(Card card)
    {
        card.Lift = new Smoothed<float>(0);
        card.Offset = new Smoothed<Vector2>(Vector2.Zero);
        (card.Dragged, card.Highlight) = (false, NoHighlight);
        View.Pending.Remove(card);
        if (View.Pressed == card)
            (View.Pressed, View.Dragging) = (null, false);
        if (View.Picked == card)
            Unpick();
        if (View.Hovered == card)
            View.Hovered = null;
    }
}
