using System.Globalization;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Battles;

/// <summary>
/// Runs turn-based battles with no code (<see cref="Battler"/>): each fighter's move on its turn, asked for by its actions
/// or keys, or picked by the computer for a team it plays, carried out where the game is run and using an action of its
/// turn taker so the turn passes on.
/// </summary>
internal sealed class BattlesSystem : GameSystem
{
    public override string Name => "Battles";

    // After the turns are taken, so a fighter whose turn has just come moves this frame.
    internal void Run()
    {
        var battlers = Game.Showing<Battler>();
        if (battlers.Count == 0)
            return;
        var still = Time.Delta <= 0 || Game.PausedThisFrame;
        foreach (var battler in battlers)
        {
            var entity = battler.Entity!;
            battler.CanAct = !still && !entity.IsPaused && Health.Of(entity) is not { IsDead: true } && !Game.Waiting(entity);
            if (!battler.CanAct || !Game.Runs(entity))
            {
                // A move asked for off its turn is dropped, not kept for later.
                battler.Asked = BattleMove.None;
                continue;
            }

            if (ComputerPlays(entity))
            {
                if (battler.ThinkLeft < 0)
                    battler.ThinkLeft = Math.Max(0, battler.ThinkTime);
                battler.ThinkLeft -= Time.Delta;
                if (battler.ThinkLeft > 0)
                    continue;
                battler.Asked = Choose(battler);
            }
            else if (battler.Controllable && !Game.LocalInputBlocked)
            {
                var input = Game.InputOf(entity);
                if (Pressed(input, battler.AttackAction))
                    battler.Asked = BattleMove.Attack;
                else if (Pressed(input, battler.SpellAction))
                    battler.Asked = BattleMove.Spell;
                else if (Pressed(input, battler.ItemAction))
                    battler.Asked = BattleMove.Item;
            }

            var move = battler.Asked;
            battler.Asked = BattleMove.None;
            if (move == BattleMove.None || !Make(battler, move))
                continue;
            battler.ThinkLeft = -1;
            Game.UseAction(entity);
            battler.CanAct = !Game.Waiting(entity);
        }
    }

    private static bool Pressed(IInput input, string? action) => !string.IsNullOrWhiteSpace(action) && input.WasPressed(action.Trim());

    // A side its turn order's computer plays.
    private bool ComputerPlays(Entity entity) =>
        Game.TurnOrderOf(entity) is { } order && TurnTaker.Of(entity) is { } taker && order.IsComputer(Game.TeamOf(taker));

    /// <summary>
    /// The computer's move: heal when hurt (an item, or a spell that heals), cast while it has the mana and the spell hits
    /// at least as hard as an attack, throw an item that hits harder, and otherwise attack.
    /// </summary>
    internal BattleMove Choose(Battler battler)
    {
        var health = Health.Of(battler.Entity!);
        var hurt = health is { } h && h.Current < h.Max * battler.HealBelow;
        var foe = FoeOf(battler);
        var attack = Math.Max(1, battler.AverageDamage - (foe?.GetComponent<Battler>()?.Armor ?? 0));
        if (hurt)
        {
            if (battler.CanUseItem && battler.ItemHeal > 0)
                return BattleMove.Item;
            if (battler.CanCast && battler.SpellHeal > 0)
                return BattleMove.Spell;
        }

        if (foe is not null && battler.CanCast && battler.SpellDamage > 0 && battler.AverageSpellDamage >= attack)
            return BattleMove.Spell;
        if (foe is not null && battler.CanUseItem && battler.ItemDamage > 0 && battler.AverageItemDamage > attack)
            return BattleMove.Item;
        if (!battler.CanCast && battler.SpellDamage > 0 && battler.CanUseItem && battler.ItemMana > 0 && battler.ItemHeal <= 0 && battler.ItemDamage <= 0)
            return BattleMove.Item;
        return BattleMove.Attack;
    }

    /// <summary>
    /// The first entity its Foe names that's still standing, if any; with no Foe, the first battler still standing on
    /// another team (see <see cref="Teams"/>).
    /// </summary>
    internal Entity? FoeOf(Battler battler)
    {
        if (string.IsNullOrWhiteSpace(battler.Foe))
        {
            var mine = Game.TeamOf(battler.Entity);
            foreach (var other in Game.Showing<Battler>())
            {
                if (other.Entity is { IsActive: true } entity && entity != battler.Entity && Health.Of(entity) is not { IsDead: true } &&
                    Teams.Foes(mine, Game.TeamOf(entity)))
                    return entity;
            }

            return null;
        }

        foreach (var part in battler.Foe.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (EntityNames.Find(Game.Scene, part, new NameLookup(battler.Entity, ActiveOnly: true, Scope: NameScope.Scene, ExceptFrom: true,
                    Where: static e => Health.Of(e) is not { IsDead: true })) is { } foe)
                return foe;
        }

        return null;
    }

