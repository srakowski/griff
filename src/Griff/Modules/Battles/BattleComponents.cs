using System.Globalization;
using Griff.Data;
using Griff.Scenes;

namespace Griff.Modules.Battles;

/// <summary>
/// A fighter in a turn-based battle, as in an RPG, with no code: on its turn it attacks its <see cref="Foe"/>, casts its
/// <see cref="Spell"/> (paid for in <see cref="Mana"/>) or uses one of its <see cref="Items"/>, a potion that heals or a
/// bomb that hurts. Attacks are softened by the foe's <see cref="Armor"/>; spells and items aren't. Each move uses an
/// action of its <see cref="TurnTaker"/>, so the turns pass by themselves. The player's fighter moves when its actions
/// are done (by buttons whose Action is Do, by a <see cref="Trigger"/>, or by its own keys); one on a side the
/// <see cref="TurnOrder"/>'s computer plays picks its own moves: it heals when hurt, casts while it has the mana and the
/// spell hits harder, throws an item that hits harder still, and otherwise attacks.
/// </summary>
/// <remarks>
/// Its numbers are made to come from a table: a <see cref="TableValues"/> beside it sets them from a class's or an
/// enemy's row (<c>Health.Max = Health</c>, <c>Battler.Damage = Damage</c>, <c>Battler.Spell = Spell</c>...), dice
/// columns rolled for each hit, so a balance simulation plays the fight by the same numbers. Without a turn order it
/// moves whenever its actions are done, and the computer moves nobody. In a network game the host carries out the moves.
/// </remarks>
[Category("Combat")]
[Requires(typeof(Health), Note = "what it has left; at 0 it's beaten")]
[WorksWith(typeof(TurnTaker), Note = "takes its turns: each move uses an action")]
[WorksWith(typeof(TableValues), Note = "its numbers from a class's or an enemy's row")]
[ComponentName("Griff.Battler")]
public sealed class Battler : Component, IActionReady, ITurnReady, IPlaytestReport
{
    /// <summary>
    /// What it fights, by name, e.g. "Enemy" (or "Knight, Mage" for a party): it goes for the first still standing. Its
    /// attacks, damaging spells and damaging items hit it.
    /// </summary>
    [EntityName(Many = true)]
    public string? Foe { get; set; } = "Enemy";

    /// <summary>What it's called in what it says, e.g. "Elf Mage"; empty uses its entity's name. Read as the "Title" output.</summary>
    [Output]
    public string? Title { get; set; }

    /// <summary>Hit points an attack takes away, before the foe's <see cref="Armor"/>.</summary>
    [Group("Attack")]
    public float Damage { get; set; } = 2;

    /// <summary>Dice rolled for each attack in place of <see cref="Damage"/>, e.g. "1d8+2"; empty hits for Damage every time.</summary>
    [RollsFor(nameof(Damage))]
    public string? DamageRoll
    {
        get => _damage.Text;
        set => _damage.Text = value;
    }

    /// <summary>Taken off each attack that hits it (never below 1). Spells and items go through it.</summary>
    public float Armor { get; set; }

    /// <summary>The most mana it can have. Changing it while full keeps it full.</summary>
    [Group("Mana")]
    public float MaxMana
    {
        get => _maxMana;
        set
        {
            if (Mana == _maxMana)
                Mana = value;
            _maxMana = value;
        }
    }

    // After Max Mana, so a scene loaded (a rewind) sets what's left after the most it can have: all of it spent stays spent.

    /// <summary>Mana it has now, which spells cost. It starts full.</summary>
    [Output(OutOf = nameof(MaxMana))]
    public float Mana { get; set; }

    /// <summary>Mana it gets back as each of its turns starts, never past <see cref="MaxMana"/>.</summary>
    public float ManaPerTurn { get; set; }

    /// <summary>Its spell's name, e.g. "Fireball", said when it's cast; empty for none. Read as the "Spell" output.</summary>
    [Group("Spell")]
    [Output]
    public string? Spell { get; set; }

    /// <summary>Mana a cast costs.</summary>
    [Output]
    public float SpellCost { get; set; }

