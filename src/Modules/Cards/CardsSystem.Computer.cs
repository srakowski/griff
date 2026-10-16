using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Cards;

// The computer playing a hand on its turn order's Computer Teams: it thinks, lifts the card it's going to play out of its
// hand so the player can see it coming, plays it, and once it has nothing it wants to play, ends its turn. It picks by a
// rule anyone can follow (see Choose), with nothing left to chance, so a seeded game plays the same every time.
internal sealed partial class CardsSystem
{
    // The share of its Think Time it shows the card it's about to play.
    private const float ShowFor = 0.5f;

    // A play the computer could make: the card, where, at what, and what it weighs.
    private readonly record struct Option(Card Card, PlayZone Zone, Entity? Target, float Lethal, float Damage, float Heal, bool More, bool Plain);

    private void RunComputer(Hand hand, List<Deck> decks, List<PlayZone> tables)
    {
        var entity = hand.Entity!;
        if (!ComputerPlays(hand))
        {
            Unready(hand);
            return;
        }

        // On its turn only, once it's dealt and the turn has begun.
        if (Game.Waiting(entity) || Game.TurnOrderOf(entity) is not { } order || !order.IsTurn(hand.Team) || hand.ToDraw > 0 || Time.Delta <= 0)
        {
            Unready(hand);
            hand.ThinkLeft = -1;
            return;
        }

        if (hand.TurnDone)
            return;
        if (hand.ThinkLeft < 0)
            hand.ThinkLeft = Math.Max(0, hand.ThinkTime) * (1 - ShowFor);
        hand.ThinkLeft -= Time.Delta;
        if (hand.ThinkLeft > 0)
            return;

        // The card it showed goes; it thinks again before the next.
        if (hand.Readied is { } readied)
        {
            var zone = hand.ReadiedZone;
            var target = hand.ReadiedTarget;
            Unready(hand);
            hand.ThinkLeft = Math.Max(0, hand.ThinkTime) * (1 - ShowFor);
            if (readied.Zone == hand && zone is not null && Play(readied, hand, zone, null, target))
                return;
        }

        if (Choose(hand, tables) is not { } option)
        {
            // Nothing it wants to play: its turn is over.
            hand.TurnDone = true;
            order.EndTurn();
            return;
        }

        (hand.Readied, hand.ReadiedZone, hand.ReadiedTarget) = (option.Card, option.Zone, option.Target);
        (option.Card.Readied, option.Card.ReadiedAt) = (true, option.Target?.Name);
        hand.ThinkLeft = Math.Max(0, hand.ThinkTime) * ShowFor;
    }

    private static void Unready(Hand hand)
    {
        if (hand.Readied is { } card)
            (card.Readied, card.ReadiedAt) = (false, null);
        (hand.Readied, hand.ReadiedZone, hand.ReadiedTarget) = (null, null, null);
    }

    // A hand on a team its turn order's computer plays.
    private bool ComputerPlays(Hand hand) =>
        hand.Team is { Length: > 0 } side && Game.TurnOrderOf(hand.Entity) is { } order && order.IsComputer(side);

    /// <summary>
    /// The computer's play, by a rule anyone can follow: a card that finishes the opponent off; its healing when its hero is
    /// below Heal Below (it keeps healing until then); the card that deals the most damage; one that does something else
    /// (draws, mana, a buff, an action); a plain card, highest Value first. Ties go to the costlier card, then the leftmost.
    /// It plays only what it can pay for, never below Keep cards in its hand. Null when it has nothing it wants to play.
    /// </summary>
    internal (Card Card, PlayZone Zone, Entity? Target)? ChooseFor(Hand hand) =>
        Choose(hand, Game.Showing<PlayZone>().ToList()) is { } option ? (option.Card, option.Zone, option.Target) : null;