    // Carries out a move, saying what happened; false (using no action) if it can't be made.
    private bool Make(Battler battler, BattleMove move)
    {
        var self = battler.Entity!;
        var foe = FoeOf(battler);
        var name = battler.Called;
        string said;
        switch (move)
        {
            case BattleMove.Attack:
            {
                if (foe is null)
                    return Fail(battler, $"{name} has nothing to attack.");
                var armor = foe.GetComponent<Battler>()?.Armor ?? 0;
                var damage = Math.Max(1, battler.RollDamage() - armor);
                Hurt(foe, damage, self, battler);
                Game.PlayIfSet(battler.AttackSound);
                said = $"{name} hits {Called(foe)} for {Amount(damage)}.";
                break;
            }

            case BattleMove.Spell:
            {
                if (string.IsNullOrWhiteSpace(battler.Spell) || !(battler.SpellDamage > 0 || battler.SpellHeal > 0))
                    return Fail(battler, $"{name} knows no spell.");
                if (battler.Mana < battler.SpellCost)
                    return Fail(battler, $"{name} hasn't the mana for {battler.Spell.Trim()} ({Amount(battler.SpellCost)}).");
                if (battler.SpellDamage > 0 && foe is null)
                    return Fail(battler, $"{name} has nothing to cast {battler.Spell.Trim()} on.");
                battler.Mana -= battler.SpellCost;
                Game.PlayIfSet(battler.SpellSound);
                var parts = new List<string>();
                if (battler.SpellDamage > 0 && foe is not null)
                {
                    var damage = battler.RollSpellDamage();
                    Hurt(foe, damage, self, battler);
                    parts.Add($"{Amount(damage)} damage");
                }

                if (battler.SpellHeal > 0)
                    parts.Add($"+{Amount(Heal(self, battler.RollSpellHeal(), battler))} health");
                said = battler.SpellDamage > 0 && foe is not null
                    ? $"{name} casts {battler.Spell.Trim()} on {Called(foe)}: {string.Join(", ", parts)}."
                    : $"{name} casts {battler.Spell.Trim()}: {string.Join(", ", parts)}.";
                break;
            }

            case BattleMove.Item:
            {
                var item = string.IsNullOrWhiteSpace(battler.Item) ? "item" : battler.Item.Trim();
                if (battler.Items <= 0)
                    return Fail(battler, $"{name} has no {item} left.");
                if (!battler.CanUseItem)
                    return Fail(battler, $"A {item} does nothing.");
                if (battler.ItemDamage > 0 && foe is null)
                    return Fail(battler, $"{name} has nothing to throw the {item} at.");
                battler.Items--;
                Game.PlayIfSet(battler.ItemSound);
                var parts = new List<string>();
                if (battler.ItemDamage > 0 && foe is not null)
                {
                    var damage = battler.RollItemDamage();
                    Hurt(foe, damage, self, battler);
                    parts.Add($"{Amount(damage)} damage");
                }

                if (battler.ItemHeal > 0)
                    parts.Add($"+{Amount(Heal(self, battler.RollItemHeal(), battler))} health");
                if (battler.ItemMana > 0)
                {
                    var before = battler.Mana;
                    battler.Mana = Math.Min(battler.MaxMana, battler.Mana + battler.ItemMana);
                    parts.Add($"+{Amount(battler.Mana - before)} mana");
                }

                said = battler.ItemDamage > 0 && foe is not null
                    ? $"{name} throws a {item} at {Called(foe)}: {string.Join(", ", parts)}."
                    : $"{name} uses a {item}: {string.Join(", ", parts)}.";
                break;
            }

            default:
                return false;
        }

        battler.Said = said;
        battler.RaiseMoved(said);
        return true;
    }

    private bool Fail(Battler battler, string said)
    {
        Game.PlayIfSet(battler.FailSound);
        battler.Said = said;
        return false;
    }

    // Hit points off the target, with the mover's effect on it.
    private void Hurt(Entity target, float damage, Entity source, Battler by)
    {
        Health.Of(target)?.Damage(damage, source);
        SpawnEffect(by, target);
    }

    private float Heal(Entity self, float amount, Battler by)
    {
        var healed = Health.Of(self)?.Heal(amount) ?? 0;
        SpawnEffect(by, self);
        return healed;
    }

    private void SpawnEffect(Battler by, Entity on)
    {
        if (!string.IsNullOrWhiteSpace(by.Effect))
            Game.TrySpawn(by.Effect, by, "its Effect", new System.Numerics.Vector2(on.Transform.WorldPosition.X, on.Transform.WorldPosition.Y));
    }

    private static string Called(Entity entity) => entity.GetComponent<Battler>()?.Called ?? entity.Name;

    private static string Amount(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
