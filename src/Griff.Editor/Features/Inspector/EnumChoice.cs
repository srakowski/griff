using Griff.Editing;

namespace Griff.Editor.Features.Inspector;

/// <summary>One value of an enum field: as it reads in the list ("Ping Pong"), and what it means (its documentation).</summary>
public sealed class EnumChoice(Enum value)
{
    public Enum Value { get; } = value;

    public string Label { get; } = InspectorPanel.Humanize(value.ToString());

    public string? Tip { get; } = Griff.Editing.DocComments.Summary(value) is { IsEmpty: false } doc ? doc.PlainText : null;

    public override string ToString() => Label;
}