    /// <summary>Hit points the spell takes from the foe (armor or not); 0 for a spell that only heals.</summary>
    public float SpellDamage { get; set; }

    /// <summary>Dice rolled for each cast's damage in place of <see cref="SpellDamage"/>, e.g. "3d6".</summary>
    [RollsFor(nameof(SpellDamage))]
    public string? SpellDamageRoll
    {
        get => _spellDamage.Text;
        set => _spellDamage.Text = value;
    }

    /// <summary>Hit points the spell gives back to its caster; 0 for a spell that only hurts.</summary>
    public float SpellHeal { get; set; }

    /// <summary>Dice rolled for each cast's healing in place of <see cref="SpellHeal"/>, e.g. "2d6+2".</summary>
    [RollsFor(nameof(SpellHeal))]
    public string? SpellHealRoll
    {
        get => _spellHeal.Text;
        set => _spellHeal.Text = value;
    }

    /// <summary>The item it carries, e.g. "Potion", said when it's used; empty for none. Read as the "Item" output.</summary>
    [Group("Item")]
    [Output]
    public string? Item { get; set; }

    /// <summary>How many of its <see cref="Item"/> it has left; each use takes one.</summary>
    [Output]
    public int Items { get; set; }

    /// <summary>Hit points one gives back to it.</summary>
    public float ItemHeal { get; set; }

    /// <summary>Dice rolled for each use's healing in place of <see cref="ItemHeal"/>, e.g. "2d4+4".</summary>
    [RollsFor(nameof(ItemHeal))]
    public string? ItemHealRoll
    {
        get => _itemHeal.Text;
        set => _itemHeal.Text = value;
    }

    /// <summary>Mana one gives back to it.</summary>
    public float ItemMana { get; set; }

    /// <summary>Hit points one takes from the foe (armor or not), as a bomb does.</summary>
    public float ItemDamage { get; set; }

    /// <summary>Dice rolled for each use's damage in place of <see cref="ItemDamage"/>, e.g. "3d6".</summary>
    [RollsFor(nameof(ItemDamage))]
    public string? ItemDamageRoll
    {
        get => _itemDamage.Text;
        set => _itemDamage.Text = value;
    }

    /// <summary>
    /// For the computer: the share of its health below which it heals (with an item, or a spell that heals), e.g. 0.4 for
    /// under 40%.
    /// </summary>
    [Group("Computer")]
    public float HealBelow { get; set; } = 0.4f;

    /// <summary>For the computer: seconds it waits on its turn before it moves, so the player sees it coming. 0 moves at once.</summary>
    public float ThinkTime { get; set; } = 0.6f;

    /// <summary>Whether it takes the player's keys (<see cref="AttackAction"/>, <see cref="SpellAction"/>, <see cref="ItemAction"/>). Its actions are done either way.</summary>
    [Group("Control")]
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>The input action that attacks on its turn, e.g. "Attack" (X); empty for none.</summary>
    [Names(NameKind.Action)]
    public string? AttackAction { get; set; } = "Attack";

    /// <summary>The input action that casts its spell on its turn; empty for none (buttons do it).</summary>
    [Names(NameKind.Action)]
    public string? SpellAction { get; set; }

    /// <summary>The input action that uses an item on its turn; empty for none (buttons do it).</summary>
    [Names(NameKind.Action)]
    public string? ItemAction { get; set; }

    /// <summary>Played as it attacks, e.g. "Sounds/swing.wav".</summary>
    [Group("Feel")]
    public string? AttackSound { get; set; }

    /// <summary>Played as it casts.</summary>
    public string? SpellSound { get; set; }

    /// <summary>Played as it uses an item.</summary>
    public string? ItemSound { get; set; }

    /// <summary>Played when a move can't be made: not enough mana, nothing left to use.</summary>
    public string? FailSound { get; set; }

    /// <summary>A prefab spawned on what each move lands on, e.g. "Sparkle".</summary>
    [Names(NameKind.Prefab)]
    public string? Effect { get; set; }

    /// <summary>What it last did, e.g. "Elf Mage casts Fireball on Ogre: 14 damage." The game sets it. Read as the "Said" output.</summary>
    [Group("State")]
    [Output]
    public string? Said { get; set; }

