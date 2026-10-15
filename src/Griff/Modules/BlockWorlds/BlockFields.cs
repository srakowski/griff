using System.Globalization;
using Griff.Scenes;

namespace Griff.Modules.BlockWorlds;

/// <summary>
/// Marks a list of strings as items and the tile each puts down, "Dirt 1" (a <see cref="Blocks"/>' Items): the Inspector lists
/// them a row each, the item named and its tile picked off the tileset.
/// </summary>
public sealed class BlockItemsAttribute : RowsAttribute
{
    public override IReadOnlyList<RowColumn> Columns { get; } =
    [
        new("Item", CellKind.Name) { Names = new NamesAttribute(NameKind.Item) { Defines = true }, Tip = "The item's name, as the inventory and hotbar show it" },
        new("Tile", CellKind.Tile) { Default = "0", Tip = "The tile it puts down, and that gives it when mined" },
    ];

    public override string Noun => "an item and its tile";

    public override string Example => "Dirt 1";

    public override string Describe() =>
        "Blocks to carry and put down, one per row: the item's name and its tile, e.g. \"Dirt 1\". The Inspector lists them a row each with the tile picked off the tileset.";

    public override IReadOnlyList<string>? Read(string row)
    {
        var text = row.Trim();
        var space = text.LastIndexOf(' ');
        return space > 0 && int.TryParse(text[(space + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tile) && tile >= 0
            ? [text[..space].Trim(), tile.ToString(CultureInfo.InvariantCulture)]
            : null;
    }

    public override string Write(IReadOnlyList<string> cells) => $"{cells[0].Trim()} {cells[1].Trim()}";
}

/// <summary>
/// Marks a string as tiles and what each gives when mined, "0 Dirt, 6 nothing" (a <see cref="Blocks"/>' Drops): the Inspector
/// lists them a row each, the tile picked off the tileset and the item picked.
/// </summary>
public sealed class TileDropsAttribute : RowsAttribute
{
    public override IReadOnlyList<RowColumn> Columns { get; } =
    [
        new("Tile", CellKind.Tile) { Default = "0", Tip = "The tile mined" },
        new("Gives", CellKind.Name) { Names = new NamesAttribute(NameKind.Item) { Defines = true }, Choices = ["nothing"], Tip = "The item it gives, or nothing" },
    ];

    public override string Noun => "a tile and what it gives";

    public override string Example => "6 Sapling";

    public override string Describe() =>
        "Tiles that give something else when mined, separated by commas: a tile and an item, e.g. \"0 Dirt, 6 Sapling\" (\"6 nothing\" gives nothing). The Inspector lists them a row each with the tile picked off the tileset.";

    public override IReadOnlyList<string>? Read(string row)
    {
        var text = row.Trim();
        var space = text.IndexOf(' ');
        return space > 0 && int.TryParse(text[..space], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tile) && tile >= 0 && text[(space + 1)..].Trim().Length > 0
            ? [tile.ToString(CultureInfo.InvariantCulture), text[(space + 1)..].Trim()]
            : null;
    }

    public override string Write(IReadOnlyList<string> cells) => $"{cells[0].Trim()} {cells[1].Trim()}";
}

/// <summary>
/// Marks a string as tiles (a tile or a range) each with a number, "2 0.8, 3-4 1.2" or "3 6%" (a <see cref="Blocks"/>'
/// Hardness, a <see cref="WorldGenerator"/>'s Ores): the Inspector lists them a row each, the tile picked off the tileset and
/// the number set.
/// </summary>
/// <param name="what">What the number is: "Seconds", "Share".</param>
/// <param name="unit">What the number is written with after it: "%", or nothing.</param>
public sealed class TileAmountsAttribute(string what, string unit = "") : RowsAttribute
{
    /// <summary>What the number is.</summary>
    public string What { get; } = what;

    /// <summary>What it's written with after it.</summary>
    public string Unit { get; } = unit;

    public override IReadOnlyList<RowColumn> Columns => _columns ??=
    [
        new("Tile", CellKind.Tile) { Default = "0", Tip = "A tile, or a range of them like 3-4" },
        new(What, CellKind.Number) { Default = Unit == "%" ? "5" : "1", Unit = Unit.Length > 0 ? Unit : null },
    ];

    private IReadOnlyList<RowColumn>? _columns;

    public override string Noun => $"a tile and its {What.ToLowerInvariant()}";

    public override string Example => $"3 {(Unit == "%" ? "6%" : "0.8")}";

    public override string Describe() =>
        $"Tiles and their {What.ToLowerInvariant()}, separated by commas: a tile (or a range, 3-4) and a number{(Unit.Length > 0 ? $" with {Unit}" : "")}, e.g. \"{Example}\". The Inspector lists them a row each with the tile picked off the tileset.";

    public override IReadOnlyList<string>? Read(string row)
    {
        var words = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 2 || Tilemap.ParseTileSet(words[0]).Length == 0 ||
            !double.TryParse(words[1].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            return null;
        return [words[0], Number(amount)];
    }

    public override string Write(IReadOnlyList<string> cells) => $"{cells[0].Trim()} {cells[1].Trim()}{Unit}";
}

/// <summary>
/// Marks a list of strings as recipes, "Torch 4 = Wood + Coal @ Workbench" (a <see cref="Crafting"/>'s Recipes): the
/// Inspector lists them a row each, what it makes and how many, what it takes picked item by item, and where.
/// </summary>
public sealed class RecipesAttribute : RowsAttribute
{
    public override IReadOnlyList<RowColumn> Columns { get; } =
    [
        new("Makes", CellKind.Name) { Names = new NamesAttribute(NameKind.Item) { Defines = true }, Tip = "What it makes" },
        new("Count", CellKind.Whole) { Default = "1", Tip = "How many it makes" },
        new("At", CellKind.Name) { Names = new NamesAttribute(NameKind.Item), Loose = true, Optional = true, Tip = "A workbench it needs nearby (a placed block of that item, or an entity of that name); empty for none" },
        new("Takes", CellKind.Amounts) { Names = new NamesAttribute(NameKind.Item), Wide = true, Tip = "What it takes from the inventory" },
    ];

    public override string Noun => "a recipe";

    public override string Example => "Torch 4 = Wood + Coal";

    public override string Describe() =>
        "One recipe per row: what it makes (and how many), \"=\", what it takes joined with \"+\", and \"@\" a workbench it needs nearby, e.g. \"Planks 4 = Wood\" or \"Furnace = Stone 8 @ Workbench\". The Inspector lists them a row each, the items picked from what the game gives.";

    public override IReadOnlyList<string>? Read(string row) => Recipe.Parse(row) is { } recipe
        ? [recipe.Item, recipe.Count.ToString(CultureInfo.InvariantCulture), recipe.Station ?? "", Amounts.Write(recipe.Needs)]
        : null;

    public override string Write(IReadOnlyList<string> cells)
    {
        var count = int.TryParse(cells[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Max(1, n) : 1;
        var makes = count == 1 ? cells[0].Trim() : string.Create(CultureInfo.InvariantCulture, $"{cells[0].Trim()} {count}");
        return $"{makes} = {Amounts.Write(Amounts.Read(cells[3]))}{(cells[2].Trim().Length > 0 ? " @ " + cells[2].Trim() : "")}";
    }

    public override IReadOnlyList<string> NewRow() => ["", "1", "", ""];
}

/// <summary>
/// Marks a list of strings as items and their colors, "Dirt #8B5A2B" (a <see cref="Hotbar"/>'s Colors): the Inspector lists
/// them a row each, the item picked and the color shown.
/// </summary>
public sealed class ItemColorsAttribute : RowsAttribute
{
    public override IReadOnlyList<RowColumn> Columns { get; } =
    [
        new("Item", CellKind.Name) { Names = new NamesAttribute(NameKind.Item) },
        new("Color", CellKind.Color) { Default = "#8B5A2B" },
    ];

    public override string Noun => "an item and its color";

    public override string Example => "Dirt #8B5A2B";

    public override string Describe() =>
        "Items' colors, one per row: a name and a hex color, e.g. \"Dirt #8B5A2B\". The Inspector lists them a row each with the color shown.";

    public override IReadOnlyList<string>? Read(string row)
    {
        var text = row.Trim();
        var space = text.LastIndexOf(' ');
        return space > 0 && Color.TryParse(text[(space + 1)..], out _) ? [text[..space].Trim(), text[(space + 1)..]] : null;
    }

    public override string Write(IReadOnlyList<string> cells) => $"{cells[0].Trim()} {cells[1].Trim()}";
}
