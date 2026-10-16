using System.Globalization;
using System.Numerics;
using System.Reflection;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Cards;

// What cards do (CardEffect): damage, healing, draws, discards, mana, buffs and any action, done to their targets where
// the game is run; who those targets are (heroes, characters, creatures on the table, each on a team); and creatures that
// die going to the discard pile.
internal sealed partial class CardsSystem
{
    // Units found this frame, kept to look through again without garbage.
    [NotState("lists kept only to use again")]
    private readonly List<Entity> _units = [];
    [NotState("lists kept only to use again")]
    private readonly List<Entity> _targets = [];

    // ----- Effects -----

    // A card's effects that happen `when`, done for `hand` (the player who played it, drew it, or had it), at `chosen` for
    // those that choose. Returns what they did, for the hand's Said; FirstTarget is then the first thing they were done to.
    private string RunEffects(Card card, Hand? hand, CardWhen when, Entity? chosen)
    {
        FirstTarget = null;
        var parts = new List<string>();
        var components = card.Entity!.Components.ToList();
        foreach (var component in components)
        {
            if (component is CardEffect effect && effect.When == when && card.Entity!.Scene == Scene)
                Apply(effect, card, hand, chosen, parts);
        }

        return string.Join(", ", parts);
    }

    // The first thing the effects RunEffects last ran were done to, or null.
    private Entity? FirstTarget { get; set; }

    private void Apply(CardEffect effect, Card card, Hand? hand, Entity? chosen, List<string> said)
    {
        var targets = new List<Entity>();
        UnitsFor(effect.Target, hand, chosen, targets);
        var source = card.Entity!;
        FirstTarget ??= targets.Count > 0 ? targets[0] : null;

        if (effect.Damage > 0 || !string.IsNullOrWhiteSpace(effect.DamageRoll))
        {
            var amount = effect.RollDamage();
            foreach (var target in targets)
            {
                if (amount > 0 && Health.Of(target)?.Damage(amount, source, always: true) == true)
                    said.Add($"{Amount(amount)} damage to {Called(target)}");
            }
        }

        if (effect.Heal > 0 || !string.IsNullOrWhiteSpace(effect.HealRoll))
        {
            var amount = effect.RollHeal();
            foreach (var target in targets)
            {
                if (Health.Of(target)?.Heal(amount) is > 0 and var healed)
                    said.Add($"+{Amount(healed)} health to {Called(target)}");
            }
        }

        if (effect.Buff != 0 && !string.IsNullOrWhiteSpace(effect.BuffStat))
        {
            foreach (var target in targets)
            {
                if (Raise(target, effect.BuffStat.Trim(), effect.Buff))
                    said.Add($"{(effect.Buff > 0 ? "+" : "")}{Amount(effect.Buff)} {effect.BuffStat.Trim()} to {Called(target)}");
            }
        }

        if (effect.Discard > 0)
        {
            foreach (var other in HandsFor(effect.Target, hand, chosen))
            {
                var gone = 0;
                for (var i = 0; i < effect.Discard; i++)
                {
                    var held = CardZone.CardsIn(other.Entity);
                    if (held.Count == 0 || PileOf(other.DiscardPile, Game.Showing<Deck>().ToList()) is not { } pile)
                        break;
                    Discard(held[Rand.Range(0, held.Count)], pile, other);
                    gone++;
                }

                if (gone > 0)
                    said.Add($"{Called(other)} discards {gone}");
            }
        }

        if (hand is not null && effect.Draw > 0)
        {
            hand.ToDraw += effect.Draw;
            said.Add($"draws {effect.Draw}");
        }

        if (hand is not null && effect.GainMana != 0)
        {
            hand.GainMana(effect.GainMana);
            said.Add($"+{effect.GainMana} mana");
        }

        if (!string.IsNullOrWhiteSpace(effect.Do))
        {
            var on = targets;
            if (!string.IsNullOrWhiteSpace(effect.DoOn))
            {
                on = [];
                Game.TargetsOf(source, effect.DoOn, on);
            }

            foreach (var target in on)
            {
                if (target.Scene == Scene && Game.Runs(target))
                    Game.DoOn(target, effect.Do.Trim(), effect.With, null);
            }
        }

        Game.PlayIfSet(effect.Sound);
        if (!string.IsNullOrWhiteSpace(effect.Effect) && Game.PrefabLoader is not null)
        {
            foreach (var target in targets.Count > 0 ? targets : [source])
            {
                var at = target.Transform.WorldPosition;
                Game.Spawn(effect.Effect.Trim(), new Vector2(at.X, at.Y));
            }
        }
    }

