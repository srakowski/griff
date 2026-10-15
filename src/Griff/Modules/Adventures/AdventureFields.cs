using System.Globalization;
using Griff.Scenes;

namespace Griff.Modules.Adventures;

/// <summary>
/// Marks a list of strings as a line for each item, "Key: A rusty old key." (an <see cref="InventoryBar"/>'s Descriptions):
/// the Inspector lists them a row each, the item picked from what the game gives and the line typed.
/// </summary>
public sealed class ItemLinesAttribute : RowsAttribute
{
    public override IReadOnlyList<RowColumn> Columns { get; } =
    [
        new("Item", CellKind.Name) { Names = new NamesAttribute(NameKind.Item) },
        new("Line", CellKind.Text) { Wide = true, Tip = "What the walker says on looking at it" },
    ];

    public override string Noun => "an item and its line";

    public override string Example => "Key: A rusty old key.";

    public override string Describe() =>
        "A line for each item, one per row: the item, a colon and what's said, e.g. \"Key: A rusty old key.\". The Inspector lists them a row each, the item picked from what the game gives.";

    public override IReadOnlyList<string>? Read(string row) => Colon(row) is { } parts ? [parts.Name, parts.Text] : null;

    public override string Write(IReadOnlyList<string> cells) => $"{cells[0].Trim()}: {cells[1].Trim()}";
}

/// <summary>
/// Marks a list of strings as each item's picture, "Key: Sprites/key.png" or a frame of a sheet, "Rope: Sprites/items.png 4x2
/// 5" (an <see cref="InventoryBar"/>'s Icons): the Inspector lists them a row each, the item picked, the image picked from the
/// project's and its frame set.
/// </summary>
public sealed class ItemIconsAttribute : RowsAttribute
{
    public override IReadOnlyList<RowColumn> Columns { get; } =
    [
        new("Item", CellKind.Name) { Names = new NamesAttribute(NameKind.Item) },
        new("Picture", CellKind.Image) { Wide = true, Tip = "An image in Assets: the whole of it, or a frame of it as a sheet" },
        new("Across", CellKind.Whole) { Under = true, Default = "1", Tip = "Frames across the sheet (1 for a picture of its own)" },
        new("Down", CellKind.Whole) { Under = true, Default = "1", Tip = "Frames down the sheet" },
        new("Frame", CellKind.Whole) { Under = true, Default = "0", Tip = "Which frame, from 0, across then down" },
    ];

    public override string Noun => "an item and its picture";

    public override string Example => "Key: Sprites/key.png";

    public override string Describe() =>
        "Each item's picture, one per row: the item, a colon and an image in Assets, or a frame of a sheet with its frames across and down and the frame, e.g. \"Key: Sprites/key.png\" or \"Rope: Sprites/items.png 4x2 5\". The Inspector lists them a row each with the picture picked from the project's.";

    public override IReadOnlyList<string>? Read(string row)
    {
        if (Colon(row) is not { } parts || parts.Text.Length == 0)
            return null;
        var words = parts.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 3 && int.TryParse(words[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame) &&
            words[^2].Split('x', 'X') is [var across, var down] && IsWhole(across) && IsWhole(down))
            return [parts.Name, string.Join(' ', words[..^2]), across, down, frame.ToString(CultureInfo.InvariantCulture)];
        return [parts.Name, parts.Text, "1", "1", "0"];
    }

    public override string Write(IReadOnlyList<string> cells)
    {
        var (across, down) = (Whole(cells[2]), Whole(cells[3]));
        return across * down > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{cells[0].Trim()}: {cells[1].Trim()} {across}x{down} {Whole(cells[4])}")
            : $"{cells[0].Trim()}: {cells[1].Trim()}";
    }

    private static int Whole(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? Math.Max(0, n) : 1;
}

/// <summary>
/// Marks a list of strings as items that go together, "Grappling Hook = Rope + Hook: I tie the rope to the hook." (an
/// <see cref="InventoryBar"/>'s Combinations): the Inspector lists them a row each, the two items picked, what they make and
/// what's said.
/// </summary>
public sealed class CombinationsAttribute : RowsAttribute
{
    public override IReadOnlyList<RowColumn> Columns { get; } =
    [
        new("First", CellKind.Name) { Names = new NamesAttribute(NameKind.Item), Tip = "One of the two items put together" },
        new("Second", CellKind.Name) { Names = new NamesAttribute(NameKind.Item), Tip = "The other" },
        new("Makes", CellKind.Amounts) { Names = new NamesAttribute(NameKind.Item) { Defines = true }, Wide = true, Tip = "What they make (both are used up): nothing, an item, or several" },
        new("Line", CellKind.Text) { Wide = true, Tip = "What the walker says" },
    ];

    public override string Noun => "a combination";

    public override string Example => "Grappling Hook = Rope + Hook: I tie them.";

    public override string Describe() =>
        "Items that go together, one per row: what they make, \"=\", the two items joined with \"+\", and after a colon what's said, e.g. \"Grappling Hook = Rope + Hook: I tie the rope to the hook.\". The Inspector lists them a row each, the items picked from what the game gives.";

    public override IReadOnlyList<string>? Read(string row) => Combination.Parse(row) is { } found
        ? [found.First, found.Second, Amounts.Write(found.Makes), found.Line ?? ""]
        : null;

    public override string Write(IReadOnlyList<string> cells) =>
        $"{Amounts.Write(Amounts.Read(cells[2]))} = {cells[0].Trim()} + {cells[1].Trim()}{(cells[3].Trim().Length > 0 ? ": " + cells[3].Trim() : "")}".TrimStart();

    public override IReadOnlyList<string> NewRow() => ["", "", "", ""];
}
