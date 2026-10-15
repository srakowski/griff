using Griff.Scenes;

namespace Griff.Modules.Fighting;

/// <summary>
/// Marks a string as a fighter's buttons and the input action each is, "P Punch, K Kick" (a
/// <see cref="FightingCharacter"/>'s Buttons): the Inspector lists them a row each, the button's letter typed and the action
/// picked from the game's.
/// </summary>
public sealed class ButtonsAttribute : RowsAttribute
{
    public override IReadOnlyList<RowColumn> Columns { get; } =
    [
        new("Button", CellKind.Text) { Width = 44, Default = "P", Tip = "The letter its moves name it by, e.g. P" },
        new("Action", CellKind.Name) { Names = new NamesAttribute(NameKind.Action), Default = "Attack", Tip = "The input action that presses it" },
    ];

    public override string Noun => "a button and its action";

    public override string Example => "P Punch";

    public override string Describe() =>
        "Buttons and the input action each is, separated by commas: a letter its moves name and the action, e.g. \"P Punch, K Kick\" (up to eight). The Inspector lists them a row each with the action picked from the game's.";

    public override IReadOnlyList<string>? Read(string row) =>
        FightingCharacter.ParseButtons(row) is [var (name, action)] ? [name, action] : null;

    public override string Write(IReadOnlyList<string> cells) => $"{cells[0].Trim().ToUpperInvariant()} {cells[1].Trim()}";
}

/// <summary>
/// Marks a list of strings as a fighter's moves, a line each (see <see cref="FightingCharacter.Moves"/>): the Inspector lists
/// each with its input drawn as arrows and its frames, and opens the Move List (Entity > Edit Moves) to edit them.
/// </summary>
public sealed class MoveListAttribute : FieldEditorAttribute
{
    public override bool Fits(Type type) => type == typeof(IReadOnlyList<string>);

    public override string FitsText => "a list of strings";

    public override string Describe() =>
        "Moves, a line each: a name and a colon, the input in numpad notation (236P), then clip, frames startup/active/recovery, low or high, blockstun, chip, push, message, sound and cancels, e.g. \"Fireball: 236P frames 12/4/30\". The Inspector lists them with their inputs as arrows; the Move List (Entity > Edit Moves, the Fighting.EditMoves command) edits them.";

    public override string? Problem(Component component, object? value)
    {
        foreach (var line in value as IReadOnlyList<string> ?? [])
        {
            if (!string.IsNullOrWhiteSpace(line) && !FighterMove.TryParse(line, out _, out var problem))
                return $"\"{line.Trim()}\": {problem}";
        }

        return null;
    }
}

/// <summary>
/// Marks a list of strings as hitboxes and hurtboxes, a line each (see <see cref="Hitboxes.Boxes"/>): the Inspector lists them by
/// clip and frame, and opens the Clip Editor (Art > Animation Clips) to draw them over the frames.
/// </summary>
public sealed class FrameBoxesAttribute : FieldEditorAttribute
{
    public override bool Fits(Type type) => type == typeof(IReadOnlyList<string>);

    public override string FitsText => "a list of strings";

    public override string Describe() =>
        "Boxes, a line each: the clip and its frame, hit or hurt, where and how big, and what it does, e.g. \"Punch 2 hit at 0.6,0.3 size 0.5x0.25 damage 3\". The Inspector lists them by clip and frame; the Clip Editor (Art > Animation Clips, the Art.Clips command) draws them over the frames.";

    public override string? Problem(Component component, object? value)
    {
        foreach (var line in value as IReadOnlyList<string> ?? [])
        {
            if (!string.IsNullOrWhiteSpace(line) && !FrameBox.TryParse(line, out _, out var problem))
                return $"\"{line.Trim()}\": {problem}";
        }

        return null;
    }
}
