using System.Globalization;
using System.Text;
using Griff.Data;

namespace Griff.Modules.Battles;

// The RPG Stats template's game data, a table each: races, classes, items, spells, the levels (generated from curves)
// and the enemies. A class names its race, weapon, spell and item by row, and works out its health, damage, mana and
// spell from them and its level, by formulas, so changing a cell anywhere changes every fighter that reads it.
//
// Fireball gains two dice every level (its Dice Per Level is 2, where the other spells gain a quarter or a half): a slip
// in the data the Balance simulation finds, the mage weak at first and all but unbeatable from level 5 on. At 1 it falls
// in line with the other classes.
internal static class RpgStatsTables
{
    public const string Races = "Races";
    public const string Classes = "Classes";
    public const string Items = "Items";
    public const string Spells = "Spells";
    public const string Levels = "Levels";
    public const string Enemies = "Enemies";

    /// <summary>The level every class plays at in the battle (the Balance simulation sweeps it from 1 to <see cref="TopLevel"/>).</summary>
    public const int StartLevel = 3;

    /// <summary>The highest level.</summary>
    public const int TopLevel = 10;

    /// <summary>The classes, in the table's order.</summary>
    public static readonly string[] ClassNames = ["Knight", "Mage", "Rogue", "Cleric"];

    /// <summary>The enemies, weakest first.</summary>
    public static readonly string[] EnemyNames = ["Slime", "Goblin", "Skeleton", "Orc", "Wyvern"];

    internal static Dictionary<string, Table> All() => new()
    {
        [$"Tables/{Races}.table"] = TableFile.Read(RacesJson, Races),
        [$"Tables/{Items}.table"] = TableFile.Read(ItemsJson, Items),
        [$"Tables/{Spells}.table"] = TableFile.Read(SpellsJson, Spells),
        [$"Tables/{Levels}.table"] = TableFile.Read(LevelsJson(), Levels),
        [$"Tables/{Classes}.table"] = TableFile.Read(ClassesJson, Classes),
        [$"Tables/{Enemies}.table"] = TableFile.Read(EnemiesJson, Enemies),
    };

    private const string RacesJson = """
        {
          "columns": [
            { "name": "Health", "type": "number" },
            { "name": "Mana", "type": "number" },
            { "name": "Power", "type": "whole number" },
            { "name": "Armor", "type": "number" }
          ],
          "rows": {
            "Human": { "Health": 2, "Mana": 2, "Power": 1, "Armor": 0 },
            "Elf": { "Health": 0, "Mana": 6, "Power": 0, "Armor": 0 },
            "Dwarf": { "Health": 6, "Mana": 0, "Power": 1, "Armor": 1 },
            "Halfling": { "Health": 2, "Mana": 2, "Power": 0, "Armor": 1 }
          }
        }
        """;

    private const string ItemsJson = """
        {
          "columns": [
            { "name": "Kind", "type": "choice", "choices": ["Weapon", "Potion", "Ether", "Bomb"] },
            { "name": "Damage", "type": "dice" },
            { "name": "Heal", "type": "dice" },
            { "name": "Mana", "type": "number" },
            { "name": "Price", "type": "whole number" }
          ],
          "rows": {
            "Sword": { "Kind": "Weapon", "Damage": "1d8", "Price": 30 },
            "Axe": { "Kind": "Weapon", "Damage": "1d10", "Price": 35 },
            "Staff": { "Kind": "Weapon", "Damage": "1d4", "Price": 10 },
            "Daggers": { "Kind": "Weapon", "Damage": "2d4", "Price": 25 },
            "Mace": { "Kind": "Weapon", "Damage": "1d6+1", "Price": 25 },
            "Potion": { "Kind": "Potion", "Heal": "2d4+4", "Price": 15 },
            "Ether": { "Kind": "Ether", "Mana": 8, "Price": 20 },
            "Bomb": { "Kind": "Bomb", "Damage": "3d6", "Price": 30 }
          }
        }
        """;