    // Adds `by` to a number property of the target's components called `stat` ("Value", or "Health.Max" for one
    // component's). True if it had one.
    private static bool Raise(Entity target, string stat, float by)
    {
        var dot = stat.LastIndexOf('.');
        var (type, name) = dot < 0 ? (null, stat) : (stat[..dot].Trim(), stat[(dot + 1)..].Trim());
        var raised = false;
        foreach (var component in target.Components.ToList())
        {
            if (type is not null && !string.Equals(component.GetType().Name, type, StringComparison.OrdinalIgnoreCase))
                continue;
            if (component.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is not { CanWrite: true, CanRead: true } property ||
                property.SetMethod?.IsPublic != true)
                continue;
            if (property.PropertyType == typeof(float))
                property.SetValue(component, (float)property.GetValue(component)! + by);
            else if (property.PropertyType == typeof(int))
                property.SetValue(component, (int)property.GetValue(component)! + (int)MathF.Round(by));
            else
                continue;
            raised = true;
        }

        return raised;
    }

    // ----- Who's who -----

    // The entities an effect is done to, for `hand`'s side.
    private void UnitsFor(CardTarget target, Hand? hand, Entity? chosen, List<Entity> into)
    {
        switch (target)
        {
            case CardTarget.Self:
                if (hand is not null && HeroOf(hand) is { } hero)
                    into.Add(hero);
                break;
            case CardTarget.Opponent:
                if (OpponentOf(hand) is { } foe)
                    into.Add(foe);
                break;
            case CardTarget.Chosen:
                if (chosen is not null && chosen.Scene == Scene && chosen.IsActive)
                    into.Add(chosen);
                break;
            default:
                foreach (var unit in Units())
                {
                    var friend = IsFriend(unit, hand);
                    if (target == CardTarget.Everyone || (target == CardTarget.AllFriends) == friend)
                        into.Add(unit);
                }

                break;
        }
    }

    // The hands an effect's discards come from: yours, the opponent's, every foe's or friend's, or the chosen unit's side's.
    private List<Hand> HandsFor(CardTarget target, Hand? hand, Entity? chosen)
    {
        var hands = Game.Showing<Hand>().ToList();
        var side = hand?.Team ?? "";
        switch (target)
        {
            case CardTarget.Self:
                return hand is null ? [] : [hand];
            case CardTarget.Opponent:
                return OpponentHand(hand, hands) is { } other ? [other] : [];
            case CardTarget.Chosen:
                return chosen is null ? [] : [.. hands.Where(h => HeroOf(h) == chosen || (chosen.GetComponent<Card>() is { } c && c.PlayedBy?.Trim() == h.Entity!.Name))];
            case CardTarget.AllFriends:
                return [.. hands.Where(h => h == hand || (side.Length > 0 && Teams.Same(h.Team, side)))];
            case CardTarget.AllFoes:
                return [.. hands.Where(h => h != hand && !(side.Length > 0 && Teams.Same(h.Team, side)))];
            default:
                return hands;
        }
    }

    // The hand the opponent plays: the first on another side, or with no sides, the first other hand.
    private static Hand? OpponentHand(Hand? hand, List<Hand> hands)
    {
        var side = hand?.Team ?? "";
        foreach (var other in hands)
        {
            if (other != hand && (side.Length == 0 || other.Team.Length == 0 || !Teams.Same(other.Team, side)))
                return other;
        }

        return null;
    }

    /// <summary>
    /// A side's hero: the entity its Hero names, else the hand itself if it has a Health, else the first entity with a Health
    /// and a turn taker on its team that isn't a card.
    /// </summary>
    internal Entity? HeroOf(Hand hand)
    {
        if (!string.IsNullOrWhiteSpace(hand.Hero))
            return Scene.FindNamed(hand.Hero.Trim(), near: hand.Entity) is { IsActive: true } named ? named : null;
        if (hand.Entity!.GetComponent<Health>() is not null)
            return hand.Entity;
        var side = hand.Team;
        if (side.Length == 0)
            return null;
        foreach (var health in Game.Showing<Health>())
        {
            var entity = health.Entity!;
            if (entity.GetComponent<Card>() is null && Teams.Same(Game.TeamOf(entity), side))
                return entity;
        }

        return null;
    }

