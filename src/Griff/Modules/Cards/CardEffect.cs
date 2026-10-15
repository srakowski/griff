using System.Globalization;
using System.Text;
using Griff.Data;
using Griff.Scenes;

namespace Griff.Modules.Cards;

/// <summary>
/// What a card does, with no code: as it's played (or drawn, discarded, or each turn while it's on the table) it deals
/// <see cref="Damage"/>, gives back <see cref="Heal"/>, has you <see cref="Draw"/> cards, has its target's side
/// <see cref="Discard"/>, gives you <see cref="GainMana"/>, raises a number (<see cref="Buff"/>) or does any action
/// (<see cref="Do"/>), like a <see cref="Trigger"/>, so a card can drive another part of the game. It does it to its
/// <see cref="Target"/>: the opponent, yourself, a card or unit the player chooses, or all of them. Give a card one for
/// each thing it does; with no Text of its own, a card writes what its effects do on its face.
/// </summary>
/// <remarks>
/// Its numbers can come from a table: a deck made from a table sets a column called Damage, Heal, Draw, Discard, Target
/// or Cost on each card (dice in a column roll for each play), so cards from a table carry their effects and costs. A
/// target's life is its <see cref="Health"/>: a side's hero (see <see cref="Hand.Hero"/>), any character with a Health,
/// or a card on the table with one (a creature, which goes to the discard pile when it dies). The computer weighs effects
/// to choose its plays (see <see cref="Hand"/>). Effects happen where the game is run, with the game's random numbers, so
/// a seeded game plays the same, and what they change is saved with the scene.
/// </remarks>
[Category("Cards")]
[Requires(typeof(Card), Note = "the card it's part of")]
[WorksWith(typeof(Health), Note = "what its damage and healing land on (a hero's, or the card's own)")]
[WorksWith(typeof(TableValues), Note = "its numbers from a table's columns")]
[ComponentName("Griff.CardEffect")]
public sealed class CardEffect : Component, ICardPart
{
    /// <summary>When it happens: as the card is played (the usual), drawn or discarded, or as each of its side's turns starts while it's on the table.</summary>
    public CardWhen When { get; set; }

    /// <summary>
    /// Whom it's done to: the <see cref="CardTarget.Opponent"/>'s hero, your own (<see cref="CardTarget.Self"/>), a card or unit
    /// the player picks as they play it (<see cref="CardTarget.Chosen"/>), or every foe, friend or both.
    /// </summary>
    public CardTarget Target { get; set; }

    /// <summary>
    /// The entities a chosen target may be, by name, separated by commas, or a place they're inside, e.g. "Enemy Row":
    /// only the cards on it can be chosen. Empty: any card on the table or character with a Health.
    /// </summary>
    [EntityName(Many = true)]
    [ShownWhen(nameof(Target), Is = [CardTarget.Chosen])]
    public string? ChooseFrom { get; set; }

    /// <summary>Hit points it takes from its target (its Health), each target once.</summary>
    [Group("Effect")]
    public float Damage { get; set; }

    /// <summary>Dice rolled each time in place of <see cref="Damage"/>, e.g. "1d6+1"; empty deals Damage every time.</summary>
    [RollsFor(nameof(Damage))]
    [Dice]
    public string? DamageRoll
    {
        get => _damage.Text;
        set => _damage.Text = value;
    }

    /// <summary>Hit points it gives back to its target, never past its Max.</summary>
    public float Heal { get; set; }

    /// <summary>Dice rolled each time in place of <see cref="Heal"/>, e.g. "2d4".</summary>
    [RollsFor(nameof(Heal))]
    [Dice]
    public string? HealRoll
    {
        get => _heal.Text;
        set => _heal.Text = value;
    }

    /// <summary>Cards the player who played it draws, one after another.</summary>
    public int Draw { get; set; }

    /// <summary>Cards its target's side discards from their hand, picked at random (the game's random numbers).</summary>
    public int Discard { get; set; }

    /// <summary>Mana the player who played it gains: a hand's Mana, for more plays this turn.</summary>
    public int GainMana { get; set; }

    /// <summary>How much it adds to a number of its target's (<see cref="BuffStat"/>), for good: +2 to a creature's Value. Less than 0 weakens.</summary>
    public float Buff { get; set; }

    /// <summary>
    /// The number <see cref="Buff"/> raises: a property of its target's components, e.g. "Value" (a card's), "Armor" or
    /// "Health.Max" (one component's).
    /// </summary>
    [ShownWhen(nameof(Buff))]
    [Stat]
    public string? BuffStat { get; set; } = "Value";

    /// <summary>
    /// An action it does too, as a Trigger does, e.g. "Open", "Spawn" or "Damage": on <see cref="DoOn"/>, or on its target
    /// without any. A card can drive anything in the scene this way: a door in the level, a spawner, a script's action.
    /// </summary>
    [Group("Action")]
    [ActionName(On = nameof(DoOn))]
    public string? Do { get; set; }

    /// <summary>The entities to do it on, by name, separated by commas. Empty does it on its target.</summary>
    [EntityName(Many = true)]
    public string? DoOn { get; set; }

    /// <summary>What to do it with, for an action that takes a value: how much, a name, or true or false. Empty uses the action's own default.</summary>
    [ActionValue(nameof(Do))]
    public string? With { get; set; }

    /// <summary>A sound played as it happens, e.g. "Sounds/zap.wav".</summary>
    [Group("Feel")]
    public string? Sound { get; set; }

    /// <summary>A prefab spawned on each target as it happens, e.g. "Sparkle".</summary>
    [Names(NameKind.Prefab)]
    public string? Effect { get; set; }

    /// <summary>True if it takes a target the player chooses as the card is played.</summary>
    public bool Chooses => Target == CardTarget.Chosen && When == CardWhen.Played;