    // A spell's dice grow with the caster's level: Dice at level 1, Dice Per Level more each level after.
    private const string SpellsJson = """
        {
          "columns": [
            { "name": "Kind", "type": "choice", "choices": ["Damage", "Heal"] },
            { "name": "Cost", "type": "number" },
            { "name": "Dice", "type": "whole number" },
            { "name": "Sides", "type": "whole number" },
            { "name": "Bonus", "type": "whole number" },
            { "name": "Dice Per Level", "type": "number" }
          ],
          "rows": {
            "Fireball": { "Kind": "Damage", "Cost": 5, "Dice": 1, "Sides": 6, "Bonus": 0, "Dice Per Level": 2 },
            "Frost Bolt": { "Kind": "Damage", "Cost": 4, "Dice": 2, "Sides": 6, "Bonus": 0, "Dice Per Level": 0.25 },
            "Poison Blade": { "Kind": "Damage", "Cost": 4, "Dice": 2, "Sides": 4, "Bonus": 1, "Dice Per Level": 0.25 },
            "Heal": { "Kind": "Heal", "Cost": 4, "Dice": 2, "Sides": 6, "Bonus": 2, "Dice Per Level": 0.5 },
            "Rally": { "Kind": "Heal", "Cost": 4, "Dice": 1, "Sides": 8, "Bonus": 2, "Dice Per Level": 0.25 }
          }
        }
        """;

    // XP to the next level and what each level adds to a class's health, damage and mana, each generated from a curve
    // (kept in the file, to change and generate again in the Tables panel).
    private static readonly (string Name, string Type, Curve Curve)[] LevelColumns =
    [
        ("XP", "whole number", new Curve { Kind = CurveKind.Exponential, First = 100, Last = 4000, Levels = TopLevel, RoundTo = 10 }),
        ("Health", "whole number", new Curve { Kind = CurveKind.Power, First = 0, Last = 40, Levels = TopLevel, Shape = 1.5, RoundTo = 1 }),
        ("Power", "whole number", new Curve { Kind = CurveKind.Linear, First = 0, Last = 4, Levels = TopLevel, RoundTo = 1 }),
        ("Mana", "whole number", new Curve { Kind = CurveKind.Linear, First = 0, Last = 9, Levels = TopLevel, RoundTo = 1 }),
    ];

    private static string LevelsJson()
    {
        static string N(double value) => TableFile.Number(value);
        var text = new StringBuilder("{\n  \"columns\": [\n");
        text.AppendJoin(",\n", LevelColumns.Select(c => string.Create(CultureInfo.InvariantCulture,
            $"    {{ \"name\": \"{c.Name}\", \"type\": \"{c.Type}\", \"curve\": {{ \"kind\": \"{Curve.KindName(c.Curve.Kind)}\", \"first\": {N(c.Curve.First)}, \"last\": {N(c.Curve.Last)}, \"levels\": {c.Curve.Levels}, \"shape\": {N(c.Curve.Shape)}, \"roundTo\": {N(c.Curve.RoundTo)} }} }}")));
        text.Append("\n  ],\n  \"rows\": {\n");
        var values = LevelColumns.Select(c => c.Curve.Values()).ToList();
        text.AppendJoin(",\n", Enumerable.Range(0, TopLevel).Select(level =>
            $"    \"{level + 1}\": {{ " + string.Join(", ", LevelColumns.Select((c, i) => $"\"{c.Name}\": {N(values[i][level])}")) + " }"));
        text.Append("\n  }\n}\n");
        return text.ToString();
    }