    /// <summary>True while it can move: it's its turn (if it takes turns) and it hasn't moved yet. The game keeps it up to date.</summary>
    [DoNotSerialize]
    public bool CanAct { get; internal set; }

    /// <summary>Attacks its foe on its turn, as its Attack Action does.</summary>
    [Action]
    public void Attack() => Asked = BattleMove.Attack;

    /// <summary>Casts its spell on its turn, if it has the mana.</summary>
    [Action]
    public void CastSpell() => Asked = BattleMove.Spell;

    /// <summary>Uses one of its items on its turn, if it has any left.</summary>
    [Action]
    public void UseItem() => Asked = BattleMove.Item;

    /// <summary>Raised as it makes a move, with what it said.</summary>
    public event Action<Battler, string>? Moved;

    /// <summary>The name it goes by in what it says: its <see cref="Title"/>, or its entity's name.</summary>
    public string Called => !string.IsNullOrWhiteSpace(Title) ? Title.Trim() : Entity?.Name ?? "";

    /// <summary>True if it has a spell and the mana for it.</summary>
    public bool CanCast => !string.IsNullOrWhiteSpace(Spell) && Mana >= SpellCost && (SpellDamage > 0 || SpellHeal > 0);

    /// <summary>True if it has an item left to use.</summary>
    public bool CanUseItem => Items > 0 && (ItemHeal > 0 || ItemMana > 0 || ItemDamage > 0);

    bool IActionReady.IsReady(string action) => action switch
    {
        nameof(Attack) => CanAct,
        nameof(CastSpell) => CanAct && CanCast,
        nameof(UseItem) => CanAct && CanUseItem,
        _ => true,
    };

    string IPlaytestReport.Report()
    {
        // Its health is its Health's to say.
        var text = MaxMana > 0 ? string.Create(CultureInfo.InvariantCulture, $", mana {Mana:0.#}/{MaxMana:0.#}") : "";
        if (!string.IsNullOrWhiteSpace(Item))
            text += $", {Items} {Item}";
        return CanAct ? text + ", can act" : text;
    }

    internal float RollDamage() => _damage.Roll(Damage);

    internal float RollSpellDamage() => _spellDamage.Roll(SpellDamage);

    internal float RollSpellHeal() => _spellHeal.Roll(SpellHeal);

    internal float RollItemHeal() => _itemHeal.Roll(ItemHeal);

    internal float RollItemDamage() => _itemDamage.Roll(ItemDamage);

    // What each move does on average, for the computer to weigh them.
    internal float AverageDamage => Average(DamageRoll, Damage);

    internal float AverageSpellDamage => Average(SpellDamageRoll, SpellDamage);

    internal float AverageItemDamage => Average(ItemDamageRoll, ItemDamage);

    private static float Average(string? dice, float otherwise) => Dice.TryParse(dice, out var parsed) ? (float)parsed.Average : otherwise;

    // Mana comes back as each of its turns starts, and the computer thinks afresh.
    void ITurnReady.TurnBegins()
    {
        if (ManaPerTurn > 0)
            Mana = Math.Min(MaxMana, Mana + ManaPerTurn);
        ThinkLeft = -1;
    }

    internal void RaiseMoved(string said) => Moved?.Invoke(this, said);

    // A move asked for (an action, a key, a button), carried out where the game is run at its next step. Not saved.
    internal BattleMove Asked { get; set; }

    // The computer's wait before it moves this turn; counted down by the game, set as each of its turns starts.
    internal float ThinkLeft { get; set; } = -1;

    private float _maxMana;
    private DiceRoll _damage;
    private DiceRoll _spellDamage;
    private DiceRoll _spellHeal;
    private DiceRoll _itemHeal;
    private DiceRoll _itemDamage;
}

/// <summary>A move a <see cref="Battler"/> makes on its turn.</summary>
public enum BattleMove
{
    /// <summary>None asked for.</summary>
    None,

    /// <summary>An attack on its foe.</summary>
    Attack,

    /// <summary>Its spell.</summary>
    Spell,

    /// <summary>One of its items.</summary>
    Item,
}
