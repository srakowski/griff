using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Griff.Data;
using Griff.Scenes;
using Griff.Editing;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// Builds dice (<see cref="DiceAttribute"/>): how many, how many sides (the tabletop set) and what's added, with the
/// lowest, highest and average roll beside them. The dice can still be typed, "1d8+1d6-1" too; dice of more than one kind
/// show only as typed.
/// </summary>
public sealed class DiceEditor(PropertyField field, DiceAttribute attribute) : FieldEditor<DiceAttribute>(field, attribute)
{
    // The dice as they are, or null when there are none (or they don't read).
    private Dice? Current => Dice.TryParse(Text, out var dice) ? dice : null;

    /// <summary>True when the dice can be built here: none yet, or one kind of die with something added.</summary>
    public bool IsBuildable => Text.Trim().Length == 0 || Current is { } dice && dice.Groups.Count <= 1;

    /// <summary>How many dice: 0 for a plain number. Setting it keeps the sides and what's added.</summary>
    public double Count
    {
        get => Current is { Groups: [var group, ..] } ? group.Count : Text.Trim().Length == 0 ? 1 : 0;
        set => Build(Math.Max(0, (int)Math.Round(value)), Sides, (int)Plus);
    }

    /// <summary>How many sides each die has, e.g. 6.</summary>
    public int Sides
    {
        get => Current is { Groups: [var group, ..] } ? group.Sides : 6;
        set
        {
            Build(Math.Max(1, (int)Count), Math.Max(1, value), (int)Plus);
            Host.Seal();
        }
    }

    /// <summary>What's added to the roll (negative to take away).</summary>
    public double Plus
    {
        get => Current?.Modifier ?? 0;
        set => Build((int)Count, Sides, (int)Math.Round(value));
    }

    /// <summary>The sides offered: 4, 6, 8, 10, 12, 20 and 100.</summary>
    public IReadOnlyList<int> SidesChoices => FieldValues.DiceSides;

    /// <summary>"Rolls 3 to 18, 10.5 on average", "Always 5", or what's wrong; for none, that it's empty.</summary>
    public string Summary
    {
        get
        {
            if (Field.IsMixed)
                return "The selected entities' dice differ.";
            if (Text.Trim().Length == 0)
                return "No dice: it uses its plain number.";
            if (Current is not { } dice)
                return Field.ValueProblem ?? "";
            if (dice.Groups.Count == 0)
                return string.Create(CultureInfo.InvariantCulture, $"Always {dice.Min}");
            return string.Create(CultureInfo.InvariantCulture, $"Rolls {dice.Min} to {dice.Max}, {dice.Average:0.#} on average");
        }
    }

    /// <summary>Clears the dice, so the field's plain number is used.</summary>
    public void Clear() => SetText(null);

    public IRelayCommand ClearCommand => _clear ??= new RelayCommand(Clear);

    private IRelayCommand? _clear;

    // Each change is its own undo step; a number dragged ends its step when the drag does.
    private void Build(int count, int sides, int plus) =>
        SetTextLive((count == 0 ? Dice.Constant(plus) : Dice.Of(count, sides, plus)).ToString());
}