    // The opponent's hero: the other side's hand's; else the first character with a Health that isn't on this side.
    private Entity? OpponentOf(Hand? hand)
    {
        var mine = hand is null ? null : HeroOf(hand);
        if (OpponentHand(hand, Game.Showing<Hand>().ToList()) is { } other && HeroOf(other) is { } theirs && theirs != mine)
            return theirs;
        foreach (var unit in Units())
        {
            if (unit != mine && unit.GetComponent<Card>() is null && !IsFriend(unit, hand))
                return unit;
        }

        return null;
    }

    // Everything effects can be done to: characters with a Health that are still standing, and the cards on the table
    // with one (creatures).
    private List<Entity> Units()
    {
        _units.Clear();
        foreach (var health in Game.Showing<Health>())
        {
            var entity = health.Entity!;
            if (health.IsDead || !entity.IsActive)
                continue;
            if (entity.GetComponent<Card>() is { } card && card.Zone is not PlayZone)
                continue;
            _units.Add(entity);
        }

        return _units;
    }

    // On `hand`'s team: its hero, a card played from it, or anything else on its team (see Teams).
    private bool IsFriend(Entity unit, Hand? hand)
    {
        if (hand is null)
            return false;
        if (unit == HeroOf(hand))
            return true;
        var side = hand.Team;
        if (unit.GetComponent<Card>() is { } card)
        {
            if (string.Equals(card.PlayedBy?.Trim(), hand.Entity!.Name, StringComparison.Ordinal))
                return true;
            return side.Length > 0 && HandNamed(card.PlayedBy) is { } from && Teams.Same(from.Team, side);
        }

        return side.Length > 0 && Teams.Same(Game.TeamOf(unit), side);
    }

    private Hand? HandNamed(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : Scene.FindNamed(name.Trim())?.GetComponent<Hand>();

    // ----- Choosing a target -----

    /// <summary>True if a card has an effect whose target the player chooses as they play it.</summary>
    internal static bool Chooses(Card card)
    {
        var components = card.Entity!.Components;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i] is CardEffect { Chooses: true })
                return true;
        }

        return false;
    }

    // What a card's chosen target may be: the units its effects' Choose From allow (the cards or characters named, or
    // inside them), never the card itself.
    private List<Entity> Candidates(Card card)
    {
        _targets.Clear();
        var froms = new List<Entity>();
        var any = false;
        foreach (var effect in card.Entity!.GetComponents<CardEffect>())
        {
            if (!effect.Chooses)
                continue;
            if (string.IsNullOrWhiteSpace(effect.ChooseFrom))
                any = true;
            else
                Game.TargetsOf(card.Entity, effect.ChooseFrom, froms);
        }

        foreach (var unit in Units())
        {
            if (unit == card.Entity || Inside(unit, card.Entity))
                continue;
            if (any || froms.Any(f => f == unit || Inside(unit, f)))
                _targets.Add(unit);
        }

        return _targets;
    }

    private static bool Inside(Entity entity, Entity ancestor)
    {
        for (var e = entity.Parent; e is not null; e = e.Parent)
        {
            if (e == ancestor)
                return true;
        }

        return false;
    }

    // Whether `target` may be the card's chosen target now.
    private bool MayChoose(Card card, Entity target) => Candidates(card).Contains(target);

    // ----- Creatures -----

    // A card on the table with a Health that has died goes to its zone's discard pile, mended for next time.
    private void BuryTheDead(List<PlayZone> tables, List<Deck> decks)
    {
        foreach (var table in tables)
        {
            if (!Game.Runs(table.Entity!) || table.Entity!.IsPaused)
                continue;
            foreach (var card in CardZone.CardsIn(table.Entity))
            {
                if (card.Entity!.GetComponent<Health>() is not { IsDead: true } health)
                    continue;
                if (PileOf(table.DiscardPile, decks) is { } pile)
                {
                    Discard(card, pile, HandNamed(card.PlayedBy));
                    health.Current = health.Max;
                }
            }
        }
    }

    // ----- Words -----

    private static string Called(Entity entity) => entity.GetComponent<Card>()?.ShownTitle ?? entity.Name;

    private static string Called(Hand hand) => hand.Team is { Length: > 0 } side ? side : hand.Entity!.Name;

    private static string Amount(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