    /// <summary>What it does, in words, e.g. "Deal 3 damage." or "Each turn: heal 1."; a card with no Text shows it.</summary>
    public string Describe()
    {
        var key = (When, Target, Damage, DamageRoll, Heal, HealRoll, Draw, Discard, GainMana, Buff, BuffStat, Do, With);
        if (_described is { } kept && key == _describedFor)
            return kept;
        _describedFor = key;
        return _described = Words();
    }

    // Effects happen where the game runs (see CardsSystem); as a card part it has no say in whether the card can be played.
    bool ICardPart.CanPlay(Card card, PlayZone zone) => true;

    internal float RollDamage() => _damage.Roll(Damage);

    internal float RollHeal() => _heal.Roll(Heal);

    // What it does on average, for the computer to weigh it.
    internal float AverageDamage => Average(DamageRoll, Damage);

    internal float AverageHeal => Average(HealRoll, Heal);

    // It does something to the cards or its target other than damage and healing.
    internal bool DoesMore => Draw > 0 || Discard > 0 || GainMana > 0 || (Buff != 0 && !string.IsNullOrWhiteSpace(BuffStat)) || !string.IsNullOrWhiteSpace(Do);

    private static float Average(string? dice, float otherwise) => Dice.TryParse(dice, out var parsed) ? (float)parsed.Average : otherwise;

    private string Words()
    {
        var parts = new List<string>();
        var damage = Amount(DamageRoll, Damage);
        var heal = Amount(HealRoll, Heal);
        if (damage is not null)
            parts.Add($"deal {damage} damage{To(Target, "")}");
        if (heal is not null)
            parts.Add(Target == CardTarget.Self ? $"heal {heal}" : $"heal{To(Target, "")} for {heal}");
        if (Buff != 0 && !string.IsNullOrWhiteSpace(BuffStat))
            parts.Add($"{(Buff > 0 ? "+" : "")}{Number(Buff)} {BuffStat.Trim()}{To(Target, " to yourself")}");
        if (Draw > 0)
            parts.Add($"draw {Cards(Draw)}");
        if (Discard > 0)
        {
            parts.Add(Target switch
            {
                CardTarget.Self => $"discard {Cards(Discard)}",
                CardTarget.Opponent => $"your opponent discards {Cards(Discard)}",
                CardTarget.AllFoes => $"your foes discard {Cards(Discard)}",
                CardTarget.AllFriends => $"your side discards {Cards(Discard)}",
                CardTarget.Everyone => $"everyone discards {Cards(Discard)}",
                _ => $"its side discards {Cards(Discard)}",
            });
        }

        if (GainMana > 0)
            parts.Add($"gain {GainMana} mana");
        if (!string.IsNullOrWhiteSpace(Do))
        {
            var with = string.IsNullOrWhiteSpace(With) ? "" : " " + With.Trim();
            var on = !string.IsNullOrWhiteSpace(DoOn) ? $" {DoOn.Trim()}" : Target == CardTarget.Self ? "" : To(Target, "").Replace(" to ", " ", StringComparison.Ordinal);
            parts.Add($"{Do.Trim()}{with}{on}");
        }

        if (parts.Count == 0)
            return "";
        var text = new StringBuilder();
        text.Append(When switch
        {
            CardWhen.Drawn => "When drawn: ",
            CardWhen.Discarded => "When discarded: ",
            CardWhen.EachTurn => "Each turn: ",
            _ => "",
        });
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            text.Append(i == 0 && text.Length == 0 ? char.ToUpperInvariant(part[0]) + part[1..] : part);
            text.Append(i < parts.Count - 2 ? ", " : i == parts.Count - 2 ? " and " : ".");
        }

        return text.ToString();

        static string Cards(int count) => count == 1 ? "a card" : $"{count} cards";
    }

    // " to a target", " to all foes"..., or `self` for the player's own.
    private static string To(CardTarget target, string self) => target switch
    {
        CardTarget.Self => self,
        CardTarget.Chosen => " to a target",
        CardTarget.AllFoes => " to all foes",
        CardTarget.AllFriends => " to all friends",
        CardTarget.Everyone => " to everyone",
        _ => "",
    };

    // "3", "1d6+1", or null for none.
    private static string? Amount(string? dice, float value) =>
        Dice.TryParse(dice, out _) ? dice!.Trim() : value > 0 ? Number(value) : null;

    private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private DiceRoll _damage;
    private DiceRoll _heal;
    private string? _described;
    private (CardWhen, CardTarget, float, string?, float, string?, int, int, int, float, string?, string?, string?) _describedFor;
}

/// <summary>When a <see cref="CardEffect"/> happens.</summary>
public enum CardWhen
{
    /// <summary>As the card is played onto a play zone.</summary>
    Played,

    /// <summary>As it's drawn into a hand.</summary>
    Drawn,

    /// <summary>As it's put on a discard pile.</summary>
    Discarded,

    /// <summary>As each of its side's turns starts, while it's on the table: a creature that strikes every turn.</summary>
    EachTurn,
}

/// <summary>Whom a <see cref="CardEffect"/> is done to.</summary>
public enum CardTarget
{
    /// <summary>The opponent: the other side's hero (see Hand.Hero), and their hand for a discard.</summary>
    Opponent,

    /// <summary>The player who played it: their own hero, and their own hand.</summary>
    Self,

    /// <summary>A card on the table or a character with a Health that the player picks as they play it (the computer picks its own).</summary>
    Chosen,

    /// <summary>Every foe: the other sides' heroes, characters and cards on the table.</summary>
    AllFoes,

    /// <summary>Every friend: its own side's hero, characters and cards on the table.</summary>
    AllFriends,

    /// <summary>Everyone, both sides.</summary>
    Everyone,
}