    private Option? Choose(Hand hand, List<PlayZone> tables)
    {
        var cards = CardZone.CardsIn(hand.Entity);
        if (cards.Count <= Math.Max(0, hand.Keep))
            return null;
        var hero = HeroOf(hand);
        var foe = OpponentOf(hand);
        var hurt = hero is not null && Health.Of(hero) is { } life && life.Current < life.Max * hand.HealBelow;
        var options = new List<Option>();
        foreach (var card in cards)
        {
            if (ZoneFor(card, hand, tables) is not { } zone)
                continue;
            var target = Chooses(card) ? ChooseTarget(card, hand) : null;
            if (Chooses(card) && target is null)
                continue;
            options.Add(Weigh(card, hand, zone, target, foe));
        }

        Option? Best(Func<Option, bool> which, Func<Option, float> by)
        {
            Option? best = null;
            foreach (var option in options)
            {
                if (!which(option))
                    continue;
                if (best is not { } b || by(option) > by(b) || (by(option) == by(b) && option.Card.Cost > b.Card.Cost))
                    best = option;
            }

            return best;
        }

        var foeLife = foe is not null && Health.Of(foe) is { } theirs ? theirs.Current : float.PositiveInfinity;
        return Best(o => o.Lethal >= foeLife, o => o.Damage)
               ?? (hurt ? Best(o => o.Heal > 0, o => o.Heal) : null)
               ?? Best(o => o.Damage > 0, o => o.Damage)
               ?? Best(o => o.More && o.Damage >= 0 && o.Heal >= 0, o => 0)
               ?? Best(o => o.Plain, o => o.Card.Value);
    }

    // What a play would do, on average: damage to the opponent's hero, damage all told (foes' less friends'), healing
    // where it's needed (friends' less foes'), and whether it does something else or nothing at all.
    private Option Weigh(Card card, Hand hand, PlayZone zone, Entity? target, Entity? foe)
    {
        float lethal = 0, damage = 0, heal = 0;
        var more = false;
        var any = false;
        var hit = new List<Entity>();
        foreach (var effect in card.Entity!.GetComponents<CardEffect>())
        {
            if (effect.When != CardWhen.Played)
            {
                // Something that happens later still makes it worth playing.
                more |= effect.When == CardWhen.EachTurn;
                any = true;
                continue;
            }

            any = true;
            more |= effect.DoesMore;
            hit.Clear();
            UnitsFor(effect.Target, hand, target, hit);
            foreach (var unit in hit)
            {
                var friend = IsFriend(unit, hand);
                var dealt = effect.AverageDamage;
                if (dealt > 0)
                {
                    damage += friend ? -dealt : dealt;
                    if (unit == foe)
                        lethal += dealt;
                }

                if (effect.AverageHeal > 0 && Health.Of(unit) is { } health)
                {
                    var mended = Math.Min(effect.AverageHeal, health.Max - health.Current);
                    heal += friend ? mended : -effect.AverageHeal;
                }
            }
        }

        return new Option(card, zone, target, lethal, damage, heal, more, !any);
    }

    // The play zone the computer puts a card on: one that names its hand in From (its own row) first, else the first that
    // takes it, left to right.
    private PlayZone? ZoneFor(Card card, Hand hand, List<PlayZone> tables)
    {
        PlayZone? any = null;
        foreach (var table in tables.OrderBy(t => t.Entity!.Transform.WorldPosition.X))
        {
            if (!CanGo(card, hand, table))
                continue;
            if (!string.IsNullOrWhiteSpace(table.From))
                return table;
            any ??= table;
        }

        return any;
    }

    /// <summary>
    /// The target the computer picks for a card that chooses one: for damage, a foe it finishes off (the toughest of those),
    /// else the opponent's hero, else the weakest foe; for healing, the friend missing the most health; otherwise the first
    /// friend for a buff, or the first it may choose. Null if there's nothing it would aim at.
    /// </summary>
    internal Entity? ChooseTarget(Card card, Hand hand)
    {
        float damage = 0, heal = 0, buff = 0;
        foreach (var effect in card.Entity!.GetComponents<CardEffect>())
        {
            if (!effect.Chooses)
                continue;
            damage += effect.AverageDamage;
            heal += effect.AverageHeal;
            buff += effect.Buff;
        }

        var candidates = Candidates(card).ToList();
        if (candidates.Count == 0)
            return null;
        var foes = candidates.Where(u => !IsFriend(u, hand)).ToList();
        var friends = candidates.Where(u => IsFriend(u, hand)).ToList();
        if (damage > 0)
        {
            var dies = foes.Where(u => Health.Of(u) is { } h && h.Current <= damage).OrderByDescending(u => Health.Of(u)!.Current).FirstOrDefault();
            if (dies is not null)
                return dies;
            if (OpponentOf(hand) is { } hero && foes.Contains(hero))
                return hero;
            return foes.OrderBy(u => Health.Of(u)!.Current).FirstOrDefault();
        }

        if (heal > 0)
            return friends.OrderByDescending(u => Health.Of(u) is { } h ? h.Max - h.Current : 0).FirstOrDefault();
        if (buff > 0)
            return friends.FirstOrDefault();
        if (buff < 0)
            return foes.FirstOrDefault();
        return candidates[0];
    }
}
