using Griff.Data;

namespace Griff.Modules.Cards;

// The Cards template's card set, one table: a row for each card, what it costs, what it does and how many go in the deck.
// Creatures (the Creature prefab, with a Health) stay on the table and act as each of their side's turns starts; spells
// (the Spell prefab) do what they do as they're played and go to the discard pile. A Card Effect on each prefab takes the
// Damage, Heal, Draw, Gain Mana and Target columns, the card its Title, Kind, Cost, Color and Image, and a creature its
// Health. Balancing the game is changing a cell.
internal static class CardsTables
{
    public const string Cards = "Cards";

    /// <summary>The creatures, cheapest first.</summary>
    public static readonly string[] Creatures = ["Slime", "Goblin", "Rogue", "Skeleton", "Cleric", "Knight", "Orc", "Wyvern"];

    /// <summary>The spells, cheapest first.</summary>
    public static readonly string[] Spells = ["Mana Coin", "Spark", "Healing Potion", "Insight", "Chain Lightning", "Fireball", "Quake", "Meteor"];

    internal static Dictionary<string, Table> All() => new()
    {
        [$"Tables/{Cards}.table"] = TableFile.Read(CardsJson(), Cards),
    };

    private const string Hit = "Sounds/hit.wav";
    private const string Swing = "Sounds/swing.wav";
    private const string Explode = "Sounds/explode.wav";
    private const string Zap = "Sounds/shoot.wav";
    private const string Heal = "Sounds/powerup.wav";
    private const string Coin = "Sounds/coin.wav";

    private static string CardsJson()
    {
        // Name, cost, health, damage, heal, draw, mana, target, color, effect, sound, count.
        (string Name, int Cost, int Health, string? Damage, string? Heal, int Draw, int Mana, string Target, string Color, string Effect, string Sound, int Count)[] creatures =
        [
            ("Slime", 1, 2, "1", null, 0, 0, "Opponent", "#E9DDC4", "Hit", Swing, 3),
            ("Goblin", 2, 2, "2", null, 0, 0, "Opponent", "#E9DDC4", "Hit", Swing, 3),
            ("Rogue", 3, 2, "3", null, 0, 0, "Opponent", "#E9DDC4", "Hit", Swing, 2),
            ("Skeleton", 3, 4, "2", null, 0, 0, "Opponent", "#E9DDC4", "Hit", Swing, 2),
            ("Cleric", 3, 3, null, "2", 0, 0, "Self", "#E9DDC4", "Mend", Heal, 2),
            ("Knight", 4, 7, "1", null, 0, 0, "Opponent", "#E9DDC4", "Hit", Swing, 2),
            ("Orc", 5, 6, "3", null, 0, 0, "Opponent", "#E9DDC4", "Hit", Swing, 2),
            ("Wyvern", 7, 8, "3", null, 0, 0, "AllFoes", "#E9DDC4", "Blast", Explode, 1),
        ];
        (string Name, int Cost, int Health, string? Damage, string? Heal, int Draw, int Mana, string Target, string Color, string Effect, string Sound, int Count)[] spells =
        [
            ("Mana Coin", 0, 0, null, null, 0, 2, "Self", "#D3E2F7", "Shine", Coin, 2),
            ("Spark", 1, 0, "2", null, 0, 0, "Chosen", "#F6D7C3", "Hit", Hit, 4),
            ("Healing Potion", 2, 0, null, "5", 0, 0, "Self", "#D6EFD8", "Mend", Heal, 2),
            ("Insight", 2, 0, null, null, 2, 0, "Self", "#E6DAF5", "Shine", Coin, 2),
            ("Chain Lightning", 2, 0, "1", null, 0, 0, "AllFoes", "#D3E2F7", "Hit", Zap, 2),
            ("Fireball", 4, 0, "1d4+3", null, 0, 0, "Chosen", "#F6D7C3", "Blast", Explode, 2),
            ("Quake", 5, 0, "3", null, 0, 0, "AllFoes", "#EADBC4", "Blast", Explode, 1),
            ("Meteor", 6, 0, "7", null, 0, 0, "Chosen", "#F6D7C3", "Blast", Explode, 1),
        ];

        // Cheapest first, creatures before spells of the same cost.
        var rows = new List<string>();
        foreach (var (kind, c) in creatures.Select(c => ("Creature", c)).Concat(spells.Select(c => ("Spell", c))).OrderBy(r => r.c.Cost))
        {
            {
                var cells = new List<string>
                {
                    $"\"Title\": \"{c.Name}\"",
                    $"\"Kind\": \"{kind}\"",
                    $"\"Prefab\": \"{kind}\"",
                    $"\"Cost\": {c.Cost}",
                };
                if (c.Health > 0)
                    cells.Add($"\"Health\": {c.Health}");
                if (c.Damage is not null)
                    cells.Add($"\"Damage\": \"{c.Damage}\"");
                if (c.Heal is not null)
                    cells.Add($"\"Heal\": \"{c.Heal}\"");
                if (c.Draw > 0)
                    cells.Add($"\"Draw\": {c.Draw}");
                if (c.Mana > 0)
                    cells.Add($"\"Gain Mana\": {c.Mana}");
                cells.Add($"\"Target\": \"{c.Target}\"");
                cells.Add($"\"Color\": \"{c.Color}\"");
                cells.Add($"\"Image\": \"{CardsArt.Picture(c.Name)}\"");
                cells.Add($"\"Effect\": \"{c.Effect}\"");
                cells.Add($"\"Sound\": \"{c.Sound}\"");
                cells.Add($"\"Count\": {c.Count}");
                rows.Add($"    \"{c.Name}\": {{ {string.Join(", ", cells)} }}");
            }
        }

        return $$"""
            {
              "columns": [
                { "name": "Title", "type": "text" },
                { "name": "Kind", "type": "choice", "choices": ["Creature", "Spell"] },
                { "name": "Prefab", "type": "prefab" },
                { "name": "Cost", "type": "whole number" },
                { "name": "Health", "type": "whole number" },
                { "name": "Damage", "type": "dice" },
                { "name": "Heal", "type": "dice" },
                { "name": "Draw", "type": "whole number" },
                { "name": "Gain Mana", "type": "whole number" },
                { "name": "Target", "type": "choice", "choices": ["Opponent", "Self", "Chosen", "AllFoes", "AllFriends", "Everyone"] },
                { "name": "Color", "type": "text" },
                { "name": "Image", "type": "sprite" },
                { "name": "Effect", "type": "prefab" },
                { "name": "Sound", "type": "sound" },
                { "name": "Count", "type": "whole number" }
              ],
              "rows": {
            {{string.Join(",\n", rows)}}
              }
            }
            """;
    }
}