    // Each class: its race, weapon, spell and item (rows of the other tables), its level, and what it starts with; the
    // rest worked out by formulas from those.
    private const string ClassesJson = """
        {
          "columns": [
            { "name": "Race", "type": "row", "table": "Races" },
            { "name": "Weapon", "type": "row", "table": "Items" },
            { "name": "Spell", "type": "row", "table": "Spells" },
            { "name": "Item", "type": "row", "table": "Items" },
            { "name": "Items", "type": "whole number" },
            { "name": "Level", "type": "whole number" },
            { "name": "Base Health", "type": "number" },
            { "name": "Toughness", "type": "number" },
            { "name": "Base Mana", "type": "number" },
            { "name": "Mana Regen", "type": "number" },
            { "name": "Base Armor", "type": "number" },
            { "name": "Picture", "type": "sprite" },
            { "name": "Title", "type": "text", "formula": "=Race & \" \" & Name" },
            { "name": "Health", "type": "number", "formula": "=[Base Health] + Race.Health + Toughness * Lookup(\"Levels\", Level, \"Health\")" },
            { "name": "Damage", "type": "dice", "formula": "=Weapon.Damage + Race.Power + Lookup(\"Levels\", Level, \"Power\")" },
            { "name": "Armor", "type": "number", "formula": "=[Base Armor] + Race.Armor" },
            { "name": "Mana", "type": "number", "formula": "=[Base Mana] + Race.Mana + Lookup(\"Levels\", Level, \"Mana\")" },
            { "name": "Spell Cost", "type": "number", "formula": "=Spell.Cost" },
            { "name": "Spell Damage", "type": "dice", "formula": "=If(Spell.Kind = \"Damage\", Dice(Floor(Spell.Dice + Spell.[Dice Per Level] * (Level - 1)), Spell.Sides, Spell.Bonus), Dice(0, 1, 0))" },
            { "name": "Spell Heal", "type": "dice", "formula": "=If(Spell.Kind = \"Heal\", Dice(Floor(Spell.Dice + Spell.[Dice Per Level] * (Level - 1)), Spell.Sides, Spell.Bonus), Dice(0, 1, 0))" },
            { "name": "Item Heal", "type": "dice", "formula": "=Item.Heal" },
            { "name": "Item Damage", "type": "dice", "formula": "=Item.Damage" },
            { "name": "Item Mana", "type": "number", "formula": "=Item.Mana" },
            { "name": "XP To Next", "type": "whole number", "formula": "=Lookup(\"Levels\", Level, \"XP\")" }
          ],
          "rows": {
            "Knight": { "Race": "Dwarf", "Weapon": "Sword", "Spell": "Rally", "Item": "Potion", "Items": 2, "Level": 3, "Base Health": 20, "Toughness": 1.2, "Base Mana": 4, "Mana Regen": 1, "Base Armor": 2, "Picture": "Textures/knight.png" },
            "Mage": { "Race": "Elf", "Weapon": "Staff", "Spell": "Fireball", "Item": "Potion", "Items": 1, "Level": 3, "Base Health": 8, "Toughness": 0.7, "Base Mana": 10, "Mana Regen": 1, "Base Armor": 0, "Picture": "Textures/mage.png" },
            "Rogue": { "Race": "Halfling", "Weapon": "Daggers", "Spell": "Poison Blade", "Item": "Bomb", "Items": 2, "Level": 3, "Base Health": 14, "Toughness": 0.8, "Base Mana": 6, "Mana Regen": 1, "Base Armor": 1, "Picture": "Textures/rogue.png" },
            "Cleric": { "Race": "Human", "Weapon": "Mace", "Spell": "Heal", "Item": "Potion", "Items": 1, "Level": 3, "Base Health": 16, "Toughness": 0.9, "Base Mana": 10, "Mana Regen": 1, "Base Armor": 1, "Picture": "Textures/cleric.png" }
          }
        }
        """;

    private const string EnemiesJson = """
        {
          "columns": [
            { "name": "Health", "type": "number" },
            { "name": "Damage", "type": "dice" },
            { "name": "Armor", "type": "number" },
            { "name": "Gold", "type": "whole number" },
            { "name": "XP", "type": "whole number" },
            { "name": "Picture", "type": "sprite" },
            { "name": "Title", "type": "text", "formula": "=Name" },
            { "name": "Reward", "type": "text", "formula": "=\"+\" & Gold & \" gold, +\" & XP & \" XP\"" }
          ],
          "rows": {
            "Slime": { "Health": 12, "Damage": "1d4", "Armor": 0, "Gold": 3, "XP": 10, "Picture": "Textures/slime.png" },
            "Goblin": { "Health": 18, "Damage": "1d6+1", "Armor": 0, "Gold": 6, "XP": 20, "Picture": "Textures/goblin.png" },
            "Skeleton": { "Health": 30, "Damage": "1d8+2", "Armor": 2, "Gold": 10, "XP": 35, "Picture": "Textures/skeleton.png" },
            "Orc": { "Health": 60, "Damage": "2d6+4", "Armor": 2, "Gold": 18, "XP": 60, "Picture": "Textures/orc.png" },
            "Wyvern": { "Health": 60, "Damage": "2d8+3", "Armor": 3, "Gold": 40, "XP": 120, "Picture": "Textures/wyvern.png" }
          }
        }
        """;
}
