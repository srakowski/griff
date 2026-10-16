using System.Globalization;
using Griff.Scenes;

namespace Griff.Modules.VisualNovels;

/// <summary>
/// Marks a list of strings as a stage's positions, "Left: -4.5" or "FarLeft: -7, -0.5" (a <see cref="Stage"/>'s Positions):
/// the Inspector lists them a row each, the name typed and how far across and up set.
/// </summary>
public sealed class PositionsAttribute : RowsAttribute
{
    public override IReadOnlyList<RowColumn> Columns { get; } =
    [
        new("Name", CellKind.Text) { Default = "Spot", Tip = "One word, as tags name it: [show Ava left]" },
        new("Across", CellKind.Number) { Default = "0", Tip = "How far right of the stage's middle (left is less than 0)" },
        new("Up", CellKind.Number) { Default = "0", Tip = "How far up from the stage's middle" },
    ];

    public override string Noun => "a position";

    public override string Example => "Left: -4.5";

    public override string Describe() =>
        "Positions, one per row: a one-word name and how far across from the stage's middle, and after a comma how far up, e.g. \"Left: -4.5\" or \"FarLeft: -7, -0.5\". The Inspector lists them a row each with the numbers set.";

    public override IReadOnlyList<string>? Read(string row) =>
        StageLines.Spots([row]) is [var spot] ? [spot.Name, Number(spot.At.X), Number(spot.At.Y)] : null;

    public override string Write(IReadOnlyList<string> cells)
    {
        var name = cells[0].Trim().Replace(" ", "", StringComparison.Ordinal);
        var up = double.TryParse(cells[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ? y : 0;
        return up == 0 ? $"{name}: {cells[1].Trim()}" : $"{name}: {cells[1].Trim()}, {Number(up)}";
    }
}

/// <summary>
/// Marks a list of strings as a stage's backgrounds, "Park: Backgrounds/park.png", "Night: #10183A" or "Dusk: #F2A65A
/// #3A2F5B" (a <see cref="Stage"/>'s Backgrounds): the Inspector lists them a row each, the name typed and a picture picked,
/// or a color, or two running from the top to the bottom.
/// </summary>
public sealed class BackgroundsAttribute : RowsAttribute
{
    public override IReadOnlyList<RowColumn> Columns { get; } =
    [
        new("Name", CellKind.Text) { Default = "Scene", Tip = "As tags name it: [background Park]" },
        new("Top", CellKind.Color) { Under = true, Default = "#8EC5FF", Optional = true, Tip = "Its color at the top (without a picture)" },
        new("Bottom", CellKind.Color) { Under = true, Default = "#E8F4FF", Optional = true, Tip = "Its color at the bottom: the same as the top for one color" },
        new("Picture", CellKind.Image) { Wide = true, Optional = true, Tip = "A picture in Assets, which shows instead of the colors" },
    ];

    public override string Noun => "a background";

    public override string Example => "Park: Backgrounds/park.png";

    public override string Describe() =>
        "Backgrounds, one per row: a name and a picture (\"Park: Backgrounds/park.png\"), a color (\"Night: #10183A\") or a color at the top running to another at the bottom (\"Dusk: #F2A65A #3A2F5B\"). The Inspector lists them a row each with the picture picked from the project's or the colors shown.";

    public override IReadOnlyList<string>? Read(string row)
    {
        if (StageLines.Looks([row]) is not [var look])
            return null;
        if (look.Image is { } image)
            return [look.Name, "", "", image];
        var words = StageLines.Entry(row)!.Value.Value.Split((char[])[' ', ','], StringSplitOptions.RemoveEmptyEntries);
        return [look.Name, words[0], words.Length > 1 ? words[1] : words[0], ""];
    }

    public override string Write(IReadOnlyList<string> cells)
    {
        var name = cells[0].Trim();
        if (cells[3].Trim().Length > 0)
            return $"{name}: {cells[3].Trim()}";
        var (top, bottom) = (cells[1].Trim(), cells[2].Trim());
        if (top.Length == 0)
            top = bottom.Length > 0 ? bottom : "#000000";
        return bottom.Length == 0 || bottom.Equals(top, StringComparison.OrdinalIgnoreCase) ? $"{name}: {top}" : $"{name}: {top} {bottom}";
    }
}

/// <summary>
/// Marks a list of strings as a character's expressions, "Happy: Characters/ava-happy.png" or a clip of their animation,
/// "Laugh: Laugh" (a <see cref="Character"/>'s Expressions): the Inspector lists them a row each, the name typed and the picture
/// or clip picked.
/// </summary>
public sealed class ExpressionsAttribute : RowsAttribute
{
    public override IReadOnlyList<RowColumn> Columns { get; } =
    [
        new("Name", CellKind.Text) { Default = "Happy", Tip = "One word, as lines and tags name it: Ava (happy): Hi!" },
        new("Shows", CellKind.Image) { Wide = true, Tip = "A picture in Assets for their sprite, or a clip of their animation" },
    ];

    public override string Noun => "an expression";

    public override string Example => "Happy: Characters/ava-happy.png";

    public override string Describe() =>
        "Expressions, one per row: a one-word name and a picture for their sprite (\"Happy: Characters/ava-happy.png\") or the name of a clip their animation plays (\"Laugh: Laugh\"). The Inspector lists them a row each with the picture or clip picked.";

    public override IReadOnlyList<string>? Read(string row) =>
        StageLines.Entry(row) is { Value.Length: > 0 } entry ? [entry.Name, entry.Value] : null;

    public override string Write(IReadOnlyList<string> cells) => $"{cells[0].Trim().Replace(" ", "", StringComparison.Ordinal)}: {cells[1].Trim()}";

    // Their animation's clips are offered with the pictures.
    public override IReadOnlyList<string> MoreChoices(int column, Component component, IProjectNames? names) =>
        column == 1 && component.Entity?.GetComponent<Animation>() is { } animation ? animation.ClipNames : [];
}
