using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Scenes;
using Transform = Griff.Scenes.Transform;

namespace Griff.Editor.Features.Inspector;

/// <summary>One component (X, Y, Z or W) of a vector property.</summary>
public sealed class AxisField(PropertyField owner, int index, string label) : ObservableObject
{
    public string Label { get; } = label;

    /// <summary>Whether it shows: a 2D Transform hides Z, and a rotation's X and Y (see <see cref="PropertyField.Is2D"/>).</summary>
    public bool IsShown => owner.IsAxisShown(index);

    /// <summary>The letter on its box; none for a 2D rotation's one angle, which the field's name drags.</summary>
    public string? ShownLabel => owner.Is2D && owner.PropertyName == nameof(Transform.Rotation) ? null : Label;

    public double Value
    {
        get => owner.GetAxis(index);
        set => owner.SetAxis(index, (float)value);
    }

    /// <summary>True when the selected entities differ on this axis.</summary>
    public bool IsMixed => owner.IsAxisMixed(index);

    public void Refresh() => OnPropertyChanged(string.Empty);
}
